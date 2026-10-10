# 02. 개발 계획

> 상위 문서: [01_Architecture_And_Requirements.md](./01_Architecture_And_Requirements.md)
> 테스트 상세: [03_Test_Strategy.md](./03_Test_Strategy.md)

## 0. 진행 원칙

CLAUDE.md "Goal-Driven Execution"과 메모리의 "테스트 주도 워크플로"에 따라:

1. **모든 마일스톤은 `단계 → 검증` 쌍으로 끝난다.** 검증이 통과해야 다음으로 간다.
2. 기능을 추가/변경할 때 **자동 테스트 추가 + 실행**을 같은 변경에 포함한다. 버그는 먼저 재현 테스트를 쓴다.
3. 마일스톤 종료 시 `doc/`에 결과(실측값, 발견한 함정)를 기록한다 — CodeAtlas가 `10_UiAutomation_Test_Design.md`에
   "이번에 실측으로 발견한 함정" 절을 남긴 것처럼.
4. 요청 범위 밖(§1.2 Out)은 만들지 않는다. 불확실하면 구현 전에 질문한다.
5. 각 마일스톤은 작게 커밋한다(커밋은 사용자 요청 시에만).

## 1. 저장소 구조 (M0에서 생성)

```
WPFXamlViewer_VSCode/
├─ CLAUDE.md, README.md, LICENSE
├─ doc/                         ← 설계/계획/결과 문서, 실행 스크립트(run_tests.ps1 등)
├─ host/                        ← WPF 렌더 호스트 (SDK-style, net10.0-windows, dotnet CLI 빌드)
│   ├─ XamlRenderHost.slnx      ← (또는 .sln)
│   ├─ XamlRenderHost/          ← exe
│   ├─ XamlRenderHost.Tests/    ← MSTest (T1, T2), dotnet test
│       └─ Fixtures/{xaml,golden}/  ← 테스트용 .xaml, 골든 PNG
├─ extension/                   ← VS Code 확장 (TypeScript)
│   ├─ package.json, tsconfig.json
│   ├─ src/                     ← extension.ts, hostClient.ts, diagnostics.ts, panel/ ...
│   ├─ test/unit/               ← 순수 로직 (T3)
│   ├─ test/integration/        ← @vscode/test-electron (T4, T5)
│   └─ media/                   ← webview html/js/css
└─ tools/ci/ci.ps1              ← 로컬/CI 공용 진입점
```

## 2. 마일스톤

각 항목의 형식: **단계 → 검증**. "검증"은 모두 자동화된 명령으로 실행 가능해야 한다.

