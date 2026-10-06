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
│   └─ Fixtures/                ← 테스트용 .xaml, golden/ PNG
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

### M1. 렌더 호스트 핵심 (CLI 모드)
| # | 단계 | 검증 |
|---|---|---|
| 1.1 | `render --in a.xaml --out a.png` (STA, XamlReader.Load → RenderTargetBitmap → PNG) | Button/Grid/StackPanel 등 기본 픽스처 10종의 골든 PNG 일치(T2) |
| 1.2 | 크기 결정 규칙: `Width/Height` 명시 → `d:DesignWidth/Height` → 요청 크기 → 콘텐츠 크기 | 규칙별 단위 테스트(T1) + 골든 |
| 1.3 | 결정성 확보(SoftwareOnly, 96 DPI, 폰트 고정) | 같은 XAML 5회 렌더 → 바이트 동일 |
| 1.4 | XAML 오류 → 코드/줄/열을 가진 구조화 오류 | 잘못된 픽스처 8종의 오류 코드/줄 일치(T1) |

### M2. 프로토콜 + 프로세스 관리
| # | 단계 | 검증 |
|---|---|---|
| 2.1 | stdin/stdout 줄 JSON 루프(`ping`/`render`/`shutdown`), STA 큐 구조(§01 4절) | 서브프로세스로 띄워 요청/응답 왕복(T1, 실제 exe) |
| 2.2 | 로그 H001~H013 구현(비동기 큐 Writer) | 시나리오별 로그 ID 순서 검증, 큐 포화 시 렌더가 막히지 않음 |
| 2.3 | 확장 `HostClient`: spawn, id 매핑, 타임아웃, 재시작, 최신 요청만 처리 | TS 단위 테스트(가짜 호스트 스크립트)로 타임아웃/크래시/폐기 검증(T3) |
| 2.4 | 결함 주입: 무한 루프성 XAML, 거대 XAML, 호스트 kill | 타임아웃 후 재시작, 이후 요청 정상(T4) |

### M3. 확장 MVP (이 시점에 "쓸 수 있다")
| # | 단계 | 검증 |
|---|---|---|
| 3.1 | 명령 `WPF XAML: Open Preview`, 에디터 옆 Webview 패널, PNG 표시 | 통합 테스트: 명령 실행 → 패널 생성, 웹뷰가 이미지 크기를 메시지로 회신(T5) |
| 3.2 | 편집 시 자동 갱신(디바운스) + 상태 표시줄(렌더 중/오류) | 문서 편집 → 갱신 횟수/마지막 결과 확인(T4/T5) |
| 3.3 | 오류 → Problems 패널(줄/열) | `languages.getDiagnostics` 결과가 기대와 일치(T4) |
| 3.4 | 호스트 미존재/오류 시 안내 메시지 | 호스트 경로 제거 상태에서 오류 알림 + 로그 E010/E012(T4) |

### M4. XAML 해석 충실도
| # | 단계 | 검증 |
|---|---|---|
| 4.1 | `x:Class`/이벤트 핸들러/`x:Code` 제거 | 해당 픽스처가 오류 없이 렌더 + 제거 대상 목록이 warnings에(T1/T2) |
| 4.2 | `d:`/`mc:Ignorable`, `d:DataContext` 처리 | 디자인 타임 전용 속성이 있는 픽스처 골든 |
| 4.3 | `ResourceDictionary` 병합(상대 경로), `App.xaml` 리소스 자동 탐색 | 병합 사전 픽스처 골든, 경로 오류 시 경고 + 계속 렌더 |
| 4.4 | 해석 불가 사용자 타입 → 자리표시자(Tier 0, H013) | `local:MyControl` 포함 픽스처: 나머지 요소 정상 + 자리표시자 박스 골든 |
| 4.5 | `Window` 루트는 콘텐츠를 `Border`로 호스팅해 렌더(창 크롬 제외) | Window/UserControl/Page/Grid 루트별 골든 |

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
