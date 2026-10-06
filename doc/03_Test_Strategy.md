# 03. 자동 테스트 전략

> 상위 문서: [02_Development_Plan.md](./02_Development_Plan.md)
> 참고: CodeAtlas_v0의 `10_UiAutomation_Test_Design.md`, `22_Accuracy_And_CI.md`
> (골든/기준선, 하네스 분리, 결함 주입, 보고서 방식을 그대로 가져온다.)

## 1. 테스트 계층

| 계층 | 대상 | 도구 | 실행 위치 | 속도 |
|---|---|---|---|---|
| **T1 호스트 단위** | 전처리, 크기 규칙, 오류 매핑, 프로토콜 파서, HitMap, 로거 | MSTest(`net10.0-windows`) + `dotnet test` | `host/XamlRenderHost.Tests` | 빠름(초) |
| **T2 호스트 렌더 골든** | XAML → PNG 픽셀 비교 | MSTest + 자체 `ImageComparer` | 같은 프로젝트 | 보통(수십 초) |
| **T3 확장 단위** | `HostClient`(가짜 호스트), 진단 변환, 디바운스/최신만 처리, 메시지 프로토콜 | mocha(+ts-node 또는 컴파일 후 실행), VS Code 불필요 | `extension/test/unit` | 빠름 |
| **T3R 확장 + 실제 호스트** | 실제 `XamlRenderHost.exe` + 실제 `HostClient`: 장애 주입(무응답/크래시/거대 XAML), 오류 전달, 로그 순서 | mocha(`npm run test:realhost`), VS Code 불필요 | `extension/test/realhost` | 보통(수 초) |
| **T4 확장 통합** | 명령 등록, Problems 패널, 문서 편집 → 갱신, 호스트 재시작, 로그 순서 | `@vscode/test-electron`(실제 VS Code, 실제 호스트 exe) | `extension/test/integration` | 느림(분 단위) |
| **T5 웹뷰 E2E** | 웹뷰가 실제로 이미지를 그렸는지, 클릭 → 줄 이동 | T4 위에서 웹뷰 ↔ 확장 메시지 사용 | 같은 위치 | 느림 |

**피라미드 원칙**: 로직은 T1/T3에서 가장 많이, T4/T5는 "연결이 됐는가"만 검증한다.

### 1.1 WebView 화면 검증 방식 (메모리의 CodeAtlas 경험 반영)
OS 화면 캡처는 신뢰할 수 없으므로(가려짐, 해상도, 세션) **스크린샷에 의존하지 않는다.**
웹뷰 스크립트가 `<img>`의 `naturalWidth/Height`, 현재 줌, 배경 모드, 선택된 요소 id를 `postMessage`로
확장에 되돌려 주고, 테스트는 그 값을 단언한다. 픽셀 수준 정확도는 T2(호스트 골든)가 담당한다.

## 2. T2 골든 이미지 규칙 (CodeAtlas의 baseline 방식 차용)

- 위치: `host/XamlRenderHost.Tests/Fixtures/xaml/*.xaml`, 기대 이미지 `.../Fixtures/golden/*.png`.
- 결정성 확보 조건(01 문서 3.2절): SoftwareOnly 렌더, 96 DPI, 고정 폰트, `TextOptions` 고정.
- 비교: 채널별 허용오차(기본 ±2), 허용 불일치 픽셀 비율(기본 0.1%) — 둘 다 이름 있는 상수.
- 실패 시 `TestResults/render-diff/<이름>-{expected,actual,diff}.png` 저장.
- 갱신: `XAMLVIEWER_UPDATE_GOLDEN=1`로 실행하면 현재 결과로 덮어쓴다. **리뷰에서 PNG diff를 눈으로 확인**한 뒤
  커밋한다(CodeAtlas `CODEATLAS_UPDATE_BASELINE`과 같은 규율).
