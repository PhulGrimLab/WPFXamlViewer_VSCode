# 변경 이력

## 0.1.3 (2026-10-16, 미리보기)
### 보안
- **신뢰하지 않은 폴더(Tier 0)에서 XAML이 코드를 실행할 수 있던 문제(중요)**: `ObjectDataProvider`(`ObjectType`/`MethodName`), `x:FactoryMethod`, 임의 형식의 `{x:Static}` 같은 선언만으로 임의 정적 메서드(파일 쓰기, 프로세스 시작 등)가 실행되었다. 이제 호스트가 파싱 전에 노드를 검사해 WPF 형식과 기본 값 형식만 허용하고(허용 목록), 위 구성과 파일/네트워크 위치(`file:`, UNC, 드라이브 경로, `http(s):`)를 가리키는 `Source`/`FontFamily` 등을 `UntrustedBlocked` 오류로 거부한다. 폴더를 신뢰하면 이전처럼 그려진다. **0.1.0~0.1.2 사용자는 업데이트를 권장한다.**
- **폴더 없이 `.xaml` 파일만 연 창에서 신뢰 확인 없이 `dotnet build`/프로젝트 DLL 로드가 일어날 수 있던 문제**: 폴더 없는 창은 VS Code 기본 설정에서 "신뢰됨"으로 취급된다. 이제 프로젝트 코드 실행(자동 빌드, Tier 1 DLL 로드)은 Workspace Trust에 더해 **XAML과 `.csproj`가 열린 워크스페이스 폴더 안에 있을 때만** 허용한다.
- 호스트 안정성: 줄바꿈이 CR뿐인 문서, 같은 키가 두 번 있는 JSON 요청이 호스트를 종료시키던 문제를 고쳤고, 처리 중 어떤 예외가 나도 오류 응답 1건을 돌려준다(`InternalError`). 잘못된 `dpi`/크기(0, 음수, 무한대)는 `InvalidParams`로 거부한다.
- `Application.Resources`에 남은 신뢰 렌더의 리소스가 이후 신뢰하지 않은 렌더에 보일 수 있던 문제를 막았다. 병합 사전이 XML이 아니거나 읽을 수 없으면 파일 내용 조각이 오류 메시지에 실리지 않고 파일 이름만 담은 경고로 바뀐다. 오류 메시지는 2000자로 제한한다.
- 다른 VS Code 창의 호스트가 쓰는 임시 복사본(`xamlviewer-user-*`)을 시작 때 일부 지우던 문제: 폴더 이름에 PID를 넣고, 실행 중인 호스트의 폴더는 지우지 않는다.
- 사용자 컨트롤이 `Console.WriteLine`을 호출해도 프로토콜(stdout)이 오염되지 않도록 `Console.Out`을 stderr로 돌렸다. 로그 쓰기 스레드의 예외와 stdout 파이프가 닫힌 경우에도 호스트가 멈추거나 죽지 않는다. 손상된 `deps.json`이 있는 출력도 오류 안내로 처리한다.
- 확장: 자동 빌드가 끝나기 전에 다른 XAML로 바꾸면 그 문서가 "빌드 중"에 멈추던 문제, 빌드 대기에 제한 시간(10분)이 없고 종료 이벤트를 놓칠 수 있던 문제, 이미 빌드 중인데 **다시 빌드**를 누르면 빌드가 둘 도는 문제를 고쳤다. 웹뷰는 `localResourceRoots: []`로 제한하고, `file`/`untitled`가 아닌 문서(`git:` 등)는 미리보기 대상에서 뺐다. 객체가 아닌 JSON 응답과 정수가 아닌 렌더 크기를 무시한다.
- 릴리스 워크플로: 쓰기 권한(`contents: write`)을 릴리스를 만드는 잡에만 주고(테스트와 `npm ci`는 읽기 전용 토큰), 액션을 커밋 SHA로 고정하고, 태그 커밋이 `main`의 조상일 때만 릴리스한다. 패키징/CI 스크립트가 PATH를 덮어쓰지 않고 덧붙이도록 바꿨다.

