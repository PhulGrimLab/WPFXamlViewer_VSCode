# WPFXamlViewer_VSCode
**WPF XAML Live Preview** (확장 ID `phulgrimlab.wpf-xaml-live-preview`) — VS Code에서 `.xaml` 파일을 편집하면서 **실제 WPF 렌더링 결과**를 옆 패널에서 실시간으로 확인한다.

> 목적: **Visual Studio 없이 VS Code에서 .NET 10 SDK로 WPF 프로젝트를 UI까지 개발**한다. **Windows 전용.**
> 사용법과 제한 사항은 **[doc/User_Guide.md](./doc/User_Guide.md)**.
>
> 상태: **M0~M6 구현 완료, 단 "VS 없는 깨끗한 PC" 검증은 아직 못 했다**([doc/05](./doc/05_Clean_Machine_Verification.md)).
> 드래그 앤 드롭 디자이너와 XAML 자동 완성은 이 확장의 범위가 아니다. 현황/이어서 작업하는 방법은 [doc/04](./doc/04_Progress_Status_And_Next_Steps.md).
> 개발 방침은 [CLAUDE.md](./CLAUDE.md).

## 설치해서 쓰기 (이미 만들어진 `.vsix`가 있을 때)
1. VS Code 확장 뷰 → `...` → **VSIX에서 설치** (또는 `code --install-extension .\wpf-xaml-live-preview-0.1.0.vsix`).
2. **.NET 10 Desktop Runtime**이 필요하다(없으면 확장이 안내한다).
3. `.xaml` 파일에서 명령 팔레트 → **WPF XAML: Open Preview**.

`.vsix`가 없으면 아래 "소스에서 `.vsix` 만들기"로 직접 만들 수 있다.

## 소스에서 `.vsix` 만들기 (어느 Windows PC에서든)

소스를 내려받아 **명령 두세 개로 `.vsix`를 만든다.** VS Code도, Visual Studio도 필요 없다.

### 1) 준비물 (한 번만 설치)
| 필요한 것 | 버전 | 설치 (PowerShell) | 확인 |
|---|---|---|---|
| Windows | 10/11 x64 | — | — |
| **.NET SDK** | **10 이상** | `winget install Microsoft.DotNet.SDK.10` | `dotnet --list-sdks` 에 `10.x` |
| **Node.js (npm 포함)** | **20 이상**(LTS 권장) | `winget install OpenJS.NodeJS.LTS` | `node -v` |
| 인터넷 | 처음 한 번 | npm/NuGet 패키지 복원에 필요 | — |
| git | 소스를 `git clone`으로 받을 때만 | `winget install Git.Git` | `git --version` |

> 설치 직후에는 **새 터미널**을 열어야 PATH가 반영된다. 빠진 것은 자동 점검으로 확인할 수 있다:
> `.\doc\check_environment.ps1 -PackagingOnly` (설치는 하지 않고 안내만 한다).

### 2) 소스 받기
```powershell
git clone https://github.com/PhulGrimLab/WPFXamlViewer_VSCode.git
cd WPFXamlViewer_VSCode
```
git이 없으면 GitHub 저장소 페이지의 **Code → Download ZIP**으로 받아 압축을 풀어도 된다(경로에 공백/한글이 있어도 된다).
ZIP으로 받았을 때 "스크립트를 실행할 수 없다"는 오류가 나면 압축 푼 폴더에서 한 번 실행한다: `Get-ChildItem -Recurse | Unblock-File`

### 3) 만들기
```powershell
.\tools\package\build_vsix.ps1
```
(또는 `cd extension; npm run package`) 이 명령 하나가 알아서 한다: 전제 조건 점검 → `npm ci`(처음에만) → 확장 컴파일 → 렌더 호스트 `dotnet publish` → `vsce package`.
처음에는 패키지를 내려받아 몇 분 걸리고, 이후에는 1분 안팎이다.

