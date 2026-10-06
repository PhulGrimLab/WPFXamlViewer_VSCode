# 00. 개발 환경 구성 가이드

> 이 문서는 WPF XAML Viewer(VS Code 확장)를 **빌드/테스트/실행**하기 위해 필요한 환경을 정리한다.
> 2026-10-06 이 개발 머신에서 직접 확인한 결과를 기준으로 한다.
> **개정(2026-10-06)**: 제품 목적이 "VS2026이 없는 PC에서 VS2026 호환 프로젝트를 VS Code로 개발"로 확정되어
> 렌더 호스트를 .NET 10(SDK-style)로 변경했다. 이전의 "VS2019 + net48" 결정은 폐기한다(2절).

## 1. 두 종류의 환경을 구분한다

| 구분 | 누가 | 필요한 것 |
|---|---|---|
| **개발 환경** (이 확장을 만드는 사람) | 우리 | .NET SDK 10, Node.js LTS, VS Code, git |
| **사용자 환경** (확장을 쓰는 사람) | VS2026 없는 PC에서 WPF 프로젝트를 VS Code로 개발하는 사람 | VS Code, **.NET 10 Desktop Runtime**(보통 .NET SDK 10에 포함), Windows 10 1607+/11 |

핵심: **Visual Studio는 개발/사용 어느 쪽에도 필요 없다.** (VS 설치 여부와 무관하게 동작해야 제품 목적이 성립한다.)

## 2. 요구사항 표 (개발 환경)

| 구성 요소 | 필수 | 용도 | 이 머신 상태 (2026-10-06) |
|---|---|---|---|
| Windows 10/11 x64 | 필수 | WPF 렌더 호스트 실행 | Windows 11 Pro ✔ |
| **.NET SDK 10.x** | 필수 | 호스트(`net10.0-windows`, WPF) 빌드/테스트 | 10.0.400 ✔ |
| VS Code | 필수 | 확장 실행/디버깅 | 1.136.1 ✔ |
| **Node.js LTS + npm** | 필수 | 확장(TypeScript) 빌드, 테스트, `vsce` 패키징 | 설치함(2026-10-06): Node 24.19.0 / npm 11.17.0 ✔ |
| git | 필수 | 형상관리 | ✔ |
| Visual Studio 2019/2026 | **불필요** | — (이 머신에는 둘 다 있으나 의존하지 않는다) | 있음(미사용) |

## 3. 결정 사항: 렌더 호스트는 SDK-style `net10.0-windows` WPF

이전 안(VS2019 MSBuild + 클래식 csproj + net48)은 CodeAtlas_v0의 제약(VS2019 고정)을 그대로 가져온 것이었으나,
이 프로젝트의 목적에는 맞지 않아 폐기한다.

- 대상 프로젝트가 VS2026 호환 = 대부분 SDK-style `net8/9/10-windows` WPF. 렌더러가 .NET Framework WPF면
  최신 WPF 기능(.NET 9+ Fluent 테마 `ThemeMode`, 신규 컨트롤/속성)을 못 그린다. **호스트 TFM ≥ 대상 프로젝트 TFM** 이어야 한다.
- 이 머신의 .NET SDK 10(MSBuild 18 계열)과 정확히 맞는다. CodeAtlas에서 겪은 "SDK 10 ↔ VS2019 MSBuild 불일치"
  문제가 사라진다(`dotnet build`/`dotnet test`만 사용).
- vstest `testhost.exe`가 `.dll.config` 바인딩 리다이렉트를 무시하는 문제(CodeAtlas doc/01 1.6)는 .NET Framework 한정이라
  해당 없음. 다만 **호스트 exe는 서브프로세스로 띄워 검증**하는 방식은 실사용 경로와 같아서 유지한다.
- 레거시(net48, 클래식 csproj) WPF 프로젝트의 XAML도 net10 WPF로 대부분 그려진다. 완전 동일을 보장하지는 않으며,
  차이는 알려진 제한으로 문서화한다(M4).
- 호스트 설정: `net10.0-windows`, `UseWPF=true`, `Platforms=x64`, `LangVersion` 기본(최신), `Nullable=enable`.
  **프레임워크 종속(framework-dependent)** 배포를 기본으로 한다(사용자 PC에 .NET 10 Desktop Runtime 필요).
  Self-contained 번들은 용량(수십~100MB+)이 커서 보류 — 선택지는 01 문서 §7 확인 항목.

## 4. Node.js 설치 (새 PC에서 없을 때)

자동 설치는 하지 않고 사용자가 직접 실행한다(`!` 접두사로 이 세션에서 실행 가능). 이 개발 머신에는 이미 설치했다.

```
winget install OpenJS.NodeJS.LTS
```

설치 후 **새 터미널**에서 확인: `node -v`, `npm -v`.

## 5. 환경 점검 스크립트 (M0에서 작성)

`doc/check_environment.ps1`: 위 표를 자동 점검하고, 빠진 것은 **설치 방법만 안내**한다(자동 설치 금지 — CodeAtlas와 같은 원칙).
점검: `dotnet --list-sdks`에 10.x, `dotnet --list-runtimes`에 `Microsoft.WindowsDesktop.App 10.x`, `node`/`npm`, `code`, git.

**확장 런타임 시작 시 점검(사용자 환경)**: 호스트 실행 전 `dotnet --list-runtimes`(또는 호스트 자체의 실패 코드)로
Desktop Runtime 10 존재를 확인하고, 없으면 **설치 안내 알림**(winget 명령/다운로드 링크 복사 버튼)을 띄운다. 이 경로도 자동 테스트 대상(I-05).

## 6. 알려진 주의사항
- 한글 콘솔 인코딩: PowerShell 5.1 기본 코드페이지에서 UTF-8 한글이 깨진다. 스크립트는 UTF-8 BOM으로 저장하고,
  출력 파싱이 필요하면 `[Console]::OutputEncoding`을 명시한다.
- `.gitignore`에 `.vscode/`가 들어 있다. 확장 개발에는 `launch.json`/`tasks.json`이 필요하므로 M0에서
  `.vscode/*` + `!.vscode/launch.json` + `!.vscode/tasks.json`으로 조정하고, `node_modules/`, `out/`, `dist/`, `*.vsix`를 추가한다.
- VS2026 신규 솔루션 형식 `.slnx`: 1차에서는 솔루션을 파싱하지 않고 **XAML 파일에서 위로 올라가며 가장 가까운 `.csproj`** 를 찾는다.
  따라서 `.sln`/`.slnx` 모두 영향 없다.
