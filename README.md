# WPFXamlViewer_VSCode
VSCode용 WPF XAML Viewer — `.xaml` 파일을 편집하면서 실제 WPF 렌더링 결과를 옆 패널에서 실시간으로 확인한다.

> 대상: **Windows 전용**. Visual Studio 2026이 없는 PC에서 VS2026 호환 프로젝트(SDK-style .NET WPF)를 VS Code로 개발할 때 사용.
>
> 상태: **계획 단계** (코드 없음). 개발 방침은 [CLAUDE.md](./CLAUDE.md).

## 문서
| 문서 | 내용 |
|---|---|
| [doc/00_Environment_Setup_Guide.md](./doc/00_Environment_Setup_Guide.md) | 개발 환경 요구사항, 이 머신 점검 결과(Node.js 미설치) |
| [doc/01_Architecture_And_Requirements.md](./doc/01_Architecture_And_Requirements.md) | 목표/범위, 렌더 방식 결정, 구성, 프로토콜, 스레드 모델, 로그 설계, 확인 필요 사항 |
| [doc/02_Development_Plan.md](./doc/02_Development_Plan.md) | 마일스톤 M0~M6 (단계 → 검증), 위험 |
| [doc/03_Test_Strategy.md](./doc/03_Test_Strategy.md) | 자동 테스트 계층(T1~T5), 골든 이미지 규칙, 테스트 케이스 목록, 실행/CI |
