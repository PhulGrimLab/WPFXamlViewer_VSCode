# 01. 아키텍처와 요구사항

> 상위 문서: 없음(이 문서가 출발점). 후속: [02_Development_Plan.md](./02_Development_Plan.md),
> [03_Test_Strategy.md](./03_Test_Strategy.md). 환경 제약은 [00_Environment_Setup_Guide.md](./00_Environment_Setup_Guide.md).

## 1. 목표

VS Code에서 `.xaml` 파일을 편집하는 동안 **실제 WPF가 그리는 것과 같은 모습**을 옆 패널에 실시간으로
보여준다. (Visual Studio의 XAML Designer 미리보기 대체)

**사용 시나리오(확정, 2026-10-06)**: **Visual Studio 2026이 설치되지 않은 Windows PC**에서, **VS2026과 호환되는
프로젝트**(SDK-style `net8/9/10-windows` WPF, `.sln`/`.slnx`)를 VS Code로 개발할 때 XAML 미리보기를 제공한다.
**지원 플랫폼은 Windows 전용**(사용자 확정)이다.
함의: ① 호스트는 .NET 10 WPF, ② 사용자 PC에 VS가 없어도 동작, ③ 프로젝트 자신의 사용자 정의 컨트롤을 그려야 하는
경우가 흔하므로 프로젝트 빌드 DLL 로드(Tier 1)가 1차 범위에 포함된다(§1.1, §3.3).

### 1.1 범위 (In)
- `.xaml` 열기 → 미리보기 패널, 편집 시 자동 갱신(디바운스).
- XAML 오류를 Problems 패널에 줄/열과 함께 표시.
- 확대/축소/이동, 배경(체크무늬/밝음/어두움), 디자인 크기 지정.
- 미리보기 요소 클릭 → 에디터 해당 줄로 이동, 에디터 커서 → 미리보기 요소 하이라이트.
- `ResourceDictionary` 병합, `App.xaml` 리소스, 디자인 타임 속성(`d:`, `mc:Ignorable`) 처리.
- code-behind가 필요한 부분(`x:Class`, 이벤트 핸들러)은 **제거하고** 가능한 만큼 그린다.
- **사용자 정의 컨트롤 2단계 지원** (§3.3):
  - Tier 0(항상): 해석 불가한 타입은 **자리표시자(이름이 적힌 점선 박스)** 로 대체하고 나머지는 계속 그린다.
  - Tier 1(신뢰된 워크스페이스 + 빌드 산출물 있음): 프로젝트가 빌드한 DLL을 로드해 실제 컨트롤을 그린다.
- 프로젝트 인식: XAML에서 위로 올라가며 가장 가까운 `.csproj`를 찾는다(`.sln`/`.slnx` 파싱 불필요).
- 최신 WPF 기능(.NET 9+ `ThemeMode`/Fluent 테마 등) 렌더.

### 1.2 범위 밖 (Out) — CLAUDE.md "Simplicity First"
- XAML **편집/디자이너**(드래그 앤 드롭, 속성 창), 코드 생성, IntelliSense.
- 런타임 데이터 바인딩 실행, 애니메이션 재생, 사용자 코드(code-behind) 실행.
- WinUI 3, MAUI, Avalonia, UWP XAML. macOS/Linux.
- 사용자 코드를 **빌드하는 것**은 확장이 하지 않는다(사용자가 `dotnet build`로 만든 산출물만 읽는다).

## 2. 핵심 설계 결정 (트레이드오프 명시)

**"WPF 화면을 VS Code 안에서 어떻게 그릴 것인가"**가 이 프로젝트의 유일하게 큰 결정이다.

| 방식 | 충실도 | 크로스 플랫폼 | 비용/위험 |
|---|---|---|---|
| **A. WPF 렌더 호스트(.NET 10) + PNG 전송** (채택) | 실제 WPF와 동일 | ✘ Windows 전용(확정) | 프로세스 관리, 이미지 전송. 가장 단순하고 정확 |
| B. XAML → HTML/CSS 변환 | 낮음(Grid, 템플릿, 스타일 트리거 재현이 사실상 WPF 재구현) | ✔ | 끝없는 충실도 문제 |
| C. 호스트가 WPF 창을 직접 띄우고 VS Code는 제어만 | 동일 | ✘ | VS Code 패널 안에 통합 안 됨 |

