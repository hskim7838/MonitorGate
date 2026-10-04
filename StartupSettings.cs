using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MonitorGate
{
    internal sealed class UserPreferences
    {
        internal TransferKey TransferKey = TransferKey.Ctrl;
        internal bool StartEnabled = true;
        internal bool ShowOverlay = true;
        internal Color OverlayBackground = Color.Black;
        internal Color OverlayForeground = Color.White;
        internal string OverlayFontName = "Segoe UI";
        internal float OverlayFontSize = 9.75f;
        internal bool OverlayFontBold = false;
        internal bool OverlayAutoSize = true;
        internal int OverlayWidth = 224;
        internal int OverlayHeight = 28;
    }

    internal sealed class PreferencesStore
    {
        private readonly string path;
        internal PreferencesStore(string path) { this.path = Path.GetFullPath(path); }
        internal static PreferencesStore ForCurrentUser()
        {
            return new PreferencesStore(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "MonitorGate", "settings.ini"));
        }
        internal UserPreferences Load()
        {
            UserPreferences preferences = new UserPreferences();
            if (!File.Exists(path)) return preferences;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=');
                if (separator < 0) continue;
                string key = line.Substring(0, separator).Trim();
                string rawValue = line.Substring(separator + 1);
                string value = rawValue.Trim();
                TransferKey transfer;
                bool flag;
                Color color;
                float fontSize;
                int dimension;
                if (key == "TransferKey" && Enum.TryParse<TransferKey>(value, true, out transfer) &&
                    Enum.IsDefined(typeof(TransferKey), transfer)) preferences.TransferKey = transfer;
                if (key == "StartEnabled" && Boolean.TryParse(value, out flag)) preferences.StartEnabled = flag;
                if (key == "ShowOverlay" && Boolean.TryParse(value, out flag)) preferences.ShowOverlay = flag;
                if (key == "OverlayBackground" && TryParseColor(value, out color)) preferences.OverlayBackground = color;
                if (key == "OverlayForeground" && TryParseColor(value, out color)) preferences.OverlayForeground = color;
                if (key == "OverlayFontName" && IsValidFontName(value) && !HasControl(rawValue)) preferences.OverlayFontName = value;
                if (key == "OverlayFontSize" && Single.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out fontSize) && IsValidFontSize(fontSize)) preferences.OverlayFontSize = fontSize;
                if (key == "OverlayFontBold" && Boolean.TryParse(value, out flag)) preferences.OverlayFontBold = flag;
                if (key == "OverlayAutoSize" && Boolean.TryParse(value, out flag)) preferences.OverlayAutoSize = flag;
                if (key == "OverlayWidth" && Int32.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out dimension) && dimension >= 48 && dimension <= 1600) preferences.OverlayWidth = dimension;
                if (key == "OverlayHeight" && Int32.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out dimension) && dimension >= 20 && dimension <= 400) preferences.OverlayHeight = dimension;
            }
            return preferences;
        }
        internal void Save(UserPreferences preferences)
        {
            if (!Enum.IsDefined(typeof(TransferKey), preferences.TransferKey))
                throw new ArgumentException("지원하지 않는 이동 키입니다.");
            ValidateColor(preferences.OverlayBackground);
            ValidateColor(preferences.OverlayForeground);
            if (!IsValidFontName(preferences.OverlayFontName)) throw new ArgumentException("지원하지 않는 글꼴 이름입니다.");
            if (!IsValidFontSize(preferences.OverlayFontSize)) throw new ArgumentException("글자 크기는 6~48pt 범위여야 합니다.");
            if (preferences.OverlayWidth < 48 || preferences.OverlayWidth > 1600 ||
                preferences.OverlayHeight < 20 || preferences.OverlayHeight > 400)
                throw new ArgumentException("상태 박스 너비는 48~1600px, 높이는 20~400px 범위여야 합니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string text = "TransferKey=" + preferences.TransferKey + Environment.NewLine +
                "StartEnabled=" + preferences.StartEnabled + Environment.NewLine +
                "ShowOverlay=" + preferences.ShowOverlay + Environment.NewLine +
                "OverlayBackground=" + FormatColor(preferences.OverlayBackground) + Environment.NewLine +
                "OverlayForeground=" + FormatColor(preferences.OverlayForeground) + Environment.NewLine +
                "OverlayFontName=" + preferences.OverlayFontName + Environment.NewLine +
                "OverlayFontSize=" + preferences.OverlayFontSize.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                "OverlayFontBold=" + preferences.OverlayFontBold + Environment.NewLine +
                "OverlayAutoSize=" + preferences.OverlayAutoSize + Environment.NewLine +
                "OverlayWidth=" + preferences.OverlayWidth.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                "OverlayHeight=" + preferences.OverlayHeight.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = Color.Empty;
            int rgb;
            if (value.Length != 7 || value[0] != '#' || !Int32.TryParse(value.Substring(1),
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        private static void ValidateColor(Color color)
        {
            if (color.IsEmpty || color.A != 255)
                throw new ArgumentException("상태 박스 색상은 불투명한 RGB 색상이어야 합니다.");
        }

        private static string FormatColor(Color color)
        {
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static bool IsValidFontName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 128 || name != name.Trim()) return false;
            return !HasControl(name);
        }

        private static bool HasControl(string value)
        {
            foreach (char character in value) if (Char.IsControl(character)) return true;
            return false;
        }

        private static bool IsValidFontSize(float size)
        {
            return !Single.IsNaN(size) && !Single.IsInfinity(size) && size >= 6 && size <= 48;
        }
    }

    internal interface IStartupStore
    {
        string Read();
        void Write(string command);
        void Delete();
    }

    internal sealed class RegistryStartupStore : IStartupStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MonitorGate";
        public string Read()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
            {
                if (key == null) return null;
                object command = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (command == null) return null;
                if (!(command is string)) throw new InvalidOperationException("자동 실행 등록 값을 읽을 수 없습니다.");
                return (string)command;
            }
        }
        public void Write(string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (key == null) throw new InvalidOperationException("자동 실행 설정을 저장할 수 없습니다.");
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
        public void Delete()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                if (key != null) key.DeleteValue(ValueName, false);
        }
    }

    internal enum StartupStatus { Missing, CurrentLocation, OtherLocation }

    internal sealed class StartupRegistration
    {
        private readonly IStartupStore store;
        internal StartupRegistration(IStartupStore store) { this.store = store; }
        internal static string CommandFor(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath) || executablePath.IndexOf('"') >= 0 ||
                !Path.IsPathRooted(executablePath) || Path.GetPathRoot(executablePath).Length < 3)
                throw new ArgumentException("실행 파일의 전체 경로가 필요합니다.");
            string command = "\"" + Path.GetFullPath(executablePath) + "\" --startup";
            if (command.Length > 260) throw new ArgumentException("자동 실행에 등록할 실행 파일 경로가 너무 깁니다.");
            return command;
        }
        internal StartupStatus ReadStatus(string executablePath)
        {
            string command = store.Read();
            if (String.IsNullOrWhiteSpace(command)) return StartupStatus.Missing;
            return String.Equals(command.Trim(), CommandFor(executablePath), StringComparison.OrdinalIgnoreCase)
                ? StartupStatus.CurrentLocation : StartupStatus.OtherLocation;
        }
        internal void SetEnabled(bool enabled, string executablePath)
        {
            if (enabled) store.Write(CommandFor(executablePath));
            else store.Delete();
        }
    }

    // Sampled cursor motion with a short hold prevents flicker between mouse reports.
    // Idle or key-up always hides the badge; showing the badge never changes the gate.
    internal sealed class MovementIndicator
    {
        internal const int IdleHideMilliseconds = 180;
        private bool sampled;
        private Point previous;
        private long lastMotion = Int64.MinValue;
        internal bool Update(bool showOverlay, bool crossingAllowed, Point position, long nowMilliseconds)
        {
            bool moved = sampled && (position.X != previous.X || position.Y != previous.Y);
            previous = position;
            sampled = true;
            if (!showOverlay || !crossingAllowed)
            {
                lastMotion = Int64.MinValue;
                return false;
            }
            if (moved) lastMotion = nowMilliseconds;
            return lastMotion != Int64.MinValue && nowMilliseconds >= lastMotion &&
                nowMilliseconds - lastMotion < IdleHideMilliseconds;
        }
        internal void Reset() { sampled = false; lastMotion = Int64.MinValue; }
    }
}