**결과물** (`artifacts\` 폴더, git에는 올라가지 않는다):
- `wpf-xaml-live-preview-<버전>.vsix` — 설치 파일(약 0.2MB, 호스트 포함)
- `SHA256SUMS.txt` — 체크섬. 확인: `(Get-FileHash .\artifacts\*.vsix).Hash`

### 4) 설치
```powershell
code --install-extension .\artifacts\wpf-xaml-live-preview-0.1.0.vsix
```
(또는 VS Code 확장 뷰 → `...` → **VSIX에서 설치**.) 실행 PC에는 **.NET 10 Desktop Runtime**이 필요하다.

### 문제가 생기면
스크립트는 실패하면 원인과 함께 `패키징 실패: ...` 로 멈춘다. 자주 만나는 경우:

| 증상 | 원인 → 해결 |
|---|---|
| `필요한 도구가 부족합니다: ...` | 위 준비물 중 빠진 것을 설치하고 **새 터미널**에서 다시 실행. 목록의 안내 명령을 그대로 쓰면 된다 |
| `.ps1 ... 실행할 수 없습니다` / `스크립트 실행이 비활성화` | 실행 정책 때문이다. `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package\build_vsix.ps1` 로 실행(`npm run package`는 이미 이 방식) |
| `npm ci` 가 `ENOTFOUND`/`ETIMEDOUT`/`EPROXY` 로 실패 | 인터넷/프록시 문제. 회사망이면 `npm config set proxy ...`, NuGet 프록시도 설정 |
| `dotnet publish` 가 `NU1301`/복원 실패 | NuGet 서버에 접근하지 못함(인터넷/프록시). 처음 한 번은 인터넷이 필요하다 |
| `A compatible .NET SDK was not found` / `NETSDK1045` | .NET SDK 10이 없다(저장소의 `global.json`이 10 이상을 요구) → 설치 |
| `npm warn EBADENGINE` 경고 | Node가 20/21 이다. **패키징에는 영향 없다**(경고만). 테스트까지 하려면 Node 22 이상 |
| `이전 스테이징 폴더를 지울 수 없습니다` | `artifacts\vsix-stage`를 다른 프로그램(탐색기/VS Code/터미널)이 열고 있다 → 닫고 재실행 |
| 콘솔 한글이 깨져 보임 | 터미널 인코딩 문제일 뿐 결과에는 영향 없다(`chcp 65001`) |
| 다른 PC에서 만든 `.vsix`와 체크섬이 다름 | 정상이다(압축에 시각 정보가 들어간다). 배포할 때는 **한 번 만든 `.vsix`와 그 `SHA256SUMS.txt`를 한 쌍으로** 쓴다 |

## 개발 (테스트까지)
```powershell
.\doc\check_environment.ps1        # 환경 점검 (.NET SDK 10 / Desktop Runtime / Node 22+ / VS Code / git)
cd extension; npm install; cd ..
.\tools\ci\ci.ps1                  # 호스트 + 확장 단위 + 실제 호스트 장애 주입 테스트
.\tools\ci\ci.ps1 -IncludeIntegration   # + 실제 VS Code 통합 테스트(I-01~I-10)
.\tools\ci\ci.ps1 -IncludePackage       # + .vsix 패키징(호스트 번들) + 설치 스모크(I-09)
```
테스트에는 VS Code와 .NET 10 Desktop Runtime, Node 22 이상이 필요하다(요구사항 표: [doc/00](./doc/00_Environment_Setup_Guide.md)).

## 문서
| 문서 | 내용 |
|---|---|
| [doc/User_Guide.md](./doc/User_Guide.md) | **사용자 가이드: 개발 흐름, 미리보기 기능, 사용자 컨트롤/신뢰, 변환 규칙, 제한, 문제 해결** |
| [doc/04_Progress_Status_And_Next_Steps.md](./doc/04_Progress_Status_And_Next_Steps.md) | **진행 상태, 다른 PC에서 이어가기, 미검증 항목, 다음 작업** |
| [doc/05_Clean_Machine_Verification.md](./doc/05_Clean_Machine_Verification.md) | VS 없는 깨끗한 PC 검증 체크리스트(미실행) |
| [doc/06_Release_Process.md](./doc/06_Release_Process.md) | 릴리스 절차(GitHub Release `.vsix`), 설치 안내, 신뢰/보안 문구 |
| [doc/00_Environment_Setup_Guide.md](./doc/00_Environment_Setup_Guide.md) | 개발 환경 요구사항(패키징만/테스트까지)과 결정(호스트는 net10 + dotnet, VS 불필요) |
| [doc/01_Architecture_And_Requirements.md](./doc/01_Architecture_And_Requirements.md) | 목표/범위, 렌더 방식 결정, 구성, 프로토콜, 스레드 모델, 로그 설계 |
| [doc/02_Development_Plan.md](./doc/02_Development_Plan.md) | 마일스톤 M0~M6 (단계 → 검증)과 마일스톤별 결과 |
| [doc/03_Test_Strategy.md](./doc/03_Test_Strategy.md) | 자동 테스트 계층, 골든 이미지 규칙, 테스트 케이스 목록, 실행/CI |
