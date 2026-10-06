<#
.SYNOPSIS
    렌더 호스트(host/)의 복원-빌드-테스트(T1+T2)를 한 번에 실행한다.

.DESCRIPTION
    1) 이전에 남은 XamlRenderHost.exe 프로세스를 정리한다(파일 잠금으로 빌드가 실패하는 것을 방지).
    2) `dotnet test`로 복원/빌드/테스트를 실행한다. Visual Studio는 필요 없다(doc/00 3절).
    3) 결과(.trx)는 TestResults/ 에 저장한다.

.PARAMETER SkipBuild
    복원/빌드를 건너뛰고 이미 빌드된 결과로 테스트만 실행한다.

.EXAMPLE
    .\run_tests.ps1
.EXAMPLE
    .\run_tests.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot "host\XamlRenderHost.slnx"
$ResultsDir = Join-Path $RepoRoot "TestResults"

function Stop-LeftoverProcesses {
    Get-Process -Name "XamlRenderHost" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  - 남은 프로세스 종료: $($_.ProcessName) (PID $($_.Id))" -ForegroundColor DarkYellow
        try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch { Write-Host "    종료 실패(무시): $_" -ForegroundColor DarkYellow }
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI를 찾을 수 없습니다. .NET SDK 10을 설치하세요 (doc/00_Environment_Setup_Guide.md)."
}

Write-Host "[1/2] 남은 호스트 프로세스 정리" -ForegroundColor Cyan
Stop-LeftoverProcesses

Write-Host "[2/2] 호스트 테스트 실행 (T1+T2)" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$testArgs = @("test", $Solution, "--results-directory", $ResultsDir, "--logger", "trx;LogFileName=host-tests.trx")
if ($SkipBuild) {
    $testArgs += "--no-build"
} else {
    # 빌드 포함 실행: 별도 인자 없음 (dotnet test가 복원/빌드를 수행).
}
& dotnet @testArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "호스트 테스트 실패 (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "호스트 테스트 통과" -ForegroundColor Green
