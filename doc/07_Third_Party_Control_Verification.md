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
| MaterialDesignInXamlToolkit | 54 | 52 (96%) | 3 |
| WPF UI (Gallery) | 86 | 81 (94%) | 21 |
| HandyControl | 149 | 측정 안 함 | 0 |
| **합계(HandyControl 제외)** | **180** | **165 (92%)** | 36 (20%) |

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

8. **WPF UI 강조색(Accent/Primary) 요소가 비어 보이던 문제**: `AccentButtonBackground`는 `Color="{DynamicResource AccentFillColorDefault}"`인데 그 키는 앱 시작 코드(`ApplicationAccentColorManager.ApplySystemAccent()`)가 `UiApplication.Current.Resources`에 쓴다. 호스트는 앱을 실행하지 않아 비었고, 훅을 호출해도 안 채워졌다. 이유는 `UiApplication`이 **스레드 정적으로 캐시**되는데, 앱 사전을 파싱하는 동안 컨트롤이 먼저 `UiApplication.Current`를 건드리면 그때는 `Application`에 WPF UI 리소스가 아직 없어서 `Application`이 아닌 **자기만의 임시 사전**을 쓰는 상태로 굳기 때문이다(그 객체에 쓴 키는 우리 `Application`에서 안 보인다). 앱 사전을 올린 뒤 `_uiApplication` 캐시를 비우고 훅을 실행하도록 했다(`HostApplication`의 라이브러리 훅 목록, 현재 WPF UI 하나).
9. **앱 수준 사전 적용의 부분 실패 복구**: `internal` 변환기 같은 요소 하나가 실패해도 앱 사전 전체를 버리지 않고 그 요소만 건너뛴다(문서 안 주입과 같은 방식). 이게 없으면 8번의 훅까지 가지 못했다.
10. **창 핸들(PresentationSource)이 필요한 컨트롤**: WPF UI `AutoSuggestBox`는 템플릿 적용 때 `(HwndSource)PresentationSource.FromVisual(this)!`로 창에 훅을 거는데, 화면 밖 렌더에는 그게 없어 `NullReferenceException`이 났다. 렌더가 실패하면 **한 번만** 숨은 메시지 전용 창(`HwndSource`)에 루트를 붙여 다시 그린다(5개 파일: `AutoSuggestBoxPage`, `IconsPage`, `NavigationViewPage`, `MainWindow`, `SandboxWindow`). 두 가지 함정이 있었다: `SizeToContent`를 쓰면 `RootVisual`을 붙이는 순간 창 메시지 콜백 안에서 동기 레이아웃이 돌아 예외가 잡히지 않고 **호스트가 `0xC000041D`로 즉사**한다(그래서 쓰지 않고 레이아웃은 우리가 `try` 안에서 돌린다). 붙인 뒤에는 `Loaded` 같은 지연 처리에서 앱 코드가 예외를 낼 수 있어(예: `PageControlDocumentation.OnLoaded`) 이 구간에서만 `Dispatcher.UnhandledException`을 가로채 무시하고 `RenderedWithHiddenWindow` 경고로 알린다. 다시 실패하면 처음 오류를 그대로 보고한다.
11. **재빌드 시 메모리 누적 완화**: 아래 "알려진 부작용" 참고.
12. **접두사가 붙은 핸들러 속성**: MaterialDesign의 `materialDesign:DialogHost.DialogClosingAttached="핸들러"`는 첨부 **이벤트**가 아니라 **델리게이트 타입의 첨부 속성**(`RegisterAttached`, `DialogClosingEventHandler`)이라 이벤트 판별(`IsEvent`)로는 잡히지 않았고, 핸들러 이름을 델리게이트로 만들려다 로드가 실패했다(처음에는 첨부 이벤트로 보고 고쳤지만 실제 파일이 계속 실패해 라이브러리 소스를 읽고서야 구조가 다르다는 것을 알았다). 소유자 타입을 속성의 접두사 네임스페이스에서 찾아 첨부 이벤트이거나 멤버 타입이 델리게이트면 제거한다(`RemovedEventHandler`). `Grid.Row` 같은 비델리게이트 첨부 속성은 유지한다. `Dialogs.xaml`, `Pickers.xaml`이 통과했다.
13. **프로젝트의 `internal` 타입**: 컴파일된 앱은 같은 어셈블리의 `internal` 컨트롤/변환기를 XAML에서 쓰지만, 컴파일되지 않은 XAML을 읽을 때는 **`XamlXmlReader`가 public이 아닌 타입을 "알 수 없는 형식"으로 만들어** 로드가 실패했다. 독립 실험 프로젝트로 확인한 사실: 스키마 조회(이름/Type, 기본·WPF 컨텍스트)와 객체 쓰기는 `AccessLevel` 없이도 성공하고, 리더의 타입 해석만 막혀 있다. 그래서 처음 시도한 `XamlObjectWriterSettings.AccessLevel`(WPF 내부 로더 리플렉션)은 **효과가 없었고 그 코드는 버렸다.** 해법은 공개 API인 `XamlXmlReaderSettings.LocalAssembly = 프로젝트 어셈블리`다. **신뢰된 프로젝트(Tier 1)에서만** 설정하고 신뢰하지 않은 렌더는 자리표시자로 남는다(테스트 `T12`, `T12b`). `PopupBox.xaml`, `SmartHint.xaml`(MaterialDesign), `SettingsPage.xaml`(WPF UI)이 통과했다.

