# 04. 진행 상태와 다음 단계 (2026-10-07 기준)

> 다른 PC에서 작업을 이어갈 때 가장 먼저 읽는 문서. 설계는 [01](./01_Architecture_And_Requirements.md),
> 계획은 [02](./02_Development_Plan.md), 테스트는 [03](./03_Test_Strategy.md), 환경은 [00](./00_Environment_Setup_Guide.md).
> 최신 커밋은 `git log`로 확인할 것(이 문서는 커밋 해시를 박지 않는다).

## 1. 한 줄 요약
**M0~M6 구현 완료. 남은 것: ① [doc/05](./05_Clean_Machine_Verification.md) 깨끗한 PC 검증(미실행) ② 웹뷰 마우스 동작(드래그/휠/클릭)을 실제로 써 보기 ③ 첫 GitHub Release 생성([06](./06_Release_Process.md), 사용자 확인 후).** VS Code 확장은 `Open Preview` 명령, 자동 갱신, Problems, 경고 표시까지 동작하며, 호스트는 실제 프로젝트 XAML(x:Class/이벤트/Window 루트/병합 사전/사용자 타입 자리표시자)을 렌더한다. **신뢰된 워크스페이스에서는 프로젝트 빌드 DLL의 사용자 컨트롤이 실제로 그려진다(빌드 산출물이 있어야 함)**(02 문서 M4/M4B 결과의 제한 목록 참고).

## 2. 확정된 전제 (사용자 결정)
- **Windows 전용.** (2026-10-06 사용자 확정)
- 목적: **VS2026이 없는 PC에서 VS2026 호환 프로젝트(SDK-style net8/9/10-windows WPF)를 VS Code로 개발**할 때 XAML 미리보기 제공.
- 호스트는 `net10.0-windows` SDK-style WPF, `dotnet`으로 빌드(VS 불필요). 렌더 방식은 "WPF 렌더 호스트 → PNG → Webview 표시"(01 문서 2절).

## 3. 마일스톤 현황

| 마일스톤 | 상태 | 비고 |
|---|---|---|
| M0 환경 + 스캐폴딩 | ✅ 완료 | |
| M1 렌더 호스트 핵심 | ✅ 완료 | 골든 14종, 크기 규칙, 구조화 오류 |
| M2 프로토콜 + 프로세스 관리 | ✅ 완료 | serve 모드, 로거, HostClient, 장애 주입 |
| M3 확장 MVP | ✅ 완료 | 미리보기 패널, 자동 갱신, Problems, 통합 테스트 5개(I-01~I-05) |
| M4 XAML 해석 충실도 | ✅ 완료(App.xaml 리소스 포함) | x:Class/이벤트 제거, d:/mc:, 병합 사전, 자리표시자, Window 루트 |
| M4B 프로젝트 인식 렌더링(Tier 1) | ✅ 완료(B.5 프로세스 분리 제외) | 사용자 컨트롤 DLL 로드, Workspace Trust |
| M5 상호작용 | ✅ 완료(웹뷰 마우스 이벤트 실사용 미검증) | 줌/팬, HitMap, 클릭 → 줄 이동 |
| M6 패키징 + 문서 | ✅ 완료(6.4 깨끗한 PC 검증 제외) | 호스트 번들, `.vsix`, VS 없는 PC 검증 |

## 4. 현재 코드 구성

