Imports System.IO
Imports System.Linq


''' <summary>
''' Thông tin 1 giọng đọc: tên hiển thị, loại (thư mục con trong Data),
''' đường dẫn tới file .onnx và file cấu hình .json tương ứng.
''' </summary>
Public Class VoiceInfo
    Public Property Name As String
    Public Property Category As String
    Public Property OnnxPath As String
    Public Property ConfigPath As String

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' Quét thư mục Data (vd: Data\Male, Data\Female, và sau này có thể thêm
''' Data\Kid, Data\Regional... mà không cần sửa code) để tìm các giọng đọc
''' hợp lệ (mỗi giọng gồm 1 file .onnx + 1 file cấu hình .json cùng tên).
''' </summary>
Public Module VoiceLibrary

    Public Function ScanVoices(dataRoot As String) As List(Of VoiceInfo)
        Dim voices As New List(Of VoiceInfo)
        If Not Directory.Exists(dataRoot) Then Return voices

        For Each categoryDir In Directory.GetDirectories(dataRoot).OrderBy(Function(d) d)
            Dim category = Path.GetFileName(categoryDir)
            For Each onnxFile In Directory.GetFiles(categoryDir, "*.onnx").OrderBy(Function(f) f)
                Dim configFile = FindConfigFor(onnxFile)
                If configFile Is Nothing Then Continue For

                voices.Add(New VoiceInfo With {
                    .Name = Path.GetFileNameWithoutExtension(onnxFile),
                    .Category = category,
                    .OnnxPath = onnxFile,
                    .ConfigPath = configFile
                })
            Next
        Next

        Return voices
    End Function

    ' Hỗ trợ vài kiểu đặt tên file cấu hình thường gặp:
    '   giong.onnx.json  |  giong_onnx.json  |  giong.json
    Private Function FindConfigFor(onnxPath As String) As String
        Dim dir = Path.GetDirectoryName(onnxPath)
        Dim baseName = Path.GetFileNameWithoutExtension(onnxPath)

        Dim candidates As String() = {
            onnxPath & ".json",
            Path.Combine(dir, baseName & "_onnx.json"),
            Path.Combine(dir, baseName & ".onnx.json"),
            Path.Combine(dir, baseName & ".json")
        }

        For Each c In candidates
            If File.Exists(c) Then Return c
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Dịch tên thư mục sang tiếng Việt cho các loại phổ biến; loại khác giữ nguyên tên
    ''' thư mục để dễ mở rộng (vd: Data\Kid, Data\Elder, Data\Northern...).
    ''' </summary>
    Public Function TranslateCategory(raw As String) As String
        Select Case raw.Trim().ToLowerInvariant()
            Case "male", "nam"
                Return "Giọng Nam"
            Case "female", "nu", "nữ"
                Return "Giọng Nữ"
            Case Else
                Return raw
        End Select
    End Function

End Module

