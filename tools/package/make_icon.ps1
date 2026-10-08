<#
.SYNOPSIS
    확장 아이콘(extension/icon.png)을 tools/package/icon.xaml 에서 이 프로젝트의 렌더 호스트로 다시 만든다.

.DESCRIPTION
    아이콘 원본을 고쳤을 때만 실행한다(결과 PNG는 저장소에 커밋되어 있다). 호스트(Debug)가 먼저 빌드되어 있어야 한다.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$HostExe = Join-Path $RepoRoot "host\XamlRenderHost\bin\Debug\net10.0-windows\win-x64\XamlRenderHost.exe"
$Source = Join-Path $RepoRoot "tools\package\icon.xaml"
$Output = Join-Path $RepoRoot "extension\icon.png"

if (-not (Test-Path $HostExe)) {
    throw "호스트가 없습니다: $HostExe  (먼저 dotnet build host\XamlRenderHost.slnx)"
}
& $HostExe render --in $Source --out $Output
if ($LASTEXITCODE -ne 0) {
    throw "아이콘 렌더 실패 (exit $LASTEXITCODE)"
}
Write-Host "생성됨: $Output" -ForegroundColor Green