```
host/                              ← .NET 10 WPF 렌더 호스트 (XamlRenderHost.slnx)
  XamlRenderHost/
    Program.cs                     ← 진입점: --version | render --in --out | serve [--log-dir --log-level]
    HostInfo.cs                    ← 버전(0.1.0), 프로토콜 번호(1)
    Rendering/XamlRenderer.cs      ← XAML → Measure/Arrange → RenderTargetBitmap → PNG, 크기 4단계 규칙
    Rendering/XamlRenderException.cs ← 오류 코드/줄/열
    Rendering/XamlPreprocessor.cs  ← x:Class/이벤트/x:Code 제거, 자리표시자, 병합 사전 인라인(줄 번호 보존)
    Rendering/StaRunner.cs         ← STA 스레드 실행 도우미
    Protocol/RequestHandler.cs     ← JSON 요청 → 응답 (ping/render/shutdown, debug.* 는 훅 켜졌을 때만)
    Protocol/ProtocolLoop.cs       ← stdin/stdout 스레드 구조
    Logging/HostLogger.cs, FileLogSink.cs ← 비동기 큐 로거, 1MB×5 회전
  XamlRenderHost.Tests/            ← MSTest 110개 (골든, 크기, 오류, 프로토콜, 로그, CLI)
    Fixtures/{xaml,golden}/        ← 골든 PNG 10종 (눈으로 확인 완료)
extension/                         ← VS Code 확장 (TypeScript)
  src/extension.ts                 ← activate: 출력 채널 + E001 로그만
  src/hostClient.ts                ← HostClient (spawn, id 매핑, 타임아웃/재시작, renderLatest)
  src/logFormat.ts, constants.ts
  test/unit/                       ← mocha 17개 (가짜 호스트 test/fixtures/fake-host.js 사용)
  test/realhost/                   ← 실제 호스트 상대 장애 주입 8개
doc/, tools/ci/ci.ps1, .vscode/{launch,tasks}.json
```

## 5. 다른 PC에서 이어가는 방법

```powershell
git clone https://github.com/PhulGrimLab/WPFXamlViewer_VSCode.git
cd WPFXamlViewer_VSCode
.\doc\check_environment.ps1        # 없는 항목은 설치 방법을 안내한다
cd extension; npm install; cd ..   # 확장 의존성 (node_modules는 git에 없다)
.\tools\ci\ci.ps1                  # 전체 검증 — 호스트 110 + 확장 46 + 실제 호스트 14 + 통합 9(-IncludeIntegration) + 설치 스모크 2(-IncludePackage) 이 모두 통과해야 정상
```
- 필요 도구: .NET SDK 10, **.NET 10 Desktop Runtime**, Node.js LTS, VS Code, git. (Visual Studio는 필요 없다.)
- 새 터미널에서 `node`가 안 잡히면 PATH 갱신 문제다(설치 직후). `ci.ps1`은 머신/사용자 PATH를 다시 합쳐서 실행한다.
- **첫 실행에서 골든 테스트가 실패하면 중요한 신호다**: 1절의 미검증 항목(다른 머신 일치)을 확인하는 첫 기회다.
  `TestResults/render-diff/`의 expected/actual/diff 이미지를 보고, 폰트·DPI·OS 테마 차이 중 무엇인지 **원인을 확정한 뒤**
  허용오차를 조정하거나 골든 갱신 정책을 정한다(03 문서 2절). 허용오차부터 느슨하게 하지 말 것.
  이 결과는 03 문서 7절 "실측 함정"에 기록한다.
- 골든 갱신: `XAMLVIEWER_UPDATE_GOLDEN=1`로 `dotnet test` 후 PNG를 눈으로 확인하고 커밋.

## 6. 미검증 / 알려진 제한 (숨기지 않고 기록)
1. **다른 PC/OS 버전에서 골든 일치 여부 미검증.** 이 머신(Windows 11 Pro 10.0.26300)에서만 확인. 5회 반복 렌더는 바이트 동일.
2. 거대 XAML 시험은 단순 도형 5,000개뿐. 템플릿/바인딩이 무거운 실제 화면의 성능은 모른다.
3. `debug.hang`은 호스트 스레드 정지 시뮬레이션. 사용자 컨트롤의 "끝나지 않는 생성자" kill/재시작은 M4B에서 검증했다(B.6). StackOverflow/네이티브 크래시는 미시험.
4. 해석 불가 타입이 요소가 아닌 자리(`TargetType`, `{x:Type}`, `{x:Static}`)에 있으면 오류(02 문서 M4 결과 참고).
5. `npm audit`: 개발 의존성(mocha 계열) 취약점 3건. `.vsix`에는 포함되지 않음. M6 패키징 전에 mocha 버전 재검토.
6. **GitHub Actions 등 CI 워크플로는 없다.** `tools/ci/ci.ps1`은 로컬/CI 공용 진입점이지만 CI 서비스는 미정.
7. 확장 이름/게시자는 **확정**(`phulgrimlab.wpf-xaml-live-preview`, 2026-10-08). 릴리스는 **아직 만들지 않았다** — 절차는 [06](./06_Release_Process.md).

