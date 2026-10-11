# 07. 유명 WPF 컨트롤 라이브러리로 검증하기

> 목적: 실제로 많이 쓰이는 오픈소스 WPF 컨트롤 라이브러리의 **데모 앱 XAML**을 확장의 호스트로 일괄 렌더해, 커스텀 컨트롤이 얼마나 그려지는지/어디서 막히는지 측정한다.
> 상태: **1차 측정 완료(2026-10-12).** 결과는 아래 4절. 자동 회귀 테스트가 아니라 **수동 실행하는 검증 절차**이며 CI에는 넣지 않았다(외부 저장소를 내려받고 수 분 빌드해야 한다).

## 1. 참고한 저장소
별 수는 2026-10-11에 GitHub API로 조회한 값이다. 샘플은 저장소 밖 `C:\Works_PhulGrimLab\WpfControlSamples\<이름>` 에 `git clone --depth 1`로 받았다(커밋 해시는 그때의 `HEAD`).

| 저장소 | 별 | 커밋 | 성격 | 렌더 대상 데모 프로젝트 | 대상 TFM(빌드한 것) |
|---|---|---|---|---|---|
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 약 16.3k | `112d83b` | Material Design 컨트롤/테마 | `src\MainDemo.Wpf` | net8.0-windows |
| [MahApps.Metro](https://github.com/MahApps/MahApps.Metro) | 약 9.8k | `89a7558` | Metro 스타일 창/컨트롤(`MetroWindow` 등) | `src\MahApps.Metro.Samples\MahApps.Metro.Demo` | net8.0-windows |
| [WPF UI (lepoco/wpfui)](https://github.com/lepoco/wpfui) | 약 9.7k | `ffebacd` | Fluent 스타일 컨트롤 | `src\Wpf.Ui.Gallery` | net10.0-windows10.0.26100.0 |
| [HandyControl](https://github.com/HandyOrg/HandyControl) | 약 7.2k | `2c0875e` | 컨트롤 모음 | `src\Net_GE45\HandyControlDemo_Net_GE45` (XAML은 `src\Shared\HandyControlDemo_Shared`) | net8.0-windows |

선정 기준: WPF 컨트롤 라이브러리 중 별 수 상위. 이 목록이 "활용이 가장 많은 것"의 엄밀한 순위는 아니다(별 수는 사용량과 다르다). 후보였던 ModernWpf(약 5k), Xceed WPF Toolkit(약 4.2k), Fluent.Ribbon(약 2.8k)은 이번에 받지 않았다.

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
`tools\verify\render_batch.ps1`이 프로젝트 폴더의 모든 `.xaml`(`obj`/`bin` 제외)을 호스트 한 프로세스에 차례로 `render` 요청하고 파일별 결과를 CSV로 남긴다(호스트가 죽거나 시간 초과면 다시 띄운다).
```powershell
$exe = "$env:USERPROFILE\.vscode\extensions\phulgrimlab.wpf-xaml-live-preview-0.1.0\bin\host\XamlRenderHost.exe"   # 또는 host\...\bin\Release\...\XamlRenderHost.exe
.\tools\verify\render_batch.ps1 -Exe $exe -ProjectDir $root\MahApps.Metro\src\MahApps.Metro.Samples\MahApps.Metro.Demo -OutCsv .\res-mahapps.csv
# 옵션: -Untrusted(신뢰하지 않은 폴더=Tier 0), -Max 400(최대 파일 수), -TimeoutMs 60000(파일당)
```
CSV 열: `File, Status(OK/ERROR/TIMEOUT/HOSTDEAD), Tier, Ms, Warnings, WarnCodes, Detail(오류 코드: 메시지)`.

### 3-4. 결과 분석
```powershell
$r = Import-Csv .\res-mahapps.csv
$r | Group Status | % { "$($_.Name): $($_.Count)" }
$r | ? Status -ne OK | Group { $_.Detail -replace "'[^']*'", "'…'" -replace '\d+','N' } | Sort Count -Desc | Select -First 10
```
**눈으로 확인할 것**: 이 스크립트는 "오류 없이 그려졌는가"만 센다. 그림이 맞는지는 별도로, 확장에서 해당 파일을 열어 보거나 `host`의 `render --in x.xaml --out x.png`로 PNG를 만들어 본다.

## 4. 1차 결과 (2026-10-12)
조건: 설치된 확장의 호스트(수정 반영본), 신뢰 상태(Tier 1), 각 데모 프로젝트 폴더의 모든 `.xaml`.

| 저장소 | 파일 수 | 오류 없이 렌더(Tier 1) | 중앙값 시간 | 비고 |
|---|---|---|---|---|
| MahApps.Metro | 40 | 12 (30%) | 51ms | |
| MaterialDesignInXamlToolkit | 54 | 3 (6%) | 35ms | |
| WPF UI (Gallery) | 86 | 21 (24%) | 308ms | |
| HandyControl | 149 | 0 (0%) | — | XAML이 `Shared` 프로젝트에 있어 **가장 가까운 `.csproj`(공유 프로젝트)에 빌드 산출물이 없다**. 데모 앱 폴더에는 XAML이 0개 |

합계(HandyControl 제외) 180개 중 36개(20%)가 오류 없이 그려졌다. **"대부분 그려진다"고 말할 수 있는 결과가 아니다.**

### 실패 원인 분류 (MahApps + MDIX + WPF UI의 실패 144건)
| 건수 | 원인 | 설명 |
|---|---|---|
| 63 | `StaticResource` 키를 찾지 못함 | 데모의 `App.xaml`이 `pack://application:,,,/라이브러리;component/Themes/X.xaml` 로 **참조 라이브러리에 컴파일된(BAML) 리소스 사전**을 병합한다. 호스트는 그 파일을 디스크에서 읽어 인라인하므로(`DictionaryUnavailable`) 라이브러리 스타일/브러시가 비어 있고, 이를 쓰는 XAML이 실패한다. 가장 큰 원인 |
| 45 | 알 수 없는 형식 | 예: WPF UI `CodeBlock`(`http://schemas.lepo.co/wpfui/2022/xaml/syntax`), MahApps `PackIconModern`. URI형 xmlns(`XmlnsDefinition`)로 정의된 형식 |
| 24 | 이미지/아이콘 리소스 | `Icon="mahapps.metro.logo2.ico"`, `pack://application:,,,/Assets/...png` 처럼 프로젝트에 포함된 리소스를 읽지 못함 |
| 3 | 루트가 `ResourceDictionary` | 미리보기 대상이 아닌 파일(스타일/리소스 사전) |
| 3 | `Application` 생성자 | `App.xaml` 자체를 렌더하려는 경우 |
| 그 외 | `MetroWindow`의 `Resources` 중복 설정, 컴파일된 사전 전용 `x:Shared`, 일부 마크업 확장 생성자 중복 오버로드 등 | 개별 사례 |

### 이번 검증으로 찾아 고친 것
- URI형 xmlns(`http://metro.mahapps.com/...` 등)를 쓰면 `MetroWindow`가 "알 수 없는 형식"이 되었다. 첫 로드 속도 개선(의존 어셈블리 미리 로드 제거)이 만든 회귀였고, **그런 xmlns가 나올 때만** 의존 어셈블리를 모두 로드하도록 고쳤다(`XamlPreprocessor`/`UserAssemblies.PreloadAllDependencies`). 수정 후 `MetroWindow`는 해석되고 오류가 `Icon`(이미지 리소스)으로 옮겨 갔다. 파일 수준 성공 건수는 그대로다(실패 원인이 한 겹 더 있었기 때문).

### 해석할 때 주의
- 이 숫자는 "XAML 파일 하나를 **앱 밖에서 따로** 그릴 수 있는가"이다. 실제 앱에서는 `App.xaml`이 라이브러리 테마를 먼저 올리므로 같은 파일이 정상 동작한다. 확장은 앱 전체를 실행하지 않으므로 라이브러리의 컴파일된 테마를 재현하는 것이 핵심 과제다.
- 설치된 확장의 호스트는 사용자 코드를 이 PC에서 실행한다. 이 검증도 내려받은 라이브러리의 컨트롤 생성자를 실제로 실행했다.
- 별도 `Window` 크롬은 그리지 않는다(원래 한계).

## 5. 다음 개선 후보 (우선순위 순, 아직 하지 않음)
1. **참조 라이브러리의 컴파일된 리소스 사전 로드**: `pack://application:,,,/라이브러리;component/…` 병합 사전을 인라인(텍스트) 대신 호스트가 로드한 라이브러리 어셈블리에서 WPF가 직접 읽게 한다(pack URI 해석 환경 필요). 63건(실패의 약 44%)이 여기에 해당한다.
2. **프로젝트에 포함된 이미지/아이콘 리소스**: 상대 경로/`pack://application` 이미지를 디스크 파일로 풀어 주거나, 못 풀면 해당 속성만 제거하고 경고.
3. **공유 프로젝트(Shared)의 XAML**: 가장 가까운 `.csproj`에 산출물이 없으면 같은 폴더 트리를 가리키는 다른 프로젝트의 산출물을 찾는다.
4. 라이브러리 자신의 URI형 xmlns 형식(`CodeBlock` 등)이 `RenderFailed`로 나오는 경로를 조사한다.

## 6. 샘플 정리
`WpfControlSamples`는 저장소 밖이며 약 140MB + 빌드 산출물이다. 필요 없으면 폴더째 지워도 이 저장소에는 영향이 없다.