**결정: A.** Windows 전용은 사용자가 확정했다(2026-10-06). B는 "WPF 렌더링을 재구현"하는 일이라 요청 범위를 크게 넘고, 항상 어긋난다.

## 3. 구성

```
┌───────────────────────── VS Code ─────────────────────────┐
│ Extension (TypeScript, Node)                               │
│  ├ 명령/상태: 미리보기 열기, 갱신, 줄 이동                   │
│  ├ HostClient: 호스트 프로세스 spawn, JSON 요청/응답, 타임아웃│
│  ├ DiagnosticsMapper: 호스트 오류 → vscode.Diagnostic       │
│  └ Webview 패널: PNG 표시, 줌/팬, 클릭 → 요소 id 전송        │
└───────────────┬───────────────────────────────────────────┘
                │ stdin/stdout, 줄 단위 JSON(UTF-8)
┌───────────────▼───────────────────────────────────────────┐
│ XamlRenderHost.exe (.NET 10, net10.0-windows, WPF, x64)    │
│  ├ ProtocolLoop: 요청 수신/응답 송신                         │
│  ├ XamlPreprocessor: x:Class/이벤트 제거, 미지원 타입 대체,   │
│  │                  d:/mc: 처리, 줄 정보 보존                │
│  ├ ResourceResolver: 병합 사전/App.xaml 리소스 로드          │
│  ├ Renderer (STA+Dispatcher): XamlReader.Load → Measure/    │
│  │                  Arrange → RenderTargetBitmap → PNG      │
│  └ HitMap: 요소 경계 사각형 ↔ 원본 줄/열 매핑                │
└────────────────────────────────────────────────────────────┘
```

### 3.1 프로토콜 (줄 단위 JSON, M2에서 확정)
요청: `{"id":1,"method":"render","params":{"xaml":"...","filePath":"...","width":800,"height":600,"dpi":96}}`
응답: `{"id":1,"ok":true,"result":{"png":"<base64>","width":800,"height":600,"elements":[{"id":"e3","line":12,"col":5,"x":0,"y":0,"w":100,"h":30}],"warnings":[...]}}`
오류: `{"id":1,"ok":false,"error":{"code":"XamlParse","message":"...","line":12,"col":5}}`
`render` 파라미터 `allowProjectAssemblies`(기본 false): 확장이 `vscode.workspace.isTrusted`로 채운다. false면 호스트는 사용자 DLL을 로드하지 않는다. 응답 `project:{tier:0|1, reason}`.
`warnings` 항목: `{"code":"PlaceholderUsed","message":"...","line":12,"col":5}`(줄/열은 생략될 수 있음). 코드는 RemovedClassAttribute, RemovedEventHandler, RemovedCodeBlock, PlaceholderUsed, DictionaryUnavailable.
그 외 메서드: `ping`(→ `{version, protocol, pid}`), `shutdown`. `ping`의 `protocol`로 확장/호스트 불일치를 감지한다.
프로토콜 오류 코드: `InvalidRequest`(JSON/형식 오류, `id`는 null), `InvalidParams`, `UnknownMethod`. 렌더 오류 코드는 `XamlRenderException` 참고.
호스트는 `serve [--log-dir D] [--log-level L]` 모드로 실행한다. 입력 EOF 또는 `shutdown`이면 종료 코드 0.
**테스트 전용 메서드** `debug.hang`/`debug.crash`는 환경 변수 `XAMLVIEWER_TEST_HOOKS=1`일 때만 동작하고, 아니면 `UnknownMethod`다
(장애 주입 테스트용 — 사용자 환경에서는 켜지지 않는다).

stdout은 **프로토콜 전용**, 로그는 절대 stdout에 쓰지 않는다(파일/ stderr 사용).

### 3.2 신뢰성 원칙
- 호스트는 확장과 **별도 프로세스**: XAML이 크래시/무한 루프를 일으켜도 VS Code가 영향받지 않는다.
- 요청마다 타임아웃(기본 10초, 상수). 초과 시 호스트 kill 후 재시작, 사용자에게 오류 표시.
- 편집 중 요청 폭주 방지: 확장에서 디바운스(기본 300ms) + **최신 요청만 처리**(진행 중인 것은 완료 후 폐기).
- code-behind는 실행하지 않는다: `x:Class`, 이벤트 핸들러, `x:Code`는 전처리에서 제거.
  `XamlReader.Load`에 `ParserContext`로 상대 경로 기준만 제공한다.