## 7. 미결 결정 (사용자 응답 없이 제안값으로 진행 중)
| 항목 | 제안(현재 적용) | 영향 시점 |
|---|---|---|
| 호스트 배포 방식 | 프레임워크 종속(.NET 10 Desktop Runtime 필요, 없으면 설치 안내) | M6 |
| 미신뢰 폴더 정책 | Workspace Trust 아니면 자리표시자만(Tier 0) | M4B |
| 지원 TFM 하한 | net8.0-windows 이상 공식, 레거시는 "그려지면 다행" | M4 |
| 확장 이름/게시자 | **확정**: WPF XAML Live Preview / phulgrimlab | 완료 |
| 소스에서 .vsix 빌드 | 어느 Windows PC에서든 `tools\package\build_vsix.ps1` 한 번(README 참고). 새 클론/공백·한글 경로에서 검증 | 완료 |

## 8. 다음 작업: M3 시작 가이드
02 문서 M3 표(3.1~3.4)가 기준이다. 시작 전 알아둘 점:
- 확장 `activate`에서 `HostClient`를 만들 때 호스트 exe 경로 해석이 필요하다. 개발 중에는
  `host/XamlRenderHost/bin/Debug/net10.0-windows/win-x64/XamlRenderHost.exe`(테스트가 쓰는 경로), 패키징 후에는 `extension/bin/host/`(M6).
  경로 해석 함수를 한 곳에 두고, 없으면 안내 알림 + 로그(E010/E012) — 테스트 I-05.
- 로그는 호스트에 `--log-dir`(확장의 `globalStorageUri/logs`), 확장은 OutputChannel. `formatLogLine`과 `LogId`를 그대로 사용.
- 갱신 흐름은 `HostClient.renderLatest`(최신 요청만)를 쓰고, 편집 디바운스(기본 300ms, 상수)는 확장 쪽에 둔다.
- 통합 테스트(T4/T5)는 `@vscode/test-electron` 도입이 처음이다. **임시 `--user-data-dir`/`--extensions-dir`** 로 개발자 환경을 건드리지 않는다.
  웹뷰는 스크린샷 대신 `postMessage`로 이미지 크기/상태를 회신해 검증한다(03 문서 1.1).
  VS Code 1.136.1이 이 머신에 설치되어 있다. 다운로드가 필요한 구조라 오프라인/사내망에서 막히면 `code` 설치본을 `vscodeExecutablePath`로 지정.
- `npm run test:integration` 스크립트를 추가하면 `ci.ps1 -IncludeIntegration`이 자동으로 실행한다(이미 연결되어 있음).
- CLAUDE.md 규칙 준수: 함수 단일 책임, 상수화, if/else 명시, 클래스 Owner/Lifetime 주석, 기능마다 테스트 추가 후 실행.

## 9. 작업 규율 메모 (이 세션에서 쓴 방식)
- 마일스톤 단위로 작게 커밋·푸시(사용자 요청: "진행하면서 중간에 커밋과 푸시 병행"). 커밋 메시지는 한국어, 끝에 Co-Authored-By 줄.
- 각 마일스톤 종료 시 02 문서에 "결과" 절(실측값, 함정, 미검증)을 추가한다.
- PowerShell 5.1에서 한글 스크립트는 UTF-8 **BOM** 필수(`doc/*.ps1`, `tools/ci/ci.ps1`은 BOM 저장). 소스(.cs/.ts)는 BOM 없음.
- WPF 프로젝트(`UseWPF`)는 암시적 using에 `System.IO`가 빠진다 → `using System.IO;` 명시.
- git이 `LF will be replaced by CRLF` 경고를 내는 것은 정상(`core.autocrlf`). 골든 PNG는 바이너리로 처리된다.
