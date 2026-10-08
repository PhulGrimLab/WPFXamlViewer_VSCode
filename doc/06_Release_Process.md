# 06. 릴리스 절차 (GitHub Release, `.vsix` 배포)

> 확정 사항(2026-10-08, 사용자 결정): 표시 이름 **WPF XAML Live Preview**, 내부 name `wpf-xaml-live-preview`, 게시자 `phulgrimlab`
> → 확장 ID **`phulgrimlab.wpf-xaml-live-preview`**. 배포 채널은 **GitHub Release에 `.vsix` 파일 첨부**(Marketplace는 아직 아님).
> **확장 ID(게시자.이름)는 한 번 배포하면 바꾸기 어렵다**(설치한 사람의 확장/설정/저장소 경로가 ID에 묶인다).

## 상태
- **릴리스는 아직 만들지 않았다.** 태그(`v0.1.0`)도 없다. 이 문서는 절차만 정리한 것이다.
- 권장: 첫 공개 전에 [05 깨끗한 PC 검증](./05_Clean_Machine_Verification.md)을 한 번 수행한다(아직 미실행). 그 전에 공개한다면 릴리스 노트에 "깨끗한 PC 검증 미완료"를 적고 **Pre-release**로 올린다.

## 1. 릴리스 전 점검표
| # | 항목 | 확인 방법 |
|---|---|---|
| 1 | 작업 트리가 깨끗하고 `main`이 원격과 같다 | `git status`, `git log origin/main..` |
| 2 | 전체 자동 검증 통과 | `.\tools\ci\ci.ps1 -IncludeIntegration -IncludePackage` 종료 코드 0 |
| 3 | 버전 올림: `extension/package.json`의 `version` + `extension/CHANGELOG.md` 새 항목 | `package.json` 버전 = CHANGELOG 최신 항목 = 태그 |
| 4 | `.vsix` 산출 + 체크섬 | `artifacts\wpf-xaml-live-preview-<버전>.vsix`, `artifacts\SHA256SUMS.txt` |
| 5 | 설치 스모크 통과(위 2번에 포함) | 설치본 + 번들 호스트로 실제 net10 WPF 프로젝트 미리보기 |
| 6 | (권장) 깨끗한 PC 검증 | [05](./05_Clean_Machine_Verification.md) 체크리스트 |
| 7 | 알려진 한계가 CHANGELOG/User_Guide에 정직하게 적혀 있다 | `extension/CHANGELOG.md`의 "알려진 한계" |

## 2. 릴리스 만들기

### 2-1. 버전/태그
```powershell
# extension/package.json 의 version, extension/CHANGELOG.md 를 고친 뒤
git add -A; git commit -m "chore(release): v<버전>"
git push
git tag v<버전>
git push origin v<버전>
```

### 2-2. `.vsix` 만들기
```powershell
cd extension; npm run package      # -> artifacts\wpf-xaml-live-preview-<버전>.vsix + artifacts\SHA256SUMS.txt
```

### 2-3-a. 웹 UI로 릴리스 (도구 설치 불필요)
1. 저장소 → **Releases** → **Draft a new release** → 태그 `v<버전>` 선택.
2. 제목: `WPF XAML Live Preview v<버전>`. 본문에 CHANGELOG 항목과 아래 "설치 방법"을 붙인다.
3. **`.vsix`와 `SHA256SUMS.txt`를 첨부**한다. 첫 공개 버전은 **Set as a pre-release**를 체크한다.
4. 게시 전에 Draft 상태로 두고 파일/본문을 한 번 더 확인한다.

### 2-3-b. GitHub CLI로 릴리스 (`gh` 설치와 `gh auth login` 필요 — 이 PC에는 아직 없다)
```powershell
gh release create v<버전> `
  artifacts\wpf-xaml-live-preview-<버전>.vsix artifacts\SHA256SUMS.txt `
  --title "WPF XAML Live Preview v<버전>" --notes-file <릴리스노트.md> --prerelease --draft
```
`--draft`로 먼저 올리고 웹에서 확인한 뒤 게시한다.

## 3. 사용자에게 안내할 설치 방법 (릴리스 본문에 붙인다)
1. 요구 사항: Windows 10/11 x64, **.NET 10 Desktop Runtime**(없으면 확장이 안내), VS Code 1.90 이상. Visual Studio는 불필요.
2. 릴리스에서 `.vsix`와 `SHA256SUMS.txt`를 내려받는다.
3. 무결성 확인(권장):
   ```powershell
   (Get-FileHash .\wpf-xaml-live-preview-<버전>.vsix -Algorithm SHA256).Hash.ToLower()   # SHA256SUMS.txt의 값과 같아야 한다
   ```
4. 설치: `code --install-extension .\wpf-xaml-live-preview-<버전>.vsix` (또는 확장 뷰 → `...` → **VSIX에서 설치**).
5. `.xaml` 파일에서 명령 팔레트 → **WPF XAML: Open Preview**. 자세한 사용법: `doc/User_Guide.md`.
6. 제거: 확장 뷰에서 제거. 남는 데이터는 VS Code 전역 저장소의 로그 폴더뿐이다.

## 4. 신뢰/보안 안내 (릴리스 본문에 같이 적는다)
- **서명되지 않았다.** `.vsix`와 번들된 `XamlRenderHost.exe`는 코드 서명이 없어서 Windows SmartScreen/백신이 경고할 수 있다. 체크섬으로 파일이 변조되지 않았는지만 확인할 수 있고 게시자를 증명하지는 못한다. 코드 서명 인증서를 구하면 `signtool`로 exe를 서명하고 다시 패키징한다.
- **사용자 코드 실행**: 신뢰한 폴더에서는 프로젝트의 빌드된 DLL을 불러와 컨트롤 생성자 등 사용자 코드를 실행한다(샌드박스 없음). 신뢰하지 않은 폴더에서는 실행하지 않는다.
- **개인정보**: 텔레메트리/네트워크 전송 없음. 호스트는 XAML 본문을 로그에 남기지 않고 길이/위치만 남긴다. 확장은 외부 서버와 통신하지 않는다(`.NET 설치 페이지 열기` 버튼만 브라우저를 연다).
- **라이선스**: GPL-3.0-only. 번들된 `bin/host`에는 .NET 런타임이 들어 있지 않다(프레임워크 종속, 사용자가 설치).

## 5. 나중에: VS Marketplace 게시 (이번에는 하지 않음)
필요한 것: ① 게시자 `phulgrimlab` 계정([Marketplace 게시자 관리](https://marketplace.visualstudio.com/manage))과 ID 일치 ② Azure DevOps Personal Access Token(Marketplace → Manage 권한) ③ 이름 중복 확인.
```powershell
cd artifacts\vsix-stage   # 또는 .vsix 파일 지정
..\..\extension\node_modules\.bin\vsce.cmd publish --packagePath ..\wpf-xaml-live-preview-<버전>.vsix --pat <PAT>
```
공개 게시는 되돌리기 어렵다(버전 철회/비공개 처리만 가능). 첫 게시는 `--pre-release`로 시작하는 것을 권한다. 토큰은 저장소/문서/로그에 남기지 않는다.

## 6. 릴리스 후 확인
- 릴리스 페이지에서 `.vsix`를 새로 내려받아 체크섬을 비교하고, 임시 프로필(`code --user-data-dir <빈폴더> --extensions-dir <빈폴더> --install-extension ...`)에 설치해 미리보기가 되는지 본다.
- 문제가 보고되면 [05](./05_Clean_Machine_Verification.md)의 실행 기록표에 환경과 증상을 남긴다.
