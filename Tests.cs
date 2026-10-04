using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
