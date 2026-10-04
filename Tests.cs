using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MonitorGate
{
    // Deliberately exercises only the pure Gate/normalization code with a fake
    // desktop. This entry point never constructs InputWorker or WindowsDesktop.
    internal static class EngineTests
    {
        private static int passed;
        private static int failed;

        public static int Main()
        {
            Test("lock, held transfer, and relock on destination", Transfer);
            Test("retained target after another app clears clipping", RetainedTarget);
            Test("negative coordinates and vertically stacked monitor", UnusualGeometry);
            Test("respect existing narrower clipping without taking ownership", ExistingNarrowClip);
            Test("do not clear another app's replacement clip", ReplacementClip);
            Test("cleanup owned clipping once", OwnedCleanup);
            Test("do not own an already matching clip", MatchingClip);
            Test("display reset discards old monitor and geometry", DisplayReset);
            Test("disable and suspend release, resume captures current monitor", DisabledAndSuspended);
            Test("confinement/read failures propagate without false ownership", Failure);
            Test("failed cleanup can be retried", CleanupFailure);
            Test("normalize left/right modifier events and passthrough", Normalize);
            Test("transfer key choices use correct physical keys", KeyChoices);
            Test("startup missing, matching, moved, and legacy registration", StartupStatusChecks);
            Test("startup uses quoted normalized executable and explicit startup flag", StartupCommand);
            Test("startup command length boundary and invalid input have no writes", StartupInputValidation);
            Test("startup enables, updates moved registration, and disables via fake store", StartupToggle);
            Test("startup store failures are surfaced", StartupFailures);
            Test("absent preferences have safe defaults", PreferencesDefaults);
            Test("all key and boolean preferences persist and overwrite", PreferencesRoundTrips);
            Test("preferences tolerate unknown and malformed values", PreferencesMalformed);
            Test("invalid preferences preserve existing file", PreferencesInvalidSave);
            Test("preferences IO failure is surfaced with no partial file", PreferencesWriteFailure);
            Test("legacy settings preserve options and use default badge colors", PreferencesLegacyColors);
            Test("badge RGB colors persist independently and overwrite", PreferencesColorRoundTrips);
            Test("badge color parser accepts lower and mixed case six digit RGB", PreferencesColorCase);
            Test("malformed badge color falls back for that field only", PreferencesInvalidColorText);
            Test("nonopaque badge colors are rejected before writing", PreferencesInvalidColorSave);
            Test("font and layout preferences round trip under non-dot culture", PreferencesAppearanceRoundTrips);
            Test("invalid appearance values default each field independently", PreferencesInvalidAppearanceText);
            Test("invalid appearance save is rejected without changing file", PreferencesInvalidAppearanceSave);
            Test("installed font list is sorted and contains supported families", FontInventory);
            Test("missing font resolves explicitly to a supported sans serif", FontFallback);
            Test("font creation supports available regular and bold styles", FontStyles);
            Test("font points and box metrics scale with DPI", AppearanceMetrics);
            Test("manual boxes honor requested size and minimum text padding", AppearanceManualMinimum);
            Test("actual badge and preview share monitor size limits", AppearanceFitMonitor);
            Test("badge requires transfer permission plus actual movement", MotionRequiresPermission);
            Test("badge hides at precise idle boundary and restarts on movement", MotionIdleBoundary);
            Test("badge setting and key release clear recent motion", MotionHideResets);
            Test("badge reset ignores stale sample and supports negative position", MotionResetAndCoordinates);
            Test("badge rejects a backwards clock without showing stale motion", MotionBackwardsClock);
            Console.WriteLine("RESULT: {0} passed, {1} failed", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + " -- " + ex.Message); }
        }

        private static Rect R(int left, int top, int right, int bottom)
        {
            return new Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + ": expected " + expected + ", got " + actual);
        }

        private static void Throws(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception(message + ": expected InvalidOperationException");
        }

        private static void Transfer()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            Equal(d.Monitors[1], d.Clip, "initial monitor is confined");
            d.Monitor = 2;
            g.Update(false);
            Equal(d.Monitors[1], d.Clip, "moving the fake pointer cannot change locked target");
            Equal(1, d.Confines, "stable clipping is not reapplied");
            g.Update(true);
            Equal(d.VirtualBounds, d.Clip, "key down releases owned clip");
            Equal(1, d.Releases, "one release on key down");
            g.Update(true);
            Equal(1, d.CurrentMonitorReads, "held key does not pick a locked monitor");
            Equal(1, d.Releases, "held key does not repeat release");
            g.Update(false);
            Equal(d.Monitors[2], d.Clip, "key up locks destination monitor");
            Equal(2, d.CurrentMonitorReads, "destination captured once");
        }

        private static void RetainedTarget()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            d.Clip = d.VirtualBounds; // Simulate external ClipCursor(NULL).
            d.Monitor = 2;
            g.Update(false);
            Equal(d.Monitors[1], d.Clip, "external release must not permit changing locked monitor");
            Equal(1, d.CurrentMonitorReads, "target retained across external clear");
            Equal(2, d.Confines, "lost confinement is restored");
        }

        private static void UnusualGeometry()
        {
            FakeDesktop d = new FakeDesktop();
            d.Monitor = 2;
            Gate g = new Gate(d);
            g.Update(false);
            Equal(R(-1920, 0, 0, 1080), d.Clip, "left monitor retains negative coordinates");
            g.Update(true);
            d.Monitor = 3;
            g.Update(false);
            Equal(R(0, -1080, 1920, 0), d.Clip, "upper monitor retains negative vertical coordinates");
        }

        private static void ExistingNarrowClip()
        {
            FakeDesktop d = new FakeDesktop();
            Rect gameClip = R(100, 100, 800, 600);
            d.Clip = gameClip;
            Gate g = new Gate(d);
            g.Update(false);
            Equal(gameClip, d.Clip, "game clip is preserved");
            Equal(0, d.Confines, "no widening of existing game clip");
            g.Update(true);
            Equal(gameClip, d.Clip, "transfer does not clear unowned game clip");
            Equal(0, d.Releases, "no release of preexisting clip");
        }

        private static void ReplacementClip()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            Rect gameClip = R(50, 40, 600, 400);
            d.Clip = gameClip;
            g.Update(false);
            g.Release();
            Equal(gameClip, d.Clip, "narrow replacement survives update and cleanup");
            Equal(0, d.Releases, "no release after relinquishing ownership");

            FakeDesktop d2 = new FakeDesktop();
            Gate g2 = new Gate(d2);
            g2.Update(false);
            Rect otherClip = R(-700, -700, 1500, 800);
            d2.Clip = otherClip;
            g2.Release(); // No intervening update; direct ownership check is required.
            Equal(otherClip, d2.Clip, "different replacement survives direct cleanup");
            Equal(0, d2.Releases, "direct cleanup respects different external clip");
        }

        private static void OwnedCleanup()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            g.Release();
            Equal(d.VirtualBounds, d.Clip, "owned clip removed");
            g.Release();
            Equal(1, d.Releases, "cleanup is idempotent");
            d.Monitor = 3;
            g.Update(false);
            Equal(d.Monitors[3], d.Clip, "cleanup forgets previous target");
        }

        private static void MatchingClip()
        {
            FakeDesktop d = new FakeDesktop();
            d.Clip = d.Monitors[1];
            Gate g = new Gate(d);
            g.Update(false);
            g.Release();
            Equal(d.Monitors[1], d.Clip, "preexisting exact monitor clip remains");
            Equal(0, d.Confines, "matching clip does not need changing");
            Equal(0, d.Releases, "matching preexisting clip is not claimed");
        }

        private static void DisplayReset()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            g.RefreshDisplay();
            Equal(d.VirtualBounds, d.Clip, "display reset releases old owned rectangle");
            d.Monitor = 3;
            d.Monitors[3] = R(-300, -1440, 2260, 0);
            g.Update(false);
            Equal(d.Monitors[3], d.Clip, "display reset obtains changed monitor geometry");
            Equal(2, d.BoundsReads, "fresh bounds queried after display change");
        }

        private static void DisabledAndSuspended()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            g.Enabled = false;
            g.Update(false);
            Equal(d.VirtualBounds, d.Clip, "disable releases clip");
            d.Monitor = 3;
            g.Update(false);
            Equal(1, d.Confines, "disabled state never confines");
            g.Enabled = true;
            g.Update(false);
            Equal(d.Monitors[3], d.Clip, "resume locks current monitor");
            g.Suspended = true;
            g.Update(false);
            Equal(d.VirtualBounds, d.Clip, "session/power suspension releases clip");
            d.Monitor = 2;
            g.Update(false);
            Equal(2, d.Confines, "suspended state never confines");
            g.Suspended = false;
            g.Update(false);
            Equal(d.Monitors[2], d.Clip, "unsuspend locks current monitor");
        }

        private static void Failure()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            d.FailConfine = true;
            Throws(delegate { g.Update(false); }, "failed confinement is surfaced");
            Equal(d.VirtualBounds, d.Clip, "failed confinement did not change clip");
            g.Release();
            Equal(0, d.Releases, "failed confinement did not claim ownership");
            d.FailConfine = false;
            d.FailReadClip = true;
            Throws(delegate { g.Update(false); }, "failed clip read is surfaced");
            Equal(1, d.Confines, "failed read does not attempt another confinement");
            d.FailReadClip = false;
            g.Release();
            d.Monitor = 2;
            g.Update(false);
            Equal(d.Monitors[2], d.Clip, "can recover after caller resets failed gate");
        }

        private static void CleanupFailure()
        {
            FakeDesktop d = new FakeDesktop();
            Gate g = new Gate(d);
            g.Update(false);
            d.FailRelease = true;
            Throws(delegate { g.Release(); }, "cleanup failure is surfaced");
            Equal(d.Monitors[1], d.Clip, "failed release preserves fake clip");
            d.FailRelease = false;
            g.Release();
            Equal(d.VirtualBounds, d.Clip, "retained ownership allows retry");
            Equal(2, d.Releases, "second release attempted");
        }

        private static void Normalize()
        {
            Equal(0xA2, NormalizeOne(0x11, 0x1D, 0), "generic left Ctrl");
            Equal(0xA3, NormalizeOne(0x11, 0x1D, 1), "generic right Ctrl uses extended bit");
            Equal(0xA2, NormalizeOne(0x11, 0x1D, 0x90), "key-up/injection flags do not imply right Ctrl");
            Equal(0xA3, NormalizeOne(0x11, 0x1D, 0x81), "extended right Ctrl on key up");
            Equal(0xA4, NormalizeOne(0x12, 0x38, 0), "generic left Alt");
            Equal(0xA5, NormalizeOne(0x12, 0x38, 1), "generic right Alt");
            Equal(0xA0, NormalizeOne(0x10, 0x2A, 1), "left Shift determined by scan code");
            Equal(0xA1, NormalizeOne(0x10, 0x36, 0), "right Shift determined by scan code");
            Equal(0xA2, NormalizeOne(0xA2, 0, 1), "explicit left Ctrl passthrough");
            Equal(0xA3, NormalizeOne(0xA3, 0, 0), "explicit right Ctrl passthrough");
            Equal(0xA4, NormalizeOne(0xA4, 0, 1), "explicit left Alt passthrough");
            Equal(0xA5, NormalizeOne(0xA5, 0, 0), "explicit right Alt passthrough");
            Equal(0xA0, NormalizeOne(0xA0, 0x36, 0), "explicit left Shift passthrough");
            Equal(0xA1, NormalizeOne(0xA1, 0x2A, 0), "explicit right Shift passthrough");
            Equal(0x41, NormalizeOne(0x41, 0, 1), "ordinary key passthrough");
        }

        private static int NormalizeOne(uint key, uint scan, uint flags)
        {
            return InputWorker.NormalizeKey(new KeyboardEvent { Key = key, ScanCode = scan, Flags = flags });
        }

        private static void KeyChoices()
        {
            Equal("162,163", Join(KeysForTransfer.Codes(TransferKey.Ctrl)), "Ctrl supports both sides");
            Equal("164,165", Join(KeysForTransfer.Codes(TransferKey.Alt)), "Alt supports both sides");
            Equal("160,161", Join(KeysForTransfer.Codes(TransferKey.Shift)), "Shift supports both sides");
            Equal("163", Join(KeysForTransfer.Codes(TransferKey.RightCtrl)), "right Ctrl excludes left");
            Equal("Ctrl", KeysForTransfer.Label(TransferKey.Ctrl), "default label");
            Equal("오른쪽 Ctrl", KeysForTransfer.Label(TransferKey.RightCtrl), "right Ctrl label");
        }

        private static string Join(int[] values)
        {
            string[] strings = new string[values.Length];
            for (int i = 0; i < values.Length; i++) strings[i] = values[i].ToString();
            return String.Join(",", strings);
        }

        private static void ThrowsOf<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (Exception ex)
            {
                if (ex is T) return;
                throw new Exception(message + ": expected " + typeof(T).Name + ", got " + ex.GetType().Name);
            }
            throw new Exception(message + ": expected " + typeof(T).Name);
        }

        private static void StartupStatusChecks()
        {
            const string path = @"C:\Program Files\MonitorGate\MonitorGate.exe";
            FakeStartupStore store = new FakeStartupStore();
            StartupRegistration startup = new StartupRegistration(store);
            Equal(StartupStatus.Missing, startup.ReadStatus(path), "absent registration");
            store.Value = " \t ";
            Equal(StartupStatus.Missing, startup.ReadStatus(path), "blank registration");
            store.Value = StartupRegistration.CommandFor(path);
            Equal(StartupStatus.CurrentLocation, startup.ReadStatus(path), "matching registration");
            store.Value = "  " + StartupRegistration.CommandFor(path).ToUpperInvariant() + "  ";
            Equal(StartupStatus.CurrentLocation, startup.ReadStatus(path), "case and outer whitespace are harmless");
            store.Value = StartupRegistration.CommandFor(@"C:\Old Folder\MonitorGate.exe");
            Equal(StartupStatus.OtherLocation, startup.ReadStatus(path), "registration from previous location");
            store.Value = "\"" + path + "\"";
            Equal(StartupStatus.OtherLocation, startup.ReadStatus(path), "legacy command without startup flag");
            store.Value = "unexpected command";
            Equal(StartupStatus.OtherLocation, startup.ReadStatus(path), "malformed command not treated as current");
            Equal(0, store.Writes, "status checks never write");
            Equal(0, store.Deletes, "status checks never delete");
        }

        private static void StartupCommand()
        {
            Equal("\"C:\\Program Files\\MonitorGate\\MonitorGate.exe\" --startup",
                StartupRegistration.CommandFor(@"C:\Program Files\MonitorGate\MonitorGate.exe"), "spaces safely quoted");
            Equal("\"C:\\Apps\\MonitorGate.exe\" --startup",
                StartupRegistration.CommandFor(@"C:\Apps\Subfolder\..\MonitorGate.exe"), "path canonicalized before registration");
            Equal("\"C:\\한글 폴더\\MonitorGate.exe\" --startup",
                StartupRegistration.CommandFor(@"C:\한글 폴더\MonitorGate.exe"), "Unicode path retained");
        }

        private static void StartupInputValidation()
        {
            string exactPath = @"C:\" + new string('a', 241) + ".exe";
            Equal(248, exactPath.Length, "boundary fixture length");
            Equal(260, StartupRegistration.CommandFor(exactPath).Length, "260 character command allowed");
            FakeStartupStore store = new FakeStartupStore();
            StartupRegistration startup = new StartupRegistration(store);
            string[] invalidPaths = new string[] { null, "", " ", "MonitorGate.exe", @"folder\MonitorGate.exe",
                "C:MonitorGate.exe", @"\MonitorGate.exe", "C:",
                "C:\\bad\"name.exe", @"C:\" + new string('a', 242) + ".exe" };
            foreach (string invalid in invalidPaths)
            {
                string captured = invalid;
                ThrowsOf<ArgumentException>(delegate { startup.SetEnabled(true, captured); }, "invalid startup path rejected");
            }
            Equal(0, store.Writes, "invalid paths have no store mutation");
            Equal(0, store.Deletes, "invalid enable requests cannot disable existing entry");
        }

        private static void StartupToggle()
        {
            const string path = @"C:\Apps with spaces\MonitorGate.exe";
            FakeStartupStore store = new FakeStartupStore();
            StartupRegistration startup = new StartupRegistration(store);
            startup.SetEnabled(true, path);
            Equal(StartupRegistration.CommandFor(path), store.Value, "enable writes exact quoted command");
            store.Value = StartupRegistration.CommandFor(@"C:\Old\MonitorGate.exe");
            startup.SetEnabled(true, path);
            Equal(StartupStatus.CurrentLocation, startup.ReadStatus(path), "enabling refreshes moved executable");
            startup.SetEnabled(false, null);
            Equal(null, store.Value, "disable deletes registration even if current path unavailable");
            Equal(2, store.Writes, "enable and moved refresh each write");
            Equal(1, store.Deletes, "disable deletes once");
        }

        private static void StartupFailures()
        {
            FakeStartupStore store = new FakeStartupStore();
            StartupRegistration startup = new StartupRegistration(store);
            const string path = @"C:\Apps\MonitorGate.exe";
            store.FailRead = true;
            Throws(delegate { startup.ReadStatus(path); }, "read failure not reported as disabled");
            store.FailWrite = true;
            Throws(delegate { startup.SetEnabled(true, path); }, "failed registration surfaced");
            Equal(null, store.Value, "failed write leaves value unchanged");
            store.FailDelete = true;
            store.Value = "prior value";
            Throws(delegate { startup.SetEnabled(false, path); }, "failed deletion surfaced");
            Equal("prior value", store.Value, "failed delete leaves value unchanged");
        }

        private static string PreferencesFixture()
        {
            // The compiler places this test EXE in work/, so every fixture stays there.
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "preferences-tests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.ini");
        }

        private static void PreferencesDefaults()
        {
            string path = PreferencesFixture();
            UserPreferences settings = new PreferencesStore(path).Load();
            Equal(TransferKey.Ctrl, settings.TransferKey, "default transfer key");
            Equal(true, settings.StartEnabled, "default starts gate enabled");
            Equal(true, settings.ShowOverlay, "default overlay enabled");
            Equal(Color.Black.ToArgb(), settings.OverlayBackground.ToArgb(), "default overlay background black");
            Equal(Color.White.ToArgb(), settings.OverlayForeground.ToArgb(), "default overlay foreground white");
            AppearanceDefaults(settings);
            Equal(false, File.Exists(path), "load does not create settings file");
        }

        private static void PreferencesRoundTrips()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            foreach (TransferKey key in Enum.GetValues(typeof(TransferKey)))
                foreach (bool enabled in new bool[] { false, true })
                    foreach (bool overlay in new bool[] { false, true })
                    {
                        store.Save(new UserPreferences { TransferKey = key, StartEnabled = enabled, ShowOverlay = overlay });
                        UserPreferences loaded = new PreferencesStore(path).Load();
                        Equal(key, loaded.TransferKey, "key survives round trip");
                        Equal(enabled, loaded.StartEnabled, "enabled state survives round trip");
                        Equal(overlay, loaded.ShowOverlay, "overlay state survives round trip");
                    }
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "overwrite leaves no temporary files");
        }

        private static void PreferencesMalformed()
        {
            string path = PreferencesFixture();
            File.WriteAllText(path, "garbage\nUnknown=anything\nTransferKey=999\nStartEnabled=maybe\nShowOverlay=perhaps\n", Encoding.UTF8);
            UserPreferences loaded = new PreferencesStore(path).Load();
            Equal(TransferKey.Ctrl, loaded.TransferKey, "undefined numeric key defaults");
            Equal(true, loaded.StartEnabled, "invalid enabled flag defaults");
            Equal(true, loaded.ShowOverlay, "invalid overlay flag defaults");
            File.WriteAllText(path, "TransferKey= rightctrl \nStartEnabled= false \nShowOverlay= TRUE \n", Encoding.UTF8);
            loaded = new PreferencesStore(path).Load();
            Equal(TransferKey.RightCtrl, loaded.TransferKey, "case insensitive enum and whitespace");
            Equal(false, loaded.StartEnabled, "valid false flag");
            Equal(true, loaded.ShowOverlay, "case insensitive true flag");
        }

        private static void PreferencesInvalidSave()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            store.Save(new UserPreferences { TransferKey = TransferKey.Alt, StartEnabled = false, ShowOverlay = false });
            string oldText = File.ReadAllText(path);
            ThrowsOf<ArgumentException>(delegate { store.Save(new UserPreferences { TransferKey = (TransferKey)999 }); }, "undefined key rejected");
            Equal(oldText, File.ReadAllText(path), "invalid save preserves existing settings");
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "invalid save creates no temporary files");
        }

        private static void PreferencesWriteFailure()
        {
            string path = PreferencesFixture();
            string blocked = Path.Combine(Path.GetDirectoryName(path), "blocked-directory");
            File.WriteAllText(blocked, "existing file");
            PreferencesStore store = new PreferencesStore(Path.Combine(blocked, "settings.ini"));
            ThrowsOf<IOException>(delegate { store.Save(new UserPreferences()); }, "directory creation failure surfaced");
            Equal("existing file", File.ReadAllText(blocked), "IO failure preserves blocking file");
            Equal(false, File.Exists(path), "IO failure does not write unrelated settings fixture");
        }

        private static void PreferencesLegacyColors()
        {
            string path = PreferencesFixture();
            File.WriteAllText(path, "TransferKey=Alt\nStartEnabled=False\nShowOverlay=False\n", Encoding.UTF8);
            UserPreferences loaded = new PreferencesStore(path).Load();
            Equal(TransferKey.Alt, loaded.TransferKey, "legacy transfer key preserved");
            Equal(false, loaded.StartEnabled, "legacy enabled flag preserved");
            Equal(false, loaded.ShowOverlay, "legacy overlay flag preserved");
            Equal(Color.Black.ToArgb(), loaded.OverlayBackground.ToArgb(), "missing background uses black");
            Equal(Color.White.ToArgb(), loaded.OverlayForeground.ToArgb(), "missing foreground uses white");
            AppearanceDefaults(loaded);
        }

        private static void PreferencesColorRoundTrips()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            Color[] palette = new Color[] { Color.Black, Color.White, Color.FromArgb(0, 255, 10),
                Color.FromArgb(171, 205, 239), Color.FromArgb(1, 127, 254) };
            foreach (Color background in palette)
                foreach (Color foreground in palette)
                {
                    store.Save(new UserPreferences { TransferKey = TransferKey.Shift, StartEnabled = false,
                        OverlayBackground = background, OverlayForeground = foreground });
                    UserPreferences loaded = new PreferencesStore(path).Load();
                    Equal(background.ToArgb(), loaded.OverlayBackground.ToArgb(), "RGB background survives round trip");
                    Equal(foreground.ToArgb(), loaded.OverlayForeground.ToArgb(), "RGB foreground survives round trip");
                    Equal(255, (int)loaded.OverlayBackground.A, "loaded background opaque");
                    Equal(255, (int)loaded.OverlayForeground.A, "loaded foreground opaque");
                    Equal(TransferKey.Shift, loaded.TransferKey, "color save preserves other options");
                }
            store.Save(new UserPreferences { OverlayBackground = Color.FromArgb(0, 255, 10),
                OverlayForeground = Color.FromArgb(171, 205, 239) });
            string saved = File.ReadAllText(path);
            Equal(true, saved.Contains("OverlayBackground=#00FF0A" + Environment.NewLine), "background has exact uppercase six digit RGB");
            Equal(true, saved.Contains("OverlayForeground=#ABCDEF" + Environment.NewLine), "foreground has exact uppercase six digit RGB");
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "color overwrites leave no temporary files");
        }

        private static void PreferencesColorCase()
        {
            string path = PreferencesFixture();
            File.WriteAllText(path, "OverlayBackground=#ab09ef\nOverlayForeground=#aBcDeF\n", Encoding.UTF8);
            UserPreferences loaded = new PreferencesStore(path).Load();
            Equal(Color.FromArgb(171, 9, 239).ToArgb(), loaded.OverlayBackground.ToArgb(), "lowercase RGB accepted");
            Equal(Color.FromArgb(171, 205, 239).ToArgb(), loaded.OverlayForeground.ToArgb(), "mixed case RGB accepted");
            Equal(255, (int)loaded.OverlayBackground.A, "lowercase background has implicit full alpha");
            Equal(255, (int)loaded.OverlayForeground.A, "mixed case foreground has implicit full alpha");
        }

        private static void PreferencesInvalidColorText()
        {
            string path = PreferencesFixture();
            string[] invalid = new string[] { "", "Black", "123456", "#123", "#12345", "#1234567",
                "#80123456", "#GG0011", "#12 456", "#+00123", "#-00123", "#１２３４５６", "#12345Z" };
            foreach (string value in invalid)
            {
                File.WriteAllText(path, "TransferKey=RightCtrl\nStartEnabled=False\nShowOverlay=False\n" +
                    "OverlayBackground=" + value + "\nOverlayForeground=#123456\n", Encoding.UTF8);
                UserPreferences loaded = new PreferencesStore(path).Load();
                Equal(Color.Black.ToArgb(), loaded.OverlayBackground.ToArgb(), "invalid background defaults: " + value);
                Equal(Color.FromArgb(18, 52, 86).ToArgb(), loaded.OverlayForeground.ToArgb(), "valid foreground independent of invalid background");
                Equal(TransferKey.RightCtrl, loaded.TransferKey, "invalid color preserves transfer key");
                Equal(false, loaded.StartEnabled, "invalid color preserves enabled flag");
                Equal(false, loaded.ShowOverlay, "invalid color preserves overlay flag");
                File.WriteAllText(path, "OverlayBackground=#123456\nOverlayForeground=" + value + "\n", Encoding.UTF8);
                loaded = new PreferencesStore(path).Load();
                Equal(Color.FromArgb(18, 52, 86).ToArgb(), loaded.OverlayBackground.ToArgb(), "valid background independent of invalid foreground");
                Equal(Color.White.ToArgb(), loaded.OverlayForeground.ToArgb(), "invalid foreground defaults: " + value);
            }
        }

        private static void PreferencesInvalidColorSave()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            store.Save(new UserPreferences { OverlayBackground = Color.FromArgb(10, 20, 30),
                OverlayForeground = Color.FromArgb(210, 220, 230) });
            string original = File.ReadAllText(path);
            Color[] invalid = new Color[] { Color.Empty, Color.Transparent, Color.FromArgb(0, 10, 20, 30),
                Color.FromArgb(1, 10, 20, 30), Color.FromArgb(254, 10, 20, 30) };
            foreach (Color color in invalid)
            {
                Color captured = color;
                ThrowsOf<ArgumentException>(delegate { store.Save(new UserPreferences { OverlayBackground = captured }); }, "invalid background rejected");
                Equal(original, File.ReadAllText(path), "invalid background preserves existing settings");
                ThrowsOf<ArgumentException>(delegate { store.Save(new UserPreferences { OverlayForeground = captured }); }, "invalid foreground rejected");
                Equal(original, File.ReadAllText(path), "invalid foreground preserves existing settings");
            }
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "invalid colors create no temporary files");
            string absentDirectory = Path.Combine(Path.GetDirectoryName(path), "must-not-create");
            PreferencesStore absentStore = new PreferencesStore(Path.Combine(absentDirectory, "settings.ini"));
            ThrowsOf<ArgumentException>(delegate { absentStore.Save(new UserPreferences { OverlayForeground = Color.Empty }); }, "invalid color rejected before creating directory");
            Equal(false, Directory.Exists(absentDirectory), "invalid save has no directory side effect");
        }

        private static void AppearanceDefaults(UserPreferences preferences)
        {
            Equal("Segoe UI", preferences.OverlayFontName, "default font family");
            Equal(9.75f, preferences.OverlayFontSize, "default point size preserves 13px at 96 DPI");
            Equal(false, preferences.OverlayFontBold, "default regular font");
            Equal(true, preferences.OverlayAutoSize, "default automatic box size");
            Equal(224, preferences.OverlayWidth, "default manual width");
            Equal(28, preferences.OverlayHeight, "default manual height");
        }

        private static void PreferencesAppearanceRoundTrips()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                foreach (float size in new float[] { 6, 9.75f, 48 })
                    foreach (bool bold in new bool[] { false, true })
                        foreach (bool automatic in new bool[] { false, true })
                        {
                            UserPreferences requested = new UserPreferences { OverlayFontName = "Saved = Missing Font",
                                OverlayFontSize = size, OverlayFontBold = bold, OverlayAutoSize = automatic,
                                OverlayWidth = automatic ? 48 : 1600, OverlayHeight = automatic ? 20 : 400 };
                            store.Save(requested);
                            UserPreferences loaded = new PreferencesStore(path).Load();
                            Equal(requested.OverlayFontName, loaded.OverlayFontName, "font name including equals preserved");
                            Equal(size, loaded.OverlayFontSize, "point size survives culture independent round trip");
                            Equal(bold, loaded.OverlayFontBold, "bold choice survives round trip");
                            Equal(automatic, loaded.OverlayAutoSize, "auto/manual choice survives round trip");
                            Equal(requested.OverlayWidth, loaded.OverlayWidth, "manual width survives round trip");
                            Equal(requested.OverlayHeight, loaded.OverlayHeight, "manual height survives round trip");
                        }
                store.Save(new UserPreferences { OverlayFontSize = 9.75f, OverlayFontName = new string('a', 128) });
                Equal(true, File.ReadAllText(path).Contains("OverlayFontSize=9.75" + Environment.NewLine), "fractional size uses invariant decimal dot");
                Equal(128, store.Load().OverlayFontName.Length, "maximum name length accepted");
                Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "appearance overwrite leaves no temporary files");
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        private static void PreferencesInvalidAppearanceText()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            string[] badNames = new string[] { "", " ", new string('x', 129), "Font\0Name", "Font\tName", "\tFont", "Font\t" };
            foreach (string value in badNames)
            {
                File.WriteAllText(path, "OverlayFontName=" + value + "\nOverlayFontSize=12.5\nOverlayWidth=500\n", Encoding.UTF8);
                UserPreferences loaded = store.Load();
                Equal("Segoe UI", loaded.OverlayFontName, "invalid name defaults");
                Equal(12.5f, loaded.OverlayFontSize, "invalid name preserves valid size");
                Equal(500, loaded.OverlayWidth, "invalid name preserves valid width");
            }
            foreach (string value in new string[] { "NaN", "Infinity", "-Infinity", "5.99", "48.01", "9,75", "1e99", "bad" })
            {
                File.WriteAllText(path, "OverlayFontName=Consolas\nOverlayFontSize=" + value + "\nOverlayFontBold=True\n", Encoding.UTF8);
                UserPreferences loaded = store.Load();
                Equal(9.75f, loaded.OverlayFontSize, "invalid font size defaults: " + value);
                Equal("Consolas", loaded.OverlayFontName, "invalid size preserves name");
                Equal(true, loaded.OverlayFontBold, "invalid size preserves bold flag");
            }
            File.WriteAllText(path, "OverlayFontBold=maybe\nOverlayAutoSize=0\nOverlayWidth=47\nOverlayHeight=401\n", Encoding.UTF8);
            AppearanceDefaults(store.Load());
            foreach (string value in new string[] { "1601", "-1", "1.5", "NaN", "2147483648" })
            {
                File.WriteAllText(path, "OverlayWidth=" + value + "\nOverlayHeight=40\n", Encoding.UTF8);
                Equal(224, store.Load().OverlayWidth, "invalid width defaults: " + value);
                Equal(40, store.Load().OverlayHeight, "invalid width preserves height");
            }
            foreach (string value in new string[] { "19", "-1", "1.5", "NaN", "2147483648" })
            {
                File.WriteAllText(path, "OverlayWidth=500\nOverlayHeight=" + value + "\n", Encoding.UTF8);
                Equal(28, store.Load().OverlayHeight, "invalid height defaults: " + value);
                Equal(500, store.Load().OverlayWidth, "invalid height preserves width");
            }
        }

        private static void PreferencesInvalidAppearanceSave()
        {
            string path = PreferencesFixture();
            PreferencesStore store = new PreferencesStore(path);
            store.Save(new UserPreferences { OverlayFontName = "Consolas", OverlayFontSize = 12.5f, OverlayWidth = 500 });
            string original = File.ReadAllText(path);
            Action<UserPreferences>[] invalid = new Action<UserPreferences>[] {
                delegate(UserPreferences p) { p.OverlayFontName = null; },
                delegate(UserPreferences p) { p.OverlayFontName = ""; },
                delegate(UserPreferences p) { p.OverlayFontName = new string('x', 129); },
                delegate(UserPreferences p) { p.OverlayFontName = "Font\r\nInjected=True"; },
                delegate(UserPreferences p) { p.OverlayFontName = "Font\0Name"; },
                delegate(UserPreferences p) { p.OverlayFontName = "Font\tName"; },
                delegate(UserPreferences p) { p.OverlayFontSize = Single.NaN; },
                delegate(UserPreferences p) { p.OverlayFontSize = Single.PositiveInfinity; },
                delegate(UserPreferences p) { p.OverlayFontSize = Single.NegativeInfinity; },
                delegate(UserPreferences p) { p.OverlayFontSize = 5.99f; },
                delegate(UserPreferences p) { p.OverlayFontSize = 48.01f; },
                delegate(UserPreferences p) { p.OverlayWidth = 47; },
                delegate(UserPreferences p) { p.OverlayWidth = 1601; },
                delegate(UserPreferences p) { p.OverlayHeight = 19; },
                delegate(UserPreferences p) { p.OverlayHeight = 401; }
            };
            foreach (Action<UserPreferences> mutate in invalid)
            {
                UserPreferences invalidPreferences = new UserPreferences();
                mutate(invalidPreferences);
                ThrowsOf<ArgumentException>(delegate { store.Save(invalidPreferences); }, "invalid appearance rejected");
                Equal(original, File.ReadAllText(path), "invalid appearance preserves saved file");
            }
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "invalid appearance leaves no temporary files");
            string directory = Path.Combine(Path.GetDirectoryName(path), "must-not-create-appearance");
            PreferencesStore absent = new PreferencesStore(Path.Combine(directory, "settings.ini"));
            ThrowsOf<ArgumentException>(delegate { absent.Save(new UserPreferences { OverlayFontSize = Single.NaN }); }, "invalid size rejected before creating directory");
            Equal(false, Directory.Exists(directory), "invalid appearance has no directory side effect");
        }

        private static void FontInventory()
        {
            string[] names = OverlayAppearance.AvailableFontNames();
            Equal(true, names.Length > 0, "installed font inventory nonempty");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) Equal(true, StringComparer.OrdinalIgnoreCase.Compare(names[i - 1], names[i]) < 0, "font names sorted and unique");
                using (FontFamily family = new FontFamily(names[i]))
                    Equal(true, family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold), "listed family has a usable style");
            }
            Console.WriteLine("Installed usable font families: " + names.Length);
        }

        private static void FontFallback()
        {
            string fallback = OverlayAppearance.ResolveFontName("MonitorGate Missing Font " + Guid.NewGuid().ToString("N"));
            string expected;
            try { using (FontFamily segoe = new FontFamily("Segoe UI")) expected = segoe.Name; }
            catch (ArgumentException) { using (FontFamily generic = FontFamily.GenericSansSerif) expected = generic.Name; }
            Equal(expected, fallback, "missing font explicitly resolves to known fallback");
            using (Font font = OverlayAppearance.CreateFont(new UserPreferences { OverlayFontName = "Missing Font Fixture" }, 1))
                Equal(expected, font.FontFamily.Name, "font actually created with fallback family");
            string resolved = OverlayAppearance.ResolveFontName(expected.ToLowerInvariant());
            Equal(expected, resolved, "font resolution accepts different case");
        }

        private static void FontStyles()
        {
            Equal(FontStyle.Regular, OverlayAppearance.SelectFontStyle(true, true, false), "bold unavailable selects available regular");
            Equal(FontStyle.Bold, OverlayAppearance.SelectFontStyle(false, false, true), "regular unavailable selects available bold");
            Equal(FontStyle.Bold, OverlayAppearance.SelectFontStyle(true, true, true), "bold preference honored when available");
            Equal(FontStyle.Regular, OverlayAppearance.SelectFontStyle(false, true, true), "regular preference honored when available");
            ThrowsOf<ArgumentException>(delegate { OverlayAppearance.SelectFontStyle(false, false, false); }, "font without usable styles rejected");
            string[] names = OverlayAppearance.AvailableFontNames();
            string regularOnly = null, boldOnly = null;
            foreach (string name in names)
                using (FontFamily family = new FontFamily(name))
                {
                    bool regular = family.IsStyleAvailable(FontStyle.Regular), bold = family.IsStyleAvailable(FontStyle.Bold);
                    Equal(bold, OverlayAppearance.SupportsBold(name), "bold capability reflects family metadata");
                    if (regular && !bold && regularOnly == null) regularOnly = name;
                    if (!regular && bold && boldOnly == null) boldOnly = name;
                }
            foreach (bool bold in new bool[] { false, true })
                using (Font font = OverlayAppearance.CreateFont(new UserPreferences { OverlayFontBold = bold }, 1))
                    Equal(true, font.FontFamily.IsStyleAvailable(font.Style), "created font has a supported style");
            if (regularOnly != null)
                using (Font font = OverlayAppearance.CreateFont(new UserPreferences { OverlayFontName = regularOnly, OverlayFontBold = true }, 1))
                    Equal(FontStyle.Regular, font.Style, "requested unavailable bold falls back to regular");
            else Console.WriteLine("NOTE: no installed regular-only family to exercise unavailable Bold");
            if (boldOnly != null)
                using (Font font = OverlayAppearance.CreateFont(new UserPreferences { OverlayFontName = boldOnly, OverlayFontBold = false }, 1))
                    Equal(FontStyle.Bold, font.Style, "requested unavailable regular falls back to bold");
            else Console.WriteLine("NOTE: no installed bold-only family to exercise unavailable Regular");
        }

        private static void AppearanceMetrics()
        {
            UserPreferences preferences = new UserPreferences();
            using (Font font = OverlayAppearance.CreateFont(preferences, 1))
            {
                Equal(GraphicsUnit.Pixel, font.Unit, "render font uses explicit pixels");
                Equal(13f, font.Size, "9.75pt maps to 13px at 96 DPI");
            }
            using (Font font = OverlayAppearance.CreateFont(preferences, 1.5f)) Equal(19.5f, font.Size, "font scales for 144 DPI");
            Size baseline = OverlayAppearance.MeasureBox(preferences, 1);
            Size largerDpi = OverlayAppearance.MeasureBox(preferences, 1.5f);
            Equal(true, largerDpi.Width > baseline.Width && largerDpi.Height > baseline.Height, "automatic box increases with DPI");
            RenderTextFits(preferences, baseline, 1);
            RenderTextFits(preferences, largerDpi, 1.5f);
            preferences.OverlayFontSize = 48;
            preferences.OverlayFontBold = true;
            Size largerFont = OverlayAppearance.MeasureBox(preferences, 1);
            Equal(true, largerFont.Width > baseline.Width && largerFont.Height > baseline.Height, "automatic box increases for larger bold text");
            RenderTextFits(preferences, largerFont, 1);
            ThrowsOf<ArgumentOutOfRangeException>(delegate { OverlayAppearance.CreateFont(preferences, 0); }, "zero DPI scale rejected");
        }

        private static void AppearanceManualMinimum()
        {
            UserPreferences preferences = new UserPreferences { OverlayAutoSize = false, OverlayWidth = 1000, OverlayHeight = 200 };
            Equal(new Size(1000, 200), OverlayAppearance.MeasureBox(preferences, 1), "larger manual dimensions honored");
            Equal(new Size(1500, 300), OverlayAppearance.MeasureBox(preferences, 1.5f), "manual dimensions are logical 96 DPI pixels");
            preferences.OverlayWidth = 48;
            preferences.OverlayHeight = 20;
            preferences.OverlayFontSize = 48;
            Size safe = OverlayAppearance.MeasureBox(preferences, 1);
            Equal(true, safe.Width > 48 && safe.Height > 20, "small manual box expanded for large text");
            RenderTextFits(preferences, safe, 1);
        }

        private static void RenderTextFits(UserPreferences preferences, Size box, float scale)
        {
            using (Bitmap bitmap = new Bitmap(box.Width, box.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                preferences.OverlayForeground = Color.White;
                TextRenderingHint previous = graphics.TextRenderingHint;
                OverlayAppearance.DrawText(graphics, new Rectangle(System.Drawing.Point.Empty, box), preferences, scale);
                Equal(previous, graphics.TextRenderingHint, "draw restores caller rendering hint");
                int minX = box.Width, minY = box.Height, maxX = -1, maxY = -1, text = 0;
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.R <= 20 && pixel.G <= 20 && pixel.B <= 20) continue;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); text++;
                    }
                Equal(true, text > 100, "measured box renders visible text");
                Equal(true, minX > 2 && minY > 1 && maxX < box.Width - 3 && maxY < box.Height - 2,
                    "rendered glyphs have safe margins on every side");
            }
        }

        private static void AppearanceFitMonitor()
        {
            Equal(new Size(1280, 284), OverlayAppearance.FitToMonitor(new Size(1600, 400), new Rectangle(-1280, 0, 1280, 300), 1),
                "oversize badge fits monitor width and top margin");
            Equal(new Size(100, 200), OverlayAppearance.FitToMonitor(new Size(100, 200), new Rectangle(0, 0, 1280, 720), 1.5f),
                "fitting badge keeps its requested dimensions");
            Equal(new Size(1280, 276), OverlayAppearance.FitToMonitor(new Size(2000, 500), new Rectangle(0, 0, 1280, 300), 1.5f),
                "top margin scales at 144 DPI");
            Equal(new Size(1, 1), OverlayAppearance.FitToMonitor(new Size(20, 20), Rectangle.Empty, 1),
                "empty monitor bounds remain at least one pixel");
        }

        private static Point P(int x, int y) { return new Point { X = x, Y = y }; }

        private static void MotionRequiresPermission()
        {
            MovementIndicator indicator = new MovementIndicator();
            Equal(false, indicator.Update(true, true, P(10, 10), 0), "first sample does not invent movement");
            Equal(false, indicator.Update(true, true, P(10, 10), 10), "held key while still does not show");
            Equal(false, indicator.Update(true, false, P(11, 10), 20), "moving without transfer permission hidden");
            Equal(false, indicator.Update(true, true, P(11, 10), 30), "pressing key at rest does not show");
            Equal(true, indicator.Update(true, true, P(12, 10), 40), "horizontal movement with key shows");
            Equal(true, indicator.Update(true, true, P(12, 11), 50), "vertical movement with key shows");
        }

        private static void MotionIdleBoundary()
        {
            Equal(180, MovementIndicator.IdleHideMilliseconds, "specified idle hide duration");
            MovementIndicator indicator = new MovementIndicator();
            indicator.Update(true, true, P(0, 0), 0);
            Equal(true, indicator.Update(true, true, P(1, 0), 100), "movement starts hold");
            Equal(true, indicator.Update(true, true, P(1, 0), 100 + MovementIndicator.IdleHideMilliseconds - 1), "visible until one ms before idle timeout");
            Equal(false, indicator.Update(true, true, P(1, 0), 100 + MovementIndicator.IdleHideMilliseconds), "hidden at exact idle timeout");
            Equal(false, indicator.Update(true, true, P(1, 0), 1000), "remains hidden while idle");
            Equal(true, indicator.Update(true, true, P(2, 0), 1001), "new motion reopens badge");
        }

        private static void MotionHideResets()
        {
            MovementIndicator indicator = new MovementIndicator();
            indicator.Update(true, true, P(0, 0), 0);
            indicator.Update(true, true, P(1, 0), 1);
            Equal(false, indicator.Update(true, false, P(1, 0), 2), "key release hides immediately");
            Equal(false, indicator.Update(true, true, P(1, 0), 3), "new key press does not restore stale motion");
            Equal(true, indicator.Update(true, true, P(2, 0), 4), "motion reopens after key press");
            Equal(false, indicator.Update(false, true, P(3, 0), 5), "user hides overlay even during motion");
            Equal(false, indicator.Update(true, true, P(3, 0), 6), "reenabling setting requires fresh movement");
            Equal(true, indicator.Update(true, true, P(3, 1), 7), "fresh movement reopens after setting restored");
        }

        private static void MotionResetAndCoordinates()
        {
            MovementIndicator indicator = new MovementIndicator();
            indicator.Update(true, true, P(-1900, -1000), 0);
            Equal(true, indicator.Update(true, true, P(-1899, -1000), 1), "negative monitor coordinates count as movement");
            indicator.Reset();
            Equal(false, indicator.Update(true, true, P(100, 100), 2), "reset starts with fresh sample even at distant coordinate");
            Equal(true, indicator.Update(true, true, P(100, 101), 3), "movement after fresh sample shows");
        }

        private static void MotionBackwardsClock()
        {
            MovementIndicator indicator = new MovementIndicator();
            indicator.Update(true, true, P(0, 0), 999);
            indicator.Update(true, true, P(1, 0), 1000);
            Equal(false, indicator.Update(true, true, P(1, 0), 900), "earlier timestamp cannot extend recent motion");
            Equal(true, indicator.Update(true, true, P(2, 0), 901), "new motion establishes a fresh timestamp");
        }

        private sealed class FakeStartupStore : IStartupStore
        {
            internal string Value;
            internal int Writes, Deletes;
            internal bool FailRead, FailWrite, FailDelete;
            public string Read()
            {
                if (FailRead) throw new InvalidOperationException("fake startup read failure");
                return Value;
            }
            public void Write(string value)
            {
                if (FailWrite) throw new InvalidOperationException("fake startup write failure");
                Writes++;
                Value = value;
            }
            public void Delete()
            {
                if (FailDelete) throw new InvalidOperationException("fake startup delete failure");
                Deletes++;
                Value = null;
            }
        }

        private sealed class FakeDesktop : IDesktop
        {
            internal readonly Dictionary<int, Rect> Monitors = new Dictionary<int, Rect>();
            internal readonly Rect VirtualBounds = R(-1920, -1080, 1920, 1080);
            internal Rect Clip;
            internal int Monitor = 1;
            internal int Confines, Releases, CurrentMonitorReads, BoundsReads;
            internal bool FailConfine, FailReadClip, FailRelease;

            internal FakeDesktop()
            {
                Monitors[1] = R(0, 0, 1920, 1080);
                Monitors[2] = R(-1920, 0, 0, 1080);
                Monitors[3] = R(0, -1080, 1920, 0);
                Clip = VirtualBounds;
            }

            public IntPtr CurrentMonitor() { CurrentMonitorReads++; return new IntPtr(Monitor); }
            public Rect MonitorBounds(IntPtr monitor) { BoundsReads++; return Monitors[monitor.ToInt32()]; }
            public Rect CurrentClip()
            {
                if (FailReadClip) throw new InvalidOperationException("fake clip read failure");
                return Clip;
            }
            public void Confine(Rect rect)
            {
                Confines++;
                if (FailConfine) throw new InvalidOperationException("fake confine failure");
                Clip = rect;
            }
            public void Release()
            {
                Releases++;
                if (FailRelease) throw new InvalidOperationException("fake release failure");
                Clip = VirtualBounds;
            }
        }
    }
}
