Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors


''' <summary>
''' Bọc một giọng nói Piper TTS (file .onnx + file cấu hình .onnx.json).
''' Cần cài sẵn espeak-ng trên máy (dùng để phiên âm IPA cho văn bản) và có trong PATH,
''' hoặc chỉ định trực tiếp đường dẫn tới espeak-ng.exe bằng EspeakPath.
''' </summary>
Public Class PiperVoice
    Implements IDisposable

    Private ReadOnly _session As InferenceSession
    Private ReadOnly _phonemeIdMap As Dictionary(Of String, Integer)
    Private ReadOnly _sortedPhonemeKeys As List(Of String)
    Private ReadOnly _espeakVoice As String
    Public Property EspeakPath As String = "espeak-ng"

    ''' <summary>
    ''' Gọi lại (nếu được gán) khi gặp ký hiệu IPA không có trong phoneme_id_map của model,
    ''' để nơi dùng PiperVoice (ví dụ MainForm) có thể ghi log cảnh báo cho người dùng biết.
    ''' </summary>
    Public Property OnWarning As Action(Of String)

    Public Property SampleRate As Integer
    Public Property NoiseScale As Single
    Public Property LengthScale As Single
    Public Property NoiseW As Single

    Private Const PAD As String = "_"
    Private Const BOS As String = "^"
    Private Const EOS As String = "$"

    Public Sub New(onnxModelPath As String, configJsonPath As String)
        If Not File.Exists(onnxModelPath) Then
            Throw New FileNotFoundException("Không tìm thấy file mô hình .onnx", onnxModelPath)
        End If
        If Not File.Exists(configJsonPath) Then
            Throw New FileNotFoundException("Không tìm thấy file cấu hình .onnx.json", configJsonPath)
        End If

        Dim jsonText = File.ReadAllText(configJsonPath, Encoding.UTF8)
        Using doc = JsonDocument.Parse(jsonText)
            Dim root = doc.RootElement

            ' phoneme_id_map: { "a": [14], "b": [15], ... }
            _phonemeIdMap = New Dictionary(Of String, Integer)
            Dim mapElement = root.GetProperty("phoneme_id_map")
            For Each prop In mapElement.EnumerateObject()
                Dim firstId = prop.Value.EnumerateArray().First().GetInt32()
                _phonemeIdMap(prop.Name) = firstId
            Next

            ' audio.sample_rate
            SampleRate = root.GetProperty("audio").GetProperty("sample_rate").GetInt32()

            ' inference: noise_scale / length_scale / noise_w (giá trị mặc định của giọng)
            Dim inf = root.GetProperty("inference")
            NoiseScale = CSng(inf.GetProperty("noise_scale").GetDouble())
            LengthScale = CSng(inf.GetProperty("length_scale").GetDouble())
            NoiseW = CSng(inf.GetProperty("noise_w").GetDouble())

            ' espeak.voice, ví dụ "vi" cho tiếng Việt
            _espeakVoice = "vi"
            Dim espeakEl As JsonElement
            If root.TryGetProperty("espeak", espeakEl) Then
                Dim voiceEl As JsonElement
                If espeakEl.TryGetProperty("voice", voiceEl) Then
                    _espeakVoice = voiceEl.GetString()
                End If
            End If
        End Using

        ' Sắp xếp các ký hiệu phoneme theo độ dài giảm dần (dùng cho tokenize kiểu "khớp dài nhất").
        ' Nhiều ký hiệu IPA thực chất gồm nhiều codepoint (chữ cái gốc + dấu kết hợp như mũi hoá,
        ' kéo dài âm...) - nếu tách theo từng Char đơn lẻ như trước đây thì các dấu kết hợp này sẽ
        ' bị bóc ra riêng và không khớp được với map (bị bỏ qua), làm mất sắc thái phát âm.
        _sortedPhonemeKeys = _phonemeIdMap.Keys.
            Where(Function(k) k <> PAD AndAlso k <> BOS AndAlso k <> EOS AndAlso k.Length > 0).
            OrderByDescending(Function(k) k.Length).
            ToList()

        Dim options As New SessionOptions()
        _session = New InferenceSession(onnxModelPath, options)
    End Sub

    ''' <summary>
    ''' Gọi espeak-ng để chuyển văn bản thành chuỗi phoneme IPA.
    ''' Tách theo dấu câu để giữ lại các dấu ngắt (. , ! ? : ;) trong luồng phoneme,
    ''' giống cách piper-phonemize xử lý câu.
    ''' </summary>
    Private Function TextToPhonemes(text As String) As List(Of String)
        Dim phonemes As New List(Of String)

        ' Tách văn bản thành các đoạn theo dấu câu, giữ lại dấu câu làm token riêng
        Dim tokens = SplitKeepingPunctuation(text)

        For Each tok In tokens
            If tok.Length = 1 AndAlso ".,!?;:".Contains(tok) Then
                phonemes.Add(tok)
                Continue For
            End If

            Dim segment = tok.Trim()
            If segment.Length = 0 Then Continue For

            Dim ipa = RunEspeak(segment)
            ' Bỏ ký tự nối âm (tie bar) trước khi tokenize - đây không phải 1 phoneme độc lập,
            ' chỉ là dấu nối 2 ký hiệu IPA đứng cạnh nhau thành 1 âm ghép (ví dụ affricate).
            ipa = ipa.Replace(ChrW(&H361).ToString(), "")

            phonemes.AddRange(TokenizeIpaToPhonemes(ipa))
            phonemes.Add(" ")
        Next

        Return phonemes
    End Function

    ''' <summary>
    ''' Tách chuỗi IPA thô (do espeak-ng xuất ra) thành danh sách phoneme khớp với
    ''' phoneme_id_map của chính model đang dùng, theo kiểu "khớp dài nhất trước"
    ''' (giống cách piper_phonemize thực sự làm) thay vì tách từng ký tự Unicode đơn lẻ.
    ''' Lý do: nhiều ký hiệu IPA gồm nhiều codepoint (chữ cái gốc + dấu kết hợp như mũi hoá,
    ''' kéo dài âm, trọng âm...) - tách sai sẽ làm rơi mất dấu kết hợp và sai/mất âm khi đọc.
    ''' </summary>
    Private Function TokenizeIpaToPhonemes(ipa As String) As List(Of String)
        Dim result As New List(Of String)
        Dim i = 0
        Dim unknownChars As New List(Of String)

        While i < ipa.Length
            Dim matched As String = Nothing

            For Each key In _sortedPhonemeKeys
                If key.Length <= ipa.Length - i AndAlso String.CompareOrdinal(ipa, i, key, 0, key.Length) = 0 Then
                    matched = key
                    Exit For
                End If
            Next

            If matched IsNot Nothing Then
                result.Add(matched)
                i += matched.Length
            Else
                ' Không khớp được ký hiệu nào trong phoneme_id_map - ghi nhận để cảnh báo,
                ' rồi bỏ qua đúng 1 ký tự (thay vì cả cụm) để không lỡ nuốt luôn ký tự kế tiếp
                ' hợp lệ đứng ngay sau nó.
                unknownChars.Add($"U+{AscW(ipa(i)):X4} '{ipa(i)}'")
                i += 1
            End If
        End While

        If unknownChars.Count > 0 AndAlso OnWarning IsNot Nothing Then
            OnWarning.Invoke($"Bỏ qua {unknownChars.Count} ký hiệu IPA không có trong phoneme_id_map: {String.Join(", ", unknownChars.Distinct())}")
        End If

        Return result
    End Function

    Private Function SplitKeepingPunctuation(text As String) As List(Of String)
        Dim result As New List(Of String)
        Dim current As New StringBuilder()
        For Each c As Char In text
            If ".,!?;:".Contains(c) Then
                If current.Length > 0 Then
                    result.Add(current.ToString())
                    current.Clear()
                End If
                result.Add(c.ToString())
            Else
                current.Append(c)
            End If
        Next
        If current.Length > 0 Then result.Add(current.ToString())
        Return result
    End Function

    Private _nativeChecked As Boolean = False
    Private _useNative As Boolean = False

    ''' <summary>
    ''' Đường dẫn thư mục "espeak-ng-data" dùng cho libespeak-ng.dll (nếu có đóng gói kèm app).
    ''' Để trống = tự dò trong thư mục chạy app (bin\espeak-ng-data), rồi tới đường dẫn cài đặt
    ''' mặc định của hệ thống.
    ''' </summary>
    Public Property EspeakDataPath As String = ""

    Private Function AutoDetectEspeakDataPath() As String
        If Not String.IsNullOrWhiteSpace(EspeakDataPath) Then Return EspeakDataPath
        Dim bundled = Path.Combine(AppContext.BaseDirectory, "espeak-ng-data")
        If Directory.Exists(bundled) Then Return bundled
        Return Nothing ' để libespeak-ng tự dò đường dẫn cài đặt mặc định của hệ thống
    End Function

    ''' <summary>
    ''' Phiên âm 1 đoạn văn bản ngắn thành IPA. Ưu tiên gọi thẳng "libespeak-ng.dll" qua P/Invoke
    ''' (nhanh và khớp với cách piper_phonemize thật sự làm lúc train); nếu không khởi tạo được
    ''' (chưa đóng gói DLL kèm app, thiếu dữ liệu ngôn ngữ...) thì tự động rơi về cách cũ: gọi
    ''' "espeak-ng.exe" như một tiến trình con.
    ''' </summary>
    Private Function RunEspeak(segment As String) As String
        If Not _nativeChecked Then
            _nativeChecked = True
            _useNative = EspeakNative.TryInitialize(AutoDetectEspeakDataPath()) AndAlso EspeakNative.EnsureVoice(_espeakVoice)
            If _useNative Then
                OnWarning?.Invoke("Dùng libespeak-ng.dll (native) để phiên âm.")
            Else
                OnWarning?.Invoke("Không tìm thấy/khởi tạo được libespeak-ng.dll, dùng espeak-ng.exe (subprocess) để phiên âm.")
            End If
        End If

        If _useNative Then
            Try
                Return EspeakNative.TextToIpa(segment)
            Catch ex As Exception
                _useNative = False ' lỗi giữa chừng -> chuyển hẳn về subprocess cho các câu còn lại
                OnWarning?.Invoke("Lỗi khi dùng libespeak-ng.dll (" & ex.Message & "), chuyển sang espeak-ng.exe.")
            End Try
        End If

        Return RunEspeakSubprocess(segment)
    End Function

    ''' <summary>
    ''' Chạy espeak-ng.exe với chế độ xuất IPA (--ipa) cho một đoạn văn bản ngắn.
    ''' Dùng làm phương án dự phòng khi không gọi được libespeak-ng.dll trực tiếp.
    ''' </summary>
    Private Function RunEspeakSubprocess(segment As String) As String
        Dim psi As New ProcessStartInfo() With {
            .FileName = EspeakPath,
            .UseShellExecute = False,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .CreateNoWindow = True
        }
        psi.ArgumentList.Add("-v")
        psi.ArgumentList.Add(_espeakVoice)
        psi.ArgumentList.Add("-q")
        psi.ArgumentList.Add("--ipa")
        psi.ArgumentList.Add(segment)

        Using p = Process.Start(psi)
            Dim output = p.StandardOutput.ReadToEnd()
            p.WaitForExit(10000)
            Return output.Replace(vbCrLf, " ").Replace(vbLf, " ").Trim()
        End Using
    End Function

    Private Function PhonemesToIds(phonemes As List(Of String)) As List(Of Integer)
        Dim ids As New List(Of Integer)
        ids.Add(_phonemeIdMap(BOS))
        For Each ph In phonemes
            If Not _phonemeIdMap.ContainsKey(ph) Then Continue For
            ids.Add(_phonemeIdMap(ph))
            ids.Add(_phonemeIdMap(PAD))
        Next
        ids.Add(_phonemeIdMap(EOS))
        Return ids
    End Function

    ''' <summary>
    ''' Tổng hợp giọng nói cho một đoạn văn bản, trả về mẫu âm thanh dạng float [-1, 1]
    ''' ở tần số lấy mẫu SampleRate.
    ''' </summary>
    Public Function Synthesize(text As String, Optional lengthScaleOverride As Single? = Nothing) As Single()
        Dim phonemes = TextToPhonemes(text)
        Dim ids = PhonemesToIds(phonemes)

        If ids.Count <= 2 Then
            Return Array.Empty(Of Single)()
        End If

        Dim inputTensor = New DenseTensor(Of Long)({1, ids.Count})
        For i = 0 To ids.Count - 1
            inputTensor(0, i) = CLng(ids(i))
        Next

        Dim lengthsTensor = New DenseTensor(Of Long)({1})
        lengthsTensor(0) = CLng(ids.Count)

        Dim finalLengthScale = If(lengthScaleOverride.HasValue, lengthScaleOverride.Value, LengthScale)
        Dim scalesTensor = New DenseTensor(Of Single)({3})
        scalesTensor(0) = NoiseScale
        scalesTensor(1) = finalLengthScale
        scalesTensor(2) = NoiseW

        Dim inputs As New List(Of NamedOnnxValue) From {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("input_lengths", lengthsTensor),
            NamedOnnxValue.CreateFromTensor("scales", scalesTensor)
        }

        Using results = _session.Run(inputs)
            Dim outputTensor = results.First(Function(r) r.Name = "output").AsTensor(Of Single)()
            Dim samples(outputTensor.Length - 1) As Single
            Dim idx = 0
            For Each v In outputTensor
                samples(idx) = v
                idx += 1
            Next
            Return samples
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _session?.Dispose()
    End Sub

End Class

