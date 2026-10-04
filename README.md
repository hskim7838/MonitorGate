# MonitorGate

[English](README.md) · [한국어](README.ko.md)

Keep your mouse pointer from accidentally slipping onto another monitor. MonitorGate keeps it on the current monitor until you **hold Ctrl and move the mouse**. Release Ctrl to keep the pointer on the monitor you reached.

The **Pointer movement enabled** badge appears at the top center of the pointer's monitor.

![Pointer movement enabled badge](overlay-preview.png)

## Get started

**Requirements:** Windows 10 or 11 and .NET Framework 4.8. MonitorGate is portable; no installer, Python, AutoHotkey, or .NET SDK is needed.

**This source checkout does not contain `MonitorGate.exe`.** Download or clone the source, then build it:

1. Extract the source ZIP if necessary.
2. Open PowerShell in the folder containing `Build.ps1`.
3. Run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
   ```

4. Double-click the generated `MonitorGate.exe`.
5. Find the shield icon in the taskbar notification area. Check the hidden-icons menu if necessary.

If a portable ZIP is available under this repository's **Releases**, extract and run it instead.

On first launch, confinement is enabled and the transfer key is Ctrl. Hold either Ctrl key, move onto another monitor, then release it. The taskbar remains accessible. MonitorGate uses Windows' monitor arrangement, including vertical layouts. Match it to your physical setup in display settings.

## Everyday controls

The menus are in Korean; use these labels to find each action.

| Action | Shortcut or tray menu |
| --- | --- |
| Move between monitors | Hold Ctrl while moving |
| Pause or resume | **Ctrl+Alt+F9**, double-click the shield, or `일시정지 / 재개` |
| Exit and release confinement | **Ctrl+Alt+F10**, or `종료` |
| Change the transfer key | Right-click the shield → `모니터 이동 키` |
| Open settings | Right-click → `설정...` |
| Toggle the badge | Right-click → `이동 상태 박스 표시` |
| Toggle login startup | Right-click → `Windows 로그인 시 자동 실행 등록` |

Choose Ctrl, Alt, Shift, or Right Ctrl (`오른쪽 Ctrl`) as the transfer key.

The badge appears only while the chosen key is held **and the pointer is moving**. It hides after about 180 ms without movement, when you release the key, or while confinement is paused. It does not intercept clicks or take keyboard focus. Turning the badge off leaves monitor confinement active.

## Settings and login startup

Open `설정...` from the tray:

- **Transfer key (`모니터 이동 키`):** Defaults to Ctrl.
- **Run at Windows login (`Windows 로그인 시 자동 실행`):** Off by default. Enable it to start after you sign into Windows.
- **Enable confinement on launch (`실행할 때 포인터 제한 켜기`):** On by default. Turn it off to start future sessions paused; it is separate from pausing the current session.
- **Show movement badge (`이동 상태 박스 표시`):** On by default.
- **Badge colors (`상태 박스 배경색`, `상태 박스 글자색`):** Choose each color and check the live preview. The defaults are black and white; `기본 색상 복원` restores those colors. Click `저장` to apply your choices, or `취소` to discard them.
- **Font and text size:** Choose from fonts installed on your computer, set a size from 6 to 48 pt, and toggle bold. The default is Segoe UI, 9.75 pt, regular. Bold is available only when the selected font supports it.
- **Box size:** Automatic sizing follows the selected font, size, and weight. Turn it off to enter a width and height. Small dimensions are enlarged enough to fit the text; oversized boxes are kept within the screen. The preview shows the resulting size. Dimensions scale with the monitor's DPI. `기본 모양 복원` resets the font and sizing options while keeping your colors.

The preview displays the box at its actual size on the current screen; scroll within it to inspect a large box. Click `저장` to apply your changes. If a saved font has been removed, MonitorGate uses an available system font instead.

Your key, launch state, badge visibility, colors, font, and sizing preferences persist in `%LOCALAPPDATA%\MonitorGate\settings.ini`.

Example of custom colors with larger, bold text:

![Badge with custom navy and gold colors](overlay-color-example.png)

Enable login startup from the folder where you intend to keep the app. After moving that folder, turn startup off and on again to register its new location.

If you disable MonitorGate in **Windows Settings → Apps → Startup** or Task Manager, that choice is respected. Re-enable it there to restore automatic startup. The settings button `Windows 시작 앱 설정 열기` opens that Windows page. Windows may delay startup apps after login. [Microsoft startup documentation](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)

## Troubleshooting and removal

- **Ctrl shortcuts:** Ctrl+C, Ctrl+click, and other shortcuts still work. They also allow monitor crossing while Ctrl is held. Choose Right Ctrl if that is inconvenient.
- **Games and remote-control tools:** These can apply their own pointer confinement or hide the badge. Pause MonitorGate if they conflict.
- **Elevated windows and secure screens:** Administrator windows may limit input detection. Confinement is suspended during lock screens, UAC secure screens, and sleep, then resumes on the normal desktop.
- **Unavailable hotkeys:** The startup notification reports conflicts. Use the tray menu instead.
- **Pointer still confined after a forced exit:** Run `Release.cmd`. It stops MonitorGate and explicitly releases confinement, including confinement another application may have set.

To remove the app, disable login startup, exit MonitorGate, then delete its folder. You can also delete `%LOCALAPPDATA%\MonitorGate` to remove saved preferences.

## For developers

`Build.ps1` compiles `MonitorGate.cs`, `StartupSettings.cs`, `SettingsDialog.cs`, and `OverlayAppearance.cs` using Windows' .NET Framework compiler. It downloads no packages. Exit MonitorGate before rebuilding its executable.

Building `MonitorGate.csproj` with Visual Studio/MSBuild requires .NET Framework 4.8 development tools and reference assemblies.

Run the simulated-monitor, preferences, startup-registration, and badge tests:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify.ps1
```

These tests do not change real pointer confinement, keyboard input, or Windows startup registration.

Optional command-line usage:

| Command | Purpose |
| --- | --- |
| `.\MonitorGate.exe RightCtrl` | Override the saved key for this launch; also accepts `Ctrl`, `Alt`, or `Shift` |
| `.\MonitorGate.exe --paused` | Start paused |
| `.\MonitorGate.exe --settings` | Open settings when starting a new instance |
| `.\MonitorGate.exe --diagnose .\monitors.txt` | Write read-only monitor diagnostics without confinement or keyboard hooks |
| `.\MonitorGate.exe --release` | Perform the same recovery as `Release.cmd` |
| `.\MonitorGate.exe --startup` | Start with saved preferences; used by login startup registration |

Windows API references: [ClipCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor), [MonitorFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfrompoint), [MONITORINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo), [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate), [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