- 골든이 다른 머신에서 흔들리면 비교 기준을 완화하기 전에 **원인(폰트/DPI/렌더 모드)을 먼저 확정**한다.
- 보고서: 테스트 후 `TestResults/render-report.md`(픽스처별 PASS/FAIL, 불일치 비율, 렌더 ms).

## 3. 테스트 케이스 목록 (초안 — 마일스톤 진행하며 `doc/Test_Case_List.md`로 확장)

### 3.1 T1/T2 호스트
| ID | 마일스톤 | 내용 |
|---|---|---|
| H-R01~R10 | M1 | 기본 렌더 골든: Button, TextBlock(폰트/크기), Grid(행/열/Span), StackPanel, DockPanel, Canvas, Border(둥근 모서리), Image(내장 리소스), 스타일/Setter, ControlTemplate |
| H-S01 | M1 | 크기 규칙 4종 우선순위 |
| H-D01 | M1 | 같은 XAML 5회 렌더 결과 바이트 동일 |
| H-E01~E08 | M1 | 오류: 닫히지 않은 태그, 알 수 없는 요소, 알 수 없는 속성, 잘못된 값, 네임스페이스 누락, 빈 문자열, 비-XAML 텍스트, 루트 두 개 → 오류 코드/줄/열 |
| H-P01~P05 | M2 | 프로토콜: ping, render, 잘못된 JSON, 알 수 없는 method, shutdown 후 종료 코드 0 |
| H-L01~L04 | M2 | 로그: H001/H011/H012 순서, XAML 본문 미기록, 큐 포화 시 렌더 지연 없음, 회전 |
| H-X01~X06 | M4 | 전처리: x:Class, 이벤트 핸들러, x:Code, d:/mc:, 중첩 mc:AlternateContent, 제거 목록 warnings |
| H-RD01~RD04 | M4 | 병합 사전 상대 경로, 경로 없음(경고+계속), 순환 병합(오류 아님·차단), 네트워크 URI 차단 |
| H-U01~U03 | M4 | Tier 0 사용자 타입 자리표시자: 단일, 중첩, 다수(로그 개수 제한) |
| H-T01~T08 | M4B | Tier 1: 프로젝트/산출물 탐색(다중 구성·TFM·없음), ALC 로드 골든, DLL 교체 재로드·잠금 없음, 생성자 예외 → 오류 자리표시자, IsInDesignMode 분기, 의존 DLL 해석, 미신뢰 → Tier 0, ThemeMode(Fluent) 골든 |
| H-W01~W04 | M4 | 루트 종류: Window/UserControl/Page/Grid |
| H-M01~M05 | M5 | HitMap: 단순/중첩/템플릿 생성 요소 귀속/Grid 겹침(z-order)/좌표 DPI |

### 3.2 T3 확장 단위
| ID | 내용 |
|---|---|
| X-C01~C05 | HostClient: 정상 왕복, 타임아웃 → kill+재시작, 호스트 즉사 → 오류 전달, 응답 순서 뒤섞임 → id로 매칭, 알 수 없는 id 응답 무시 |
| X-D01~D03 | 디바운스: 연속 편집 → 1회 요청, 진행 중 요청 중 새 편집 → 이전 결과 폐기(E013), 비-xaml 문서 무시 |
| X-G01~G03 | 진단 변환: 줄/열 0-base 변환, 줄 없음 오류 → 문서 첫 줄, 경고 심각도 |
| X-M01~M02 | 웹뷰 메시지 스키마 검증(알 수 없는 메시지 무시) |
| X-P01 | package.json 명령/설정 키 ↔ User_Guide 일치 린트 |

