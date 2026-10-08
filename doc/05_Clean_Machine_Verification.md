# 05. 깨끗한 PC(Visual Studio 없음) 검증 체크리스트

> doc/02 M6.4. **상태: 미실행.** 개발 PC(Windows 10 Pro 19045)에는 Visual Studio 2017/2019/2022가 설치되어 있고
> Windows Sandbox 기능이 꺼져 있어(켜려면 관리자 권한 + 재부팅) 이 검증을 **수행하지 못했다.** 아래는 사람이 따라 하면 되는 절차이고,
> 결과를 이 문서 맨 아래 "실행 기록"에 남긴다. 그 전까지 "VS 없는 PC에서 동작한다"고 주장하지 않는다.

## 이미 자동으로 확인된 것 (개발 PC)
`tools\ci\ci.ps1 -IncludePackage` 의 설치 스모크(I-09)가 다음을 확인한다. 단, **이 PC에는 .NET SDK 10과 VS가 있다**는 점이 깨끗한 PC와 다르다.
- `.vsix`를 **임시 VS Code 프로필**(빈 user-data/extensions 폴더)에 설치하면 확장이 활성화된다.
- 확장이 개발 경로가 아니라 **설치 폴더의 번들 호스트(`bin\host\XamlRenderHost.exe`)** 를 쓴다.
- `dotnet new wpf` 로 만든 실제 net10 프로젝트(App.xaml 리소스, `x:Class`/이벤트, 사용자 컨트롤 포함)가 미리보기된다(800x450, Tier 1).
- .NET 런타임이 없는 환경(`DOTNET_ROOT`를 빈 폴더로)에서 호스트가 죽고 확장이 이를 "런타임 없음"으로 식별한다(I-12, 실제 호스트).

## 깨끗한 PC에서 사람이 확인할 것
준비물: Windows 10/11 x64, **Visual Studio 미설치**, **.NET 미설치**(또는 Windows Sandbox / 새 VM), 인터넷, 이 저장소의 `artifacts\wpf-xaml-live-preview-*.vsix`.

자동화 시도: `tools\verify\clean_machine.wsb` (Windows Sandbox) — `sandbox_bootstrap.ps1`이 .NET SDK 10/VS Code 설치, `.vsix` 설치, 샘플 프로젝트 생성/빌드까지 한다. 이 스크립트도 **실행해 본 적이 없다.**

수동 체크리스트(각 항목에 ✔/✘와 메모를 기록):

| # | 확인 | 기대 결과 |
|---|---|---|
| 1 | VS Code만 설치(.NET 없음) → `.vsix` 설치 → `.xaml` 열고 **WPF XAML: Open Preview** | **".NET 10 Desktop Runtime이 설치되어 있지 않아…" 알림**과 `.NET 설치 페이지 열기` 버튼. 확장/VS Code는 계속 동작 |
| 2 | .NET SDK 10 설치(Desktop Runtime 포함) 후 VS Code 재시작 → 같은 파일에서 다시 Open Preview | 미리보기가 그려진다 |
| 3 | `dotnet new wpf -n SampleApp --framework net10.0` → `MainWindow.xaml` 미리보기 | 800x450 창 영역이 그려진다(기본 템플릿은 빈 Grid) |
| 4 | `MainWindow.xaml`에 `<Button Content="Hi" Click="X" Width="100" Height="30"/>` 추가 | 버튼이 그려지고 상태 표시줄에 `경고 1`(이벤트 제거) |
| 5 | 일부러 `<Button NoSuchProp="1"/>` 입력 | Problems에 오류가 **해당 줄**에 표시, 미리보기는 마지막 정상 이미지 유지 |
| 6 | 사용자 컨트롤 클래스 추가 + `xmlns:local` 사용, **폴더 미신뢰** 상태 | 컨트롤이 붉은 자리표시자, 경고에 "신뢰되지 않은 폴더" |
| 7 | 폴더 신뢰 → `dotnet build` | 컨트롤이 실제로 그려진다(자동 재렌더) |
| 8 | 미리보기에서 요소 클릭 / 에디터에서 커서 이동 | 해당 줄로 이동 / 요소 강조 |
| 9 | 미리보기 줌(+/−/맞춤/100%, Ctrl+휠), 드래그 이동, 배경 전환, W/H 크기 지정 | 모두 동작 (**M5 웹뷰 마우스 동작은 자동 검증되지 않았다 — 여기서 처음 확인**) |
| 10 | VS Code를 닫고 작업 관리자에서 `XamlRenderHost.exe`가 남아 있지 않은지 확인 | 남아 있지 않다 |

## 실행 기록
| 날짜 | 환경(OS/VM/Sandbox) | 수행자 | 결과 요약 | 발견한 문제 |
|---|---|---|---|---|
| (미실행) | | | | |
