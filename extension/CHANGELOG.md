# 변경 이력

## 미출시
### 개선
- 사용자 프로젝트를 처음 불러올 때 훨씬 빨라졌다(실측: 큰 WPF 앱 프로젝트 첫 렌더 약 3.5~4.4초 → 약 1.7초). 의존 어셈블리를 미리 전부 로드하지 않고 XAML이 `assembly=`로 참조한 것만 로드하며, `deps.json`이 없는 .NET Framework 출력은 하위 폴더(런타임/스크립트 등)를 복사하지 않는다.
- 요청 제한 시간 기본값을 10초에서 30초로 늘렸다(콜드 스타트 첫 렌더가 10초를 넘을 수 있었다).
- 빌드 산출물이 `.exe`뿐인 WPF 앱 프로젝트(`OutputType=WinExe`)도 사용자 컨트롤을 실제로 그린다(`<AssemblyName>.dll`이 없으면 `.exe`를 쓴다).
- Tier 0(미신뢰/산출물 없음)에서 `TargetType="{x:Type local:Foo}"`/`DataType`을 가진 `Style`/`ControlTemplate`/`DataTemplate`과 속성 값 속 `{x:Type local:Foo}` 때문에 문서 전체가 실패하던 문제를 고쳤다(정의를 비우거나 제거하고 `PlaceholderUsed` 경고).
- 병합 사전/`App.xaml` 리소스 안의 요소 하나가 읽히지 않아도 문서 전체가 실패하지 않는다. 그 요소만 건너뛰고 `InlinedElementSkipped` 경고를 낸다(문서 본문의 오류는 그대로 오류로 보고).
- `pack://application:` 값을 쓰는 `Setter`(앱 아이콘 등)를 제거해 `Setter 초기화` 오류를 막는다(`RemovedApplicationResource` 경고).
- 사용자 컨트롤(`clr-namespace`, `assembly=` 없음)의 `Click` 같은 이벤트 핸들러 속성도 제거한다(이전에는 '`Click`을 만들지 못했습니다' 오류).

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
