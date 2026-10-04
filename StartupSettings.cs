using System;
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
                string value = line.Substring(separator + 1).Trim();
                TransferKey transfer;
                bool flag;
                if (key == "TransferKey" && Enum.TryParse<TransferKey>(value, true, out transfer) &&
                    Enum.IsDefined(typeof(TransferKey), transfer)) preferences.TransferKey = transfer;
                if (key == "StartEnabled" && Boolean.TryParse(value, out flag)) preferences.StartEnabled = flag;
                if (key == "ShowOverlay" && Boolean.TryParse(value, out flag)) preferences.ShowOverlay = flag;
            }
            return preferences;
        }
        internal void Save(UserPreferences preferences)
        {
            if (!Enum.IsDefined(typeof(TransferKey), preferences.TransferKey))
                throw new ArgumentException("지원하지 않는 이동 키입니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string text = "TransferKey=" + preferences.TransferKey + Environment.NewLine +
                "StartEnabled=" + preferences.StartEnabled + Environment.NewLine +
                "ShowOverlay=" + preferences.ShowOverlay + Environment.NewLine;
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
