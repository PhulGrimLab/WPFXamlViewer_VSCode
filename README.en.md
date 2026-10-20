# WPFXamlViewer_VSCode

[한국어](./README.md) · English

**WPF XAML Live Preview** (extension ID `phulgrimlab.wpf-xaml-live-preview`) — see the **real WPF rendering** of a `.xaml` file in a side panel, live, while you edit it in VS Code.

> Purpose: **develop WPF projects, including the UI, in VS Code with the .NET 10 SDK, without Visual Studio.** **Windows only.**
> Usage and limitations: **[doc/User_Guide.en.md](./doc/User_Guide.en.md)** ([한국어](./doc/User_Guide.md)).
>
> Status: **M0–M6 implemented** ([doc/05](./doc/05_Clean_Machine_Verification.md), Korean).
> A drag-and-drop designer and XAML completion are outside the scope of this extension. For the current state and how to continue the work see [doc/04](./doc/04_Progress_Status_And_Next_Steps.md) (Korean).
> Development policy: [CLAUDE.md](./CLAUDE.md).

## Install and use (when you already have a `.vsix`)
1. VS Code Extensions view → `...` → **Install from VSIX** (or `code --install-extension .\wpf-xaml-live-preview-0.1.8.vsix`).
2. The **.NET 10 Desktop Runtime** is required (the extension tells you if it is missing).
3. In a `.xaml` file, run **WPF XAML: Open Preview** from the Command Palette.

If you do not have a `.vsix`, build one yourself with "Build the `.vsix` from source" below. Releases are published on the GitHub Releases page together with `SHA256SUMS.txt`.

## Build the `.vsix` from source (on any Windows PC)

Download the source and **build the `.vsix` with two or three commands.** Neither VS Code nor Visual Studio is required.

### 1) Prerequisites (install once)
| Needed | Version | Install (PowerShell) | Check |
|---|---|---|---|
| Windows | 10/11 x64 | — | — |
| **.NET SDK** | **10 or later** | `winget install Microsoft.DotNet.SDK.10` | `dotnet --list-sdks` shows `10.x` |
| **Node.js (with npm)** | **20 or later** (LTS recommended) | `winget install OpenJS.NodeJS.LTS` | `node -v` |
| Internet | the first time | needed to restore npm/NuGet packages | — |
| git | only to get the source with `git clone` | `winget install Git.Git` | `git --version` |

> After installing, open a **new terminal** so the PATH is refreshed. You can check what is missing with the automatic check:
> `.\doc\check_environment.ps1 -PackagingOnly` (it installs nothing, it only tells you what to do).

### 2) Get the source
```powershell
git clone https://github.com/PhulGrimLab/WPFXamlViewer_VSCode.git
cd WPFXamlViewer_VSCode
```
Without git, use **Code → Download ZIP** on the GitHub repository page and extract it (spaces or non-ASCII characters in the path are fine).
If a ZIP download gives a "cannot run scripts" error, run this once in the extracted folder: `Get-ChildItem -Recurse | Unblock-File`

### 3) Build
```powershell
.\tools\package\build_vsix.ps1
```
(or `cd extension; npm run package`) This single command does everything: prerequisite check → `npm ci` (first time only) → compile the extension → `dotnet publish` the render host → `vsce package`.
The first run downloads packages and takes a few minutes; later runs take about a minute.

