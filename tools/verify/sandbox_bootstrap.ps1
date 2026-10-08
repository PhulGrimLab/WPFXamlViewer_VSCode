<#
.SYNOPSIS
    Windows Sandbox 안에서 "Visual Studio가 없는 깨끗한 Windows"를 재현해 .vsix 설치와 실제 WPF 프로젝트 미리보기를 준비한다(doc/02 M6.4).

.DESCRIPTION
    tools/verify/clean_machine.wsb 가 이 스크립트를 샌드박스 로그온 시 실행한다. 샌드박스에는 .NET도 VS Code도 없다.
    순서: .NET SDK 10 설치(dotnet-install.ps1) → VS Code 사용자 설치 → .vsix 설치 → `dotnet new wpf` 프로젝트 생성/빌드 → VS Code로 폴더 열기.
    마지막 "미리보기가 보이는가" 확인은 사람이 한다(doc/05_Clean_Machine_Verification.md 체크리스트).

    주의: 이 스크립트는 **작성만 했고 실행해 보지 못했다**(개발 PC에서 Windows Sandbox 기능이 꺼져 있고 켜려면 관리자 권한과 재부팅이 필요).
    첫 실행에서 URL/옵션이 어긋날 수 있다. 샌드박스는 인터넷 연결이 필요하다(다운로드). 결과는 doc/05에 기록한다.
#>
$ErrorActionPreference = "Stop"

# 호스트 PC의 저장소 루트가 C:\repo 로 읽기 전용 마운트된다(clean_machine.wsb). 작업 폴더는 샌드박스 안 임시 위치를 쓴다.
$Repo = "C:\repo"
$Work = "C:\work"
New-Item -ItemType Directory -Force $Work | Out-Null

Write-Host "== .NET SDK 10 설치 (Desktop Runtime 포함)" -ForegroundColor Cyan
Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile "$Work\dotnet-install.ps1"
& "$Work\dotnet-install.ps1" -Channel 10.0 -InstallDir "C:\dotnet"
& "$Work\dotnet-install.ps1" -Channel 10.0 -Runtime windowsdesktop -InstallDir "C:\dotnet"
$env:DOTNET_ROOT = "C:\dotnet"
$env:Path = "C:\dotnet;" + $env:Path
& dotnet --list-runtimes

Write-Host "== VS Code 설치(사용자 설치, 무인)" -ForegroundColor Cyan
Invoke-WebRequest "https://update.code.visualstudio.com/latest/win32-x64-user/stable" -OutFile "$Work\VSCodeUserSetup.exe"
Start-Process "$Work\VSCodeUserSetup.exe" -ArgumentList "/VERYSILENT", "/NORESTART", "/MERGETASKS=!runcode" -Wait
$code = Join-Path $env:LOCALAPPDATA "Programs\Microsoft VS Code\bin\code.cmd"

Write-Host "== 확장(.vsix) 설치" -ForegroundColor Cyan
$vsix = Get-ChildItem "$Repo\artifacts\*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
& $code --install-extension $vsix.FullName

Write-Host "== 샘플 WPF 프로젝트 생성/빌드" -ForegroundColor Cyan
& dotnet new wpf -n SampleApp -o "$Work\SampleApp" --framework net10.0
& dotnet build "$Work\SampleApp" -c Debug

Write-Host "== VS Code로 폴더 열기 - 이제 체크리스트(doc/05)를 따라 확인하세요" -ForegroundColor Green
& $code "$Work\SampleApp" "$Work\SampleApp\MainWindow.xaml"