### 수정
- **프로젝트를 다시 빌드한 뒤 미리보기가 모두 `RenderFailed`(`UIElementCollection` 값 추가 예외)로 실패하던 문제**: 이미 떠 있는 호스트가 다시 빌드된 DLL을 다시 불러오면 WPF UI 같은 라이브러리의 의존 속성이 같은 프로세스에서 중복 등록되어 이후 렌더가 모두 깨졌다(솔루션을 정리한 뒤 자동 빌드되거나, 직접 다시 빌드한 직후 재현). 이제 호스트가 `HostRestartRequired`로 알리면 확장이 호스트를 새로 띄워 같은 요청을 한 번 다시 보낸다.

## 0.1.2 (2026-10-16, 미리보기)
### 추가
- **한국어/영어 지원**: 확장의 문구(상태 표시줄, 알림, 자동 빌드 안내, 미리보기 도구 모음)와 호스트의 경고/오류 문구를 한국어·영어로 표시한다. 기본(`wpfXamlViewer.language` = `auto`)은 VS Code 표시 언어(기본적으로 Windows 언어)를 따르고 한국어가 아니면 영어다. 명령 **WPF XAML: 언어 전환 (한국어 / English)** 또는 설정으로 바꿀 수 있으며 바로 적용된다. 메뉴/명령/설정 제목은 `package.nls*.json`으로 번역되어 VS Code 표시 언어를 따른다. 로그와 .NET이 만드는 XAML 파싱 오류 원문은 번역하지 않는다.
- **빌드 산출물이 없으면 미리보기가 프로젝트를 자동 빌드**: `bin`을 정리하는 등 프로젝트가 빌드되어 있지 않으면 라이브러리 컨트롤(예: WPF UI)을 찾지 못해 "알 수 없는 형식" 같은 오류가 났다. 이제 호스트가 "산출물 없음"을 오류 응답(`projectReason`)에 실어 알리고, 확장이 그 XAML이 속한 `.csproj`를 `dotnet build`(설정 `buildConfiguration`)로 자동 빌드한 뒤 다시 그린다. 빌드에 실패하면 미리보기 화면·상태 표시줄·알림에 "이 XAML을 미리 보려면 프로젝트가 먼저 빌드되어야 하는데 빌드에 실패했습니다"를 표시한다(알림의 **다시 빌드**로 재시도). 신뢰된 폴더에서만 동작하며, 끄려면 설정 `wpfXamlViewer.autoBuildOnPreview`를 `false`로 한다.
- **우클릭 "구성 선택 빌드" 서브메뉴**: `.csproj`/`.sln`/`.slnx`/`.slnf`를 우클릭하면 빌드 / 다시 빌드(`.csproj`만) / 정리를 **Debug 또는 Release로 직접 골라** 실행한다(설정 `buildConfiguration`과 무관). 기존 단일 항목은 그대로 설정을 따른다. 작업 이름에 구성이 표시된다.

## 0.1.1 (2026-10-14, 미리보기)
### 수정
- **자체 포함(self-contained) 출력의 프로젝트에서 WPF UI 같은 라이브러리 컨트롤이 해석되지 않던 문제**: 출력 폴더에 든 프레임워크 복사본(`PresentationFramework`, `System.Xaml`, `WindowsBase` 등)을 사용자 컨텍스트에 또 로드해 라이브러리의 `XmlnsDefinition` 특성이 호스트의 WPF에서 보이지 않았다(`ui:FluentWindow`/`ui:SymbolIcon` 등 `XamlParse` 오류). 호스트의 공유 프레임워크에 있는 어셈블리는 사용자 출력의 복사본 대신 호스트 것을 쓴다. (PathDock `SelfContained=true`로 확인: 뷰 13개 중 12개 오류 → 0개)