**눈으로 확인한 그림**(결과 PNG를 직접 열어 봄, 6개 파일뿐이다. 나머지는 "오류 없음"만 확인했다):
- MaterialDesign `Buttons.xaml`: Material 스타일(색 버튼, 원형/분할 버튼, 배지, 칩)이 정상으로 그려진다.
- MahApps `ExampleViews\ButtonsExample.xaml`: Metro 스타일 버튼/토글 스위치/체크박스/라디오 버튼이 그려진다.
- WPF UI `Views\Pages\BasicInput\ButtonPage.xaml`: 8번 수정 전에는 Accent/FontIcon/ImageIcon/Primary 버튼이 **비어 보였고**(오류 없이!), 수정 후에는 강조색 파랑으로 그려진다. 즉 "오류 없이 렌더됨"이 "그림이 맞음"은 아니다.
- WPF UI `AutoSuggestBoxPage.xaml`: 검색창과 "Show clear button" 체크박스가 그려진다(10번 수정 전에는 오류).
- WPF UI `Views\Windows\MainWindow.xaml`(1450x802): 창 크롬(최소화/최대화/닫기), 검색창, 도구 버튼이 그려진다. 네비게이션 목록과 본문 영역은 앱 코드가 실행 중에 채우는 부분이라 **비어 있다**(정적 렌더의 한계).
- 다른 파일에 부분적 깨짐이 얼마나 더 있는지는 측정하지 않았다.

### 남은 실패 15건의 원인 (3개 합산)
| 건수 | 원인 | 비고 |
|---|---|---|
| 5 | 루트가 `ResourceDictionary` | 스타일/리소스 파일이라 미리보기 대상이 아님(정상): MahApps `XamlDisplay`, WPF UI 4개 |
| 3 | 루트가 `Application`(`App.xaml` 자체) | 미리보기 대상이 아님(정상). 합산하면 **미리보기 대상이 아닌 파일이 8건**이다 |
| 4 | 이미지/아이콘 리소스 | `Icon="….ico"`, `Icon="..\x.ico"`처럼 문서 기준 **상대 경로**로 프로젝트 리소스를 가리키는 `Window` 아이콘: MahApps `MainWindow`/`CleanWindowDemo`/`StyleSetsDemo`, MaterialDesign `MainWindow`(`favicon.ico`) |
| 2 | `MetroWindow`의 `Resources` 중복 설정 | `Window.Resources`를 직접 쓰는 `MetroWindow` 파생 XAML(MahApps `FlyoutDemo`, `VSDemo`) |
| 1 | 컴파일된 사전 전용 `x:Shared` | MahApps `OtherExamples` |

