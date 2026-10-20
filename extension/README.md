# WPF XAML Live Preview

**English** · [한국어](#한국어)

See the **real WPF rendering** of a `.xaml` file in a side panel while you edit it in VS Code.
Built for developing WPF projects with the .NET 10 SDK, **without Visual Studio**. **Windows only.**

## How to use
1. Open a `.xaml` file and run **WPF XAML: Open Preview** from the Command Palette (`Ctrl+Shift+P`). By default the preview also opens automatically when you open a `.xaml` file.
2. It redraws automatically as you edit. Errors appear in the Problems panel with line/column. If a render fails, fix it and press **↻ Refresh** in the preview toolbar.
3. Click an element in the preview to jump to its XAML line; move the editor cursor to highlight the element.
4. To draw your own controls for real, **build the project**, **trust the folder** (Workspace Trust) and **allow the project's code to run** when asked (first preview of each project). Until then, your controls are shown as placeholders and the rest of the XAML is still drawn.
5. Right-click a `.csproj` in the Explorer to **Build / Rebuild / Clean / Restore** it (only that project runs through `dotnet`; errors go to the Problems panel), or choose **Build with Configuration** to pick Debug/Release each time. **Build This XAML's Project** in a `.xaml` editor's context menu does the same for the nearest project.

## Requirements
- Windows 10/11 x64
- **.NET 10 Desktop Runtime** (the extension shows an install hint if it is missing)
- Visual Studio is not required.

## Languages
The extension supports **English and Korean (한국어)**. Messages, the status bar and the preview toolbar follow the VS Code display language by default; switch with **WPF XAML: Switch Language** or the setting `wpfXamlViewer.language`. Menu and command titles follow the VS Code display language.

## Security
To draw your own controls, the project's built DLL is loaded and **user code such as constructors runs on your PC**. This happens only in a trusted folder, only for projects you have allowed, and only for projects inside the opened workspace folder. Otherwise user controls are shown as placeholders. A DLL committed to a repository's `bin/` can run without a build, so allow only projects whose origin you know. Untrusted XAML is checked against an allow-list before it is parsed; this is not a complete sandbox.

## Documentation
- [User Guide (English)](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/doc/User_Guide.en.md)
- [사용자 가이드 (한국어)](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/doc/User_Guide.md)
- [Changelog](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/extension/CHANGELOG.md) (Korean)

---

# 한국어

VS Code에서 `.xaml` 파일을 편집하면서 **실제 WPF 렌더링 결과**를 옆 패널에서 바로 확인합니다.
Visual Studio 없이 .NET SDK 10으로 WPF 프로젝트를 개발할 때 쓰는 것을 목표로 합니다. **Windows 전용.**

## 사용법
1. `.xaml` 파일을 열고 명령 팔레트(`Ctrl+Shift+P`)에서 **WPF XAML: Open Preview** 를 실행합니다. 기본값에서는 `.xaml` 파일을 열면 미리보기도 자동으로 열립니다.
2. 편집하면 자동으로 다시 그려집니다. 오류는 Problems 패널에 줄/열과 함께 표시됩니다. 렌더가 실패하면 고친 뒤 미리보기 툴바의 **↻ 새로고침**을 누르세요.
3. 미리보기에서 요소를 클릭하면 해당 XAML 줄로 이동하고, 에디터 커서를 옮기면 요소가 강조됩니다.
4. 사용자 컨트롤을 실제로 그리려면 프로젝트를 먼저 빌드하고 **폴더를 신뢰**(Workspace Trust)한 뒤, 처음 미리보기할 때 묻는 **프로젝트 코드 실행 허용**에 동의하세요. 그 전에는 사용자 컨트롤이 자리표시자로 표시되고 나머지는 그려집니다.
5. 탐색기에서 `.csproj`를 우클릭하면 **WPF XAML: 프로젝트 빌드 / 다시 빌드 / 정리 / 복원**을 고를 수 있습니다(그 프로젝트만 `dotnet`으로 실행, 오류는 Problems 패널). **구성 선택 빌드**에서는 Debug/Release를 그때그때 고릅니다. `.xaml` 에디터 우클릭의 **이 XAML의 프로젝트 빌드**도 같습니다. 구성은 설정 `wpfXamlViewer.buildConfiguration`(Debug/Release).

## 요구 사항
- Windows 10/11 x64
- **.NET 10 Desktop Runtime** (없으면 확장이 설치 안내를 보여 줍니다)
- Visual Studio는 필요 없습니다.

## 언어
**한국어와 영어**를 지원합니다. 메시지, 상태 표시줄, 미리보기 툴바는 기본적으로 VS Code 표시 언어를 따르며, **WPF XAML: 언어 전환** 명령이나 설정 `wpfXamlViewer.language`로 바꿀 수 있습니다. 메뉴/명령 제목은 VS Code 표시 언어를 따릅니다.

## 보안
사용자 컨트롤을 실제로 그리려면 프로젝트의 빌드된 DLL을 불러와 **생성자 같은 사용자 코드를 실행**합니다.
이 동작은 신뢰한 폴더에서, 사용자가 허용한 프로젝트만, 열린 워크스페이스 폴더 안의 프로젝트에 대해서만 켜지고, 그 밖에는 사용자 컨트롤이 자리표시자로 표시됩니다.
저장소에 커밋된 `bin/`의 DLL은 빌드 없이도 실행될 수 있으니 출처를 아는 프로젝트만 허용하세요. 신뢰하지 않은 XAML은 파싱 전에 허용 목록으로 검사하지만 완전한 샌드박스는 아닙니다.

## 문서
- [사용자 가이드 (한국어)](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/doc/User_Guide.md)
- [User Guide (English)](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/doc/User_Guide.en.md)
- [변경 이력](https://github.com/PhulGrimLab/WPFXamlViewer_VSCode/blob/main/extension/CHANGELOG.md)