## 0.1.0 (2026-10-14, 미리보기)
### 추가
- **프로젝트 우클릭 빌드/정리**: 탐색기에서 `.csproj`를 우클릭하면 프로젝트 빌드 / 다시 빌드 / 정리 / 복원(`.sln`/`.slnx`는 빌드·정리)을 그 프로젝트만 `dotnet`으로 실행한다. 오류는 문제 패널에 올라간다(`$msCompile`). `.xaml` 에디터 우클릭 "이 XAML의 프로젝트 빌드"는 가장 가까운 `.csproj`를 빌드한다. 설정 `wpfXamlViewer.buildConfiguration`(Debug/Release). 신뢰하지 않은 폴더에서는 숨기고 실행도 막는다(빌드는 프로젝트 코드를 실행). 호스트 exe가 없어도 쓸 수 있다.
- 빌드 설정 추가: `wpfXamlViewer.buildTargetFramework`(다중 대상 `.csproj`에서 한 TFM만 빌드, 그 `.csproj`가 TFM을 선언한 경우에만 `-f` 적용), `wpfXamlViewer.buildExtraArgs`(`dotnet` 인자 덧붙임, 신뢰하지 않은 폴더에서는 읽지 않음). `.slnf`(솔루션 필터) 우클릭 빌드/정리 지원. 실측(wpfui `Wpf.Ui`, 전체 재빌드): 6개 TFM 58초 → 1개 TFM 18초, 분석기 끔 27초, 둘 다 8초.

### 개선
- 사용자 프로젝트를 처음 불러올 때 훨씬 빨라졌다(실측: 큰 WPF 앱 프로젝트 첫 렌더 약 3.5~4.4초 → 약 1.7초). 의존 어셈블리를 미리 전부 로드하지 않고 XAML이 `assembly=`로 참조한 것만 로드하며, `deps.json`이 없는 .NET Framework 출력은 하위 폴더(런타임/스크립트 등)를 복사하지 않는다.
- 요청 제한 시간 기본값을 10초에서 30초로 늘렸다(콜드 스타트 첫 렌더가 10초를 넘을 수 있었다).
- 빌드 산출물이 `.exe`뿐인 WPF 앱 프로젝트(`OutputType=WinExe`)도 사용자 컨트롤을 실제로 그린다(`<AssemblyName>.dll`이 없으면 `.exe`를 쓴다).
- Tier 0(미신뢰/산출물 없음)에서 `TargetType="{x:Type local:Foo}"`/`DataType`을 가진 `Style`/`ControlTemplate`/`DataTemplate`과 속성 값 속 `{x:Type local:Foo}` 때문에 문서 전체가 실패하던 문제를 고쳤다(정의를 비우거나 제거하고 `PlaceholderUsed` 경고).
- 병합 사전/`App.xaml` 리소스 안의 요소 하나가 읽히지 않아도 문서 전체가 실패하지 않는다. 그 요소만 건너뛰고 `InlinedElementSkipped` 경고를 낸다(문서 본문의 오류는 그대로 오류로 보고).
- `pack://application:` 값을 쓰는 `Setter`(앱 아이콘 등)를 제거해 `Setter 초기화` 오류를 막는다(`RemovedApplicationResource` 경고).
- 사용자 컨트롤(`clr-namespace`, `assembly=` 없음)의 `Click` 같은 이벤트 핸들러 속성도 제거한다(이전에는 '`Click`을 만들지 못했습니다' 오류).

- **유명 컨트롤 라이브러리(MaterialDesign/MahApps.Metro/WPF UI) 지원 대폭 개선**: 데모 XAML 180개 중 오류 없이 렌더되는 비율 20% → 96%(미리보기 대상인 파일은 전부)(`doc/07_Third_Party_Control_Verification.md`).
  - 라이브러리 DLL에 컴파일된 리소스 사전(`pack://application:,,,/라이브러리;component/…`)을 로드된 어셈블리에서 읽는다.
  - 신뢰된 폴더에서 호스트가 `Application`을 만들어 `App.xaml` 리소스를 얹는다(라이브러리가 `Application.Current.Resources`를 본다).
  - URI형 xmlns(`http://…`)를 쓰면, 그리고 XAML에서만 쓰는 의존 어셈블리까지 필요할 때 출력 폴더의 관리 DLL을 로드한다.
  - `pack://application:,,,/Assets/…`(마크업 확장 인자 안 포함)를 프로젝트 어셈블리 경로로 바꾼다. 같은 개수 인자의 생성자가 여럿인 마크업 확장을 허용한다.
