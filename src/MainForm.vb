Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms


Public Class MainForm
    Inherits Form

    ' Chế độ đọc
    Private rbSingleVoice As RadioButton
    Private rbMultiVoice As RadioButton

    ' Thư viện giọng đọc (chế độ giọng đơn)
    Private voiceGroup As GroupBox
    Private cmbCategory As ComboBox
    Private cmbVoice As ComboBox
    Private btnRefreshVoices As Button
    Private chkAdvanced As CheckBox
    Private advancedHost As TableLayoutPanel
    Private advancedPanel As TableLayoutPanel
    Private txtModel As TextBox
    Private txtConfig As TextBox
    Private lblVoiceHint As Label

    ' Lồng tiếng nhiều giọng
    Private multiVoiceGroup As GroupBox
    Private btnScanSpeakers As Button
    Private lblMultiHint As Label
    Private speakerScrollPanel As Panel
    Private speakerMapPanel As TableLayoutPanel
    Private _speakerRows As New Dictionary(Of String, SpeakerRow)

    ' Tệp phụ đề / đầu ra
    Private txtSrt As TextBox
    Private txtOutput As TextBox
    Private txtEspeak As TextBox
    Private btnInstallEspeak As Button

    Private numSpeed As NumericUpDown
    Private btnStart As Button
    Private btnCancel As Button
    Private progressBar As ProgressBar
    Private txtLog As TextBox
    Private lblStatus As Label

    Private _cts As CancellationTokenSource
    Private _allVoices As New List(Of VoiceInfo)

    Private Class CategoryItem
        Public Property Display As String
        Public Property Raw As String
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    ' 1 dòng gán giọng cho 1 nhãn: combobox Loại giọng (Nam/Nữ/...) cascading với combobox Giọng đọc cụ thể
    Private Class SpeakerRow
        Public Property CategoryCombo As ComboBox
        Public Property VoiceCombo As ComboBox

        Public ReadOnly Property SelectedVoice As VoiceInfo
            Get
                Return TryCast(VoiceCombo.SelectedItem, VoiceInfo)
            End Get
        End Property
    End Class

    Private ReadOnly _dataRoot As String = Path.Combine(AppContext.BaseDirectory, "Data")

    Public Sub New()
        Text = "SRT → Speech (Piper TTS)"
        Width = 900
        Height = 720
        StartPosition = FormStartPosition.CenterScreen
        MinimumSize = New Drawing.Size(780, 600)
        Font = New Drawing.Font("Segoe UI", 9.5F)

        BuildUi()
        LoadVoices()
        PrefillDefaultPaths()
    End Sub

    Private Sub BuildUi()
        Dim root As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .Padding = New Padding(12)
        }
        Controls.Add(root)

        ' ================= Chế độ đọc =================
        Dim modeGroup As New GroupBox() With {.Text = "Chế độ đọc", .Dock = DockStyle.Top, .Height = 66}
        Dim modePanel As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .Padding = New Padding(10, 4, 10, 4)}
        rbSingleVoice = New RadioButton() With {.Text = "Giọng đơn (đọc toàn bộ file bằng 1 giọng)", .AutoSize = True, .Checked = True, .Margin = New Padding(0, 4, 24, 0)}
        rbMultiVoice = New RadioButton() With {.Text = "Lồng tiếng nhiều giọng (gắn nhãn [Tên] trong file .srt)", .AutoSize = True, .Margin = New Padding(0, 4, 0, 0)}
        AddHandler rbSingleVoice.CheckedChanged, AddressOf Mode_CheckedChanged
        AddHandler rbMultiVoice.CheckedChanged, AddressOf Mode_CheckedChanged
        modePanel.Controls.Add(rbSingleVoice)
        modePanel.Controls.Add(rbMultiVoice)
        modeGroup.Controls.Add(modePanel)

        ' ================= Thư viện giọng đọc (giọng đơn) =================
        voiceGroup = New GroupBox() With {.Text = "Thư viện giọng đọc (thư mục Data)", .Dock = DockStyle.Top, .Height = 170}
        Dim voiceLayout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 3,
            .Padding = New Padding(10)
        }
        voiceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        voiceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        voiceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        voiceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        voiceGroup.Controls.Add(voiceLayout)

        voiceLayout.Controls.Add(New Label() With {.Text = "Loại giọng:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 8, 6, 0)}, 0, 0)
        cmbCategory = New ComboBox() With {.Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList}
        AddHandler cmbCategory.SelectedIndexChanged, AddressOf CmbCategory_SelectedIndexChanged
        voiceLayout.Controls.Add(cmbCategory, 1, 0)

        voiceLayout.Controls.Add(New Label() With {.Text = "Giọng đọc:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(12, 8, 6, 0)}, 2, 0)
        cmbVoice = New ComboBox() With {.Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList}
        voiceLayout.Controls.Add(cmbVoice, 3, 0)

        btnRefreshVoices = New Button() With {.Text = "⟳ Làm mới danh sách", .AutoSize = True, .Margin = New Padding(0, 8, 0, 0)}
        AddHandler btnRefreshVoices.Click, Sub() LoadVoices()
        voiceLayout.Controls.Add(btnRefreshVoices, 1, 1)

        lblVoiceHint = New Label() With {
            .AutoSize = True,
            .ForeColor = Drawing.Color.Gray,
            .Text = "Đặt file .onnx + .json vào: Data\Male\ hoặc Data\Female\ (có thể thêm thư mục loại khác)",
            .Margin = New Padding(0, 6, 0, 0)
        }
        voiceLayout.SetColumnSpan(lblVoiceHint, 4)
        voiceLayout.Controls.Add(lblVoiceHint, 0, 2)

        chkAdvanced = New CheckBox() With {.Text = "Tùy chỉnh thủ công (chọn trực tiếp file .onnx / .json khác)", .AutoSize = True, .Margin = New Padding(0, 10, 0, 0)}
        AddHandler chkAdvanced.CheckedChanged, AddressOf ChkAdvanced_CheckedChanged

        advancedPanel = New TableLayoutPanel() With {
            .Dock = DockStyle.Top,
            .ColumnCount = 3,
            .RowCount = 2,
            .AutoSize = True,
            .Visible = False,
            .Padding = New Padding(0, 4, 0, 4)
        }
        advancedPanel.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        advancedPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        advancedPanel.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        txtModel = New TextBox() With {.Dock = DockStyle.Fill}
        txtConfig = New TextBox() With {.Dock = DockStyle.Fill}
        AddRow(advancedPanel, 0, "File .onnx tùy chỉnh:", txtModel, Sub() BrowseOpen(txtModel, "Mô hình ONNX|*.onnx|Tất cả tệp|*.*"))
        AddRow(advancedPanel, 1, "File .onnx.json tùy chỉnh:", txtConfig, Sub() BrowseOpen(txtConfig, "Cấu hình JSON|*.json|Tất cả tệp|*.*"))

        advancedHost = New TableLayoutPanel() With {.Dock = DockStyle.Top, .ColumnCount = 1, .AutoSize = True}
        advancedHost.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        advancedHost.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        advancedHost.Controls.Add(chkAdvanced, 0, 0)
        advancedHost.Controls.Add(advancedPanel, 0, 1)

        ' ================= Lồng tiếng nhiều giọng =================
        multiVoiceGroup = New GroupBox() With {.Text = "Lồng tiếng nhiều giọng", .Dock = DockStyle.Top, .Height = 240, .Visible = False}
        Dim multiLayout As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(10)}
        multiLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        multiLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        multiLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        multiVoiceGroup.Controls.Add(multiLayout)

        lblMultiHint = New Label() With {
            .AutoSize = True,
            .ForeColor = Drawing.Color.Gray,
            .Text = "Gắn nhãn [Tên] ở đầu mỗi dòng thoại trong file .srt để chỉ định giọng đọc, ví dụ:" & vbCrLf &
                    "[Nam] Xin chào các bạn.      [Nữ] Chào anh, khỏe không?" & vbCrLf &
                    "Dòng không gắn nhãn sẽ dùng giọng ""(mặc định)"" bên dưới.",
            .Margin = New Padding(0, 0, 0, 6)
        }
        multiLayout.Controls.Add(lblMultiHint, 0, 0)

        btnScanSpeakers = New Button() With {.Text = "🔍 Quét nhãn từ file SRT đã chọn", .AutoSize = True, .Margin = New Padding(0, 0, 0, 8)}
        AddHandler btnScanSpeakers.Click, AddressOf BtnScanSpeakers_Click
        multiLayout.Controls.Add(btnScanSpeakers, 0, 1)

        speakerScrollPanel = New Panel() With {.Dock = DockStyle.Fill, .AutoScroll = True, .BorderStyle = BorderStyle.FixedSingle}
        speakerMapPanel = New TableLayoutPanel() With {.Dock = DockStyle.Top, .ColumnCount = 3, .AutoSize = True, .Padding = New Padding(6)}
        speakerMapPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        speakerMapPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        speakerMapPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
        speakerScrollPanel.Controls.Add(speakerMapPanel)
        multiLayout.Controls.Add(speakerScrollPanel, 0, 2)

        ' ================= Tệp phụ đề & đầu ra =================
        Dim filesGroup As New GroupBox() With {.Text = "Tệp phụ đề & đầu ra", .Dock = DockStyle.Top, .Height = 150}
        Dim grid As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 3,
            .Padding = New Padding(10)
        }
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        filesGroup.Controls.Add(grid)

        txtSrt = New TextBox() With {.Dock = DockStyle.Fill}
        txtOutput = New TextBox() With {.Dock = DockStyle.Fill}
        txtEspeak = New TextBox() With {.Dock = DockStyle.Fill, .Text = "espeak-ng"}

        AddRow(grid, 0, "File phụ đề (.srt):", txtSrt, Sub() BrowseOpen(txtSrt, "Phụ đề SRT|*.srt|Tất cả tệp|*.*"))
        AddRow(grid, 1, "Tệp âm thanh xuất ra (.wav):", txtOutput, Sub() BrowseSave(txtOutput, "Tệp WAV|*.wav"))
        AddRow(grid, 2, "Đường dẫn espeak-ng.exe:", txtEspeak, Sub() BrowseOpen(txtEspeak, "espeak-ng.exe|espeak-ng.exe|Tất cả tệp|*.*"))

        btnInstallEspeak = New Button() With {.Text = "⬇ Cài đặt", .Width = 90, .Margin = New Padding(4, 2, 0, 2)}
        AddHandler btnInstallEspeak.Click, AddressOf BtnInstallEspeak_Click
        grid.Controls.Add(btnInstallEspeak, 3, 2)

        ' ================= Tuỳ chọn tốc độ =================
        Dim optionsPanel As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40, .Padding = New Padding(0, 6, 0, 4)}
        optionsPanel.Controls.Add(New Label() With {.Text = "Tốc độ đọc (length scale):", .AutoSize = True, .Padding = New Padding(0, 8, 6, 0)})
        numSpeed = New NumericUpDown() With {
            .DecimalPlaces = 2,
            .Increment = 0.05D,
            .Minimum = 0.3D,
            .Maximum = 3D,
            .Value = 1D,
            .Width = 70
        }
        optionsPanel.Controls.Add(numSpeed)
        optionsPanel.Controls.Add(New Label() With {.Text = "(1.0 = bình thường, >1 chậm hơn, <1 nhanh hơn)", .AutoSize = True, .Padding = New Padding(8, 8, 0, 0), .ForeColor = Drawing.Color.Gray})

        ' ================= Nút điều khiển =================
        Dim buttonPanel As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 40}
        btnStart = New Button() With {.Text = "▶  Bắt đầu chuyển đổi", .Width = 180, .Height = 32}
        AddHandler btnStart.Click, AddressOf BtnStart_Click
        btnCancel = New Button() With {.Text = "Hủy", .Width = 90, .Height = 32, .Enabled = False}
        AddHandler btnCancel.Click, AddressOf BtnCancel_Click
        buttonPanel.Controls.Add(btnStart)
        buttonPanel.Controls.Add(btnCancel)

        ' ================= Log =================
        Dim logGroup As New GroupBox() With {.Text = "Nhật ký xử lý", .Dock = DockStyle.Fill}
        txtLog = New TextBox() With {
            .Multiline = True,
            .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical,
            .Dock = DockStyle.Fill,
            .Font = New Drawing.Font("Consolas", 9)
        }
        logGroup.Controls.Add(txtLog)

        ' ================= Thanh trạng thái =================
        Dim statusPanel As New TableLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 50, .ColumnCount = 1}
        progressBar = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 20, .Minimum = 0, .Maximum = 100}
        lblStatus = New Label() With {.Dock = DockStyle.Top, .Text = "Sẵn sàng.", .Height = 22, .Padding = New Padding(2)}
        statusPanel.Controls.Add(progressBar, 0, 0)
        statusPanel.Controls.Add(lblStatus, 0, 1)

        root.RowCount = 7
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 0: modeGroup
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 1: voiceGroup / multiVoiceGroup
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 2: advancedHost
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 3: filesGroup
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 4: optionsPanel
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))     ' 5: buttonPanel
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100)) ' 6: logGroup

        root.Controls.Add(modeGroup, 0, 0)
        root.Controls.Add(voiceGroup, 0, 1)
        root.Controls.Add(multiVoiceGroup, 0, 1) ' cùng ô với voiceGroup, chỉ 1 cái Visible tại 1 thời điểm
        root.Controls.Add(advancedHost, 0, 2)
        root.Controls.Add(filesGroup, 0, 3)
        root.Controls.Add(optionsPanel, 0, 4)
        root.Controls.Add(buttonPanel, 0, 5)
        root.Controls.Add(logGroup, 0, 6)

        Controls.Add(statusPanel)
        statusPanel.BringToFront()
    End Sub

    Private Sub AddRow(grid As TableLayoutPanel, row As Integer, label As String, box As TextBox, onBrowse As Action)
        grid.Controls.Add(New Label() With {.Text = label, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 8, 6, 0)}, 0, row)
        grid.Controls.Add(box, 1, row)
        Dim btn As New Button() With {.Text = "...", .Width = 36}
        AddHandler btn.Click, Sub() onBrowse()
        grid.Controls.Add(btn, 2, row)
    End Sub

    ' ================= Chuyển đổi chế độ =================
    Private Sub Mode_CheckedChanged(sender As Object, e As EventArgs)
        voiceGroup.Visible = rbSingleVoice.Checked
        advancedHost.Visible = rbSingleVoice.Checked
        multiVoiceGroup.Visible = rbMultiVoice.Checked
    End Sub

    ' ================= Thư viện giọng: nạp / làm mới =================
    Private Sub LoadVoices()
        _allVoices = VoiceLibrary.ScanVoices(_dataRoot)

        cmbCategory.Items.Clear()
        cmbVoice.Items.Clear()

        If _allVoices.Count = 0 Then
            lblVoiceHint.Text = $"Không tìm thấy giọng nào trong ""{_dataRoot}"". Hãy tạo thư mục Data\Male hoặc Data\Female và đặt file .onnx + .json vào đó, rồi bấm Làm mới. (Tạm thời có thể dùng chế độ tùy chỉnh thủ công bên dưới.)"
            lblVoiceHint.ForeColor = Drawing.Color.DarkRed
            cmbCategory.Enabled = False
            cmbVoice.Enabled = False
            chkAdvanced.Checked = True
            Return
        End If

        lblVoiceHint.ForeColor = Drawing.Color.Gray
        lblVoiceHint.Text = $"Đã tìm thấy {_allVoices.Count} giọng trong ""{_dataRoot}"". Đặt thêm file .onnx + .json vào các thư mục con để mở rộng."
        cmbCategory.Enabled = Not chkAdvanced.Checked
        cmbVoice.Enabled = Not chkAdvanced.Checked

        Dim categories = _allVoices.Select(Function(v) v.Category).Distinct().OrderBy(Function(c) c).ToList()
        For Each cat In categories
            cmbCategory.Items.Add(New CategoryItem With {.Display = VoiceLibrary.TranslateCategory(cat), .Raw = cat})
        Next
        If cmbCategory.Items.Count > 0 Then cmbCategory.SelectedIndex = 0
    End Sub

    Private Sub CmbCategory_SelectedIndexChanged(sender As Object, e As EventArgs)
        cmbVoice.Items.Clear()
        Dim selected = TryCast(cmbCategory.SelectedItem, CategoryItem)
        If selected Is Nothing Then Return

        Dim voicesInCategory = _allVoices.Where(Function(v) v.Category = selected.Raw).OrderBy(Function(v) v.Name).ToList()
        For Each v In voicesInCategory
            cmbVoice.Items.Add(v)
        Next
        If cmbVoice.Items.Count > 0 Then cmbVoice.SelectedIndex = 0
    End Sub

    Private Sub ChkAdvanced_CheckedChanged(sender As Object, e As EventArgs)
        advancedPanel.Visible = chkAdvanced.Checked
        cmbCategory.Enabled = Not chkAdvanced.Checked AndAlso _allVoices.Count > 0
        cmbVoice.Enabled = Not chkAdvanced.Checked AndAlso _allVoices.Count > 0
    End Sub

    ' ================= Lồng tiếng nhiều giọng: quét nhãn =================
    Private Sub BtnScanSpeakers_Click(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(txtSrt.Text) OrElse Not File.Exists(txtSrt.Text) Then
            MessageBox.Show(Me, "Vui lòng chọn file .srt trước khi quét nhãn.", "Thiếu tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If _allVoices.Count = 0 Then
            MessageBox.Show(Me, "Chưa có giọng nào trong thư viện (thư mục Data). Hãy thêm giọng rồi bấm Làm mới trước.", "Thiếu giọng đọc", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim cues As List(Of SrtCue)
        Try
            cues = SrtParser.ParseFile(txtSrt.Text)
        Catch ex As Exception
            MessageBox.Show(Me, "Không đọc được file .srt: " & ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim speakers = SrtParser.GetDistinctSpeakers(cues)
        If Not speakers.Contains("") Then speakers.Insert(0, "")

        ' Giữ lại lựa chọn cũ (nếu nhãn vẫn còn) khi quét lại
        Dim previousSelections As New Dictionary(Of String, VoiceInfo)
        For Each kv In _speakerRows
            If kv.Value.SelectedVoice IsNot Nothing Then previousSelections(kv.Key) = kv.Value.SelectedVoice
        Next

        Dim categories = _allVoices.Select(Function(v) v.Category).Distinct().OrderBy(Function(c) c).ToList()

        speakerMapPanel.Controls.Clear()
        speakerMapPanel.RowStyles.Clear()
        _speakerRows.Clear()

        ' Hàng tiêu đề
        speakerMapPanel.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        speakerMapPanel.Controls.Add(New Label() With {.Text = "Nhãn", .AutoSize = True, .Font = New Drawing.Font(Font, Drawing.FontStyle.Bold)}, 0, 0)
        speakerMapPanel.Controls.Add(New Label() With {.Text = "Loại giọng", .AutoSize = True, .Font = New Drawing.Font(Font, Drawing.FontStyle.Bold)}, 1, 0)
        speakerMapPanel.Controls.Add(New Label() With {.Text = "Giọng đọc", .AutoSize = True, .Font = New Drawing.Font(Font, Drawing.FontStyle.Bold)}, 2, 0)

        Dim row = 1
        For Each spk In speakers
            speakerMapPanel.RowStyles.Add(New RowStyle(SizeType.AutoSize))

            Dim displayLabel = If(spk = "", "(Không gắn nhãn / mặc định)", spk)
            Dim lbl As New Label() With {.Text = displayLabel, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(4, 8, 6, 4)}
            speakerMapPanel.Controls.Add(lbl, 0, row)

            Dim catCombo As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150, .Margin = New Padding(0, 4, 4, 4)}
            Dim voiceCombo As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220, .Margin = New Padding(0, 4, 4, 4)}

            For Each cat In categories
                catCombo.Items.Add(New CategoryItem With {.Display = VoiceLibrary.TranslateCategory(cat), .Raw = cat})
            Next

            ' Đoán loại giọng mặc định: khớp tên nhãn với "Nam"/"Nữ"/tên thư mục, hoặc theo lựa chọn cũ, hoặc lấy loại đầu tiên
            Dim prevVoice As VoiceInfo = Nothing
            previousSelections.TryGetValue(spk, prevVoice)

            Dim defaultCatIndex = 0
            If prevVoice IsNot Nothing Then
                Dim idx = categories.IndexOf(prevVoice.Category)
                If idx >= 0 Then defaultCatIndex = idx
            Else
                Dim guess = categories.FindIndex(Function(c) String.Equals(VoiceLibrary.TranslateCategory(c), spk, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(c, spk, StringComparison.OrdinalIgnoreCase))
                If guess >= 0 Then defaultCatIndex = guess
            End If

            ' Cascading: khi đổi loại giọng thì nạp lại danh sách giọng đọc tương ứng
            Dim capturedVoiceCombo = voiceCombo
            Dim capturedSpeaker = spk
            AddHandler catCombo.SelectedIndexChanged, Sub(s2, e2) RefreshVoiceCombo(catCombo, capturedVoiceCombo, capturedSpeaker, previousSelections)

            speakerMapPanel.Controls.Add(catCombo, 1, row)
            speakerMapPanel.Controls.Add(voiceCombo, 2, row)

            If catCombo.Items.Count > 0 Then catCombo.SelectedIndex = defaultCatIndex ' kích hoạt cascading nạp voiceCombo

            _speakerRows(spk) = New SpeakerRow With {.CategoryCombo = catCombo, .VoiceCombo = voiceCombo}
            row += 1
        Next

        Dim taggedCount = speakers.Where(Function(s) s <> "").Count()
        Log($"Đã quét được {taggedCount} nhãn giọng khác nhau trong file srt (chưa kể mặc định).")
    End Sub

    ' Nạp lại danh sách giọng đọc cụ thể khi người dùng đổi "Loại giọng" ở 1 dòng gán nhãn
    Private Sub RefreshVoiceCombo(catCombo As ComboBox, voiceCombo As ComboBox, speaker As String, previousSelections As Dictionary(Of String, VoiceInfo))
        voiceCombo.Items.Clear()
        Dim selectedCat = TryCast(catCombo.SelectedItem, CategoryItem)
        If selectedCat Is Nothing Then Return

        Dim voicesInCategory = _allVoices.Where(Function(v) v.Category = selectedCat.Raw).OrderBy(Function(v) v.Name).ToList()
        For Each v In voicesInCategory
            voiceCombo.Items.Add(v)
        Next

        Dim chosenIndex = 0
        Dim prevVoice As VoiceInfo = Nothing
        If previousSelections.TryGetValue(speaker, prevVoice) Then
            Dim idx = voicesInCategory.FindIndex(Function(v) v.OnnxPath = prevVoice.OnnxPath)
            If idx >= 0 Then chosenIndex = idx
        End If
        If voiceCombo.Items.Count > 0 Then voiceCombo.SelectedIndex = chosenIndex
    End Sub

    Private Sub PrefillDefaultPaths()
        Dim baseDir = AppContext.BaseDirectory
        txtOutput.Text = Path.Combine(baseDir, "output.wav")
    End Sub

    Private Sub BrowseOpen(target As TextBox, filter As String)
        Using dlg As New OpenFileDialog() With {.Filter = filter}
            If dlg.ShowDialog() = DialogResult.OK Then target.Text = dlg.FileName
        End Using
    End Sub

    Private Sub BrowseSave(target As TextBox, filter As String)
        Using dlg As New SaveFileDialog() With {.Filter = filter, .FileName = "output.wav"}
            If dlg.ShowDialog() = DialogResult.OK Then target.Text = dlg.FileName
        End Using
    End Sub

    Private Async Sub BtnInstallEspeak_Click(sender As Object, e As EventArgs)
        btnInstallEspeak.Enabled = False
        SetStatus("Đang tìm bản cài đặt eSpeak NG mới nhất...")
        Try
            Using http As New HttpClient()
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SrtToSpeechApp/1.0")
                http.Timeout = TimeSpan.FromMinutes(3)

                Log("Đang kiểm tra bản phát hành mới nhất từ GitHub (espeak-ng)...")
                Dim releaseJson = Await http.GetStringAsync("https://api.github.com/repos/espeak-ng/espeak-ng/releases/latest")

                Dim assetName As String = Nothing
                Dim downloadUrl As String = Nothing
                Using doc = JsonDocument.Parse(releaseJson)
                    Dim assets = doc.RootElement.GetProperty("assets")
                    ' Ưu tiên file .msi (bộ cài Windows chính thức); nếu không có thì lấy .exe
                    For Each preferredExt In {".msi", ".exe"}
                        For Each asset In assets.EnumerateArray()
                            Dim name = asset.GetProperty("name").GetString()
                            If name.EndsWith(preferredExt, StringComparison.OrdinalIgnoreCase) Then
                                assetName = name
                                downloadUrl = asset.GetProperty("browser_download_url").GetString()
                                Exit For
                            End If
                        Next
                        If assetName IsNot Nothing Then Exit For
                    Next
                End Using

                If assetName Is Nothing Then
                    Log("Không tìm thấy file cài đặt (.msi/.exe) trong bản phát hành mới nhất.")
                    Log("Vui lòng tải thủ công tại: https://github.com/espeak-ng/espeak-ng/releases")
                    Return
                End If

                Log($"Đang tải {assetName} ...")
                Dim tempFile = Path.Combine(Path.GetTempPath(), assetName)
                Dim bytes = Await http.GetByteArrayAsync(downloadUrl)
                File.WriteAllBytes(tempFile, bytes)
                Log("Tải xong. Đang chạy trình cài đặt (có thể hiện hộp thoại xin quyền Quản trị viên, vui lòng bấm Yes/Có)...")

                SetStatus("Đang cài đặt eSpeak NG...")
                Dim exitCode As Integer
                If assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) Then
                    Dim psi As New ProcessStartInfo("msiexec.exe") With {
                        .Arguments = $"/i ""{tempFile}"" /qn /norestart",
                        .UseShellExecute = True,
                        .Verb = "runas"
                    }
                    Using proc = Process.Start(psi)
                        Await Task.Run(Sub() proc.WaitForExit())
                        exitCode = proc.ExitCode
                    End Using
                Else
                    Dim psi As New ProcessStartInfo(tempFile) With {
                        .Arguments = "/S",
                        .UseShellExecute = True,
                        .Verb = "runas"
                    }
                    Using proc = Process.Start(psi)
                        Await Task.Run(Sub() proc.WaitForExit())
                        exitCode = proc.ExitCode
                    End Using
                End If

                If exitCode <> 0 Then
                    Log($"Cài đặt kết thúc với mã lỗi {exitCode} (có thể bạn đã hủy hộp thoại UAC). Hãy thử lại hoặc cài thủ công.")
                    Return
                End If

                Log("Cài đặt thành công. Đang tìm đường dẫn espeak-ng.exe...")
                Dim candidates() As String = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "eSpeak NG", "espeak-ng.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "eSpeak NG", "espeak-ng.exe")
                }
                Dim found = candidates.FirstOrDefault(Function(p) File.Exists(p))
                If found IsNot Nothing Then
                    txtEspeak.Text = found
                    Log("Đã tự động điền đường dẫn: " & found)
                    SetStatus("Đã cài đặt eSpeak NG thành công.")
                Else
                    Log("Cài đặt xong nhưng không tìm thấy espeak-ng.exe ở vị trí mặc định.")
                    Log("Vui lòng bấm nút '...' cạnh ô đường dẫn để chọn thủ công.")
                    SetStatus("Cài đặt xong, cần chọn đường dẫn thủ công.")
                End If
            End Using
        Catch ex As Exception
            Log("Lỗi khi tải/cài đặt eSpeak NG: " & ex.Message)
            SetStatus("Cài đặt eSpeak NG thất bại.")
        Finally
            btnInstallEspeak.Enabled = True
        End Try
    End Sub

    Private Sub Log(msg As String)
        If txtLog.InvokeRequired Then
            txtLog.Invoke(Sub() Log(msg))
            Return
        End If
        txtLog.AppendText(msg & Environment.NewLine)
    End Sub

    Private Sub SetStatus(msg As String, Optional percent As Integer? = Nothing)
        If InvokeRequired Then
            Invoke(Sub() SetStatus(msg, percent))
            Return
        End If
        lblStatus.Text = msg
        If percent.HasValue Then progressBar.Value = Math.Max(0, Math.Min(100, percent.Value))
    End Sub

    Private Async Sub BtnStart_Click(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(txtSrt.Text) OrElse Not File.Exists(txtSrt.Text) Then
            MessageBox.Show(Me, "Vui lòng chọn 1 file .srt hợp lệ.", "Thiếu tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If String.IsNullOrWhiteSpace(txtOutput.Text) Then
            MessageBox.Show(Me, "Vui lòng chọn nơi lưu file .wav xuất ra.", "Thiếu tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim speakerVoiceMap As Dictionary(Of String, VoiceInfo) = Nothing
        Dim singleModelPath As String = Nothing
        Dim singleConfigPath As String = Nothing

        If rbMultiVoice.Checked Then
            If _speakerRows.Count = 0 Then
                MessageBox.Show(Me, "Vui lòng bấm ""Quét nhãn từ file SRT"" và gán giọng cho từng nhãn trước.", "Chưa quét nhãn", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            speakerVoiceMap = New Dictionary(Of String, VoiceInfo)
            For Each kv In _speakerRows
                Dim info = kv.Value.SelectedVoice
                If info Is Nothing Then
                    Dim lbl = If(kv.Key = "", "(mặc định)", kv.Key)
                    MessageBox.Show(Me, $"Vui lòng chọn giọng cho nhãn ""{lbl}"".", "Thiếu lựa chọn", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                speakerVoiceMap(kv.Key) = info
            Next
        Else
            If chkAdvanced.Checked Then
                singleModelPath = txtModel.Text
                singleConfigPath = txtConfig.Text
                If String.IsNullOrWhiteSpace(singleModelPath) OrElse Not File.Exists(singleModelPath) Then
                    MessageBox.Show(Me, "Vui lòng chọn file .onnx hợp lệ (chế độ tùy chỉnh thủ công).", "Thiếu tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                If String.IsNullOrWhiteSpace(singleConfigPath) OrElse Not File.Exists(singleConfigPath) Then
                    MessageBox.Show(Me, "Vui lòng chọn file .onnx.json hợp lệ (chế độ tùy chỉnh thủ công).", "Thiếu tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
            Else
                Dim voice = TryCast(cmbVoice.SelectedItem, VoiceInfo)
                If voice Is Nothing Then
                    MessageBox.Show(Me, "Vui lòng chọn 1 giọng đọc trong thư viện, hoặc bật chế độ tùy chỉnh thủ công.", "Thiếu giọng đọc", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                singleModelPath = voice.OnnxPath
                singleConfigPath = voice.ConfigPath
            End If
        End If

        txtLog.Clear()
        progressBar.Value = 0
        btnStart.Enabled = False
        btnCancel.Enabled = True

        _cts = New CancellationTokenSource()
        Dim token = _cts.Token

        Dim srtPath = txtSrt.Text
        Dim outputPath = txtOutput.Text
        Dim espeakPath = If(String.IsNullOrWhiteSpace(txtEspeak.Text), "espeak-ng", txtEspeak.Text)
        Dim speed = CSng(numSpeed.Value)

        Try
            Await Task.Run(Sub() RunConversion(srtPath, singleModelPath, singleConfigPath, speakerVoiceMap, outputPath, espeakPath, speed, token), token)
            If Not token.IsCancellationRequested Then
                SetStatus("Hoàn tất.", 100)
                Log("")
                Log("File kết quả: " & Path.GetFullPath(outputPath))
                MessageBox.Show(Me, "Chuyển đổi hoàn tất!" & Environment.NewLine & outputPath, "Xong", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        Catch ex As OperationCanceledException
            SetStatus("Đã hủy.")
            Log("Đã hủy theo yêu cầu người dùng.")
        Catch ex As Exception
            SetStatus("Lỗi.")
            Log("LỖI: " & ex.Message)
            MessageBox.Show(Me, ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            btnStart.Enabled = True
            btnCancel.Enabled = False
            _cts = Nothing
        End Try
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As EventArgs)
        _cts?.Cancel()
    End Sub

    ''' <summary>
    ''' Chạy trên luồng nền. Nếu speakerVoiceMap khác Nothing thì chạy chế độ lồng tiếng
    ''' nhiều giọng (mỗi nhãn dùng 1 giọng riêng); ngược lại dùng singleModelPath/ConfigPath
    ''' cho toàn bộ file.
    ''' </summary>
    Private Sub RunConversion(srtPath As String, singleModelPath As String, singleConfigPath As String,
                               speakerVoiceMap As Dictionary(Of String, VoiceInfo), outputPath As String,
                               espeakPath As String, speed As Single, token As CancellationToken)

        SetStatus("Đang đọc file phụ đề...", 0)
        Dim cues = SrtParser.ParseFile(srtPath)
        Log($"Tìm thấy {cues.Count} câu thoại.")

        Dim voiceCache As New Dictionary(Of String, PiperVoice)
        Dim clips As New List(Of (StartMs As Long, Samples As Single()))
        Dim skipped = 0
        Dim commonSampleRate As Integer = 22050

        Try
            If speakerVoiceMap Is Nothing Then
                SetStatus("Đang nạp mô hình giọng nói...", 2)
                Log("Model : " & singleModelPath)
                Log("Config: " & singleConfigPath)
                Dim v As New PiperVoice(singleModelPath, singleConfigPath)
                v.EspeakPath = espeakPath
                voiceCache(singleModelPath) = v
                commonSampleRate = v.SampleRate
            Else
                Log("Chế độ lồng tiếng nhiều giọng - danh sách giọng đã gán:")
                For Each kv In speakerVoiceMap
                    Dim lbl = If(kv.Key = "", "(mặc định)", kv.Key)
                    Log($"  [{lbl}] -> {kv.Value.Name}")
                Next
            End If

            For idx = 0 To cues.Count - 1
                token.ThrowIfCancellationRequested()
                Dim cue = cues(idx)
                Dim percent = CInt((idx + 1) / CDbl(Math.Max(1, cues.Count)) * 88) + 2
                SetStatus($"Đang tổng hợp câu {idx + 1}/{cues.Count}...", percent)

                Try
                    Dim voice As PiperVoice = Nothing

                    If speakerVoiceMap Is Nothing Then
                        voice = voiceCache(singleModelPath)
                    Else
                        Dim info As VoiceInfo = Nothing
                        If Not speakerVoiceMap.TryGetValue(cue.Speaker, info) Then
                            speakerVoiceMap.TryGetValue("", info) ' rơi về giọng mặc định nếu nhãn lạ
                        End If
                        If info Is Nothing Then
                            Log($"[{idx + 1}/{cues.Count}] Bỏ qua: không tìm thấy giọng cho nhãn ""{cue.Speaker}""")
                            skipped += 1
                            Continue For
                        End If

                        If Not voiceCache.ContainsKey(info.OnnxPath) Then
                            Log($"Đang nạp giọng ""{info.Name}""...")
                            Dim newVoice As New PiperVoice(info.OnnxPath, info.ConfigPath)
                            newVoice.EspeakPath = espeakPath
                            voiceCache(info.OnnxPath) = newVoice
                            commonSampleRate = newVoice.SampleRate
                        End If
                        voice = voiceCache(info.OnnxPath)
                    End If

                    Dim samples = voice.Synthesize(cue.Text, speed)
                    If samples.Length = 0 Then
                        Log($"[{idx + 1}/{cues.Count}] ({cue.Start:hh\:mm\:ss}) {Truncate(cue.Text, 45)} -> bỏ qua (không tạo được âm thanh)")
                        skipped += 1
                        Continue For
                    End If

                    clips.Add((CLng(cue.Start.TotalMilliseconds), samples))

                    Dim synthDurationMs = samples.Length / CDbl(voice.SampleRate) * 1000.0
                    Dim warn = ""
                    If synthDurationMs > cue.Duration.TotalMilliseconds + 200 Then
                        warn = "  ⚠ dài hơn thời lượng phụ đề, có thể chồng lấn câu kế tiếp"
                    End If
                    Dim spkTag = If(cue.Speaker = "", "", $"[{cue.Speaker}] ")
                    Log($"[{idx + 1}/{cues.Count}] ({cue.Start:hh\:mm\:ss}) {spkTag}{Truncate(cue.Text, 45)} -> ok ({synthDurationMs:F0} ms){warn}")
                Catch ex As Exception
                    Log($"[{idx + 1}/{cues.Count}] LỖI: {ex.Message}")
                    skipped += 1
                End Try
            Next

            token.ThrowIfCancellationRequested()
            SetStatus("Đang ghép các đoạn âm thanh...", 92)
            Dim canvas = WavFile.BuildCanvas(clips, commonSampleRate)

            SetStatus("Đang ghi file wav...", 96)
            WavFile.WritePcm16(outputPath, canvas, commonSampleRate)

            Log("")
            Log($"Hoàn tất. Đã tổng hợp {clips.Count}/{cues.Count} câu (bỏ qua {skipped}).")
        Finally
            For Each v In voiceCache.Values
                v.Dispose()
            Next
        End Try
    End Sub

    Private Function Truncate(s As String, len As Integer) As String
        If s.Length <= len Then Return s
        Return s.Substring(0, len) & "..."
    End Function

End Class

