// Build with the .NET Framework C# compiler. No NuGet packages required.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MonitorGate")]
[assembly: AssemblyDescription("Hold Ctrl to move the pointer between monitors")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace MonitorGate
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect : IEquatable<Rect>
    {
        public int Left, Top, Right, Bottom;
        public bool Equals(Rect other)
        {
            return Left == other.Left && Top == other.Top &&
                Right == other.Right && Bottom == other.Bottom;
        }
        public bool Contains(Rect other)
        {
            return Left <= other.Left && Top <= other.Top &&
                Right >= other.Right && Bottom >= other.Bottom;
        }
        public override string ToString()
        {
            return String.Format("({0}, {1}) - ({2}, {3})", Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardEvent
    {
        public uint Key, ScanCode, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    internal static class Native
    {
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        internal delegate bool MonitorProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ClipCursor(ref Rect rect);
        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        internal static extern bool ReleaseCursor(IntPtr rect);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClipCursor(out Rect rect);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc callback, IntPtr data);
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index,
            out int information, uint length, out uint needed);

        internal static bool IsInputDesktop()
        {
            int receivingInput;
            uint needed;
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            return desktop != IntPtr.Zero && GetUserObjectInformation(desktop, 6 /* UOI_IO */,
                out receivingInput, 4, out needed) && receivingInput != 0;
        }

        internal static void Check(bool success, string operation)
        {
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
        }
        internal static Rect Bounds(IntPtr monitor)
        {
            MonitorInfo info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            Check(GetMonitorInfo(monitor, ref info), "GetMonitorInfo");
            return info.Monitor; // Includes the taskbar; coordinates can be negative.
        }
    }

    internal interface IDesktop
    {
        IntPtr CurrentMonitor();
        Rect MonitorBounds(IntPtr monitor);
        Rect CurrentClip();
        void Confine(Rect rect);
        void Release();
    }

    internal sealed class WindowsDesktop : IDesktop
    {
        public IntPtr CurrentMonitor()
        {
            Point point;
            Native.Check(Native.GetCursorPos(out point), "GetCursorPos");
            IntPtr monitor = Native.MonitorFromPoint(point, 2 /* nearest */);
            if (monitor == IntPtr.Zero) throw new InvalidOperationException("모니터를 찾을 수 없습니다.");
            return monitor;
        }
        public Rect MonitorBounds(IntPtr monitor) { return Native.Bounds(monitor); }
        public Rect CurrentClip()
        {
            Rect rect;
            Native.Check(Native.GetClipCursor(out rect), "GetClipCursor");
            return rect;
        }
        public void Confine(Rect rect) { Native.Check(Native.ClipCursor(ref rect), "ClipCursor"); }
        public void Release() { Native.Check(Native.ReleaseCursor(IntPtr.Zero), "ClipCursor(NULL)"); }
    }

    // All calls occur on the dedicated input thread. The tray cannot stall this state machine.
    internal sealed class Gate
    {
        private readonly IDesktop desktop;
        private IntPtr target;
        private Rect? ownedClip;
        private Rect bounds;
        internal bool Enabled = true;
        internal bool Suspended;
        internal Gate(IDesktop desktop) { this.desktop = desktop; }

        internal void Update(bool allowCrossing)
        {
            if (!Enabled || Suspended || allowCrossing)
            {
                Release();
                return;
            }
            if (target == IntPtr.Zero)
            {
                target = desktop.CurrentMonitor();
                bounds = desktop.MonitorBounds(target);
            }
            Rect current = desktop.CurrentClip();
            if (current.Equals(bounds)) return;
            // Respect a tighter clip inside this monitor, e.g. a game's own clip.
            if (bounds.Contains(current))
            {
                ownedClip = null;
                return;
            }
            desktop.Confine(bounds);
            ownedClip = bounds;
        }

        internal void RefreshDisplay()
        {
            // Geometry/handles may have changed. Never restore an old screen rectangle.
            Release();
        }

        internal void Release()
        {
            target = IntPtr.Zero;
            if (!ownedClip.HasValue) return;
            // Do not remove a different clip subsequently installed by another app.
            if (desktop.CurrentClip().Equals(ownedClip.Value)) desktop.Release();
            ownedClip = null;
        }
    }

    internal enum TransferKey { Ctrl, Alt, Shift, RightCtrl }

    internal static class KeysForTransfer
    {
        internal static int[] Codes(TransferKey key)
        {
            switch (key)
            {
                case TransferKey.Alt: return new int[] { 0xA4, 0xA5 };
                case TransferKey.Shift: return new int[] { 0xA0, 0xA1 };
                case TransferKey.RightCtrl: return new int[] { 0xA3 };
                default: return new int[] { 0xA2, 0xA3 };
            }
        }
        internal static string Label(TransferKey key)
        {
            return key == TransferKey.RightCtrl ? "오른쪽 Ctrl" : key.ToString();
        }
    }

    internal sealed class InputWorker : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly EventWaitHandle stopRequest;
        private Control dispatcher;
        private Gate gate;
        private IntPtr hook;
        private Native.HookProc hookCallback;
        private int[] keyCodes;
        private readonly Dictionary<int, bool> keyStates = new Dictionary<int, bool>();
        private volatile bool stopping;
        private volatile bool sessionLocked;
        private volatile bool sleeping;
        private volatile bool displayChanged;
        private bool desktopInactive;
        internal volatile bool Enabled = true;
        internal volatile bool CrossingAllowed;
        internal volatile bool Exited;
        internal volatile string Error;
        internal volatile string Warning;
        internal volatile TransferKey SelectedKey;
        internal event Action StopRequested;

        internal InputWorker(TransferKey key, EventWaitHandle stopRequest, bool enabled)
        {
            SelectedKey = key;
            Enabled = enabled;
            this.stopRequest = stopRequest;
            thread = new Thread(Run);
            thread.Name = "MonitorGate input";
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne();
        }

        private void Run()
        {
            System.Windows.Forms.Timer timer = null;
            try
            {
                dispatcher = new Control();
                IntPtr handle = dispatcher.Handle; // Creates the input thread's message queue.
                gate = new Gate(new WindowsDesktop());
                SetKeyNow(SelectedKey);
                hookCallback = OnKeyboard;
                hook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookCallback,
                    Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    Warning = "키 훅을 설치하지 못해 10ms 간격 감지로 동작합니다.";
                SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerChanged;
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 10;
                timer.Tick += OnTick;
                timer.Start();
                ready.Set();
                Application.Run();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Enabled = false;
            }
            finally
            {
                stopping = true;
                CrossingAllowed = false;
                ready.Set();
                if (timer != null) timer.Dispose();
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerChanged;
                if (gate != null) TryRelease();
                if (dispatcher != null) dispatcher.Dispose();
                Exited = true;
            }
        }

        private void OnTick(object sender, EventArgs args)
        {
            if (stopping || stopRequest.WaitOne(0))
            {
                stopping = true;
                Action callback = StopRequested;
                if (callback != null) callback();
                Application.ExitThread();
                return;
            }
            try
            {
                if (!Native.IsInputDesktop())
                {
                    SuspendForDesktop();
                    return;
                }
                if (desktopInactive)
                {
                    desktopInactive = false;
                    displayChanged = true;
                }
                if (displayChanged)
                {
                    displayChanged = false;
                    gate.RefreshDisplay();
                }
                // A fallback if a hook is removed by Windows, or events were missed on lock.
                foreach (int key in keyCodes) keyStates[key] = Native.GetAsyncKeyState(key) < 0;
                UpdateGate();
            }
            catch (Exception ex) { HandleFailure(ex); }
        }

        private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !stopping)
            {
                try
                {
                    int kind = message.ToInt32();
                    bool down = kind == 0x100 || kind == 0x104;
                    bool up = kind == 0x101 || kind == 0x105;
                    if (down || up)
                    {
                        KeyboardEvent input = (KeyboardEvent)Marshal.PtrToStructure(data, typeof(KeyboardEvent));
                        int key = NormalizeKey(input);
                        if (Array.IndexOf(keyCodes, key) >= 0)
                        {
                            // The async key state is still OLD inside this callback.
                            // Use the actual event to lock/unlock before forwarding the key.
                            keyStates[key] = down;
                            UpdateGate();
                        }
                    }
                }
                catch (Exception ex) { HandleFailure(ex); }
            }
            return Native.CallNextHookEx(hook, code, message, data); // Never consume Ctrl/Alt.
        }

        internal static int NormalizeKey(KeyboardEvent input)
        {
            int key = (int)input.Key;
            bool right = (input.Flags & 1) != 0;
            if (key == 0x11) return right ? 0xA3 : 0xA2;
            if (key == 0x12) return right ? 0xA5 : 0xA4;
            if (key == 0x10) return input.ScanCode == 0x36 ? 0xA1 : 0xA0;
            return key;
        }

        private void UpdateGate()
        {
            gate.Enabled = Enabled;
            gate.Suspended = sessionLocked || sleeping || desktopInactive;
            bool allow = false;
            foreach (int key in keyCodes) allow |= keyStates[key];
            bool canCross = Enabled && !gate.Suspended && allow;
            if (!canCross) CrossingAllowed = false;
            gate.Update(allow);
            CrossingAllowed = canCross;
        }

        private void SetKeyNow(TransferKey key)
        {
            SelectedKey = key;
            keyCodes = KeysForTransfer.Codes(key);
            keyStates.Clear();
            foreach (int code in keyCodes) keyStates[code] = Native.GetAsyncKeyState(code) < 0;
        }

        private void Post(Action action)
        {
            if (stopping || dispatcher == null || dispatcher.IsDisposed) return;
            try { dispatcher.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        internal void SetKey(TransferKey key)
        {
            Post(delegate
            {
                try { gate.Release(); SetKeyNow(key); UpdateGate(); }
                catch (Exception ex) { HandleFailure(ex); }
            });
        }

        internal void Toggle()
        {
            Post(delegate
            {
                try
                {
                    Enabled = !Enabled;
                    Error = null;
                    gate.Release();
                    UpdateGate();
                }
                catch (Exception ex) { HandleFailure(ex); }
            });
        }

        internal void SetEnabled(bool enabled)
        {
            Post(delegate
            {
                try
                {
                    Enabled = enabled;
                    Error = null;
                    gate.Release();
                    UpdateGate();
                }
                catch (Exception ex) { HandleFailure(ex); }
            });
        }

        private void FailOpen(Exception error)
        {
            Enabled = false;
            CrossingAllowed = false;
            Error = "제한을 일시정지했습니다: " + error.Message;
            TryRelease();
        }

        private void SuspendForDesktop()
        {
            CrossingAllowed = false;
            if (!desktopInactive)
            {
                desktopInactive = true;
                gate.Suspended = true;
                TryRelease();
            }
        }

        private void HandleFailure(Exception error)
        {
            // UAC/lock screens belong to another desktop. Keep the user's enabled state
            // and reacquire on return instead of permanently disabling the utility.
            if (!Native.IsInputDesktop()) SuspendForDesktop();
            else FailOpen(error);
        }

        private void TryRelease()
        {
            try { gate.Release(); }
            catch { Native.ReleaseCursor(IntPtr.Zero); }
        }

        private void OnDisplayChanged(object sender, EventArgs e) { displayChanged = true; }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
                sessionLocked = true;
            if (e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
                sessionLocked = true;
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            {
                displayChanged = true;
                sessionLocked = false;
            }
            if (e.Reason == SessionSwitchReason.RemoteConnect || e.Reason == SessionSwitchReason.ConsoleConnect)
            {
                displayChanged = true;
                sessionLocked = false;
            }
        }
        private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) sleeping = true;
            if (e.Mode == PowerModes.Resume) { displayChanged = true; sleeping = false; }
        }

        public void Dispose()
        {
            stopping = true;
            CrossingAllowed = false;
            PostExit();
            thread.Join(); // The worker must stop before cleanup; it must never reclip afterwards.
            ready.Dispose();
        }
        private void PostExit()
        {
            if (dispatcher == null || dispatcher.IsDisposed) return;
            try { dispatcher.BeginInvoke((Action)Application.ExitThread); }
            catch (InvalidOperationException) { }
        }
    }

    // A topmost, click-through status badge. Showing it never activates a window.
    internal sealed class TransferOverlay : Form
    {
        private float scale = 1;
        private UserPreferences appearance = new UserPreferences();
        private Size desiredSize;
        internal TransferOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            ForeColor = Color.White;
            Opacity = 1.0;
            DoubleBuffered = true;
            RecalculateSize();
        }
        internal void ApplyAppearance(UserPreferences preferences)
        {
            if (preferences == null) throw new ArgumentNullException("preferences");
            if (preferences.OverlayBackground.A != 255 || preferences.OverlayForeground.A != 255)
                throw new ArgumentException("상태 박스에는 불투명한 색상을 사용해야 합니다.");
            appearance = new UserPreferences
            {
                OverlayBackground = preferences.OverlayBackground,
                OverlayForeground = preferences.OverlayForeground,
                OverlayFontName = preferences.OverlayFontName,
                OverlayFontSize = preferences.OverlayFontSize,
                OverlayFontBold = preferences.OverlayFontBold,
                OverlayAutoSize = preferences.OverlayAutoSize,
                OverlayWidth = preferences.OverlayWidth,
                OverlayHeight = preferences.OverlayHeight
            };
            ApplyColors(appearance.OverlayBackground, appearance.OverlayForeground);
            RecalculateSize();
        }
        private void RecalculateSize()
        {
            desiredSize = OverlayAppearance.MeasureBox(appearance, scale);
            Size = desiredSize;
            Invalidate();
        }
        internal void ApplyColors(Color background, Color foreground)
        {
            if (background.A != 255 || foreground.A != 255)
                throw new ArgumentException("상태 박스에는 불투명한 색상을 사용해야 합니다.");
            appearance.OverlayBackground = background;
            appearance.OverlayForeground = foreground;
            BackColor = background;
            ForeColor = foreground;
            Invalidate();
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A fully opaque layered window still needs explicit initialization.
            // Keeping LAYERED + TRANSPARENT makes clicks pass to other apps.
            Native.Check(Native.SetLayeredWindowAttributes(Handle, 0, 255, 2 /* LWA_ALPHA */),
                "SetLayeredWindowAttributes");
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT */ |
                    0x08000000 /* NOACTIVATE */ | 0x80000 /* LAYERED */;
                return parameters;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84 /* WM_NCHITTEST */)
            {
                message.Result = new IntPtr(-1 /* HTTRANSPARENT */);
                return;
            }
            base.WndProc(ref message);
        }
        internal void FollowCursor(bool allowed)
        {
            if (!allowed) { if (Visible) Hide(); return; }
            try
            {
                WindowsDesktop desktop = new WindowsDesktop();
                Rect bounds = desktop.MonitorBounds(desktop.CurrentMonitor());
                // Place on the new monitor first, then obtain that window's DPI.
                Location = new System.Drawing.Point(bounds.Left + (bounds.Right - bounds.Left - Width) / 2,
                    bounds.Top + (int)(16 * scale));
                uint dpi = 96;
                try { dpi = Native.GetDpiForWindow(Handle); }
                catch (EntryPointNotFoundException) { }
                float nextScale = (dpi == 0 ? 96 : dpi) / 96f;
                if (nextScale != scale)
                {
                    scale = nextScale;
                    RecalculateSize();
                }
                // Restore the requested size on larger monitors; keep the box within smaller ones.
                Size fitted = OverlayAppearance.FitToMonitor(desiredSize,
                    new Rectangle(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), scale);
                if (Size != fitted) { Size = fitted; Invalidate(); }
                Location = new System.Drawing.Point(bounds.Left + (bounds.Right - bounds.Left - Width) / 2,
                    bounds.Top + (int)(16 * scale));
                if (!Visible) Show();
            }
            catch { if (Visible) Hide(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            OverlayAppearance.DrawText(e.Graphics, ClientRectangle, appearance, scale);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 8 || Height < 8) return;
            int diameter = Math.Max(8, (int)(8 * scale));
            using (System.Drawing.Drawing2D.GraphicsPath shape = new System.Drawing.Drawing2D.GraphicsPath())
            {
                shape.AddArc(0, 0, diameter, diameter, 180, 90);
                shape.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
                shape.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
                shape.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
                shape.CloseFigure();
                Region old = Region;
                Region = new Region(shape);
                if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class TrayWindow : Form
    {
        private readonly InputWorker worker;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pause;
        private readonly ToolStripMenuItem status;
        private readonly Dictionary<TransferKey, ToolStripMenuItem> keyItems = new Dictionary<TransferKey, ToolStripMenuItem>();
        private readonly System.Windows.Forms.Timer statusTimer;
        private readonly System.Windows.Forms.Timer overlayTimer;
        private readonly TransferOverlay overlay;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem overlayItem;
        private readonly PreferencesStore preferencesStore;
        private readonly StartupRegistration startup;
        private UserPreferences preferences;
        private readonly MovementIndicator motion = new MovementIndicator();
        private readonly Stopwatch motionClock = Stopwatch.StartNew();
        private bool pauseHotkey, exitHotkey;
        private bool cleaned;
        private bool settingsOpen;
        private string reportedError;

        internal TrayWindow(InputWorker worker, PreferencesStore preferencesStore,
            UserPreferences preferences, StartupRegistration startup)
        {
            this.worker = worker;
            this.preferencesStore = preferencesStore;
            this.preferences = preferences;
            this.startup = startup;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            WindowState = FormWindowState.Minimized;
            ContextMenuStrip menu = new ContextMenuStrip();
            status = new ToolStripMenuItem("Ctrl을 누른 채 모니터 이동");
            status.Enabled = false;
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem choose = new ToolStripMenuItem("모니터 이동 키");
            foreach (TransferKey key in Enum.GetValues(typeof(TransferKey)))
            {
                TransferKey captured = key;
                ToolStripMenuItem item = new ToolStripMenuItem(KeysForTransfer.Label(key));
                item.Click += delegate { ChangeTransferKey(captured); };
                keyItems.Add(key, item);
                choose.DropDownItems.Add(item);
            }
            menu.Items.Add(choose);
            overlayItem = new ToolStripMenuItem("이동 상태 박스 표시");
            overlayItem.Checked = preferences.ShowOverlay;
            overlayItem.Click += delegate
            {
                UserPreferences next = CopyPreferences();
                next.ShowOverlay = !next.ShowOverlay;
                SavePreferences(next);
            };
            menu.Items.Add(overlayItem);
            startupItem = new ToolStripMenuItem("Windows 로그인 시 자동 실행 등록");
            startupItem.Click += ToggleStartup;
            menu.Items.Add(startupItem);
            ToolStripMenuItem settings = new ToolStripMenuItem("설정...");
            settings.Click += delegate { ShowSettings(); };
            menu.Items.Add(settings);
            menu.Opening += delegate { RefreshStartupStatus(); };
            menu.Items.Add(new ToolStripSeparator());
            pause = new ToolStripMenuItem("일시정지 / 재개  (Ctrl+Alt+F9)");
            pause.Click += delegate { TogglePause(); };
            menu.Items.Add(pause);
            ToolStripMenuItem exit = new ToolStripMenuItem("종료  (Ctrl+Alt+F10)");
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Shield;
            tray.ContextMenuStrip = menu;
            tray.Text = "MonitorGate: Ctrl을 누른 채 모니터 이동";
            tray.Visible = true;
            tray.DoubleClick += delegate { TogglePause(); };
            worker.StopRequested += RequestClose;
            overlay = new TransferOverlay();
            overlay.ApplyAppearance(preferences);
            overlayTimer = new System.Windows.Forms.Timer();
            overlayTimer.Interval = 25;
            overlayTimer.Tick += UpdateOverlay;
            overlayTimer.Start();
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 250;
            statusTimer.Tick += UpdateStatus;
            statusTimer.Start();
        }

        private UserPreferences CopyPreferences()
        {
            return new UserPreferences { TransferKey = preferences.TransferKey,
                StartEnabled = preferences.StartEnabled, ShowOverlay = preferences.ShowOverlay,
                OverlayBackground = preferences.OverlayBackground,
                OverlayForeground = preferences.OverlayForeground,
                OverlayFontName = preferences.OverlayFontName,
                OverlayFontSize = preferences.OverlayFontSize,
                OverlayFontBold = preferences.OverlayFontBold,
                OverlayAutoSize = preferences.OverlayAutoSize,
                OverlayWidth = preferences.OverlayWidth,
                OverlayHeight = preferences.OverlayHeight };
        }
        private bool SavePreferences(UserPreferences next)
        {
            try
            {
                preferencesStore.Save(next);
                preferences = next;
                overlay.ApplyAppearance(next);
                overlayItem.Checked = next.ShowOverlay;
                if (!next.ShowOverlay) { motion.Reset(); overlay.Hide(); }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("설정을 저장하지 못했습니다: " + ex.Message, "MonitorGate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
        private void ChangeTransferKey(TransferKey key)
        {
            UserPreferences next = CopyPreferences();
            next.TransferKey = key;
            if (SavePreferences(next)) { motion.Reset(); worker.SetKey(key); }
        }
        private void RefreshStartupStatus()
        {
            try
            {
                StartupStatus state = startup.ReadStatus(Application.ExecutablePath);
                startupItem.Checked = state != StartupStatus.Missing;
                startupItem.Text = state == StartupStatus.OtherLocation ?
                    "Windows 자동 실행 등록 (다른 실행 파일 위치)" : "Windows 로그인 시 자동 실행 등록";
                startupItem.Enabled = true;
            }
            catch
            {
                startupItem.Checked = false;
                startupItem.Text = "자동 실행 등록 상태를 읽을 수 없음";
                startupItem.Enabled = false;
            }
        }
        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                StartupStatus state = startup.ReadStatus(Application.ExecutablePath);
                startup.SetEnabled(state == StartupStatus.Missing, Application.ExecutablePath);
                RefreshStartupStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("자동 실행 설정을 변경하지 못했습니다: " + ex.Message, "MonitorGate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        internal void ShowSettings()
        {
            if (settingsOpen) return;
            settingsOpen = true;
            bool restoreEnabled = worker.Enabled;
            worker.SetEnabled(false);
            overlay.Hide();
            motion.Reset();
            try
            {
                UserPreferences initial = CopyPreferences();
                initial.TransferKey = worker.SelectedKey;
                using (SettingsDialog dialog = new SettingsDialog(initial,
                    startup.ReadStatus(Application.ExecutablePath)))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    if (!SavePreferences(dialog.ResultPreferences)) return;
                    worker.SetKey(preferences.TransferKey);
                    if (dialog.ChangedStartup)
                    {
                        try { startup.SetEnabled(dialog.ResultStartupEnabled, Application.ExecutablePath); }
                        catch (Exception ex)
                        {
                            MessageBox.Show("다른 설정은 저장했습니다. 자동 실행 등록은 변경하지 못했습니다: " +
                                ex.Message, "MonitorGate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    RefreshStartupStatus();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "MonitorGate 설정"); }
            finally
            {
                settingsOpen = false;
                worker.SetEnabled(restoreEnabled);
            }
        }
        private void TogglePause() { if (!settingsOpen) worker.Toggle(); }
        private void UpdateOverlay(object sender, EventArgs e)
        {
            Point position;
            if (settingsOpen || !preferences.ShowOverlay || !Native.GetCursorPos(out position))
            {
                motion.Reset();
                overlay.FollowCursor(false);
                return;
            }
            bool visible = motion.Update(preferences.ShowOverlay, worker.CrossingAllowed,
                position, motionClock.ElapsedMilliseconds);
            overlay.FollowCursor(visible);
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            pauseHotkey = Native.RegisterHotKey(Handle, 1, 0x4003, 0x78 /* F9 */);
            exitHotkey = Native.RegisterHotKey(Handle, 2, 0x4003, 0x79 /* F10 */);
            tray.ShowBalloonTip(5000, "MonitorGate 실행 중",
                (worker.Enabled ? KeysForTransfer.Label(worker.SelectedKey) + "을 누른 동안 모니터 이동이 가능합니다.\n" :
                    "포인터 제한을 일시정지한 상태로 시작했습니다.\n") +
                (pauseHotkey && exitHotkey ? "Ctrl+Alt+F9: 일시정지 / Ctrl+Alt+F10: 종료" :
                "일부 단축키가 사용 중입니다. 트레이 메뉴 또는 Release.cmd로 종료하세요."),
                ToolTipIcon.Info);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x312)
            {
                if (message.WParam.ToInt32() == 1) TogglePause();
                if (message.WParam.ToInt32() == 2) Close();
            }
            base.WndProc(ref message);
        }
        private void RequestClose()
        {
            try { BeginInvoke((Action)Close); }
            catch (InvalidOperationException) { }
        }
        private void UpdateStatus(object sender, EventArgs e)
        {
            if (worker.Exited)
            {
                if (worker.Error != null) MessageBox.Show(worker.Error, "MonitorGate 오류");
                Close();
                return;
            }
            string label = KeysForTransfer.Label(worker.SelectedKey);
            overlayItem.Checked = preferences.ShowOverlay;
            status.Text = worker.Enabled ? label + "을 누른 동안 모니터 이동" : "제한 일시정지 중";
            pause.Checked = !worker.Enabled;
            tray.Icon = worker.Enabled ? SystemIcons.Shield : SystemIcons.Information;
            tray.Text = worker.Enabled ? "MonitorGate: " + label + "을 누른 채 모니터 이동" : "MonitorGate: 일시정지";
            foreach (KeyValuePair<TransferKey, ToolStripMenuItem> item in keyItems)
                item.Value.Checked = item.Key == worker.SelectedKey;
            string error = worker.Error ?? worker.Warning;
            if (error != null && error != reportedError)
            {
                reportedError = error;
                tray.ShowBalloonTip(6000, "MonitorGate", error, ToolTipIcon.Warning);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleaned)
            {
                cleaned = true;
                worker.StopRequested -= RequestClose;
                statusTimer.Dispose();
                overlayTimer.Dispose();
                overlay.Dispose();
                if (pauseHotkey) Native.UnregisterHotKey(Handle, 1);
                if (exitHotkey) Native.UnregisterHotKey(Handle, 2);
                tray.Visible = false;
                tray.ContextMenuStrip.Dispose();
                tray.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        private const string MutexName = "Local\\MonitorGate.Instance.93D180F4";
        private const string StopName = "Local\\MonitorGate.Stop.93D180F4";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--diagnose") return Diagnose(args);
            if (args.Length > 0 && args[0] == "--release") return RequestRelease();
            bool paused = args.Length > 0 && args[0] == "--paused";
            bool startupLaunch = args.Length > 0 && args[0] == "--startup";
            bool openSettings = args.Length > 0 && args[0] == "--settings";
            PreferencesStore preferencesStore = PreferencesStore.ForCurrentUser();
            UserPreferences preferences;
            string settingsWarning = null;
            try { preferences = preferencesStore.Load(); }
            catch (Exception ex)
            {
                preferences = new UserPreferences();
                settingsWarning = "저장한 설정을 읽지 못해 기본값으로 시작했습니다: " + ex.Message;
            }
            TransferKey selected = preferences.TransferKey;
            if (args.Length > 1 || (!paused && !startupLaunch && !openSettings && args.Length > 0 &&
                (!Enum.TryParse<TransferKey>(args[0], true, out selected) ||
                !Enum.IsDefined(typeof(TransferKey), selected)))
                )
            {
                MessageBox.Show("사용법: MonitorGate.exe [Ctrl|Alt|Shift|RightCtrl|--paused|--settings|--startup]", "MonitorGate");
                return 2;
            }
            bool created;
            using (Mutex single = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    if (!startupLaunch)
                        MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘에서 MonitorGate를 확인하세요.", "MonitorGate");
                    return 0;
                }
                try
                {
                    using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName))
                    {
                        stop.Reset();
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        using (InputWorker worker = new InputWorker(selected, stop, !paused && preferences.StartEnabled))
                        using (TrayWindow tray = new TrayWindow(worker, preferencesStore, preferences,
                            new StartupRegistration(new RegistryStartupStore())))
                        {
                            if (settingsWarning != null)
                                worker.Warning = settingsWarning + (worker.Warning == null ? "" : "\n" + worker.Warning);
                            // Ensure the hidden window and emergency hotkeys exist before Run.
                            IntPtr handle = tray.Handle;
                            if (openSettings) tray.BeginInvoke((Action)tray.ShowSettings);
                            Application.Run(tray);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "MonitorGate 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { single.ReleaseMutex(); }
            }
            return 0;
        }

        private static int RequestRelease()
        {
            // Ask the running instance to EXIT first, so it cannot apply another clip.
            try
            {
                using (EventWaitHandle stop = EventWaitHandle.OpenExisting(StopName)) stop.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            using (Mutex single = new Mutex(false, MutexName))
            {
                bool acquired;
                try { acquired = single.WaitOne(5000); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show("프로그램이 응답하지 않습니다. 작업 관리자에서 MonitorGate를 종료한 후 Release.cmd를 다시 실행하세요.", "MonitorGate");
                    return 1;
                }
                try
                {
                    Native.Check(Native.ReleaseCursor(IntPtr.Zero), "ClipCursor(NULL)");
                }
                finally { single.ReleaseMutex(); }
            }
            return 0;
        }

        private static int Diagnose(string[] args)
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("MonitorGate read-only diagnostics (no cursor clipping or input hooks)");
                Native.MonitorProc callback = delegate(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data)
                {
                    lines.Add("Monitor " + (lines.Count) + ": " + Native.Bounds(monitor));
                    return true;
                };
                Native.Check(Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero), "EnumDisplayMonitors");
                lines.Add("Current clip: " + new WindowsDesktop().CurrentClip());
                lines.Add("This thread is on the input desktop: " + Native.IsInputDesktop());
                string output = String.Join(Environment.NewLine, lines.ToArray());
                if (args.Length == 2) File.WriteAllText(args[1], output);
                else Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
    }
}
