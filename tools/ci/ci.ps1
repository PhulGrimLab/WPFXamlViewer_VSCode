<#
.SYNOPSIS
    로컬/CI 공용 진입점: 호스트 테스트(T1+T2)와 확장 단위 테스트(T3)를 한 번에 실행한다.

.DESCRIPTION
    doc/03_Test_Strategy.md 4절. 순서: 호스트(T1+T2) → 확장 단위(T3) → 확장+실제 호스트(T3R). 통합 테스트(T4/T5)는 VS Code 인스턴스가 필요해 -IncludeIntegration으로만 포함한다
    (M3에서 추가될 `npm run test:integration`).
    패키징(.vsix 생성)과 설치 스모크(I-09)는 -IncludePackage로만 포함한다(dotnet publish + VS Code 설치가 들어 시간이 오래 걸린다).

.PARAMETER IncludeIntegration
    확장 통합 테스트(T4/T5)도 실행한다. 아직 M3 전이라 스크립트가 없으면 건너뛴다고 알린다.

.PARAMETER IncludePackage
    .vsix를 만들고(tools/package/build_vsix.ps1) 임시 VS Code 프로필에 설치해 번들 호스트로 실제 net10 WPF 프로젝트를 미리보기하는
    설치 스모크(npm run test:smoke)를 실행한다.
#>
[CmdletBinding()]
param(
    [switch]$IncludeIntegration,
    [switch]$IncludePackage
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ExtensionDir = Join-Path $RepoRoot "extension"

# 설치 직후 같은 세션에서 PATH가 갱신되지 않은 경우를 대비해 머신/사용자 PATH를 다시 합친다.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "실패: $Title (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    } else {
        Write-Host "통과: $Title" -ForegroundColor Green
    }
}

Invoke-Step "호스트 테스트 (T1+T2)" {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "doc\run_tests.ps1")
}

Invoke-Step "확장 단위 테스트 (T3)" {
    Push-Location $ExtensionDir
    try {
        if (-not (Test-Path (Join-Path $ExtensionDir "node_modules"))) {
            & npm ci
            if ($LASTEXITCODE -ne 0) { return }
        } else {
            # 의존성이 이미 설치되어 있음: 재설치 불필요.
        }
        & npm test
    } finally {
        Pop-Location
    }
}

# 실제 호스트 exe + 실제 HostClient 장애 주입 테스트(M2.4). 위 호스트 테스트 단계가 exe를 빌드해 두었다.
Invoke-Step "확장 + 실제 호스트 테스트 (장애 주입)" {
    Push-Location $ExtensionDir
    try { & npm run test:realhost } finally { Pop-Location }
}

if ($IncludeIntegration) {
    $pkg = Get-Content (Join-Path $ExtensionDir "package.json") -Raw | ConvertFrom-Json
    if ($pkg.scripts.PSObject.Properties.Name -contains "test:integration") {
        Invoke-Step "확장 통합 테스트 (T4/T5)" {
            Push-Location $ExtensionDir
            # VS Code가 진행 메시지를 stderr로 내보낸다. $ErrorActionPreference=Stop이면 PowerShell 5.1이 이를 오류로 보고 중단하므로
            # 이 단계에서만 Continue로 낮추고, 성공 여부는 종료 코드($LASTEXITCODE)로 판단한다.
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try { & npm run test:integration } finally { $ErrorActionPreference = $previousPreference; Pop-Location }
        }
    } else {
        Write-Host ""
        Write-Host "통합 테스트 스크립트(test:integration)가 아직 없습니다 - M3에서 추가 예정. 건너뜀." -ForegroundColor Yellow
    }
}

if ($IncludePackage) {
    Invoke-Step "패키징 (.vsix 생성, 호스트 번들)" {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\package\build_vsix.ps1")
    }
    Invoke-Step "설치 스모크 (I-09: .vsix 설치 + 번들 호스트 + 실제 WPF 프로젝트)" {
        Push-Location $ExtensionDir
        # VS Code가 진행 메시지를 stderr로 내보내므로 이 단계만 오류 처리를 낮추고 종료 코드로 판정한다(통합 테스트와 같은 이유).
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try { & npm run test:smoke } finally { $ErrorActionPreference = $previousPreference; Pop-Location }
    }
} else {
    Write-Host ""
    Write-Host "패키징/설치 스모크는 -IncludePackage 로 실행합니다(건너뜀)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "모든 단계 통과" -ForegroundColor Green
exit 0