### 3.3 T4/T5 확장 통합 (실제 VS Code + 실제 호스트)
| ID | 내용 |
|---|---|
| I-01 | 확장 활성화, 명령 3개 등록 확인 |
| I-02 | `.xaml` 열고 `Open Preview` → 패널 생성 + 웹뷰가 이미지 크기 회신(T5) |
| I-03 | 문서 편집(정상→오류→정상) → Problems 진단 생성/해제, 이미지 갱신 |
| I-04 | 호스트 프로세스 강제 종료 → 다음 편집에서 자동 복구(E010/E012 로그) |
| I-05 | 호스트 exe 없음 → 안내 알림, 확장은 계속 살아 있음 |
| I-06 | 미리보기 클릭 메시지 → 에디터 selection 이동(T5) |
| I-07 | 로그: 시나리오 후 확장/호스트 로그에 기대 ID가 순서대로 존재, XAML 본문 없음 |
| I-08 | 큰 XAML(요소 5,000개) → 타임아웃 이내 또는 타임아웃 오류 후 복구 |
| I-09 | `.vsix` 설치 스모크(M6) |
| I-10 | Workspace Trust 신뢰/미신뢰에서 Tier 결정(H023)과 알림 (M4B) |
| I-11 | 결함 주입: 사용자 컨트롤 무한 루프/크래시 → 타임아웃·재시작·VS Code 무영향 (M4B) |
| I-12 | .NET 10 Desktop Runtime 없음(가짜 `dotnet` 경로로 시뮬레이션) → 설치 안내 알림 |

## 4. 실행 방법 (M0에서 스크립트 작성)

```
PS> .\doc\run_tests.ps1               # T1+T2 (`dotnet test`, VS 불필요) — CodeAtlas run_tests.ps1의 프로세스 정리/보고서 구조만 이식
PS> .\doc\run_tests.ps1 -SkipBuild    # 빌드 생략
PS> cd extension; npm test            # T3 (단위)
PS> cd extension; npm run test:integration   # T4/T5
PS> .\tools\ci\ci.ps1                 # 전부 + 보고서
```

- `run_tests.ps1`의 호스트 프로세스 정리 단계: 남은 `XamlRenderHost.exe`를 종료(파일 잠금으로 빌드 실패 방지).
- 통합 테스트는 VS Code 인스턴스를 띄우므로 **임시 `--user-data-dir`/`--extensions-dir`** 를 사용해 개발자의 실제 환경을 건드리지 않는다.
- 한글 출력: 스크립트는 UTF-8 BOM, 보고서는 UTF-8.

## 4.1 장애 주입 훅 (M2)
호스트의 `debug.hang`(응답 없음) / `debug.crash`(종료 코드 99)는 환경 변수 `XAMLVIEWER_TEST_HOOKS=1`일 때만 켜진다.
꺼져 있으면 `UnknownMethod`이며 이 동작도 테스트로 고정했다. 확장이 사용자 환경에서 이 변수를 설정하는 코드는 없어야 한다.
`npm run test:realhost`는 호스트 exe가 없으면 **건너뛰지 않고 실패**한다(조용한 건너뜀은 복구 검증 누락을 가린다).

## 5. 결함 주입과 회귀 규율
- 버그 보고 → **먼저 재현 테스트** → 수정 → 통과(CLAUDE.md §4).
- 발견한 함정은 이 문서 하단 "실측 함정" 절에 날짜와 함께 기록한다(CodeAtlas 10 문서 3절 방식).
- 테스트를 지우거나 허용오차를 느슨하게 하는 변경은 사유를 문서에 남긴다.

## 6. CI
- `tools/ci/ci.ps1`이 로컬/CI 공용 진입점(CodeAtlas `tools/ci/ci.ps1` 방식).
- 호스트 빌드는 .NET SDK 10만 필요(VS 불필요) → GitHub `windows-latest` 같은 호스트 러너로도 가능(WPF 렌더 테스트는 Windows 필요).
  CI 서비스는 미정이므로 워크플로 작성은 M6 이후 사용자 결정 사항.
- T4/T5는 데스크톱 세션/디스플레이가 필요할 수 있어 기본 CI에서는 T1~T3만, 통합은 야간/수동 실행을 기본으로 한다
  (실제 필요 여부는 M3에서 실측 후 기록).

## 7. 실측 함정 (진행하며 추가)
_아직 없음. 마일스톤 진행 중 발견하는 대로 날짜와 함께 기록한다._