**Output** (in the `artifacts\` folder, not committed to git):
- `wpf-xaml-live-preview-<version>.vsix` — the installer (about 0.2 MB, host included)
- `SHA256SUMS.txt` — checksum. Verify with: `(Get-FileHash .\artifacts\*.vsix).Hash`

### 4) Install
```powershell
code --install-extension .\artifacts\wpf-xaml-live-preview-0.1.8.vsix
```
(or VS Code Extensions view → `...` → **Install from VSIX**.) The target PC needs the **.NET 10 Desktop Runtime**.

### If something goes wrong
On failure the script stops with `패키징 실패: ...` (packaging failed) and the cause. Common cases:

| Symptom | Cause → fix |
|---|---|
| `필요한 도구가 부족합니다: ...` (required tools are missing) | Install the missing item from the prerequisites above and run again in a **new terminal**. The install commands in the list can be used as they are |
| `.ps1 ... cannot be run` / `running scripts is disabled` | An execution-policy issue. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package\build_vsix.ps1` (`npm run package` already does this) |
| `npm ci` fails with `ENOTFOUND`/`ETIMEDOUT`/`EPROXY` | Internet/proxy problem. On a corporate network set `npm config set proxy ...` and the NuGet proxy too |
| `dotnet publish` fails with `NU1301` / restore failure | The NuGet server is unreachable (internet/proxy). The first run needs internet |
| `A compatible .NET SDK was not found` / `NETSDK1045` | .NET SDK 10 is missing (the repository's `global.json` requires 10 or later) → install it |
| `npm warn EBADENGINE` warning | Node is 20/21. **Packaging is not affected** (warning only). To run the tests too, use Node 22 or later |
| `이전 스테이징 폴더를 지울 수 없습니다` (cannot delete the previous staging folder) | Another program (Explorer/VS Code/terminal) has `artifacts\vsix-stage` open → close it and run again |
| Korean text in the console looks garbled | Only a terminal-encoding issue; the result is unaffected (`chcp 65001`) |
| The checksum differs from a `.vsix` built on another PC | Normal (the archive contains timestamps). When distributing, use **one built `.vsix` together with its `SHA256SUMS.txt` as a pair** |

## Development (including tests)
```powershell
.\doc\check_environment.ps1        # environment check (.NET SDK 10 / Desktop Runtime / Node 22+ / VS Code / git)
cd extension; npm install; cd ..
.\tools\ci\ci.ps1                  # host + extension unit + real-host fault-injection tests
.\tools\ci\ci.ps1 -IncludeIntegration   # + real VS Code integration tests
.\tools\ci\ci.ps1 -IncludePackage       # + .vsix packaging (host bundled) + install smoke test
```
The tests need VS Code, the .NET 10 Desktop Runtime and Node 22 or later (requirements table: [doc/00](./doc/00_Environment_Setup_Guide.md), Korean).

## Documentation
| Document | Contents |
|---|---|
| [doc/User_Guide.en.md](./doc/User_Guide.en.md) | **User guide (English): development flow, preview features, user controls/trust, conversion rules, limitations, troubleshooting** |
| [doc/User_Guide.md](./doc/User_Guide.md) | The same user guide in Korean |
| [extension/CHANGELOG.md](./extension/CHANGELOG.md) | Changelog (Korean) |
| [doc/04_Progress_Status_And_Next_Steps.md](./doc/04_Progress_Status_And_Next_Steps.md) | Progress, continuing on another PC, unverified items, next work (Korean) |
| [doc/05_Clean_Machine_Verification.md](./doc/05_Clean_Machine_Verification.md) | Checklist for a clean PC without Visual Studio (not yet run) (Korean) |
| [doc/06_Release_Process.md](./doc/06_Release_Process.md) | Release procedure (GitHub Release `.vsix`), install notes, trust/security wording (Korean) |
| [doc/00_Environment_Setup_Guide.md](./doc/00_Environment_Setup_Guide.md) | Development environment requirements and decisions (host is net10 + dotnet, no VS) (Korean) |
| [doc/01_Architecture_And_Requirements.md](./doc/01_Architecture_And_Requirements.md) | Goals/scope, rendering decision, components, protocol, thread model, log design (Korean) |
| [doc/02_Development_Plan.md](./doc/02_Development_Plan.md) | Milestones M0–M6 and results per milestone (Korean) |
| [doc/03_Test_Strategy.md](./doc/03_Test_Strategy.md) | Automated test layers, golden-image rules, test case list, running/CI (Korean) |
