# WPF XAML Live Preview

VS Code에서 `.xaml` 파일을 편집하면서 **실제 WPF 렌더링 결과**를 옆 패널에서 바로 확인합니다.
Visual Studio 없이 .NET SDK 10으로 WPF 프로젝트를 개발할 때 쓰는 것을 목표로 합니다. **Windows 전용.**

## 사용법
1. `.xaml` 파일을 열고 명령 팔레트(`Ctrl+Shift+P`)에서 **WPF XAML: Open Preview** 를 실행합니다.
2. 편집하면 자동으로 다시 그려집니다. 오류는 Problems 패널에 줄/열과 함께 표시됩니다.
3. 미리보기에서 요소를 클릭하면 해당 XAML 줄로 이동하고, 에디터 커서를 옮기면 요소가 강조됩니다.
4. 사용자 컨트롤을 실제로 그리려면 프로젝트를 먼저 `dotnet build` 하고 **폴더를 신뢰**(Workspace Trust)하세요.

## 요구 사항
- Windows 10/11 x64
- **.NET 10 Desktop Runtime** (없으면 확장이 설치 안내를 보여 줍니다)
- Visual Studio는 필요 없습니다.

자세한 사용법, 제한 사항, 보안 모델은 저장소의 `doc/User_Guide.md` 를 보세요.

## 보안
사용자 컨트롤을 실제로 그리려면 프로젝트의 빌드된 DLL을 불러와 **생성자 같은 사용자 코드를 실행**합니다.
이 동작은 신뢰한 폴더에서만 켜지고, 신뢰하지 않은 폴더에서는 사용자 컨트롤이 자리표시자로 표시됩니다.