(앞선 측정의 "`internal` 타입", "접두사가 붙은 첨부 핸들러", "`StaticResource` 키 없음(`SettingsPage`)"은 12·13번 수정으로 모두 해결됐다.)

### 알려진 부작용과 한계
- **메모리**: 프로젝트 어셈블리 컨텍스트가 수집 불가라서 **산출물을 다시 빌드해 다시 불러올 때마다 이전 어셈블리가 프로세스에 남는다.** 큰 WPF 앱 프로젝트(CodeAtlas)로 재빌드를 흉내 내 측정하니 재빌드 1회당 비공개 메모리가 약 7MB 늘었다(8회 56→111MB). 호스트가 응답 없이 강제 종료되거나 VS Code **Reload Window**를 하면 초기화된다. 라이브러리 사전을 쓰는 프로젝트는 불러오는 어셈블리가 더 많아 증가량이 더 클 수 있다(측정하지 않음).
  - **완화(11번)**: 호스트 비공개 메모리가 처음 사용자 어셈블리를 로드한 직후보다 768MB(`UserAssemblies.RecycleGrowthBytes`) 넘게 늘면 렌더 응답에 `recycle: true`를 싣고(`H024`), 확장이 **처리 중인 요청이 없을 때** 그 호스트를 정상 종료(shutdown)하고 새 호스트로 바꾼다(`E015`, 크래시 로그 없음). 호스트가 스스로 종료하면 이미 보낸 다음 요청이 `HostCrashedError`로 거절될 수 있어 확장이 주도하게 했다. 확장 단위 테스트(`X-C06`, `X-C07`)로 검증했다. **임계값(768MB)에 실제로 도달하는 상황은 시험하지 못했다**(임의로 정한 값이다). 기준을 절대 크기가 아니라 증가량으로 한 것은 프로젝트 자체가 커서 늘 큰 경우 응답마다 재시작을 요청하지 않게 하려는 것이다. 재빌드 시 증가량은 CodeAtlas 약 7MB, MahApps 약 12MB(4회 56→…→161MB 실측)다.
- 호스트는 사용자 코드를 이 PC에서 실행한다. 이 검증도 내려받은 라이브러리의 컨트롤 생성자를 실제로 실행했다.
- 별도 `Window` 크롬은 그리지 않는다(원래 한계).
- 이 숫자는 "XAML 파일 하나를 앱 밖에서 따로 그릴 수 있는가"다. 실제 앱의 데이터(DataContext)와 코드 비하인드 동작은 재현하지 않는다.

## 5. 다음 개선 후보 (아직 하지 않음)
1. 문서 기준 상대 경로 이미지/아이콘(`Icon="x.ico"`, `Icon="..\x.ico"`)을 프로젝트 어셈블리의 컴파일된 리소스 경로로 바꾸기(프로젝트 폴더 기준 경로 계산 필요, 4건).
2. 공유 프로젝트(Shared)의 XAML: 가장 가까운 `.csproj`에 산출물이 없으면 같은 폴더 트리를 참조하는 다른 프로젝트의 산출물을 찾기(HandyControl 149개).
3. `MetroWindow`의 `Resources` 중복 설정(2건), 컴파일된 사전 전용 `x:Shared`(1건) 같은 라이브러리별 개별 사례.
4. 라이브러리별 시작 훅(`HostApplication`의 목록)은 현재 WPF UI 하나뿐이다. 다른 라이브러리에서 비슷한 "앱 시작 코드가 채우는 값" 문제가 보이면 같은 방식으로 추가한다.

## 6. 샘플 정리
`WpfControlSamples`는 저장소 밖이며 약 140MB + 빌드 산출물이다. 필요 없으면 폴더째 지워도 이 저장소에는 영향이 없다.
