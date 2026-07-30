Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions


''' <summary>
''' Một dòng thoại (cue) trong file SRT.
''' </summary>
Public Class SrtCue
    Public Property Index As Integer
    Public Property Start As TimeSpan
    Public Property [End] As TimeSpan
    Public Property Text As String

    ''' <summary>
    ''' Nhãn giọng đọc lấy từ tiền tố "[Tên]" ở đầu dòng thoại, dùng cho chế độ
    ''' lồng tiếng nhiều giọng. Rỗng ("") nếu dòng thoại không gắn nhãn.
    ''' </summary>
    Public Property Speaker As String = ""

    Public ReadOnly Property Duration As TimeSpan
        Get
            Return [End] - Start
        End Get
    End Property
End Class

Public Module SrtParser

    Private ReadOnly TimeLineRegex As New Regex(
        "(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})",
        RegexOptions.Compiled)

    ' Nhãn giọng đọc ở đầu dòng thoại, ví dụ: "[Nam] Xin chào" hoặc "[Cô giáo] ..."
    Private ReadOnly SpeakerTagRegex As New Regex(
        "^\[\s*([^\]]{1,40}?)\s*\]\s*(.*)$",
        RegexOptions.Compiled Or RegexOptions.Singleline)

    ''' <summary>
    ''' Đọc toàn bộ file .srt và trả về danh sách các câu thoại theo thứ tự thời gian.
    ''' </summary>
    Public Function ParseFile(path As String) As List(Of SrtCue)
        Dim raw = File.ReadAllText(path, Encoding.UTF8)
        Return ParseText(raw)
    End Function

    Public Function ParseText(raw As String) As List(Of SrtCue)
        Dim cues As New List(Of SrtCue)

        ' Chuẩn hoá xuống dòng rồi tách theo khối cách nhau bởi dòng trống
        Dim normalized = raw.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        Dim blocks = Regex.Split(normalized.Trim(), "\n\s*\n")

        For Each block In blocks
            If String.IsNullOrWhiteSpace(block) Then Continue For

            Dim lines = block.Split(New Char() {vbLf}, StringSplitOptions.None)
            If lines.Length < 2 Then Continue For

            Dim lineIdx = 0
            Dim cueIndex As Integer

            ' Dòng đầu có thể là số thứ tự (không bắt buộc trong vài file srt lỗi)
            If Integer.TryParse(lines(0).Trim(), cueIndex) Then
                lineIdx = 1
            Else
                cueIndex = cues.Count + 1
            End If

            If lineIdx >= lines.Length Then Continue For
            Dim m = TimeLineRegex.Match(lines(lineIdx))
            If Not m.Success Then Continue For

            Dim startTs = New TimeSpan(0, CInt(m.Groups(1).Value), CInt(m.Groups(2).Value), CInt(m.Groups(3).Value), CInt(m.Groups(4).Value))
            Dim endTs = New TimeSpan(0, CInt(m.Groups(5).Value), CInt(m.Groups(6).Value), CInt(m.Groups(7).Value), CInt(m.Groups(8).Value))

            Dim textLines As New List(Of String)
            For i = lineIdx + 1 To lines.Length - 1
                Dim cleanLine = StripTags(lines(i)).Trim()
                If cleanLine.Length > 0 Then textLines.Add(cleanLine)
            Next

            Dim text = String.Join(" ", textLines).Trim()
            If text.Length = 0 Then Continue For

            Dim speaker = ""
            Dim tagMatch = SpeakerTagRegex.Match(text)
            If tagMatch.Success Then
                speaker = tagMatch.Groups(1).Value.Trim()
                text = tagMatch.Groups(2).Value.Trim()
                If text.Length = 0 Then Continue For
            End If

            cues.Add(New SrtCue With {
                .Index = cueIndex,
                .Start = startTs,
                .End = endTs,
                .Text = text,
                .Speaker = speaker
            })
        Next

        Return cues.OrderBy(Function(c) c.Start).ToList()
    End Function

    ''' <summary>
    ''' Lấy danh sách các nhãn giọng đọc khác nhau xuất hiện trong danh sách cue,
    ''' dùng để hiển thị bảng gán giọng cho chế độ lồng tiếng nhiều giọng.
    ''' Chuỗi rỗng ("") đại diện cho các dòng KHÔNG gắn nhãn.
    ''' </summary>
    Public Function GetDistinctSpeakers(cues As List(Of SrtCue)) As List(Of String)
        Return cues.Select(Function(c) c.Speaker).Distinct().OrderBy(Function(s) s).ToList()
    End Function

    ' Loại bỏ các thẻ định dạng kiểu <i>, <b>, {\an8} thường gặp trong srt
    Private Function StripTags(line As String) As String
        Dim s = Regex.Replace(line, "<[^>]+>", "")
        s = Regex.Replace(s, "\{[^}]*\}", "")
        Return s
    End Function

End Module

