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

### M5. 상호작용
| # | 단계 | 검증 |
|---|---|---|
| 5.1 | 줌/팬/배경 전환/크기 지정 UI | 웹뷰 상태 메시지로 확인(T5) |
| 5.2 | HitMap: 요소 경계 ↔ 원본 줄/열(XML LineInfo와 논리 트리 대응) | 픽스처별 기대 매핑 표 일치(T1) |
| 5.3 | 미리보기 클릭 → 에디터 줄 이동, 커서 → 하이라이트 | 통합 테스트: 클릭 메시지 → `activeTextEditor.selection` 확인(T5) |

### M6. 패키징 + 문서
| # | 단계 | 검증 |
|---|---|---|
| 6.1 | 호스트를 확장에 번들(`extension/bin/host/`), 경로 해석 | 번들된 `.vsix`를 임시 VS Code 프로필에 설치 후 스모크 통과 |
| 6.2 | `vsce package`, `tools/ci/ci.ps1`에 패키징 단계 | `.vsix` 생성 + 설치 스모크 |
| 6.3 | `doc/User_Guide.md`, README 갱신 | 문서 내 명령/설정 이름이 package.json과 일치(린트 테스트) |
| 6.4 | **VS 미설치 환경 검증**: VS 없는 Windows(VM/Windows Sandbox)에서 `.vsix` 설치 → 실제 net10 WPF 샘플 프로젝트 미리보기 | 체크리스트(가능하면 Sandbox 스크립트로 자동화), 결과를 문서에 기록 |

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
