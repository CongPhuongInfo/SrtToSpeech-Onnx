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
    Private ReadOnly _espeakVoice As String
    Public Property EspeakPath As String = "espeak-ng"

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
            For Each ch In ipa
                ' Bỏ qua ký tự nối âm (tie bar) và khoảng trắng thừa, giữ lại khoảng trắng phân tách từ
                If ch = ChrW(&H361) Then Continue For ' tie bar U+0361
                phonemes.Add(ch.ToString())
            Next
            phonemes.Add(" ")
        Next

        Return phonemes
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

    ''' <summary>
    ''' Chạy espeak-ng.exe với chế độ xuất IPA (--ipa) cho một đoạn văn bản ngắn.
    ''' </summary>
    Private Function RunEspeak(segment As String) As String
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

