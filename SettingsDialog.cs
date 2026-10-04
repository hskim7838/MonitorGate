using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MonitorGate
{
    // This dialog collects choices only. The caller saves preferences and startup registration.
    internal sealed class SettingsDialog : Form
    {
        private readonly ComboBox transferKey;
        private readonly CheckBox startup;
        private readonly CheckBox startEnabled;
        private readonly CheckBox showOverlay;
        private readonly Panel backgroundSwatch;
        private readonly Panel foregroundSwatch;
        private readonly Label backgroundHex;
        private readonly Label foregroundHex;
        private readonly OverlayColorPreview overlayPreview;
        private readonly ComboBox fontName;
        private readonly NumericUpDown fontSize;
        private readonly CheckBox fontBold;
        private readonly CheckBox autoBoxSize;
        private readonly NumericUpDown boxWidth;
        private readonly NumericUpDown boxHeight;
        private readonly Label boxSizeLabel;
        private readonly Panel previewViewport;
        private Color overlayBackground;
        private Color overlayForeground;
        private bool updatingAppearance;
        private bool previewUpdateQueued;
        private readonly TransferKey[] transferKeys = new TransferKey[]
        {
            TransferKey.Ctrl, TransferKey.Alt, TransferKey.Shift, TransferKey.RightCtrl
        };

        internal UserPreferences ResultPreferences { get; private set; }
        internal bool ResultStartupEnabled { get; private set; }
        internal bool ChangedStartup { get; private set; }

        internal SettingsDialog(UserPreferences preferences, StartupStatus startupStatus)
        {
            if (preferences == null) throw new ArgumentNullException("preferences");
            Text = "MonitorGate 설정";
            Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleDimensions = new SizeF(7f, 17f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, startupStatus == StartupStatus.OtherLocation ? 846 : 800);

            // Keep all choices local until SaveChoices creates a new preferences object.
            overlayBackground = OpaqueColor(preferences.OverlayBackground);
            overlayForeground = OpaqueColor(preferences.OverlayForeground);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.AutoScroll = true;
            Controls.Add(content);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.MinimumSize = new Size(568, 0);
            layout.Padding = new Padding(16);
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 18;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                startupStatus == StartupStatus.OtherLocation ? 46 : 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            content.Controls.Add(layout);

            Label transferLabel = new Label();
            transferLabel.Text = "모니터 이동 키";
            transferLabel.AutoSize = true;
            transferLabel.Anchor = AnchorStyles.Left;
            layout.Controls.Add(transferLabel, 0, 0);
            transferKey = new ComboBox();
            transferKey.DropDownStyle = ComboBoxStyle.DropDownList;
            transferKey.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            foreach (TransferKey key in transferKeys) transferKey.Items.Add(KeysForTransfer.Label(key));
            transferKey.SelectedIndex = Math.Max(0, Array.IndexOf(transferKeys, preferences.TransferKey));
            layout.Controls.Add(transferKey, 1, 0);

            startup = AddCheckBox(layout, "Windows 로그인 시 자동 실행", 1);
            startup.Checked = startupStatus != StartupStatus.Missing;
            // Attach after initialization; opening or canceling the dialog changes no registration.
            startup.CheckedChanged += delegate { ChangedStartup = true; };
            startEnabled = AddCheckBox(layout, "실행할 때 포인터 제한 켜기", 2);
            startEnabled.Checked = preferences.StartEnabled;
            showOverlay = AddCheckBox(layout, "이동 상태 박스 표시", 3);
            showOverlay.Checked = preferences.ShowOverlay;

            AddColorChoice(layout, "상태 박스 배경색", 4, delegate { ChooseOverlayColor(true); },
                out backgroundSwatch, out backgroundHex);
            AddColorChoice(layout, "상태 박스 글자색", 5, delegate { ChooseOverlayColor(false); },
                out foregroundSwatch, out foregroundHex);

            AddFieldLabel(layout, "상태 박스 글꼴", 6);
            fontName = new ComboBox();
            fontName.DropDownStyle = ComboBoxStyle.DropDownList;
            fontName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            fontName.Items.AddRange(OverlayAppearance.AvailableFontNames());
            SelectFont(OverlayAppearance.ResolveFontName(preferences.OverlayFontName));
            layout.Controls.Add(fontName, 1, 6);

            AddFieldLabel(layout, "글자 크기 / 굵기", 7);
            FlowLayoutPanel fontChoices = new FlowLayoutPanel();
            fontChoices.Dock = DockStyle.Fill;
            fontChoices.WrapContents = false;
            fontChoices.Margin = new Padding(3, 0, 3, 0);
            fontSize = MakeNumber(6, 48, SafeFontSize(preferences.OverlayFontSize), 100);
            fontSize.DecimalPlaces = 2;
            fontSize.Increment = 0.25m;
            fontSize.AccessibleName = "상태 박스 글자 크기 (포인트)";
            fontChoices.Controls.Add(fontSize);
            Label pointUnit = new Label();
            pointUnit.Text = "pt";
            pointUnit.AutoSize = true;
            pointUnit.Margin = new Padding(3, 7, 16, 0);
            fontChoices.Controls.Add(pointUnit);
            fontBold = new CheckBox();
            fontBold.Text = "굵게";
            fontBold.AutoSize = true;
            fontBold.Checked = preferences.OverlayFontBold;
            fontBold.Margin = new Padding(3, 5, 3, 0);
            fontChoices.Controls.Add(fontBold);
            layout.Controls.Add(fontChoices, 1, 7);

            autoBoxSize = AddCheckBox(layout, "글자에 맞춰 박스 크기 자동 조절", 8);
            autoBoxSize.Checked = preferences.OverlayAutoSize;
            AddFieldLabel(layout, "직접 지정 크기", 9);
            FlowLayoutPanel dimensions = new FlowLayoutPanel();
            dimensions.Dock = DockStyle.Fill;
            dimensions.WrapContents = false;
            dimensions.Margin = new Padding(3, 0, 3, 0);
            AddNumberLabel(dimensions, "가로(px)", 0);
            boxWidth = MakeNumber(48, 1600, preferences.OverlayWidth, 90);
            boxWidth.AccessibleName = "상태 박스 가로 크기 (96 DPI 픽셀)";
            dimensions.Controls.Add(boxWidth);
            AddNumberLabel(dimensions, "세로(px)", 12);
            boxHeight = MakeNumber(20, 400, preferences.OverlayHeight, 90);
            boxHeight.AccessibleName = "상태 박스 세로 크기 (96 DPI 픽셀)";
            dimensions.Controls.Add(boxHeight);
            layout.Controls.Add(dimensions, 1, 9);

            AddFieldLabel(layout, "적용될 박스 크기", 10);
            boxSizeLabel = new Label();
            boxSizeLabel.AutoSize = true;
            boxSizeLabel.Anchor = AnchorStyles.Left;
            layout.Controls.Add(boxSizeLabel, 1, 10);
            Label previewLabel = new Label();
            previewLabel.Text = "실제 크기 미리보기 (큰 박스는 스크롤)";
            previewLabel.AutoSize = true;
            previewLabel.Anchor = AnchorStyles.Left;
            layout.Controls.Add(previewLabel, 0, 11);
            layout.SetColumnSpan(previewLabel, 2);
            previewViewport = new Panel();
            previewViewport.Dock = DockStyle.Fill;
            previewViewport.AutoScroll = true;
            previewViewport.Padding = new Padding(8);
            previewViewport.BorderStyle = BorderStyle.FixedSingle;
            previewViewport.BackColor = SystemColors.ControlLight;
            layout.Controls.Add(previewViewport, 0, 12);
            layout.SetColumnSpan(previewViewport, 2);
            overlayPreview = new OverlayColorPreview();
            overlayPreview.Size = new Size(224, 28);
            overlayPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            overlayPreview.AccessibleName = "상태 박스 색상 미리보기";
            previewViewport.Controls.Add(overlayPreview);

            FlowLayoutPanel appearanceActions = new FlowLayoutPanel();
            appearanceActions.Dock = DockStyle.Fill;
            appearanceActions.WrapContents = false;
            appearanceActions.Margin = new Padding(0);
            Button resetColors = new Button();
            resetColors.Text = "기본 색상 복원";
            resetColors.AutoSize = true;
            resetColors.Anchor = AnchorStyles.Left;
            resetColors.Padding = new Padding(6, 0, 6, 0);
            resetColors.Click += delegate
            {
                overlayBackground = Color.Black;
                overlayForeground = Color.White;
                UpdateColorPreview();
            };
            appearanceActions.Controls.Add(resetColors);
            Button resetAppearance = new Button();
            resetAppearance.Text = "기본 모양 복원";
            resetAppearance.AutoSize = true;
            resetAppearance.Padding = new Padding(6, 0, 6, 0);
            resetAppearance.Click += ResetAppearance;
            appearanceActions.Controls.Add(resetAppearance);
            layout.Controls.Add(appearanceActions, 0, 13);
            layout.SetColumnSpan(appearanceActions, 2);
            AddNote(layout, "작은 박스는 글자에 맞춰 늘립니다. 화면보다 큰 박스는 화면 안에 맞춥니다.", 14);

            AddNote(layout,
                "컴퓨터를 켠 뒤 Windows에 로그인할 때 실행됩니다.\r\n" +
                "Windows ‘시작 앱’에서 끄면 자동 실행되지 않습니다.", 15);
            if (startupStatus == StartupStatus.OtherLocation)
                AddNote(layout,
                    "다른 위치의 실행 파일이 등록되어 있습니다.\r\n" +
                    "자동 실행을 껐다 켜면 현재 위치로 갱신됩니다.", 16);

            Button startupApps = new Button();
            startupApps.Text = "Windows 시작 앱 설정 열기";
            startupApps.AutoSize = true;
            startupApps.Anchor = AnchorStyles.Left;
            startupApps.Padding = new Padding(6, 0, 6, 0);
            startupApps.Click += OpenStartupApps;
            layout.Controls.Add(startupApps, 0, 17);
            layout.SetColumnSpan(startupApps, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 50;
            buttons.Padding = new Padding(16, 0, 16, 12);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            Button cancel = new Button();
            cancel.Text = "취소";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Size = new Size(80, 30);
            cancel.Margin = new Padding(8, 3, 0, 0);
            Button save = new Button();
            save.Text = "저장";
            save.Size = new Size(80, 30);
            save.Margin = new Padding(8, 3, 0, 0);
            save.Click += SaveChoices;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;
            fontName.SelectedIndexChanged += AppearanceChanged;
            fontSize.ValueChanged += AppearanceChanged;
            fontBold.CheckedChanged += AppearanceChanged;
            autoBoxSize.CheckedChanged += AppearanceChanged;
            boxWidth.ValueChanged += AppearanceChanged;
            boxHeight.ValueChanged += AppearanceChanged;
            UpdateColorPreview();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClampToWorkingArea();
            UpdateColorPreview();
        }

        private void ClampToWorkingArea()
        {
            // Keep the actions visible even when a large DPI scale exceeds the work area.
            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, Math.Max(1, workArea.Width - 24)),
                Math.Min(Height, Math.Max(1, workArea.Height - 24)));
            Location = new System.Drawing.Point(
                Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width)),
                Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height)));
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            QueuePreviewUpdate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x02E0 /* WM_DPICHANGED */) QueuePreviewUpdate();
        }

        private void QueuePreviewUpdate()
        {
            if (overlayPreview == null || previewUpdateQueued || IsDisposed || Disposing || !IsHandleCreated) return;
            previewUpdateQueued = true;
            // Child-window DPI and automatic layout finish after the native message.
            // Re-measure then so the preview does not retain the previous monitor's scale.
            try
            {
                BeginInvoke((Action)delegate
                {
                    previewUpdateQueued = false;
                    if (IsDisposed || Disposing) return;
                    ClampToWorkingArea();
                    UpdateColorPreview();
                });
            }
            catch (InvalidOperationException) { previewUpdateQueued = false; }
        }

        private static void AddFieldLabel(TableLayoutPanel layout, string text, int row)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            layout.Controls.Add(label, 0, row);
        }

        private static void AddNumberLabel(FlowLayoutPanel panel, string text, int leadingMargin)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(leadingMargin, 7, 6, 0);
            panel.Controls.Add(label);
        }

        private static NumericUpDown MakeNumber(decimal minimum, decimal maximum,
            decimal value, int width)
        {
            NumericUpDown number = new NumericUpDown();
            number.Minimum = minimum;
            number.Maximum = maximum;
            number.Value = Math.Max(minimum, Math.Min(maximum, value));
            number.Width = width;
            number.Margin = new Padding(3, 3, 3, 0);
            number.ThousandsSeparator = false;
            return number;
        }

        private static decimal SafeFontSize(float size)
        {
            if (Single.IsNaN(size) || Single.IsInfinity(size)) return 9.75m;
            return Math.Max(6m, Math.Min(48m, (decimal)Math.Max(6f, Math.Min(48f, size))));
        }

        private void SelectFont(string name)
        {
            for (int index = 0; index < fontName.Items.Count; index++)
            {
                if (String.Equals(name, (string)fontName.Items[index], StringComparison.OrdinalIgnoreCase))
                {
                    fontName.SelectedIndex = index;
                    return;
                }
            }
            // ResolveFontName returns an installed fallback even if a saved font was removed.
            fontName.SelectedIndex = fontName.Items.Add(name);
        }

        private static void AddColorChoice(TableLayoutPanel layout, string text, int row,
            EventHandler chooseColor, out Panel swatch, out Label hex)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            layout.Controls.Add(label, 0, row);

            TableLayoutPanel choice = new TableLayoutPanel();
            choice.Dock = DockStyle.Fill;
            choice.Margin = new Padding(3, 4, 3, 4);
            choice.RowCount = 1;
            choice.ColumnCount = 3;
            choice.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            choice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            choice.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            choice.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(choice, 1, row);

            swatch = new Panel();
            swatch.Size = new Size(24, 24);
            swatch.BorderStyle = BorderStyle.FixedSingle;
            swatch.Anchor = AnchorStyles.Left;
            swatch.Margin = new Padding(0);
            swatch.AccessibleName = text + " 색상";
            choice.Controls.Add(swatch, 0, 0);

            hex = new Label();
            hex.AutoSize = true;
            hex.Anchor = AnchorStyles.Left;
            hex.Margin = new Padding(3, 0, 3, 0);
            hex.AccessibleName = text + " 색상 코드";
            choice.Controls.Add(hex, 1, 0);

            Button choose = new Button();
            choose.Text = "색 선택…";
            choose.Dock = DockStyle.Fill;
            choose.Margin = new Padding(3, 0, 0, 0);
            choose.AccessibleName = text + " 선택";
            choose.Click += chooseColor;
            choice.Controls.Add(choose, 2, 0);
        }

        private static Color OpaqueColor(Color color)
        {
            return Color.FromArgb(255, color.R, color.G, color.B);
        }

        private static string ColorHex(Color color)
        {
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private void ChooseOverlayColor(bool chooseBackground)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = chooseBackground ? overlayBackground : overlayForeground;
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (chooseBackground) overlayBackground = OpaqueColor(dialog.Color);
                else overlayForeground = OpaqueColor(dialog.Color);
                UpdateColorPreview();
            }
        }

        private void UpdateColorPreview()
        {
            if (updatingAppearance) return;
            updatingAppearance = true;
            try
            {
                backgroundSwatch.BackColor = overlayBackground;
                foregroundSwatch.BackColor = overlayForeground;
                backgroundHex.Text = ColorHex(overlayBackground);
                foregroundHex.Text = ColorHex(overlayForeground);
                string selectedFont = fontName.SelectedItem as string;
                string resolvedFont = OverlayAppearance.ResolveFontName(selectedFont);
                if (!String.Equals(selectedFont, resolvedFont, StringComparison.OrdinalIgnoreCase))
                    SelectFont(resolvedFont);

                bool supportsBold = OverlayAppearance.SupportsBold(resolvedFont);
                UserPreferences probe = new UserPreferences
                {
                    OverlayFontName = resolvedFont,
                    OverlayFontSize = (float)fontSize.Value,
                    OverlayFontBold = false
                };
                bool supportsRegular;
                using (Font regular = OverlayAppearance.CreateFont(probe, 1f))
                    supportsRegular = !regular.Bold;
                fontBold.Enabled = supportsBold && supportsRegular;
                if (!supportsBold) fontBold.Checked = false;
                else if (!supportsRegular) fontBold.Checked = true;
                boxWidth.Enabled = !autoBoxSize.Checked;
                boxHeight.Enabled = !autoBoxSize.Checked;

                UserPreferences appearance = CollectChoices();
                float scale = PreviewScale();
                Size measured = OverlayAppearance.FitToMonitor(
                    OverlayAppearance.MeasureBox(appearance, scale), Screen.FromControl(this).Bounds, scale);
                overlayPreview.ApplyAppearance(appearance, scale);
                overlayPreview.Size = measured;
                previewViewport.AutoScrollMinSize = new Size(
                    measured.Width + previewViewport.Padding.Horizontal,
                    measured.Height + previewViewport.Padding.Vertical);
                previewViewport.AutoScrollPosition = System.Drawing.Point.Empty;
                overlayPreview.Location = new System.Drawing.Point(
                    previewViewport.Padding.Left, previewViewport.Padding.Top);
                boxSizeLabel.Text = String.Format("{0} × {1} px · 화면 배율 {2:0}%",
                    measured.Width, measured.Height, scale * 100);
            }
            finally { updatingAppearance = false; }
        }

        private float PreviewScale()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(overlayPreview.Handle);
                return (dpi == 0 ? 96 : dpi) / 96f;
            }
            catch (EntryPointNotFoundException)
            {
                using (Graphics graphics = overlayPreview.CreateGraphics())
                    return graphics.DpiX / 96f;
            }
        }

        private void AppearanceChanged(object sender, EventArgs args)
        {
            UpdateColorPreview();
        }

        private void ResetAppearance(object sender, EventArgs args)
        {
            updatingAppearance = true;
            try
            {
                SelectFont(OverlayAppearance.ResolveFontName("Segoe UI"));
                fontSize.Value = 9.75m;
                fontBold.Checked = false;
                autoBoxSize.Checked = true;
                boxWidth.Value = 224;
                boxHeight.Value = 28;
            }
            finally { updatingAppearance = false; }
            UpdateColorPreview();
        }

        private static CheckBox AddCheckBox(TableLayoutPanel layout, string text, int row)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.AutoSize = true;
            checkBox.Anchor = AnchorStyles.Left;
            layout.Controls.Add(checkBox, 0, row);
            layout.SetColumnSpan(checkBox, 2);
            return checkBox;
        }

        private static void AddNote(TableLayoutPanel layout, string text, int row)
        {
            Label note = new Label();
            note.Text = text;
            note.ForeColor = SystemColors.GrayText;
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.Margin = new Padding(3, 3, 3, 3);
            layout.Controls.Add(note, 0, row);
            layout.SetColumnSpan(note, 2);
        }

        private UserPreferences CollectChoices()
        {
            return new UserPreferences
            {
                TransferKey = transferKeys[transferKey.SelectedIndex],
                StartEnabled = startEnabled.Checked,
                ShowOverlay = showOverlay.Checked,
                OverlayBackground = overlayBackground,
                OverlayForeground = overlayForeground,
                OverlayFontName = (string)fontName.SelectedItem,
                OverlayFontSize = (float)fontSize.Value,
                OverlayFontBold = fontBold.Checked,
                OverlayAutoSize = autoBoxSize.Checked,
                OverlayWidth = (int)boxWidth.Value,
                OverlayHeight = (int)boxHeight.Value
            };
        }

        private void SaveChoices(object sender, EventArgs args)
        {
            ResultPreferences = CollectChoices();
            ResultStartupEnabled = startup.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        // Draw the same message and font as the floating status box, without opening one.
        private sealed class OverlayColorPreview : Control
        {
            private UserPreferences appearance;
            private float renderScale = 1f;
            internal OverlayColorPreview()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                Text = OverlayAppearance.Message;
                AccessibleRole = AccessibleRole.StaticText;
            }

            internal void ApplyAppearance(UserPreferences preferences, float scale)
            {
                appearance = preferences;
                renderScale = scale;
                BackColor = preferences.OverlayBackground;
                ForeColor = preferences.OverlayForeground;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (appearance != null)
                    OverlayAppearance.DrawText(e.Graphics, ClientRectangle, appearance, renderScale);
            }
        }

        private void OpenStartupApps(object sender, EventArgs args)
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo("ms-settings:startupapps")
                {
                    UseShellExecute = true
                })) { }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Windows 시작 앱 설정을 열지 못했습니다.\r\n" + error.Message,
                    "MonitorGate 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
