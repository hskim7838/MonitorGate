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
            ClientSize = new Size(440, startupStatus == StartupStatus.OtherLocation ? 386 : 340);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 9;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                startupStatus == StartupStatus.OtherLocation ? 46 : 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(layout);

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

            AddNote(layout,
                "컴퓨터를 켠 뒤 Windows에 로그인할 때 실행됩니다.\r\n" +
                "Windows ‘시작 앱’에서 끄면 자동 실행되지 않습니다.", 4);
            if (startupStatus == StartupStatus.OtherLocation)
                AddNote(layout,
                    "다른 위치의 실행 파일이 등록되어 있습니다.\r\n" +
                    "자동 실행을 껐다 켜면 현재 위치로 갱신됩니다.", 5);

            Button startupApps = new Button();
            startupApps.Text = "Windows 시작 앱 설정 열기";
            startupApps.AutoSize = true;
            startupApps.Anchor = AnchorStyles.Left;
            startupApps.Padding = new Padding(6, 0, 6, 0);
            startupApps.Click += OpenStartupApps;
            layout.Controls.Add(startupApps, 0, 6);
            layout.SetColumnSpan(startupApps, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
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
            layout.Controls.Add(buttons, 0, 8);
            layout.SetColumnSpan(buttons, 2);
            AcceptButton = save;
            CancelButton = cancel;
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

        private void SaveChoices(object sender, EventArgs args)
        {
            ResultPreferences = new UserPreferences
            {
                TransferKey = transferKeys[transferKey.SelectedIndex],
                StartEnabled = startEnabled.Checked,
                ShowOverlay = showOverlay.Checked
            };
            ResultStartupEnabled = startup.Checked;
            DialogResult = DialogResult.OK;
            Close();
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
