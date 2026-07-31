Imports System.Runtime.InteropServices
Imports System.Text

''' <summary>
''' Bọc trực tiếp thư viện gốc "libespeak-ng.dll" qua P/Invoke, thay vì gọi "espeak-ng.exe"
''' như một tiến trình con riêng cho mỗi câu. Đây chính là cách piper_phonemize (bản gốc,
''' dùng lúc train model) thực sự phiên âm - nên khi dùng được, kết quả sẽ khớp với model
''' gần như tuyệt đối, đồng thời nhanh hơn nhiều so với spawn 1 process mới mỗi lần gọi.
'''
''' Nếu không tìm thấy "libespeak-ng.dll" (chưa được đóng gói kèm app) hoặc khởi tạo thất bại
''' vì bất kỳ lý do gì, PiperVoice sẽ tự động rơi về cách cũ (gọi espeak-ng.exe qua subprocess)
''' - xem RunEspeak() trong PiperVoice.vb.
''' </summary>
Friend Module EspeakNative

    ' --- Cờ dùng cho espeak_Initialize (xem speak_lib.h của espeak-ng) ---
    Private Const AUDIO_OUTPUT_SYNCHRONOUS As Integer = 0
    Private Const espeakINITIALIZE_DONT_EXIT As Integer = &H8000

    ' --- Cờ dùng cho espeak_TextToPhonemes ---
    Private Const CHARS_UTF8 As Integer = 1
    Private Const PHONEMES_IPA As Integer = 2 ' bit 1 bật = xuất ký hiệu IPA (UTF-8) thay vì bảng ký hiệu ascii nội bộ của espeak

    Private _initialized As Boolean = False
    Private _initFailed As Boolean = False
    Private _lastSetVoice As String = Nothing

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function espeak_Initialize(output As Integer, buflength As Integer, path As IntPtr, options As Integer) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Private Function espeak_SetVoiceByName(name As String) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function espeak_TextToPhonemes(ByRef textptr As IntPtr, textmode As Integer, phonememode As Integer) As IntPtr
    End Function

    ''' <summary>
    ''' Thử khởi tạo libespeak-ng (chỉ thực sự chạy 1 lần cho cả tiến trình - các lần gọi sau
    ''' chỉ trả lại kết quả đã cache, kể cả khi dataPath khác đi).
    ''' dataPath: thư mục "espeak-ng-data" (Nothing/rỗng = để espeak tự dò đường dẫn cài đặt
    ''' mặc định của hệ thống, ví dụ khi đã cài eSpeak NG qua nút "Cài đặt" trong app).
    ''' </summary>
    Public Function TryInitialize(dataPath As String) As Boolean
        If _initialized Then Return True
        If _initFailed Then Return False

        Dim pathPtr As IntPtr = IntPtr.Zero
        Try
            If Not String.IsNullOrWhiteSpace(dataPath) Then
                ' Mã hoá tay bằng UTF-8 thay vì để marshaler tự làm, để không vỡ đường dẫn
                ' có ký tự tiếng Việt (ví dụ "C:\Người dùng\...").
                Dim bytes = Encoding.UTF8.GetBytes(dataPath & Chr(0))
                pathPtr = Marshal.AllocHGlobal(bytes.Length)
                Marshal.Copy(bytes, 0, pathPtr, bytes.Length)
            End If

            Dim result = espeak_Initialize(AUDIO_OUTPUT_SYNCHRONOUS, 0, pathPtr, espeakINITIALIZE_DONT_EXIT)
            If result < 0 Then
                _initFailed = True
                Return False
            End If
            _initialized = True
            Return True
        Catch ex As Exception
            ' Bao gồm cả DllNotFoundException khi chưa đóng gói libespeak-ng.dll kèm app
            _initFailed = True
            Return False
        Finally
            If pathPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(pathPtr)
        End Try
    End Function

    ''' <summary>Đặt giọng espeak (ví dụ "vi") cho lần phiên âm tiếp theo, có cache để tránh gọi thừa.</summary>
    Public Function EnsureVoice(voiceName As String) As Boolean
        If Not _initialized Then Return False
        If _lastSetVoice = voiceName Then Return True
        Try
            Dim r = espeak_SetVoiceByName(voiceName)
            If r = 0 Then
                _lastSetVoice = voiceName
                Return True
            End If
            Return False
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Phiên âm 1 đoạn văn bản ngắn (không chứa dấu ngắt câu - đã tách sẵn ở PiperVoice)
    ''' thành chuỗi IPA, bằng cách gọi trực tiếp hàm gốc của thư viện espeak-ng.
    ''' </summary>
    Public Function TextToIpa(text As String) As String
        Dim buffer = Encoding.UTF8.GetBytes(text & Chr(0))
        Dim bufPtr = Marshal.AllocHGlobal(buffer.Length)
        Try
            Marshal.Copy(buffer, 0, bufPtr, buffer.Length)
            Dim textPtr = bufPtr
            Dim sb As New StringBuilder()

            ' espeak_TextToPhonemes trả về phoneme của 1 "clause" mỗi lần gọi và tự cập nhật
            ' textptr trỏ tới phần văn bản còn lại - gọi lặp tới khi trả về NULL (hết văn bản).
            ' Giới hạn số vòng lặp để tránh treo app nếu thư viện có hành vi bất thường.
            Dim guard = 0
            Do
                guard += 1
                If guard > 1000 Then Exit Do

                Dim resultPtr = espeak_TextToPhonemes(textPtr, CHARS_UTF8, PHONEMES_IPA)
                If resultPtr = IntPtr.Zero Then Exit Do

                Dim resultStr = PtrToUtf8String(resultPtr)
                If resultStr.Length > 0 Then
                    sb.Append(resultStr)
                    sb.Append(" "c)
                End If
            Loop

            Return sb.ToString().Trim()
        Finally
            Marshal.FreeHGlobal(bufPtr)
        End Try
    End Function

    Private Function PtrToUtf8String(ptr As IntPtr) As String
        If ptr = IntPtr.Zero Then Return String.Empty
        Dim len = 0
        While Marshal.ReadByte(ptr, len) <> 0
            len += 1
        End While
        If len = 0 Then Return String.Empty
        Dim bytes(len - 1) As Byte
        Marshal.Copy(ptr, bytes, 0, len)
        Return Encoding.UTF8.GetString(bytes)
    End Function

End Module
