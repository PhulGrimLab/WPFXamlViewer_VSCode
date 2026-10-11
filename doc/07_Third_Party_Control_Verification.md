# 07. 유명 WPF 컨트롤 라이브러리로 검증하기

> 목적: 실제로 많이 쓰이는 오픈소스 WPF 컨트롤 라이브러리의 **데모 앱 XAML**을 확장의 호스트로 일괄 렌더해, 커스텀 컨트롤이 얼마나 그려지는지/어디서 막히는지 측정한다.
> 상태: **2차 측정 완료(2026-10-12)** — 결과는 4절. 자동 회귀 테스트가 아니라 **수동으로 실행하는 검증 절차**이며 CI에는 넣지 않았다(외부 저장소를 내려받고 수 분 빌드해야 한다).

## 1. 참고한 저장소
별 수는 2026-10-11에 GitHub API로 조회한 값이다. 샘플은 저장소 밖 `C:\Works_PhulGrimLab\WpfControlSamples\<이름>` 에 `git clone --depth 1`로 받았다(커밋 해시는 그때의 `HEAD`).

| 저장소 | 별 | 커밋 | 성격 | 렌더 대상 데모 프로젝트 | 대상 TFM(빌드한 것) |
|---|---|---|---|---|---|
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 약 16.3k | `112d83b` | Material Design 컨트롤/테마 | `src\MainDemo.Wpf` | net8.0-windows |
| [MahApps.Metro](https://github.com/MahApps/MahApps.Metro) | 약 9.8k | `89a7558` | Metro 스타일 창/컨트롤(`MetroWindow` 등) | `src\MahApps.Metro.Samples\MahApps.Metro.Demo` | net8.0-windows |
| [WPF UI (lepoco/wpfui)](https://github.com/lepoco/wpfui) | 약 9.7k | `ffebacd` | Fluent 스타일 컨트롤 | `src\Wpf.Ui.Gallery` | net10.0-windows10.0.26100.0 |
| [HandyControl](https://github.com/HandyOrg/HandyControl) | 약 7.2k | `2c0875e` | 컨트롤 모음 | `src\Net_GE45\HandyControlDemo_Net_GE45` (XAML은 `src\Shared\HandyControlDemo_Shared`) | net8.0-windows |

선정 기준: WPF 컨트롤 라이브러리 중 별 수 상위. 이 목록이 "활용이 가장 많은 것"의 엄밀한 순위는 아니다(별 수는 사용량과 다르다). 후보였던 ModernWpf(약 5k), Xceed WPF Toolkit(약 4.2k), Fluent.Ribbon(약 2.8k)은 받지 않았다.

## 2. 준비물
- Windows, .NET SDK 10(`dotnet --list-sdks`), 이 저장소의 호스트 또는 설치된 확장(`code --install-extension artifacts\wpf-xaml-live-preview-*.vsix`).
- 인터넷(저장소와 NuGet 패키지를 받는다).
- **주의: 내려받은 저장소를 빌드하면 그 저장소의 빌드 스크립트가 실행된다.** 신뢰하는 저장소만 쓰고, 이 저장소(`WPFXamlViewer_VSCode`) 폴더 안에 두지 않는다.

## 3. 절차
### 3-1. 내려받기
```powershell
$root = 'C:\Works_PhulGrimLab\WpfControlSamples'; New-Item -ItemType Directory -Force $root | Out-Null
git clone --depth 1 https://github.com/MahApps/MahApps.Metro.git $root\MahApps.Metro   # 나머지도 같은 방식
```

### 3-2. 빌드
데모 앱을 **해당 프로젝트 폴더에서** 빌드한다. 확장은 빌드하지 않고 `bin` 아래 산출물을 찾기 때문이다.
```powershell
cd $root\MahApps.Metro\src\MahApps.Metro.Samples\MahApps.Metro.Demo
dotnet build -f net8.0-windows -c Debug     # 다중 TFM 프로젝트는 -f 로 하나를 고른다(호스트가 net10이라 net8 산출물도 로드된다)
```
- 소요: 저장소마다 약 2분(첫 빌드, NuGet 복원 포함). 병렬로 돌려도 된다.
- **MahApps.Metro**는 `global.json`이 SDK 9를 요구한다(`rollForward: feature`). 설치된 SDK 10으로 빌드하려고 **받아 둔 로컬 복사본의** `global.json`에서 `rollForward`를 `latestMajor`로 바꿨다. 이 변경은 샘플 폴더에만 있고 이 저장소에는 없다.
- MaterialDesignInXamlToolkit은 `global.json`(10.0.301, `latestMinor`)이 설치된 SDK 10.0.401과 맞았다.

### 3-3. 일괄 렌더
`tools\verify\render_batch.ps1`이 프로젝트 폴더의 모든 `.xaml`(`obj`/`bin` 제외)을 호스트에 `render` 요청하고 파일별 결과를 CSV로 남긴다(호스트가 죽거나 시간 초과면 다시 띄운다).
```powershell
$exe = "$env:USERPROFILE\.vscode\extensions\phulgrimlab.wpf-xaml-live-preview-0.1.0\bin\host\XamlRenderHost.exe"   # 또는 host\...\bin\Release\...\XamlRenderHost.exe
.\tools\verify\render_batch.ps1 -Exe $exe -ProjectDir $root\MahApps.Metro\src\MahApps.Metro.Samples\MahApps.Metro.Demo -OutCsv .\res-mahapps.csv -FreshHost
# 옵션: -FreshHost(파일마다 호스트 새로 시작), -Untrusted(신뢰하지 않은 폴더=Tier 0), -Max 400(최대 파일 수), -TimeoutMs 60000(파일당)
```
- **측정에는 `-FreshHost`를 쓴다.** 한 호스트에 연속으로 보내면 앞선 요청이 남긴 상태가 뒤 결과를 바꾼다. 실제로 1차 측정에서 알파벳 순으로 가장 먼저 렌더되는 `App.xaml`이 `Application` 객체를 부수 효과로 만들어, 뒤 파일들이 실제(단독 실행)보다 잘 그려지는 것처럼 보였다. `-FreshHost`는 느리지만(프로젝트당 4~6분) 편집기에서 파일을 하나 열었을 때와 같은 조건이다.
- CSV 열: `File, Status(OK/ERROR/TIMEOUT/HOSTDEAD), Tier, Ms, Warnings, WarnCodes, Detail(오류 코드: 메시지)`.

### 3-4. 결과 분석
```powershell
$r = Import-Csv .\res-mahapps.csv
$r | Group Status | % { "$($_.Name): $($_.Count)" }
$r | ? Status -ne OK | Group { $_.Detail -replace "'[^']*'", "'…'" -replace '\d+','N' } | Sort Count -Desc | Select -First 10
```
**눈으로 확인할 것**: 이 스크립트는 "오류 없이 그려졌는가"만 센다. 그림이 맞는지는 확장에서 해당 파일을 열어 보거나 호스트로 PNG를 받아 본다(결과 JSON의 `result.png`가 base64 PNG).

## 4. 결과
조건: 호스트(수정 반영본), 신뢰 상태(Tier 1), 각 데모 프로젝트 폴더의 모든 `.xaml`, **파일마다 새 호스트**(`-FreshHost`).

| 저장소 | 파일 수 | 오류 없이 렌더 | 처음(수정 전) |
|---|---|---|---|
| MahApps.Metro | 40 | 32 (80%) | 12 |
| MaterialDesignInXamlToolkit | 54 | 48 (89%) | 3 |
| WPF UI (Gallery) | 86 | 75 (87%) | 21 |
| HandyControl | 149 | 측정 안 함 | 0 |
| **합계(HandyControl 제외)** | **180** | **155 (86%)** | 36 (20%) |

- HandyControl은 XAML이 `Shared` 프로젝트에 있어 **가장 가까운 `.csproj`(공유 프로젝트)에 빌드 산출물이 없다.** 데모 앱 폴더에는 XAML이 0개이고, 공유 폴더의 파일은 Tier 1이 되지 않아 이번 재측정에서 제외했다(5절 3번).
- "오류 없이 그려졌는가"만 센 값이다. 그림이 맞는지는 일부만(아래) 눈으로 확인했다.
- 파일마다 새 호스트로 렌더한 시간은 중앙값 약 3.6~5.6초다(프로젝트와 라이브러리 어셈블리를 매번 처음 불러오는 비용 포함). 같은 호스트를 계속 쓰면 이후 렌더는 0.1~0.3초다.

### 이번에 고친 것 (측정으로 찾음)
1. **라이브러리의 컴파일된(BAML) 리소스 사전**(`pack://application:,,,/라이브러리;component/Themes/X.xaml`): 디스크에 `.xaml`이 없으면 인라인하지 않고 그대로 두어 WPF가 로드된 어셈블리에서 읽게 했다. 이전 실패의 가장 큰 원인(63건)이었다.
2. **`Application` 인스턴스**: 라이브러리(예: MaterialDesign의 `MaterialDesignColors.StaticResourceExtension`)가 `Application.Current.Resources`에서 키를 찾는다. Tier 1에서 호스트가 `Application`을 만들고 `App.xaml` 리소스를 얹는다(`HostApplication`).
3. **프로젝트 어셈블리 로드 컨텍스트를 수집 불가(non-collectible)로**: WPF가 BAML 로드 때 만드는 동적 코드가 수집 가능한 어셈블리를 참조하지 못한다("A non-collectible assembly may not reference a collectible assembly"). 대가는 아래 "알려진 부작용".
4. **URI형 xmlns와 XAML에서만 쓰는 어셈블리**: `http://…` xmlns(MahApps `MetroWindow`, WPF UI `CodeBlock` 등)를 쓰면 의존 어셈블리를 모두 로드한다. 컴파일된 주 어셈블리의 참조 목록에 없는 어셈블리(XAML에서만 쓰는 것)도 있어, 출력 폴더의 관리 DLL을 함께 로드한다.
5. **`pack://application:,,,/Assets/x.png`**(속성 값과 `{ui:ImageIcon 'pack://…'}` 같은 마크업 확장 인자 안): 호스트가 앱이 아니라 읽지 못해, Tier 1에서는 프로젝트 어셈블리의 `;component/` 경로로 바꾼다.
6. **같은 개수의 인자를 받는 생성자가 여러 개인 마크업 확장**(WPF UI의 `{ui:SymbolIcon …}` 등): 스키마 컨텍스트에서 허용했다(`SupportMarkupExtensionsWithDuplicateArity`).
7. **신뢰하지 않은 문서(Tier 0)가 앞서 로드된 사용자 타입을 쓰는 문제**: 어셈블리를 수집 불가로 바꾸면서, 같은 프로세스에서 앞서 신뢰된 프로젝트를 로드했을 때 그 타입이 신뢰하지 않은 문서에서도 해석될 수 있게 되어(테스트 `T02`가 실패) 막았다. Tier 0에서는 사용자 컨텍스트에 로드된 타입도 해석 불가(자리표시자)로 취급한다.

**눈으로 확인한 그림**(결과 PNG를 직접 열어 봄, 파일 3개뿐이다. 나머지는 "오류 없음"만 확인했다):
- MaterialDesign `Buttons.xaml`: Material 스타일(색 버튼, 원형/분할 버튼, 배지, 칩)이 정상으로 그려진다.
- MahApps `ExampleViews\ButtonsExample.xaml`: Metro 스타일 버튼/토글 스위치/체크박스/라디오 버튼이 그려진다.
- WPF UI `Views\Pages\BasicInput\ButtonPage.xaml`: 표준 버튼과 일부 버튼은 맞지만 **Accent 버튼, FontIcon 버튼, ImageIcon 버튼, Primary 버튼이 비어 보인다**(오류는 없다). 강조색(accent) 배경이 적용되지 않은 것으로 추정되나 **원인은 확인하지 못했다.** 즉 "오류 없이 렌더됨"이 "그림이 맞음"은 아니다. 86개 중 얼마나 이런 부분적 깨짐이 있는지는 측정하지 않았다.

### 남은 실패 25건의 원인 (3개 합산)
| 건수 | 원인 | 비고 |
|---|---|---|
| 5 | `RenderFailed: Object reference not set to an instance of an object` | 원인 미조사. 라이브러리 컨트롤이 렌더 중 `null`을 참조 |
| 4 | 루트가 `ResourceDictionary` | 스타일/리소스 파일이라 미리보기 대상이 아님(정상) |
| 4 | 이미지/아이콘 리소스 | `Icon="….ico"`처럼 문서 기준 **상대 경로**로 프로젝트 리소스를 가리키는 경우(미해결) |
| 3 | 알 수 없는 형식 | 개별 사례(예: 프로젝트 자신의 변환기 `ComboBoxItemToDataTemplateConverter`) |
| 2 | 루트가 `Application` | `App.xaml` 자체(미리보기 대상이 아님) |
| 그 외 | `MetroWindow`의 `Resources` 중복 설정(2), 컴파일된 사전 전용 `x:Shared`(1), 라이브러리의 첨부 이벤트 핸들러(2), 앱 테마 키 의존(1) 등 | 개별 사례 |

### 알려진 부작용과 한계
- **메모리**: 프로젝트 어셈블리 컨텍스트가 수집 불가라서 **산출물을 다시 빌드해 다시 불러올 때마다 이전 어셈블리가 프로세스에 남는다.** 큰 WPF 앱 프로젝트(CodeAtlas)로 재빌드를 흉내 내 측정하니 재빌드 1회당 비공개 메모리가 약 7MB 늘었다(8회 56→111MB). 호스트가 응답 없이 강제 종료되거나 VS Code **Reload Window**를 하면 초기화된다. 라이브러리 사전을 쓰는 프로젝트는 불러오는 어셈블리가 더 많아 증가량이 더 클 수 있다(측정하지 않음).
- 호스트는 사용자 코드를 이 PC에서 실행한다. 이 검증도 내려받은 라이브러리의 컨트롤 생성자를 실제로 실행했다.
- 별도 `Window` 크롬은 그리지 않는다(원래 한계).
- 이 숫자는 "XAML 파일 하나를 앱 밖에서 따로 그릴 수 있는가"다. 실제 앱의 데이터(DataContext)와 코드 비하인드 동작은 재현하지 않는다.

## 5. 다음 개선 후보 (아직 하지 않음)
0. **WPF UI 강조색(accent) 요소가 비어 보이는 원인 조사**(4절 "눈으로 확인한 그림"). 시스템 강조색 리소스가 호스트에 없어서일 수 있다.
1. `RenderFailed: Object reference not set…` 5건의 원인 조사.
2. 문서 기준 상대 경로 이미지/아이콘(`Icon="x.ico"`)을 프로젝트 어셈블리의 컴파일된 리소스 경로로 바꾸기(프로젝트 폴더 기준 경로 계산 필요).
3. 공유 프로젝트(Shared)의 XAML: 가장 가까운 `.csproj`에 산출물이 없으면 같은 폴더 트리를 참조하는 다른 프로젝트의 산출물을 찾기.
4. 재빌드 시 메모리 증가 완화: 일정 횟수 이상 다시 불러오면 호스트를 다시 시작하거나, 라이브러리 사전이 필요 없는 프로젝트는 수집 가능 컨텍스트를 유지.
5. 라이브러리 사전 안의 개별 컨트롤 실패가 문서 전체 실패로 번지지 않도록 하는 복구 범위 확대(현재는 인라인 구간만).

## 6. 샘플 정리
`WpfControlSamples`는 저장소 밖이며 약 140MB + 빌드 산출물이다. 필요 없으면 폴더째 지워도 이 저장소에는 영향이 없다.