- 공유 프로젝트(`.projitems`)의 XAML도 그것을 Import하는 앱 프로젝트의 산출물로 그린다(HandyControl 데모 149개 중 130개가 오류 없이 렌더, 이전 0개).
- `Window`류 요소의 문서 기준 상대 경로 `Icon`(`Icon="x.ico"`)을 제거한다(`RemovedApplicationResource` 경고). `x:Shared` 속성은 텍스트 XAML에서 오류라 제거한다.
- 루트가 `Controls:MetroWindow`처럼 파생 클래스인데 `Window.Resources`를 쓴 문서에서 "`Resources` 속성이 이미 설정되어 있습니다" 오류가 나던 문제를 고쳤다.
- 인라인된 앱 리소스 안의 사용자 타입(예: `ViewModelLocator`)이 생성자 예외를 내도 그 요소만 건너뛴다.
- 보안: `deps.json`이 있는 출력에서는 `deps.json`에 없는 DLL을 미리 로드하지 않는다(참조되지 않는 DLL의 로드 시점 코드 실행 방지).
- **WPF UI 강조색(Accent/Primary) 요소가 비어 보이던 문제 해결**: `UiApplication`이 `Application`이 아닌 자기만의 사전을 쓰는 상태로 굳어 앱 시작 코드(`ApplySystemAccent`)가 쓴 값이 `Application.Resources`에 없었다. 앱 사전을 올린 뒤 알려진 라이브러리(WPF UI)의 시작 훅을 실행한다(캐시를 비운 뒤 `ApplicationAccentColorManager.ApplySystemAccent()`).
- 앱 수준 리소스 사전 적용도 실패한 요소만 건너뛴다(예: `internal` 변환기 타입). 문서 안 주입과 같은 방식이다.
- **창 핸들(PresentationSource)이 필요한 컨트롤 지원**: 렌더가 실패하면 한 번만 숨은 메시지 전용 창(`HwndSource`)에 루트를 붙여 다시 그린다(WPF UI `AutoSuggestBox`, `NavigationView`, `FluentWindow` 등). 그 뒤 `Loaded` 같은 지연 처리에서 앱 코드가 낸 예외는 무시하고 `RenderedWithHiddenWindow` 경고로 알린다. 다시 실패하면 처음 오류를 그대로 보고한다.
- 렌더 실패 원인(형식+스택, 메시지 제외)을 Debug 로그(`H012 cause=`)에 남긴다.
- **재빌드 시 메모리 누적 완화**: 호스트 비공개 메모리가 처음 로드 직후보다 768MB 넘게 늘면 렌더 응답에 `recycle:true`를 싣고(`H024`), 확장은 처리 중인 요청이 없을 때 그 호스트를 정상 종료하고 새 호스트로 바꾼다(`E015`, 크래시 로그 없음).
- 알려진 부작용: 프로젝트 어셈블리 컨텍스트가 수집 불가(non-collectible)라 **다시 빌드해 다시 불러올 때마다 호스트 메모리가 약 7MB씩 늘 수 있다**(측정값, 768MB 넘게 늘면 위처럼 호스트를 교체하고, VS Code Reload Window로도 초기화된다). 수집 가능 컨텍스트는 WPF의 동적 코드와 충돌해("non-collectible assembly may not reference a collectible assembly") 쓸 수 없었다.
- 신뢰하지 않은 문서(Tier 0)는 같은 프로세스에서 앞서 로드된 사용자 타입도 자리표시자로 취급한다.
- 알려진 한계: "오류 없이 렌더"가 "그림이 완전히 맞음"은 아니다(예: WPF UI 강조색 버튼이 비어 보임, 원인 미확인).