- 사용자 어셈블리 로드(Tier 1)는 아래 §3.3의 안전 규칙을 모두 만족할 때만 한다.
- 결정성: `RenderOptions.ProcessRenderMode = SoftwareOnly`, 96 DPI 기본 → 골든 이미지 테스트 가능.

### 3.3 사용자 정의 컨트롤 로드 (Tier 1) 설계

사용자 컨트롤을 실제로 그리려면 프로젝트 DLL을 로드해 컨트롤 **생성자/정적 생성자 등 사용자 코드가 실행**된다.
따라서 다음을 필수 규칙으로 한다.

1. **Workspace Trust**: `vscode.workspace.isTrusted === true`일 때만 Tier 1을 켠다. 아니면 Tier 0(자리표시자)만 사용하고
   상태 표시줄/알림에 이유를 보여준다.
2. **산출물 탐색**: 가장 가까운 `.csproj` → `bin/<Config>/<TFM>/<AssemblyName>.dll`(+ `.deps.json`). 여러 개면 수정 시각이
   가장 최신인 것. 없으면 "먼저 `dotnet build` 하세요" 안내 + Tier 0. 확장은 빌드하지 않는다.
3. **로드 방식**: 호스트 내부 `AssemblyLoadContext`(수집 가능)에 로드, 의존성은 `AssemblyDependencyResolver`로 해석.
   프로젝트가 바뀌거나 DLL이 갱신되면 ALC를 버리고 다시 만든다(파일 잠금 방지를 위해 DLL을 **임시 폴더에 복사 후 로드**).
4. **디자인 모드 표시**: 사용자 코드가 `DesignerProperties.GetIsInDesignMode`로 분기할 수 있도록 루트에
   `IsInDesignMode=true`를 설정한다(Visual Studio 디자이너와 같은 관례).
5. **격리**: 사용자 코드가 예외/무한 루프/크래시를 일으켜도 호스트 프로세스 안에서 끝난다 → 타임아웃 → kill → 재시작.
   프로젝트가 바뀌면 호스트도 프로젝트별로 분리(1프로젝트=1호스트 프로세스, 유휴 시 종료).
6. **예외 처리**: 컨트롤 생성 중 예외는 해당 요소만 **오류 자리표시자**(예외 타입/메시지 요약)로 대체하고 나머지는 계속 그린다.
7. **로그**: 로드한 어셈블리 경로, 소요 시간, 실패 사유(H020~H023, §5).
8. **제한(알려진 한계)**: 생성자에서 DB/네트워크/파일에 접근하는 컨트롤은 그릴 수 없거나 느릴 수 있다.
   `x:Class`가 붙은 **자기 자신 파일**의 code-behind 로직(예: `InitializeComponent` 이후 코드)은 실행하지 않는다.

## 4. 스레드 모델 (CLAUDE.md 규칙 5 — Owner / Lifetime / 공유자원)

| 스레드 | 소유자 | 수명 | 공유자원과 동기화 |
|---|---|---|---|
| Main(STA) + Dispatcher: 모든 WPF 객체 생성/렌더 | `Program.Main` | 프로세스 전체 | WPF 객체는 이 스레드에서만 접근. 다른 스레드는 `Dispatcher.InvokeAsync`로만 요청 |
| StdinReader: 줄 읽기/파싱 | `ProtocolLoop` | 프로세스 전체(종료 요청/EOF 시 종료) | `BlockingCollection<Request>`(한 방향 큐)로 Main에 전달 |
| StdoutWriter: 응답 직렬화/쓰기 | `ProtocolLoop` | 프로세스 전체 | `BlockingCollection<string>`(bounded)로 수신, stdout 핸들은 이 스레드만 사용 |
| LogWriter: 파일 로그 | `HostLogger` | 프로세스 전체 | 논블로킹 bounded 큐(기본 1024줄). 가득 차면 **새 로그를 버리고 개수를 세어 H090으로 한 줄 남김**(렌더 경로가 파일 I/O를 기다리지 않음). 구현 단순화를 위해 초안의 "오래된 Debug부터 버림"에서 변경 |

확장 쪽(Node)은 단일 스레드 이벤트 루프이므로 `HostClient`가 요청 id → Promise 맵과 타임아웃 타이머의
**소유자**이며, 패널 dispose 또는 확장 deactivate 시 호스트 `shutdown` → 3초 후 kill 한다.

## 5. 로그 설계 (CLAUDE.md 규칙 8 — 개발 초기에 정의, 흐름에서 검증)

