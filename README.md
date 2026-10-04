# MonitorGate 1.2

Keep your pointer on its current monitor until you hold **Ctrl**. MonitorGate is a small Windows 10/11 tray app: hold either Ctrl key to move between monitors, then release it to keep the pointer on the monitor you reached.

While you hold the transfer key and move the mouse, a small **`Pointer movement enabled`** badge appears at the top center of the monitor containing the pointer. Version 1.2 uses a **black background, white text, and Segoe UI at 13 px at 96 DPI**, scaled for the display. The badge follows you between monitors and hides when you release the key or stop moving for about 180 ms. It passes clicks through and does not take keyboard focus.

![Pointer movement enabled badge](overlay-preview.png)

## Quick start

1. Extract the ZIP into a folder you want to keep.
2. Double-click `MonitorGate.exe`.
3. Use the mouse normally within your current monitor. To cross to another monitor, **hold Ctrl, move the mouse, then release Ctrl**.

The GitHub source package does not include an executable; build it first using the instructions below. The executable runs without an installer or administrator privileges. It uses .NET Framework 4.8 on Windows 10/11; Python, AutoHotkey, and the .NET SDK are not required. This is a personal build and the executable is unsigned.

If an older version is running, exit it with **Ctrl+Alt+F10** before starting the new version. Only one instance can run at a time.

Look for the shield icon in the taskbar notification area, including the hidden-icons menu. MonitorGate uses the monitor arrangement configured in Windows, including vertical layouts and monitors above or below one another. No coordinate setup is needed; make sure the Windows arrangement matches your physical setup.

## Controls

The app's menus and settings remain in Korean. Their labels are included below so you can identify them.

| Action | Control |
| --- | --- |
| Move between monitors | Hold Ctrl while moving the mouse |
| Pause or resume confinement | Ctrl+Alt+F9 |
| Exit and release confinement | Ctrl+Alt+F10 |
| Choose the transfer key | Right-click the shield → Transfer key (`모니터 이동 키`) |
| Open settings | Right-click the shield → Settings (`설정...`) |
| Enable or disable login startup | Settings, or right-click → Register login startup (`Windows 로그인 시 자동 실행 등록`) |
| Show or hide the badge | Settings, or right-click → Show movement badge (`이동 상태 박스 표시`) |
| Pause or resume from the tray | Double-click the shield, or use Pause / Resume (`일시정지 / 재개`) |
| Exit and explicitly release the pointer | Run `Release.cmd` from the same folder |

If another app already uses a hotkey, the startup notification reports it. Use the tray menu or `Release.cmd` instead. F12 is reserved for the Windows debugger, so it is not used. [Microsoft hotkey documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

Ctrl is the default transfer key. You can choose Alt, Shift, or Right Ctrl (`오른쪽 Ctrl`), and the choice is saved for future launches. To override the saved key for one launch:

```powershell
.\MonitorGate.exe RightCtrl
```

## Settings and login startup

Right-click the shield and select Settings (`설정...`).

- **Run at Windows login (`Windows 로그인 시 자동 실행`):** Starts MonitorGate after the current user signs into Windows. This is off by default and is registered only when you enable it. Administrator privileges are not required.
- **Enable confinement on launch (`실행할 때 포인터 제한 켜기`):** When enabled, future launches start with confinement active. When disabled, they start paused. This is separate from pausing the current session.
- **Show movement badge (`이동 상태 박스 표시`):** Hides or shows the top-of-screen badge. Disabling it leaves pointer confinement and key-controlled monitor crossing active.
- **Transfer key (`모니터 이동 키`):** Saves your preferred key for future launches. The badge responds to that key when you choose something other than Ctrl.

Enable login startup from the folder where you intend to keep the executable. If you move the folder, turn startup off and on again to register the new location. An entry pointing elsewhere is marked `Windows 자동 실행 등록 (다른 실행 파일 위치)` in the tray menu.

If you disable MonitorGate in **Windows Settings → Apps → Startup** or Task Manager, you must re-enable it there too. MonitorGate does not override that Windows setting. The dialog includes an **Open Windows startup settings** button (`Windows 시작 앱 설정 열기`).

Startup happens after Windows login, and Windows may delay running startup apps. [Microsoft's Run and RunOnce documentation](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)

The transfer key, initial confinement state, and badge preference are stored in `%LOCALAPPDATA%\MonitorGate\settings.ini`. Login startup uses the `MonitorGate` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turning startup off deletes only that value. To remove the app, disable login startup, exit MonitorGate, then delete its folder.

## Behavior and limitations

- Windows `ClipCursor` confines the pointer to the entire current monitor, including its taskbar.
- A dedicated thread handles key events. A timer with a 10 ms interval checks for missed events. Windows scheduling, elevated windows, and secure desktops can cause detection delays or limitations.
- Confinement is suspended during lock screens, UAC secure screens, and sleep, then reapplied when the normal desktop returns.
- Key events are not blocked. Ctrl+C, Ctrl+click, and other shortcuts keep working, and monitor crossing is allowed while Ctrl is held. Choose Right Ctrl if this interferes with your usual shortcuts.
- Fullscreen games and remote-control tools may set their own pointer confinement. Pause MonitorGate if they conflict or the badge is hidden. Smaller confinement regions set by games are preserved where possible.
- The badge appears only while the chosen transfer key is held and the pointer is moving. Holding the key without moving, or pausing confinement, keeps it hidden.
- Normal exit and detected errors release confinement applied by MonitorGate. If confinement remains after a forced exit, run `Release.cmd`. This helper explicitly releases the pointer, including confinement another app may have applied.
- Confinement applies only while MonitorGate is running. Login startup registration changes only when you enable or disable that setting.

## Build and test

The C# source is in `MonitorGate.cs`, `StartupSettings.cs`, and `SettingsDialog.cs`. After editing, run this from the app folder. Exit any instance using the executable you are rebuilding first.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

The execution-policy option applies only to that PowerShell process; it does not change the system policy. The script uses the .NET Framework compiler included with Windows and downloads no external packages.

`MonitorGate.csproj` is a .NET Framework 4.8 project for Visual Studio/MSBuild. That route requires the development tools and .NET Framework 4.8 reference assemblies. Use `Build.ps1` to build without installing those development tools.

Run the simulated-monitor, preferences, startup-registration, and badge-condition tests in `Tests.cs` with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify.ps1
```

These tests do not affect the real pointer, keyboard input, or Windows startup registration.

## Command-line options

Create a read-only monitor diagnostic file without enabling confinement or keyboard hooks:

```powershell
.\MonitorGate.exe --diagnose .\monitors.txt
```

Start paused, then enable confinement from the tray or with Ctrl+Alt+F9:

```powershell
.\MonitorGate.exe --paused
```

Open settings when starting a new instance:

```powershell
.\MonitorGate.exe --settings
```

`--startup` is the argument used by login startup registration and launches with the saved preferences. `Release.cmd` runs `MonitorGate.exe --release` to stop the app and explicitly release confinement. The supported key overrides are `Ctrl`, `Alt`, `Shift`, and `RightCtrl`.

The GitHub source package excludes executables, user preferences, and monitor diagnostics. See [GITHUB.md](GITHUB.md) for preparation and upload instructions.

## Windows API references

- [ClipCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor)
- [MonitorFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfrompoint)
- [MONITORINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)
- [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
