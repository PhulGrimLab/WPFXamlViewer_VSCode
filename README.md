# WPFXamlViewer_VSCode
VSCode용 WPF XAML Viewer — `.xaml` 파일을 편집하면서 **실제 WPF 렌더링 결과**를 옆 패널에서 실시간으로 확인한다.

> 목적: **Visual Studio 없이 VS Code에서 .NET 10 SDK로 WPF 프로젝트를 UI까지 개발**한다. **Windows 전용.**
> 사용법과 제한 사항은 **[doc/User_Guide.md](./doc/User_Guide.md)**.
>
> 상태: **M0~M6 구현 완료, 단 "VS 없는 깨끗한 PC" 검증은 아직 못 했다**([doc/05](./doc/05_Clean_Machine_Verification.md)).
> 드래그 앤 드롭 디자이너와 XAML 자동 완성은 이 확장의 범위가 아니다. 현황/이어서 작업하는 방법은 [doc/04](./doc/04_Progress_Status_And_Next_Steps.md).
> 개발 방침은 [CLAUDE.md](./CLAUDE.md).

## 쓰는 법 (사용자)
1. `.vsix` 설치: VS Code 확장 뷰 → `...` → **VSIX에서 설치** (또는 `code --install-extension artifacts\wpf-xaml-viewer-0.1.0.vsix`).
2. **.NET 10 Desktop Runtime**이 필요하다(없으면 확장이 안내한다).
3. `.xaml` 파일에서 명령 팔레트 → **WPF XAML: Open Preview**.

## 빠른 시작 (개발)
```powershell
.\doc\check_environment.ps1        # 환경 점검 (.NET SDK 10 / Desktop Runtime 10 / Node / VS Code / git)
cd extension; npm install; cd ..
.\tools\ci\ci.ps1                  # 호스트 + 확장 단위 + 실제 호스트 장애 주입 테스트
.\tools\ci\ci.ps1 -IncludeIntegration   # + 실제 VS Code 통합 테스트(I-01~I-10)
.\tools\ci\ci.ps1 -IncludePackage       # + .vsix 패키징(호스트 번들) + 설치 스모크(I-09)
cd extension; npm run package      # .vsix만 만든다 -> artifacts\wpf-xaml-viewer-<버전>.vsix
```

## 문서
| 문서 | 내용 |
|---|---|
| [doc/User_Guide.md](./doc/User_Guide.md) | **사용자 가이드: 개발 흐름, 미리보기 기능, 사용자 컨트롤/신뢰, 변환 규칙, 제한, 문제 해결** |
| [doc/04_Progress_Status_And_Next_Steps.md](./doc/04_Progress_Status_And_Next_Steps.md) | **진행 상태, 다른 PC에서 이어가기, 미검증 항목, 다음 작업** |
| [doc/05_Clean_Machine_Verification.md](./doc/05_Clean_Machine_Verification.md) | VS 없는 깨끗한 PC 검증 체크리스트(미실행) |
| [doc/00_Environment_Setup_Guide.md](./doc/00_Environment_Setup_Guide.md) | 개발 환경 요구사항과 결정(호스트는 net10 + dotnet, VS 불필요) |
| [doc/01_Architecture_And_Requirements.md](./doc/01_Architecture_And_Requirements.md) | 목표/범위, 렌더 방식 결정, 구성, 프로토콜, 스레드 모델, 로그 설계 |
| [doc/02_Development_Plan.md](./doc/02_Development_Plan.md) | 마일스톤 M0~M6 (단계 → 검증)과 마일스톤별 결과 |
| [doc/03_Test_Strategy.md](./doc/03_Test_Strategy.md) | 자동 테스트 계층, 골든 이미지 규칙, 테스트 케이스 목록, 실행/CI |
