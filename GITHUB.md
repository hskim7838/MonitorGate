# GitHub 업로드 준비

현재는 **로컬 준비만 완료**한 상태입니다. GitHub 저장소 생성이나 업로드를 실행하지 않았습니다.

## 패키지

- `MonitorGate-v1.2.zip`: 직접 사용할 실행 파일과 소스·설명서.
- `MonitorGate-source.zip`: GitHub에 올릴 소스·프로젝트·테스트·설명서. 실행 파일과 개인 설정은 제외.

저장소 이름은 `monitor-gate`를 사용할 수 있습니다. 설명 예시:

> Windows multi-monitor cursor guard. Hold Ctrl to cross monitors, with an optional movement badge and configurable startup.

## 파일 구성

| 파일 | 용도 |
| --- | --- |
| `MonitorGate.cs` | 포인터 제한, 키 감지, 트레이 메뉴, 상태 박스 |
| `StartupSettings.cs` | 자동 실행 등록, 설정 저장, 움직임 표시 조건 |
| `SettingsDialog.cs` | 설정 창 |
| `MonitorGate.csproj` | Visual Studio/MSBuild 프로젝트 |
| `MonitorGate.manifest` | DPI 및 일반 사용자 권한 설정 |
| `MonitorGate.exe.config` | .NET Framework 실행 구성 |
| `Build.ps1` | 외부 패키지 없이 빌드 |
| `Verify.ps1`, `Tests.cs` | 실제 시스템 설정을 바꾸지 않는 회귀 테스트 |
| `.gitignore` | 빌드 결과 및 개인 설정 제외 |
| `README.md`, `CHANGELOG.md`, `VALIDATION.md` | 사용법, 변경 기록, 검증 범위 |

## 업로드할 때

1. `MonitorGate-source.zip`을 압축 해제합니다.
2. GitHub에서 저장소를 만들고 공개 또는 비공개 범위를 선택합니다.
3. 소스 파일을 업로드하거나, 압축 해제한 프로젝트 폴더에서 아래 명령을 사용합니다.

```powershell
git init
git add .
git commit -m "Add MonitorGate cursor guard and startup settings"
git branch -M main
git remote add origin https://github.com/YOUR_ACCOUNT/monitor-gate.git
git push -u origin main
```

`YOUR_ACCOUNT`는 실제 GitHub 계정으로 바꿉니다. 명령을 실행하기 전 저장소 주소가 맞는지 확인하세요. Git은 사용자의 이름과 이메일 설정, GitHub 인증이 필요할 수 있습니다.

실행 파일은 소스 저장소에 커밋하지 않고, 원할 때 GitHub Releases에 `MonitorGate-v1.2.zip`을 첨부하면 됩니다. 공개 배포 전 사용할 라이선스를 정해 `LICENSE` 파일을 추가할 수 있습니다. 이번 준비에는 라이선스 선택을 포함하지 않았습니다.
