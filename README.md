# WPFXamlViewer_VSCode
VSCode용 WPF XAML Viewer — `.xaml` 파일을 편집하면서 실제 WPF 렌더링 결과를 옆 패널에서 실시간으로 확인한다.

> 대상: **Windows 전용**. Visual Studio 2026이 없는 PC에서 VS2026 호환 프로젝트(SDK-style .NET WPF)를 VS Code로 개발할 때 사용.
>
> 상태: **개발 중 (M0~M2 완료, 다음 M3)**. 렌더 호스트와 통신 계층은 동작·검증되었지만 확장에 아직 연결되지 않아
> **사용자가 쓸 수 있는 기능은 아직 없다.** 현황과 이어서 작업하는 방법은 [doc/04](./doc/04_Progress_Status_And_Next_Steps.md).
> 개발 방침은 [CLAUDE.md](./CLAUDE.md).

## 빠른 시작 (개발)
```powershell
.\doc\check_environment.ps1        # 환경 점검 (.NET SDK 10 / Desktop Runtime 10 / Node / VS Code / git)
cd extension; npm install; cd ..
.\tools\ci\ci.ps1                  # 호스트 + 확장 + 실제 호스트 장애 주입 테스트 전체 실행
```

## 문서
| 문서 | 내용 |
|---|---|
| [doc/04_Progress_Status_And_Next_Steps.md](./doc/04_Progress_Status_And_Next_Steps.md) | **진행 상태, 다른 PC에서 이어가기, 미검증 항목, 다음 작업(M3)** |
| [doc/00_Environment_Setup_Guide.md](./doc/00_Environment_Setup_Guide.md) | 개발 환경 요구사항과 결정(호스트는 net10 + dotnet, VS 불필요) |
| [doc/01_Architecture_And_Requirements.md](./doc/01_Architecture_And_Requirements.md) | 목표/범위, 렌더 방식 결정, 구성, 프로토콜, 스레드 모델, 로그 설계 |
| [doc/02_Development_Plan.md](./doc/02_Development_Plan.md) | 마일스톤 M0~M6 (단계 → 검증)과 마일스톤별 결과 |
| [doc/03_Test_Strategy.md](./doc/03_Test_Strategy.md) | 자동 테스트 계층, 골든 이미지 규칙, 테스트 케이스 목록, 실행/CI |