- **접두사가 붙은 핸들러 속성 제거**: 라이브러리의 첨부 멤버를 `materialDesign:DialogHost.DialogClosingAttached="핸들러"`처럼 쓰면 핸들러 이름을 델리게이트로 만들려다 로드가 실패했다. 이 멤버는 이벤트가 아니라 **델리게이트 타입의 첨부 속성**(`RegisterAttached`)이라 이벤트 판별(`IsEvent`)로는 못 잡는다. 접두사가 붙은 첨부 이벤트와 델리게이트 타입 첨부 속성을 소유자 타입의 네임스페이스에서 찾아 제거한다(`RemovedEventHandler`). 델리게이트가 아닌 첨부 속성(`Grid.Row` 등)은 유지한다. MaterialDesign `Dialogs.xaml`/`Pickers.xaml`이 렌더된다.

- **프로젝트의 `internal` 타입 지원**(신뢰된 폴더): 컴파일된 앱은 같은 어셈블리의 `internal` 컨트롤/변환기를 XAML에서 쓸 수 있지만, 컴파일되지 않은 XAML을 읽을 때는 `XamlXmlReader`가 public이 아닌 타입을 "알 수 없는 형식"으로 만들어 로드가 실패했다(WPF UI Gallery의 `internal sealed` 변환기, MaterialDesign 데모의 `internal` 변환기 등). `XamlXmlReaderSettings.LocalAssembly`를 로드한 프로젝트 어셈블리로 지정해 해결했다(공개 API만 사용). 처음에는 `XamlObjectWriterSettings.AccessLevel`을 WPF 내부 로더로 넘기는 방법을 시도했으나 **효과가 없었다**(원인이 쓰기 단계가 아니라 리더의 타입 해석이라서, 순수 System.Xaml로 실험해 확인하고 그 코드는 버렸다). **신뢰하지 않은 렌더(Tier 0)에서는 적용하지 않는다**(자리표시자, 테스트 `T12b`).

