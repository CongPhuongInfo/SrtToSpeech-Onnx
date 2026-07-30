Imports System.IO


Public Module WavFile

    ''' <summary>
    ''' Ghi mảng mẫu âm thanh float [-1, 1] ra file .wav PCM16 mono.
    ''' </summary>
    Public Sub WritePcm16(path As String, samples As Single(), sampleRate As Integer)
        Using fs As New FileStream(path, FileMode.Create, FileAccess.Write)
            Using bw As New BinaryWriter(fs)
                Dim byteRate = sampleRate * 2 ' mono, 16-bit
                Dim dataSize = samples.Length * 2

                ' RIFF header
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"))
                bw.Write(CUInt(36 + dataSize))
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"))

                ' fmt chunk
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "))
                bw.Write(CUInt(16)) ' chunk size
                bw.Write(CUShort(1)) ' PCM
                bw.Write(CUShort(1)) ' mono
                bw.Write(CUInt(sampleRate))
                bw.Write(CUInt(byteRate))
                bw.Write(CUShort(2)) ' block align
                bw.Write(CUShort(16)) ' bits per sample

                ' data chunk
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"))
                bw.Write(CUInt(dataSize))

                For Each s In samples
                    Dim clamped = Math.Max(-1.0F, Math.Min(1.0F, s))
                    Dim v = CShort(clamped * Short.MaxValue)
                    bw.Write(v)
                Next
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Tạo một "khung tranh" âm thanh (canvas) đủ dài rồi ghi đè từng đoạn thoại
    ''' vào đúng thời điểm bắt đầu của nó (theo mili-giây). Nếu 2 đoạn chồng lấn,
    ''' các mẫu được cộng dồn (mix) thay vì ghi đè hoàn toàn.
    ''' </summary>
    Public Function BuildCanvas(clips As List(Of (StartMs As Long, Samples As Single())), sampleRate As Integer) As Single()
        If clips.Count = 0 Then Return Array.Empty(Of Single)()

        Dim maxEndSample = 0L
        For Each clip In clips
            Dim startSample = CLng(clip.StartMs / 1000.0 * sampleRate)
            Dim endSample = startSample + clip.Samples.Length
            If endSample > maxEndSample Then maxEndSample = endSample
        Next

        Dim canvas(CInt(maxEndSample) - 1) As Single

        For Each clip In clips
            Dim startSample = CLng(clip.StartMs / 1000.0 * sampleRate)
            For i = 0 To clip.Samples.Length - 1
                Dim pos = startSample + i
                If pos >= 0 AndAlso pos < canvas.Length Then
                    canvas(pos) += clip.Samples(i)
                End If
            Next
        Next

        Return canvas
    End Function

End Module