### M0. 환경 + 스캐폴딩
| # | 단계 | 검증 |
|---|---|---|
| 0.1 | Node LTS 설치(사용자), `doc/check_environment.ps1` 작성(.NET SDK 10, Desktop Runtime 10, node/npm, code) | 스크립트가 전 항목 OK 출력 |
| 0.2 | `.gitignore` 조정(.vscode launch/tasks 허용, node_modules/out/dist/*.vsix 제외) | `git status`에 산출물 미노출 |
| 0.3 | host 솔루션(`net10.0-windows`, UseWPF, x64, 빈 exe + MSTest 1개) | `doc/run_tests.ps1`(= `dotnet test`)이 복원→빌드→테스트 통과. VS 미설치 PC를 가정해 VS 도구를 호출하지 않음 |
| 0.4 | extension 스캐폴딩(activate 시 로그 1줄) + mocha 1개 | `npm test`(unit) 통과, F5 없이 `npm run compile` 성공 |
| 0.5 | `tools/ci/ci.ps1`: host 테스트 + extension 테스트 일괄 실행 | 한 번의 명령으로 양쪽 통과 |

#### M0 결과 (2026-10-06, 전 항목 검증 통과)
- 0.1 `doc/check_environment.ps1`: 전 항목 OK. 0.3 `doc/run_tests.ps1`: 호스트 테스트 3/3. 0.4 `npm test`: 확장 단위 3/3.
  0.5 `tools/ci/ci.ps1` 한 번으로 양쪽 통과(통합 테스트는 M3 전이라 건너뜀 안내). 0.2 `git status`에 산출물 미노출.
- **실측 함정**: WPF 프로젝트(`UseWPF`)는 `ImplicitUsings`에서 `System.IO`가 빠진다 → 코드에 `using System.IO;`를 명시해야 한다.
- **알려진 사항**: `npm audit`가 개발 의존성(mocha → diff, serialize-javascript)에 취약점 3건(low 1/moderate 1/high 1)을 보고한다.
  테스트 도구 내부이며 `.vsix`에 포함되지 않는다. `npm audit fix`로는 해결되지 않고 mocha major 변경(`--force`)이 필요해 보류.
  M6 패키징 전에 mocha 최신 버전으로 재검토한다.

### M1. 렌더 호스트 핵심 (CLI 모드)
| # | 단계 | 검증 |
|---|---|---|
| 1.1 | `render --in a.xaml --out a.png` (STA, XamlReader.Load → RenderTargetBitmap → PNG) | Button/Grid/StackPanel 등 기본 픽스처 10종의 골든 PNG 일치(T2) |
| 1.2 | 크기 결정 규칙: `Width/Height` 명시 → `d:DesignWidth/Height` → 요청 크기 → 콘텐츠 크기 | 규칙별 단위 테스트(T1) + 골든 |
| 1.3 | 결정성 확보(SoftwareOnly, 96 DPI, 폰트 고정) | 같은 XAML 5회 렌더 → 바이트 동일 |
| 1.4 | XAML 오류 → 코드/줄/열을 가진 구조화 오류 | 잘못된 픽스처 8종의 오류 코드/줄 일치(T1) |

#### M1 결과 (2026-10-06, 1.1~1.4 검증 통과)
- 구현: `Rendering/XamlRenderer`(렌더), `XamlRenderException`(코드/줄/열), `StaRunner`, `Program render --in --out [--width --height --dpi]`.
- 테스트(호스트 39개 통과): 골든 10종 + 결정성(5회 바이트 동일) + 크기 규칙 8 + 오류 9 + 비교기 4 + CLI 4 + 스모크 3.
  골든 PNG 4종을 눈으로 확인(Grid 비율/Auto 열, 템플릿 버튼, 글꼴 4종, DrawingImage). 픽스처를 일부러 바꿔 골든이 실제로 실패하고
  `TestResults/render-diff/`에 expected/actual/diff를 남기는 것도 확인했다.
- 경로 변경: 픽스처는 `host/XamlRenderHost.Tests/Fixtures/{xaml,golden}` (계획의 `host/Fixtures`에서 변경 — 테스트 프로젝트 옆이 자연스럽다).
- **실측**: 같은 머신에서 5회 렌더가 바이트 동일(소프트웨어 렌더 + 회색조 텍스트). **다른 머신/OS 버전에서의 일치는 아직 미검증** —
  Button 같은 기본 컨트롤 룩은 OS 테마에 영향받을 수 있다. 다른 PC에서 `run_tests.ps1`을 돌려 확인 필요(미확인 항목).
- 오류 줄 번호 규칙: 닫히지 않은 태그는 닫는 태그 줄, 알 수 없는 요소/속성/잘못된 값은 해당 줄, 루트 2개는 두 번째 루트 줄, 비-XML 텍스트는 1줄.
- 알려진 제한: `Window` 루트는 `UnsupportedRoot`(M4.5에서 지원), `x:Class`/이벤트 핸들러는 전처리 전이라 XamlParse 오류(M4.1).

### M2. 프로토콜 + 프로세스 관리
| # | 단계 | 검증 |
|---|---|---|
| 2.1 | stdin/stdout 줄 JSON 루프(`ping`/`render`/`shutdown`), STA 큐 구조(§01 4절) | 서브프로세스로 띄워 요청/응답 왕복(T1, 실제 exe) |
| 2.2 | 로그 H001~H013 구현(비동기 큐 Writer) | 시나리오별 로그 ID 순서 검증, 큐 포화 시 렌더가 막히지 않음 |
| 2.3 | 확장 `HostClient`: spawn, id 매핑, 타임아웃, 재시작, 최신 요청만 처리 | TS 단위 테스트(가짜 호스트 스크립트)로 타임아웃/크래시/폐기 검증(T3) |
| 2.4 | 결함 주입: 무한 루프성 XAML, 거대 XAML, 호스트 kill | 타임아웃 후 재시작, 이후 요청 정상(T4) |

#### M2 결과 (2026-10-06, 2.1~2.4 검증 통과)
- 호스트: `serve` 모드(STA 메인 + StdinReader + StdoutWriter), `ping`/`render`/`shutdown`, 구조화 오류, `HostLogger`(비동기 유계 큐, H090) + `FileLogSink`(1MB×5 회전).
  호스트 테스트 58개(프로토콜 12, 로그 6 포함) 통과.
- 확장: `HostClient`(지연 시작, id 매핑, 타임아웃 시 kill+재시작, 크래시/시작 실패 구분, `renderLatest` 최신만 처리, dispose 시 shutdown) — 가짜 호스트 단위 테스트 14개.
- 실제 호스트 장애 주입(T3R) 8개: 무응답 → `E010,E011,E010` 순서로 재시작, 크래시(exit 99) 복구, 훅 꺼짐 시 `UnknownMethod`,
  5,000요소 XAML(0.4초), `TooLarge`가 호스트를 죽이지 않음, 호스트 로그에 XAML 본문 미기록.
- 변경/추가: 큐 포화 정책을 "새 로그 폐기 + H090"으로 단순화, 로그 ID `E014` 추가(01 문서 반영), 장애 주입 훅 문서화(03 4.1절).
- **미검증/제한**: 거대 XAML 시험은 "단순 도형 5,000개"라 무거운 템플릿/바인딩이 섞인 실제 화면의 성능은 모른다. `debug.hang`은 호스트의 STA 스레드를
  막는 시뮬레이션이며, 실제 무한 루프(사용자 컨트롤, M4B)에서의 kill 동작은 M4B B.6에서 다시 검증한다.

### M3. 확장 MVP (이 시점에 "쓸 수 있다")
| # | 단계 | 검증 |
|---|---|---|
| 3.1 | 명령 `WPF XAML: Open Preview`, 에디터 옆 Webview 패널, PNG 표시 | 통합 테스트: 명령 실행 → 패널 생성, 웹뷰가 이미지 크기를 메시지로 회신(T5) |
| 3.2 | 편집 시 자동 갱신(디바운스) + 상태 표시줄(렌더 중/오류) | 문서 편집 → 갱신 횟수/마지막 결과 확인(T4/T5) |
| 3.3 | 오류 → Problems 패널(줄/열) | `languages.getDiagnostics` 결과가 기대와 일치(T4) |
| 3.4 | 호스트 미존재/오류 시 안내 메시지 | 호스트 경로 제거 상태에서 오류 알림 + 로그 E010/E012(T4) |

**M3 결과 (2026-10-07)**
- 구현: 명령 `Open Preview`, 에디터 옆 웹뷰(nonce CSP), 300ms 디바운스 자동 갱신(`renderLatest`), 상태 표시줄, Problems 진단(줄/열 0-base 변환), 호스트 exe 탐색(`hostLocator.ts`, 없으면 안내 알림).
- 검증: 확장 단위 29개, 통합 3개(I-01 명령 등록, I-02 웹뷰가 120x40 이미지 회신, I-03 오류→Problems 생성/정상→해제). `npm run test:integration`은 설치된 VS Code(`XAMLVIEWER_VSCODE_EXE`로 변경 가능)와 임시 user-data/extensions 폴더를 쓴다.
- **추가 (I-04/I-05)**: 통합 테스트 5개가 되었다. I-04는 확장이 띄운 호스트 PID를 `process.kill`로 죽인 뒤 다음 편집에서 새 호스트로 복구되고 로그에 E012 → E010이 순서대로 남는지 확인한다. I-05는 존재하지 않는 `XAMLVIEWER_HOST_EXE`로 **별도 VS Code를 한 번 더 띄워**(스위트별 실행) 안내 알림과 E012 로그, 확장 활성 유지를 확인한다. 테스트용 API(`ExtensionTestApi`: hostFound/getHostPid/getLogLines)를 `activate` 반환값으로 노출한다.
- **동작 변경**: `XAMLVIEWER_HOST_EXE`가 설정되면 그 경로**만** 쓴다(틀린 경로일 때 다른 호스트로 조용히 대체되지 않게). 단위 테스트로 고정.
- **CI 수정**: `ci.ps1 -IncludeIntegration`이 VS Code의 stderr 진행 메시지를 PowerShell 5.1이 오류로 취급해 중단되던 문제를 고침(해당 단계만 `Continue`, 종료 코드로 판정).
- **미검증/제한**: 경고(warnings) 표시는 아직 없다. I-05의 알림은 `showErrorMessage`를 테스트에서 교체해 검증했고 실제 알림 UI 표시는 눈으로 확인하지 않았다. 통합 테스트는 `.vscode-test`에 VS Code 1.140.0을 내려받아 쓴다(인터넷 필요, git 무시됨).
- 이 PC(Windows 10 Pro 19045)에서 호스트 골든 58개가 통과했다 → 다른 머신 일치 항목(04 문서 6-1)은 2대에서 확인됨.

### M4. XAML 해석 충실도
| # | 단계 | 검증 |
|---|---|---|
| 4.1 | `x:Class`/이벤트 핸들러/`x:Code` 제거 | 해당 픽스처가 오류 없이 렌더 + 제거 대상 목록이 warnings에(T1/T2) |
| 4.2 | `d:`/`mc:Ignorable`, `d:DataContext` 처리 | 디자인 타임 전용 속성이 있는 픽스처 골든 |
| 4.3 | `ResourceDictionary` 병합(상대 경로), `App.xaml` 리소스 자동 탐색 | 병합 사전 픽스처 골든, 경로 오류 시 경고 + 계속 렌더 |
| 4.4 | 해석 불가 사용자 타입 → 자리표시자(Tier 0, H013) | `local:MyControl` 포함 픽스처: 나머지 요소 정상 + 자리표시자 박스 골든 |
| 4.5 | `Window` 루트는 콘텐츠를 `Border`로 호스팅해 렌더(창 크롬 제외) | Window/UserControl/Page/Grid 루트별 골든 |

**M4 결과 (2026-10-07)**
- 구현: `XamlPreprocessor`(호스트) — ① x:Class/x:Subclass/x:ClassModifier/x:FieldModifier 제거 ② 이벤트 핸들러 속성 제거(System.Xaml로 "이 요소 타입의 이벤트인가" 판별, `Owner.Event` 연결 이벤트 포함) ③ x:Code 제거 ④ `clr-namespace` 타입이 해석되지 않으면 Border+TextBlock 자리표시자(Width/Height/Margin/정렬/연결 속성/x:Name/x:Key만 승계, 자식은 버림) ⑤ `ResourceDictionary Source` 병합 사전 인라인(문서 폴더 → 위쪽 폴더 순 탐색, pack `;component/` 경로 지원, 순환/깊이 8 초과/네트워크 URI/없음은 경고 + 빈 사전). `Window` 루트는 콘텐츠를 Border에 호스팅(크기/배경/리소스/글꼴·전경 로컬 값 승계). d:/mc:는 XamlReader가 직접 처리해 별도 코드가 없다(R12 골든으로 확인).
- **줄 번호 보존**: 제거/대체 구간은 줄바꿈 수를 유지한다(원본 문자열을 오프셋으로 편집). 여러 줄에 걸친 속성을 제거해도 뒤쪽 오류 줄이 원본과 같다(H-X05, U02). XML이 깨진 문서는 전처리가 직접 XmlMalformed로 보고한다(x:Class 오류가 원인을 가리지 않게).
- 프로토콜: `render` 파라미터 `filePath`(병합 사전 해석용), 응답 `warnings:[{code,message,line?,col?}]` 활성. 코드: RemovedClassAttribute, RemovedEventHandler, RemovedCodeBlock, PlaceholderUsed, DictionaryUnavailable. 자리표시자는 H013 로그(최대 5개). 확장은 `filePath`를 보내고 상태 표시줄에 경고 개수와 툴팁을 보여준다.
- 검증: 호스트 78개(신규 20: H-X01~X06, H-W01~W02, H-U01~U02, H-RD01~RD04, 골든 R11~R14 눈으로 확인), 실제 호스트 + HostClient 9개(신규: x:Class/이벤트 경고 + filePath 병합).
- 테스트 프로젝트: `Fixtures/**/*.xaml`을 WPF Page로 컴파일하지 않도록 제외(해석 불가 타입 픽스처가 빌드 오류가 되므로).
- **미검증/제한 (실제 프로젝트 XAML에서 만날 수 있는 것)**:
  1. ~~`App.xaml` 리소스 자동 탐색 미구현~~ → **구현 완료**(아래 "M4 잔여: App.xaml" 참고).
  2. 해석 불가 타입이 **요소가 아닌 자리**(`TargetType="local:Foo"`, `{x:Type local:Foo}`, `{x:Static local:Foo.Bar}`, `{local:MyExtension}`, `Style TargetType`)에 쓰이면 자리표시자로 대체되지 않고 XamlParse 오류가 난다.
  3. 자리표시자는 자식을 버린다(사용자 컨트롤 안의 내용은 보이지 않는다). Tier 1(M4B)에서 실제 렌더 예정.
  4. `Window`의 `SizeToContent`, `WindowStyle`, 타이틀바는 무시한다(콘텐츠만, 명시 크기 우선).
  5. 이벤트 판별은 WPF 기본 네임스페이스 타입 기준이다. 해석되는 사용자 타입의 이벤트는 그 타입이 로드될 때(M4B)만 알 수 있다.
  6. 기존 골든 PNG 10개를 이 PC(Windows 10)에서 `XAMLVIEWER_UPDATE_GOLDEN=1`로 다시 만들면 **바이트가 달랐다**(허용오차 안이라 비교 테스트는 두 머신 모두 통과). 원본(Windows 11에서 만든 것)은 되돌려 두었고 신규 골든 4개만 이 PC에서 생성했다. 허용오차를 넘는 차이가 생기면 03 문서 2절 절차(원인 확정 먼저)를 따른다.

**M4 잔여: App.xaml 리소스 자동 탐색 결과 (2026-10-08)**
- 구현(`XamlPreprocessor.AppResources.cs`): 문서 폴더에서 위로 올라가며 가장 가까운 `App.xaml`을 찾는다(**`.csproj`가 있는 폴더가 프로젝트 경계** — 거기서 멈춰 다른 프로젝트의 App.xaml을 쓰지 않는다, 최대 6단계). `Application.Resources`를 전처리(App.xaml 안의 병합 사전/자리표시자 포함)한 뒤, 문서 루트의 `Resources`에 **맨 앞 병합 사전**으로 텍스트 주입한다(문서 자신의 리소스가 앱 리소스보다 우선). 주입 조각은 줄바꿈이 없어 오류 줄 번호가 보존된다.
- 주입 경우: 루트에 Resources 없음 / 명시 ResourceDictionary(MergedDictionaries가 있으면 그 앞, 없으면 생성) / 항목만 나열한 암시적 사전(ResourceDictionary로 감쌈) / 자기 닫는 루트.
- **실측 함정 2가지(둘 다 테스트로 고정)**:
  1. WPF `XamlReader`는 **속성 요소에 붙인 `xmlns` 선언을 거부**한다("PROPERTYELEMENT 예기치 않음"). 객체 요소에 접두사(`rxapp`)를 선언하고 속성 요소를 그 접두사로 쓴다.
  2. 루트 요소 **자신의** `Width="{StaticResource W}"` 같은 속성은 같은 요소의 `Resources` 속성 요소보다 먼저 평가돼 주입한 리소스를 못 본다. 그래서 루트의 단순 `{StaticResource Key}` 속성은 속성 요소(`<Root.Width><StaticResource .../></Root.Width>`)로 옮겨 Resources 뒤에 둔다(접두사/연결 속성/복합 값은 제외).
- App.xaml 자체의 `x:Class`/`Startup=` 등 제거 경고는 사용자 문서와 무관해 버리고, 자리표시자/사전 문제만 "App.xaml:" 접두에 줄 번호 없이 경고로 전달한다.
- 검증: 호스트 87개(신규 9: H-RD05~RD13 — 항목 없음/명시 사전/기존 병합/암시적 사전/테마 상대 경로/자리표시자 경고/자기 닫는 루트+줄 번호/프로젝트 경계/App.xaml 없음).
- **미검증/제한**: App.xaml이 `Application.Resources`를 **코드 비하인드에서** 채우는 경우, 루트 아닌 요소의 속성에서 쓰는 `StaticResource`가 같은 요소의 자기 Resources를 가리키는 경우(WPF 컴파일 동작과 다를 수 있음), 루트의 복합 값(`{StaticResource K}` 이외 마크업)은 처리하지 않는다. 앱 리소스가 매우 큰 경우 매 렌더마다 App.xaml을 다시 읽고 전처리한다(캐시 없음; 성능은 미측정).

### M4B. 프로젝트 인식 렌더링 (Tier 1 — 사용자 정의 컨트롤 실제 렌더)
설계: 01 문서 §3.3. 사용자 코드가 실행되므로 이 마일스톤은 **격리·신뢰 검증이 핵심**이다.

| # | 단계 | 검증 |
|---|---|---|
| B.1 | 가장 가까운 `.csproj` 탐색, 산출물(`bin/<Config>/<TFM>/*.dll` + `.deps.json`) 해석 | 픽스처 프로젝트(SDK-style WPF, 사용자 컨트롤 포함) 구조별 경로 해석 단위 테스트(T1): 다중 구성, 다중 TFM, 산출물 없음 |
| B.2 | `AssemblyLoadContext` 로드(임시 폴더 복사), 의존성 해석, 갱신 시 재로드 | 사용자 컨트롤이 든 XAML 골든 일치(T2), DLL 교체 후 새 결과 반영 + 원본 파일 잠금 없음(빌드 가능) |
| B.3 | `IsInDesignMode=true`, 생성자 예외 → 오류 자리표시자(H022) | 생성자가 throw/IsInDesignMode 분기하는 컨트롤 픽스처 골든 |
| B.4 | Workspace Trust 연동(미신뢰면 Tier 0, H023) | 통합 테스트: 신뢰/미신뢰 각각에서 H023 로그와 결과 확인(T4) |
| B.5 | 프로젝트별 호스트 분리, 유휴 종료 | 프로젝트 2개를 번갈아 편집 → 호스트 2개, 유휴 시간 후 종료(T3/T4) |
| B.6 | 결함 주입: 정적 생성자 무한 루프, 크래시(StackOverflow), 느린 생성자 | 타임아웃 → kill → 재시작, VS Code 무영향(T4) |
| B.7 | 최신 WPF 기능 픽스처(.NET 9+ `ThemeMode`/Fluent) | 골든 일치(T2) |

**M4B 결과 (2026-10-08)** — B.1~B.4, B.6, B.7 완료 / **B.5(프로젝트별 호스트 프로세스 분리, 유휴 종료)는 미구현**
- 구현(호스트): `ProjectLocator`(가장 가까운 .csproj → `bin/**/<AssemblyName>.dll` 중 수정 시각 최신, `<AssemblyName>` 속성 반영), `UserAssemblies`(수집 가능 `AssemblyLoadContext` + 산출물 폴더 **임시 복사본 로드**로 원본 비잠금, 수정 시각/크기 변경 시 Unload 후 재로드, deps.json 의존성 + 참조 어셈블리 선로드), `ProjectTier`(신뢰/산출물/로드 결과 → Tier 0/1과 이유), 사용자 컨트롤 예외 → 해당 요소만 **오류 자리표시자(주황)** + `UserControlFailed` 경고 후 재파싱(최대 20회), `DesignerProperties.IsInDesignMode` 기본값 true, `assembly=` 없는 `clr-namespace`는 프로젝트 어셈블리로 보정.
- 구현(확장): `render` 파라미터 `allowProjectAssemblies = vscode.workspace.isTrusted`, 폴더를 신뢰하는 순간(`onDidGrantWorkspaceTrust`) 자동 재렌더. 호스트는 이 플래그가 false면 사용자 DLL을 **절대 로드하지 않는다**(테스트로 고정: 미신뢰에서 던지는 생성자가 실행되지 않음).
- 프로토콜: 응답에 `project:{tier,reason}`, 경고 코드 `UserControlFailed`, `ProjectTier0`(자리표시자가 생겼고 이유가 Tier 0일 때만: Untrusted/NoFilePath/NoProject/NoArtifact("먼저 dotnet build")/LoadFailed). 로그 H020(로드/재로드, 경로·ms) / H021(로드 실패) / H022(생성자 예외, 최대 5개) / H023(Tier 결정, **세션 첫 렌더와 결정이 바뀔 때만** — 그래서 기존 로그 순서 테스트가 `H001 H011 H023 H012 H002`로 바뀜).
- 검증: 호스트 100개(신규: H-T01~T09 + 골든 R15/R16 Fluent Light/Dark를 눈으로 확인), 실제 호스트 + HostClient 13개(신규 4: 신뢰/미신뢰/생성자 예외/B.6 무한 대기 생성자 → 타임아웃 → kill → 새 호스트 정상), 실제 VS Code 통합 6개(신규 I-10: 신뢰된 워크스페이스에서 프로젝트 DLL의 `RedBox`가 40x20으로 그려짐). 샘플 사용자 프로젝트 `Fixtures/projects/SampleControls`는 테스트가 `dotnet build`로 빌드한다(변형 B = 폭 80으로 재로드 검증).
- **실측 함정(테스트로 고정)**:
  1. 같은 어셈블리 이름이 여러 번(여러 프로젝트/DLL 교체 전후) 로드되면, WPF의 **공유 스키마 컨텍스트는 이름으로 찾고 결과를 캐시**해서 엉뚱한(또는 옛) 복사본의 타입을 쓴다 — 병렬 테스트에서 "알 수 없는 형식"으로 드러났고 DLL 재로드도 막는다. 해결: Tier 1에서는 **렌더마다 새 `XamlSchemaContext`를 만들고 참조 어셈블리 목록을 명시**(기본 컨텍스트 어셈블리 + 이번에 로드한 사용자 어셈블리와 그 컨텍스트의 의존 어셈블리)한 뒤 `XamlReader.Load(XamlXmlReader)`로 파싱한다. Tier 0은 기존 `XamlReader.Parse` 경로 그대로다(골든 불변).
  2. 기존 로그 ID 규약상 H023이 첫 렌더에 추가돼 로그 순서 테스트 두 곳(호스트 L01, 확장 I-07)을 갱신했다.
- **미검증/제한 (숨기지 않고 기록)**:
  1. **보안 모델은 Workspace Trust 하나뿐이다.** 신뢰하면 사용자 코드(생성자/정적 생성자)가 호스트 프로세스 안에서 사용자 권한으로 그대로 실행된다. 샌드박스는 없다.
  2. **B.5 미구현**: 지금은 호스트 프로세스 1개가 모든 프로젝트를 ALC로 분리해 처리한다. 사용자 코드가 멈추거나 죽으면 호스트가 kill되고 다음 요청에서 재시작되므로(B.6 검증) 확장 영향은 없지만, 다른 프로젝트의 로드 상태도 같이 사라진다. 프로세스 분리/유휴 종료는 필요해지면 확장 쪽 `HostClient` 풀로 구현한다.
  3. **Unload된 ALC가 실제로 GC되어 메모리가 해제되는지는 검증하지 않았다**(정적 필드/WPF 내부 캐시가 참조를 잡으면 수집되지 않을 수 있다). DLL을 계속 다시 빌드하며 오래 쓰는 시나리오의 메모리 증가는 미측정.
  4. 사용자 코드가 무한 루프이면 그 요청은 기본 10초 타임아웃까지 호스트를 막는다(그 동안 같은 호스트의 다른 요청도 대기). `Hanging` 생성자로 kill/재시작은 검증했지만 StackOverflow/네이티브 크래시는 시험하지 않았다(호스트 크래시 복구는 `debug.crash`로 검증됨).
  5. 생성자 예외 판별은 "예외 스택에 사용자 컨텍스트 어셈블리의 프레임이 있다"는 휴리스틱이다. 사용자 코드를 거치지 않은 값 오류는 그대로 XamlParse 오류로 보고되고, 비동기/지연 실행되는 사용자 코드의 예외(로드 이벤트 등)는 다루지 않는다.
  6. 신뢰되지 않은 워크스페이스의 **통합(VS Code) 테스트는 없다**(테스트 러너가 `--disable-workspace-trust`로 신뢰 상태). 미신뢰 경로는 실제 호스트 테스트로 검증했고 `vscode.workspace.isTrusted` 전달 자체는 코드 리뷰 수준이다.
  7. 사용자 프로젝트의 TFM이 net8/9여도 호스트(net10)에 로드된다(상위 호환 가정). net10 이전 전용 API/런타임 동작 차이, 32비트/AnyCPU 외 산출물, 서명/강한 이름 충돌은 시험하지 않았다. `Microsoft.NET.Sdk.WindowsDesktop` 등 구형 SDK 프로젝트의 `bin` 구조도 샘플 한 가지만 확인했다.
  8. Tier 1에서 `XamlReader.Load(XamlXmlReader)` 경로가 `Parse`와 완전히 같은 결과인지는 샘플 시나리오(스타일/템플릿 일부, App.xaml 리소스, 루트 속성 이동, 줄 번호)로만 확인했다.

### M5. 상호작용
| # | 단계 | 검증 |
|---|---|---|
| 5.1 | 줌/팬/배경 전환/크기 지정 UI | 웹뷰 상태 메시지로 확인(T5) |
| 5.2 | HitMap: 요소 경계 ↔ 원본 줄/열(XML LineInfo와 논리 트리 대응) | 픽스처별 기대 매핑 표 일치(T1) |
| 5.3 | 미리보기 클릭 → 에디터 줄 이동, 커서 → 하이라이트 | 통합 테스트: 클릭 메시지 → `activeTextEditor.selection` 확인(T5) |

**M5 결과 (2026-10-08)**
- **HitMap(호스트)**: 요소↔원본 위치를 잇는 방법은 **전처리가 UIElement 타입 요소의 시작 태그에 `Uid="xv_<번호>"`를 붙이는 것**이다(번호 = 원본 위치 목록의 인덱스). XamlReader는 줄 정보를 남기지 않고, 논리 트리 순서와 XML 순서를 맞추는 방식은 속성 요소/템플릿/콘텐츠 래퍼 때문에 깨지기 쉬워 버렸다. 렌더 후 비주얼 트리를 선위 순회해 `Uid`가 있는 요소의 루트 기준 경계를 `dpi/96` 배율로 PNG 픽셀로 바꿔 `elements:[{id,line,col,endLine,endCol,x,y,w,h}]`로 보낸다(최대 5,000개, 넘으면 `HitMapTruncated` 경고). 목록 순서 = 선위 순회라 **점을 포함하는 마지막 요소가 가장 안쪽/위쪽**이다. 템플릿이 만든 내부 요소는 Uid가 없어 제외되고 클릭은 가장 가까운 태그된 조상에 귀속된다(ControlTemplate 정의 안의 요소는 태그되지만 렌더 결과에서는 그 컨트롤 영역으로 나타남). 자리표시자(미해석/오류), `Window` 루트(호스트 Border가 Window의 위치를 대신 가짐)도 매핑된다.
- **줄/열**: 열은 시작 태그의 `<` 위치, 끝은 끝 태그 `>` 다음 위치(커서 → 요소 선택에 필요). 태그 삽입은 같은 줄의 열을 밀기 때문에 **XamlParse 오류가 나면 태그 없이 한 번 더 파싱해 원본 기준 열을 보고**한다(H-M08).
- **확장/웹뷰(M5.1)**: 툴바(줌 −/+/맞춤/100%, 배경 체크무늬/흰색/어둡게, 렌더 크기 W/H 적용·자동), Ctrl+휠 줌(포인터 기준 유지), 드래그 팬, 3px 미만 이동은 클릭으로 보고 이미지 픽셀 좌표를 확장에 전송, 선택 요소 강조 사각형, 줌 2배 이상은 픽셀 확대. 인라인 style 속성은 CSP 때문에 쓰지 않는다(CSSOM/클래스만).
- **클릭 ↔ 에디터(M5.3)**: 클릭 → 가장 안쪽 요소 선택 → 에디터 커서를 요소 시작으로 이동·화면 중앙 표시(보이는 에디터가 없으면 연다) + 강조. 에디터 커서 이동 → 커서를 포함하는 가장 안쪽 요소를 미리보기에서 강조(마지막으로 반영한 렌더의 위치 기준; 새 렌더가 오면 다시 계산). 빈 곳 클릭/문서 밖 커서는 강조 해제.
- 프로토콜: `elements` 항목에 `endLine`,`endCol` 추가, `render`에 `width/height`(요청 크기)는 기존 그대로 UI가 사용.
- 검증: 호스트 110개(신규 HitMap M01~M10: 단순/중첩/ControlTemplate/겹침 z-order/DPI 192/Window/자리표시자+제거 속성/오류 열/사용자 Uid/5,050개 상한), 확장 단위 40개(신규: 클릭·커서 선택 로직, 메시지 범위 검증, 웹뷰 스크립트 컴파일, style 속성 없음), 실제 VS Code 통합 9개(신규 I-06 클릭 → 줄/열 이동 + 가장 안쪽 요소 + 빈 곳, I-06b 커서 → 강조/해제, I-05b 확장이 지정한 줌/배경을 **실제 웹뷰가 적용하고 회신**, I-05c 렌더 크기 지정/해제/잘못된 값 무시).
- **미검증/제한 (숨기지 않고 기록)**:
  1. **웹뷰의 실제 마우스/휠 이벤트는 자동 검증하지 못했다.** 확장 호스트에서 브라우저 DOM 이벤트를 만들 수 없어 클릭은 `simulateWebviewMessage`(실제 수신 경로와 같은 처리 함수)로 넣었다. 드래그 팬, Ctrl+휠 줌, `getBoundingClientRect` 기반 클릭 좌표 계산, 버튼/입력 UI는 **눈으로도 확인하지 않았다**(스크립트 문법 컴파일과 setView/viewState 왕복만 확인). 첫 실사용 때 가장 먼저 의심할 곳이다.
  2. 크기가 0인 요소(빈 Border, Collapsed 등)는 HitMap에서 제외되어 클릭할 수 없다. 사용자가 `Uid`를 직접 지정한 요소와 UIElement가 아닌 요소(Run 등)는 매핑되지 않는다(Run 안 커서는 그 TextBlock이 강조됨).
  3. 같은 XAML 요소가 DataTemplate/ControlTemplate로 여러 번 나타나면 같은 원본 위치의 항목이 여러 개다(클릭은 그중 맞는 곳, 커서 → 강조는 첫 항목).
  4. Tier 1에서 XAML 오류가 나면 오류 위치 재계산을 위해 **사용자 생성자가 한 번 더 실행**된다(오류 경로 한정).
  5. 렌더 중 줌/스크롤 위치는 웹뷰가 유지하지만(retainContextWhenHidden) 패널을 닫았다 다시 열면 기본값(100%, 체크무늬, 크기 자동)으로 돌아간다.
  6. HitMap 수집 비용은 5,000개 문서에서 기존 I-08 시간 예산 안이었지만 별도로 측정하지는 않았다.

### M6. 패키징 + 문서
| # | 단계 | 검증 |
|---|---|---|
| 6.1 | 호스트를 확장에 번들(`extension/bin/host/`), 경로 해석 | 번들된 `.vsix`를 임시 VS Code 프로필에 설치 후 스모크 통과 |
| 6.2 | `vsce package`, `tools/ci/ci.ps1`에 패키징 단계 | `.vsix` 생성 + 설치 스모크 |
| 6.3 | `doc/User_Guide.md`, README 갱신 | 문서 내 명령/설정 이름이 package.json과 일치(린트 테스트) |
| 6.4 | **VS 미설치 환경 검증**: VS 없는 Windows(VM/Windows Sandbox)에서 `.vsix` 설치 → 실제 net10 WPF 샘플 프로젝트 미리보기 | 체크리스트(가능하면 Sandbox 스크립트로 자동화), 결과를 문서에 기록 |

**M6 결과 (2026-10-08)** — 6.1~6.3 완료 / **6.4(VS 없는 깨끗한 PC 검증)는 수행하지 못했다**
- **6.1/6.2 패키징**: `tools/package/build_vsix.ps1`(= `npm run package`)가 `artifacts/vsix-stage`에 필요한 파일만 모아 거기서 `vsce package --no-dependencies`를 실행한다. 호스트는 `dotnet publish -c Release -r win-x64 --self-contained false`로 `bin/host/`에 번들(exe + dll + deps.json + runtimeconfig, 4개 파일). **결과 `.vsix` 약 160KB**(프레임워크 종속이라 .NET 10 Desktop Runtime이 사용자 PC에 필요). `.vscodeignore`는 `out/src`와 `bin/host`만 포함한다. 스테이징을 쓰는 이유: 개발 트리에 `extension/bin/host`가 생기면 `hostLocator`가 번들 위치를 먼저 보므로 테스트가 개발 빌드 대신 낡은 번들 호스트를 집어 갈 수 있다. 라이선스는 `GPL-3.0-only`(LICENSE 파일을 스테이징에 복사).
- **I-09 설치 스모크**(`npm run test:smoke`, `ci.ps1 -IncludePackage`): `.vsix`를 임시 user-data/extensions 폴더에 `--install-extension`으로 설치 → `dotnet new wpf --framework net10.0`으로 만든 실제 프로젝트에 App.xaml 리소스, `x:Class`/이벤트, 사용자 컨트롤(`Badge`)을 얹어 빌드 → **개발 확장 없이 설치본만** 로드한 VS Code에서 확인: 설치 폴더의 확장이 활성화, 호스트 경로가 설치 폴더의 `bin\host`, MainWindow가 800x450으로 그려짐, 경고에 `RemovedClassAttribute`/`RemovedEventHandler`는 있고 `PlaceholderUsed`/`ProjectTier0`는 없음(Tier 1 동작), 요소 3개 이상 매핑. 첫 실행에 통과.
- **I-12 .NET 런타임 없음**: 실측 — 런타임이 없으면 apphost가 종료 코드 `0x80008083`(-2147450749)과 stderr "You must install .NET to run this application."을 낸다(`DOTNET_ROOT_X64`가 지정되면 그 위치만 찾는다). `runtimeCheck.ts`가 종료 코드(부호 있는/없는 표현) 또는 문구로 식별해 설치 안내 알림(+ 설치 페이지 열기 버튼)을 띄운다. 검증: 단위 3개 + **실제 호스트를 빈 `DOTNET_ROOT`로 띄워** HostCrashedError가 "런타임 없음"으로 식별됨(알림 UI 자체는 눈으로 확인하지 않음).
- **6.3 문서**: `doc/User_Guide.md`(개발 흐름, 이 확장이 하는 일/안 하는 일, 명령, 미리보기 기능, 사용자 컨트롤/신뢰/보안, 변환 규칙과 경고 코드, 제한, 문제 해결, 로그), 루트 README와 `extension/README.md` 갱신. **린트(X-P01)**: package.json의 모든 명령 ID/제목이 가이드에 있고 가이드의 `wpfXamlViewer.*` 이름이 전부 package.json에 있으며, 설정이 없다는 문장이 사실과 맞는지 단위 테스트가 검사한다.
- **6.4 미수행(사유)**: 개발 PC에는 Visual Studio 2017/2019/2022가 설치되어 있고 Windows Sandbox 기능이 꺼져 있어(켜려면 관리자 권한 + 재부팅) "VS 없는 PC"를 재현할 수 없었다. `tools/verify/clean_machine.wsb` + `sandbox_bootstrap.ps1`(.NET SDK 10/VS Code 설치 → .vsix 설치 → 샘플 생성/빌드 → VS Code 열기)을 **작성만 했고 실행해 보지 못했다**(URL/옵션이 어긋날 수 있음). 사람이 따를 체크리스트와 결과 기록표는 `doc/05_Clean_Machine_Verification.md`. 이 PC의 스모크는 SDK 10과 VS가 있는 환경이라는 점에서 깨끗한 PC 검증을 **대체하지 못한다**(특히 "런타임 없는 PC에서 첫 실행 → 설치 안내 → 설치 후 동작" 흐름).
- **미검증/제한**: ① 위 6.4 전체 ② ~~확장 이름/게시자 임시값~~ → 확정됨(아래 "배포 준비" 참고) ③ ~~아이콘/배너/변경 이력 없음~~ → 추가됨(아래) ④ 호스트가 프레임워크 종속이라 런타임 설치가 사용자 몫(self-contained 번들은 `.vsix`가 수십~100MB+로 커져서 보류, 01 문서 §7 결정 유지) ⑤ `npm audit`의 개발 의존성(mocha 계열) 경고는 `.vsix`에 포함되지 않지만 정리하지 않았다 ⑥ 설치 스모크는 인터넷이 필요하다(VS Code 다운로드 캐시가 없을 때, `dotnet new` 템플릿/복원) ⑦ 서명되지 않은 `.vsix`/호스트 exe라 SmartScreen/백신이 경고할 수 있다.

**배포 준비 결과 (2026-10-08, M6 후속)**
- **이름/게시자 확정(사용자 결정)**: 표시 이름 `WPF XAML Live Preview`, name `wpf-xaml-live-preview`, 게시자 `phulgrimlab` → 확장 ID `phulgrimlab.wpf-xaml-live-preview`. 변경 범위: package.json(name/displayName), 출력 채널/진단 출처/알림 문구/launch.json 이름, 테스트의 확장 ID 상수 3곳, 문서 표시 이름, 산출물 파일명(`wpf-xaml-live-preview-<버전>.vsix`). **바꾸지 않은 것**: 저장소 이름(`WPFXamlViewer_VSCode`), 명령 ID(`wpfXamlViewer.openPreview`)와 명령 제목(`WPF XAML: Open Preview`) — 내부 식별자라 바꿔도 사용자 이득이 없고 린트가 일관성을 지킨다. 설치된 사용자의 확장 전역 저장소(로그) 경로는 확장 ID를 따라가므로 이름이 바뀌면 달라진다(이번이 첫 확정이라 영향 없음).
- **배포 채널(사용자 결정)**: GitHub Release에 `.vsix` 첨부. 절차/점검표/설치 안내/신뢰·보안 문구는 `doc/06_Release_Process.md`. `build_vsix.ps1`이 `artifacts/SHA256SUMS.txt`(SHA256)를 함께 만든다.
- **패키지 메타데이터**: 아이콘(`tools/package/icon.xaml` → **이 프로젝트의 호스트로 렌더**한 256x256 `extension/icon.png`, 재생성 `tools/package/make_icon.ps1`, 눈으로 확인), `preview: true`, categories(Visualization/Other), homepage/bugs, 갤러리 배너 색, `extension/CHANGELOG.md`(0.1.0, 알려진 한계 포함). `.vsix` 176KB(35개 파일), vsce 경고 없음.
- **하지 않은 것(의도적)**: GitHub Release/태그 생성(외부 공개 행위 — 사용자 확인 필요), Marketplace 게시(게시자 계정/PAT 필요, 되돌리기 어려움), 코드 서명(인증서 없음). 이 PC에는 `gh`가 설치되어 있지 않다(doc/06에 웹 UI 방법과 gh 방법 모두 기록).
- **미검증/제한**: ① 깨끗한 PC 검증(doc/05)이 여전히 미실행이라 "처음 설치한 사람이 겪는 흐름"은 사람 눈으로 확인된 적이 없다 ② 서명되지 않은 exe/vsix는 SmartScreen/백신 경고 가능 ③ Marketplace의 이름 중복/게시자 계정 존재 여부는 확인하지 않았다 ④ 릴리스 본문/설치 안내는 문서로만 존재하며 실제 릴리스 페이지에서 시험하지 않았다.

**"어느 PC에서든 소스에서 .vsix 빌드" 결과 (2026-10-10, 사용자 요구)** — 다른 PC에서 `npm run package`가 `Cannot find module '@vscode/test-electron'`으로 실패한 것이 계기. 원인: 패키징이 테스트 코드까지 컴파일해서 개발 도구가 빠진 새 클론에서 실패.
- **고친 것**: ① 패키징 전용 `extension/tsconfig.build.json`(src만 컴파일, `npm run compile:build`) — 테스트 도구 없이도 빌드 ② `build_vsix.ps1`이 시작할 때 **전제 조건을 한 번에 점검**(Windows, .NET SDK 10+, Node 20+, npm)하고 부족한 항목을 설치 명령과 함께 모두 알려 주고 종료 코드 1로 중단, 의존성(`vsce`/`tsc`)이 없으면 `npm ci` 자동 실행, 스테이징 폴더 삭제 실패/컴파일 결과 없음/호스트 publish 결과 없음에 구체적 메시지 ③ `@vscode/vsce`를 4.x(Node 22 필요) → **3.9.2(Node 20 필요)** 로 낮춰 지원 PC 확대(`@vscode/test-electron`은 Node 22 필요하나 테스트 전용이라 패키징에 무관, EBADENGINE 경고만) ④ `global.json`(SDK 10 이상, `rollForward: latestMajor`) — SDK 10이 없으면 불친절한 빌드 오류 대신 명확한 메시지 ⑤ `.gitattributes`(`* text=auto`, `*.ps1/*.cmd/*.wsb`는 CRLF, 바이너리 지정) — PC의 `core.autocrlf` 설정과 무관하게 BOM+CRLF 스크립트로 체크아웃 ⑥ `check_environment.ps1 -PackagingOnly`(패키징 필수 항목만, Desktop Runtime/VS Code/git은 선택) + Node 버전 점검 ⑦ README "소스에서 .vsix 만들기"(준비물 표, 4단계, 문제 해결 표)와 doc/00 요구사항 표를 "패키징만 / 테스트까지"로 분리.
- **검증(실측)**: 방금 푼 **새 클론**을 **공백+한글이 들어간 경로**(`...\빌드 테스트 폴더\소스 클론`)에, `core.autocrlf=false`로, `node_modules`/`artifacts` 없이 만들고, **저장소 밖 작업 폴더(C:\Windows)** 에서 README의 명령 그대로 실행 → **102초 만에 성공**(npm ci + publish 포함, 176KB .vsix). 그 클론의 `.vsix`로 **설치 스모크 통과**(번들 호스트, 실제 `dotnet new wpf` 프로젝트 미리보기). 실패 경로: 요구 버전을 높인 복사본은 .NET/Node 부족 두 항목을 한 번에 안내하고 종료 코드 1, `node_modules` 없이 `-SkipInstall`은 `npm ci` 안내와 종료 코드 1.
- **미검증/제한**: ① 같은 PC에서 한 시뮬레이션이라 **.NET SDK 10/Node가 아예 없는 PC**, Node 20·21 PC(EBADENGINE 경로), 프록시/오프라인 PC, 실행 정책이 `Restricted`인 PC에서의 첫 실행은 직접 확인하지 못했다(이 PC에는 SDK 8도 함께 설치되어 있어 "여러 SDK 중 10 선택"은 확인) ② 오프라인 환경은 지원하지 않는다(npm/NuGet 복원 필요) ③ `winget`이 없는 PC에서는 README의 설치 링크(공식 사이트)를 써야 한다 ④ ZIP으로 받은 소스의 "인터넷에서 받은 파일" 차단은 `Unblock-File` 안내만 있고 시험하지 않았다.

## 3. 위험과 대응

| 위험 | 영향 | 대응 |
|---|---|---|
| 골든 이미지가 머신/폰트/DPI마다 달라져 테스트가 흔들림 | T2 신뢰도 | SoftwareOnly + 96DPI + 시스템 폰트(Segoe UI/Consolas)만 사용, 픽셀 허용오차 + 차이 비율 기준, 차이 이미지를 TestResults에 저장 |
| 요소 ↔ 원본 줄 매핑 불일치(템플릿으로 생성된 요소) | M5 | 논리 트리에서 XAML에 **직접 쓴** 요소만 매핑(나머지는 부모로 귀속) |
| XAML이 외부 리소스(웹 URL, 절대 경로)를 읽음 | 보안/지연 | 네트워크 URI 차단, 파일 경로는 작업 폴더 하위만 허용(옵션) |
| 메모리 증가(큰 Bitmap) | 장시간 사용 | 최대 렌더 크기 상수, 호스트 N회 렌더/메모리 임계 시 재시작 |
| Node 미설치 | 일정 | M0 첫 단계에서 사용자 설치 필요(00 문서 4절) |
| 사용자 컨트롤 생성자가 부작용(파일/DB/네트워크)을 일으킴 | 안전/신뢰 | Workspace Trust 필수, 디자인 모드 플래그, 타임아웃/프로세스 격리, 한계를 문서에 명시(01 §3.3) |
| 사용자 PC에 .NET 10 Desktop Runtime 없음 | 첫 사용 실패 | 시작 시 점검 + 설치 안내 알림(00 문서 5절), 테스트 I-12 |
| 호스트 .NET 버전 < 대상 프로젝트 TFM(예: net11) | 렌더 실패/부분 렌더 | 대상 TFM을 H020 로그에 남기고 경고 표시, 호스트 TFM 상향 정책 문서화 |

## 4. 완료 정의 (전체)
- `tools/ci/ci.ps1` 한 번으로 T1~T4(+가능하면 T5)가 모두 통과.
- 골든 이미지/로그 검증/결함 주입 테스트가 포함되어 있다.
- 문서: 설계(01), 계획(02), 테스트(03), 사용자 가이드, 마일스톤별 결과 기록.