### 보안 수정
- **병합 사전/`App.xaml` 경로 탈출 차단**(신뢰하지 않은 폴더의 XAML이 로컬 파일을 읽어 인라인할 수 있었다): `ResourceDictionary Source`에 절대 경로(`C:\…`)를 쓰면 `Path.Combine`이 기준 폴더를 무시했고, `..\`로 프로젝트 밖 파일을 읽었으며, 프로젝트 안의 정션/심볼릭 링크로도 우회됐다. 이제 프로젝트 경계(가장 가까운 `.csproj`/`.sln`/`.slnx`/`.git`) 안에서만, 링크를 지나지 않는 파일만 읽는다. 재현 테스트 3개(`RD07`~`RD09`)로 취약점을 확인한 뒤 수정했다.
- 신뢰하지 않은 렌더(Tier 0)가 앞서 신뢰된 렌더가 올린 `Application.Resources`(사용자 타입 인스턴스 포함)와 이미 로드된 사용자 어셈블리의 이름 해석을 쓰지 못하게 했다.
- 어셈블리 이름 검증: 프로젝트 파일의 `AssemblyName`과 XAML의 `assembly=`/`;component/` 값에 위험 문자(따옴표, 꺾쇠, `;`, 경로 구분자 등)가 있으면 쓰지 않는다. 프로젝트 파일은 DTD를 금지하고 읽는다(엔티티 확장 폭주 방지).
- 렌더 실패 원인 로그에는 예외 **메시지를 싣지 않는다**(파서 메시지에 XAML 조각이 들어갈 수 있다).

### 코드 리뷰로 고친 것
- 재빌드로 버려진 임시 복사본(`%TEMP%\xamlviewer-user-*`)이 지워지지 않고 쌓이던 문제(측정 중 159개 폴더, 637MB): 수집 불가 컨텍스트는 DLL을 프로세스가 끝날 때까지 잠가 종료 시 삭제가 실패했다. 버린 폴더를 기억해 종료 때 다시 시도하고, 다음 호스트가 시작할 때 10분 넘은 폴더를 백그라운드로 지운다.
- 라이브러리 시작 훅이 재빌드 뒤 **낡은 복사본**의 어셈블리에 실행되던 문제: 현재 프로젝트의 컨텍스트에서 찾는다.
- 의존성 미리 로드가 xmlns마다 폴더 전체를 다시 훑던 문제: 컨텍스트당 한 번만 한다. 자체 포함 출력의 프레임워크 DLL(`PresentationFramework` 등)은 사용자 컨텍스트에 따로 로드하지 않는다(형식 ID가 달라진다).
- 숨은 창 폴백이 정리할 때 쌓인 지연 콜백(`Unloaded` 등)을 예외 처리기를 떼고 나서야 처리해 호스트가 죽을 수 있던 문제: 처리기가 붙어 있는 동안 비운다.
- 확장의 재활용 요청이 크래시/타임아웃으로 사라진 호스트의 것을 새 호스트가 이어받던 문제: 호스트가 바뀌면 초기화한다. 호스트의 재시작 판단을 "절대 1GiB"에서 "처음 로드 직후보다 768MB 증가"로 바꿨다(프로젝트 자체가 커서 늘 1GiB를 넘으면 응답마다 재시작을 요청하던 문제).
- 렌더 전체를 하나의 잠금으로 직렬화했다(프로세스 전역 `Application` 상태를 여러 스레드가 동시에 바꾸면 안 된다. 호스트는 원래 직렬이라 테스트의 병렬 실행에서만 문제였다).

### 테스트
- Fluent 테마(`ThemeMode`) 골든 2건(R15/R16)은 .NET 런타임 서비스 버전/시스템 강조색에 따라 색이 달라 같은 크기면 "비교 불가"(건너뜀)로 처리한다.

## 0.1.0 (미리보기 릴리스)
첫 공개 버전. **Windows 전용**, **.NET 10 Desktop Runtime 필요**.

### 기능
- `.xaml` 편집 중 실제 WPF 렌더링 미리보기(명령 `WPF XAML: Open Preview`), 편집 시 자동 갱신(300ms 디바운스).
- XAML 오류를 줄/열과 함께 Problems 패널에 표시. 오류가 나도 마지막 정상 이미지를 유지.
- 코드 비하인드 없이 실제 프로젝트 XAML 렌더: `x:Class`/이벤트 핸들러/`x:Code` 제거, `d:`/`mc:` 처리, `Window` 루트(콘텐츠만), 병합 사전, `App.xaml` 리소스.
- 사용자 컨트롤: 신뢰된 폴더에서 프로젝트의 빌드된 DLL을 불러와 실제로 렌더(Tier 1). 미신뢰/산출물 없음이면 자리표시자(Tier 0). 생성자 예외는 해당 컨트롤만 오류 박스.
- 미리보기 조작: 줌(버튼/Ctrl+휠/맞춤/100%), 드래그 이동, 배경 전환, 렌더 크기 지정.
- 클릭 ↔ 코드: 요소 클릭 시 XAML 줄로 이동, 에디터 커서 이동 시 요소 강조.
- .NET Desktop Runtime이 없으면 설치 안내.

### 알려진 한계
- 정적 렌더(코드 비하인드/이벤트/애니메이션 실행 없음), 창 크롬 없음. 드래그 앤 드롭 디자이너와 XAML 자동 완성은 제공하지 않음.
- "Visual Studio가 없는 깨끗한 PC" 검증은 아직 수행하지 않음. 웹뷰 마우스 조작(드래그/휠/클릭)은 자동 테스트로 검증하지 못함.
- `.vsix`/호스트 실행 파일은 코드 서명되지 않음(Windows SmartScreen/백신 경고 가능).
- 자세한 내용은 `doc/User_Guide.md`.