**위치/방식**: 확장은 `OutputChannel`(UI 비용 낮음) + 필요 시 `globalStorage/logs/extension.log`.
호스트는 `globalStorage/logs/host.log`(경로를 `--log-dir` 인자로 받음). 회전: 파일당 1MB × 5개.
기본 수준 Info. **XAML 본문은 로그에 남기지 않는다**(길이와 해시만) — 사용자 소스 보호.

| ID | 수준 | 기록 시점 | 필드 |
|---|---|---|---|
| H001 | Info | 호스트 시작 | 버전, protocol, pid, CLR, 로그 수준 |
| H002 | Info | 호스트 종료 | 사유(shutdown/EOF/crash), 처리한 요청 수 |
| H010 | Debug | 요청 수신 | id, method, 본문 길이 |
| H011 | Info | 렌더 성공 | id, 소요 ms, 폭×높이, 요소 수, 경고 수 |
| H012 | Warn | 렌더 실패 | id, 오류 코드, 줄/열 |
| H013 | Warn | 자리표시자 대체 | id, 타입 이름(개수 제한) |
| H020 | Info | 사용자 어셈블리 로드 | 프로젝트, DLL 경로, 소요 ms |
| H021 | Warn | 사용자 어셈블리 로드 실패/없음 | 사유(없음/의존성/버전) |
| H022 | Warn | 사용자 컨트롤 생성 예외 | 타입 이름, 예외 타입(메시지는 길이 제한) |
| H023 | Info | Tier 결정 | Tier 0/1, 사유(미신뢰/산출물 없음/정상) |
| E001 | Info | 확장 활성/비활성 | 버전 |
| E010 | Info | 호스트 spawn/재시작 | 경로, 사유, 재시작 횟수 |
| E011 | Warn | 요청 타임아웃 | id, 경과 ms |
| E012 | Error | 호스트 비정상 종료 | exit code, 마지막 요청 id |
| E013 | Debug | 요청 폐기(최신만 처리) | 폐기 id |
| E014 | Warn/Debug | 호스트 출력 줄 무시(JSON 아님 / 짝 없는 id) | 길이 또는 id |

검증: 통합 테스트(T5)가 정상/오류/타임아웃/크래시 시나리오에서 위 로그 ID가 **기대 순서로** 남는지 확인한다.

## 6. 코드 규칙 적용 요약 (CLAUDE.md §5)
- 호스트(C# 13/.NET 10): `_camelCase` private 필드, `if`는 항상 중괄호 + else 고려, Magic Number는 `const`
  (타임아웃, 디바운스, 최대 크기, DPI 등), 스레드/매니저 클래스에 Owner·Lifetime 주석.
- 확장(TS): 같은 정신으로 `private _field`, 상수는 `constants.ts`에 모은다.
- 함수는 단일 책임. 전처리는 "제거 규칙"마다 작은 함수로 나눠 각각 단위 테스트한다.

## 7. 확인이 필요한 가정 (사용자 결정 요청)

확정된 사항: **Windows 전용**, **VS2026 없는 PC에서 VS2026 호환 프로젝트 개발용**(2026-10-06).

1. **호스트 배포 방식** — 기본은 프레임워크 종속(사용자 PC에 .NET 10 Desktop Runtime 필요, 보통 .NET SDK 10에 포함,
   없으면 설치 안내). 대안은 self-contained 번들(런타임 설치 불필요, `.vsix`가 수십~100MB+로 커짐).
   **제안: 프레임워크 종속** — 사용자는 어차피 .NET SDK로 프로젝트를 빌드하므로 런타임이 있다. 다른 선택이 필요하면 알려 주세요.
2. **Tier 1(프로젝트 DLL 로드)이 1차 범위**로 올라왔다. 사용자 코드 실행이 수반되므로 Workspace Trust 요구가 §3.3의
   전제인데, 이 정책(미신뢰 폴더에서는 자리표시자만)이 괜찮은지 확인이 필요하다.
3. **확장 이름/게시자 ID** — 임시로 `wpf-xaml-viewer`. Marketplace 게시 계획이 있으면 publisher 필요.
4. 호스트 **x64 고정** 가정. 32비트 전용 사용자 컨트롤은 지원하지 않는다. (ARM64 Windows는 필요 시 후속 검토.)
5. **지원 TFM 하한**: net8.0-windows 이상을 공식 지원, net48/레거시는 "그려지면 다행" 수준(M4에서 실측 후 확정).
