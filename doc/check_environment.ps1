<#
.SYNOPSIS
    WPF XAML Live Preview 개발 환경을 점검한다 (doc/00_Environment_Setup_Guide.md 2절의 표).

.DESCRIPTION
    빠진 항목은 설치 방법만 안내하고 자동으로 설치하지 않는다
    (대용량 다운로드/관리자 권한이 필요한 작업을 스크립트가 몰래 실행하지 않기 위함).
    모두 만족하면 종료 코드 0, 하나라도 부족하면 1.
#>
[CmdletBinding()]
param()

$script:Failed = $false

function Write-Check([string]$Name, [bool]$Ok, [string]$Detail, [string]$Hint) {
    if ($Ok) {
        Write-Host ("  [OK]   {0,-34} {1}" -f $Name, $Detail) -ForegroundColor Green
    } else {
        Write-Host ("  [빠짐] {0,-34} {1}" -f $Name, $Detail) -ForegroundColor Red
        Write-Host ("         -> {0}" -f $Hint) -ForegroundColor Yellow
        $script:Failed = $true
    }
}

function Get-FirstLine([scriptblock]$Command) {
    try { return (& $Command 2>$null | Select-Object -First 1) } catch { return $null }
}

Write-Host "WPF XAML Live Preview 환경 점검" -ForegroundColor Cyan

# Windows 전용 제품이다(doc/01 1절).
$isWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
Write-Check "Windows" $isWindows ([Environment]::OSVersion.VersionString) "이 도구는 Windows에서만 동작합니다."

# .NET SDK 10.x : 호스트 빌드/테스트
$sdks = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $sdks = @(dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }) }
Write-Check ".NET SDK 10.x" ($sdks.Count -gt 0) ($sdks | Select-Object -First 1) "winget install Microsoft.DotNet.SDK.10"

# .NET 10 Desktop Runtime : 호스트 실행(사용자 환경에서도 필요)
$rts = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $rts = @(dotnet --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' }) }
Write-Check ".NET 10 Desktop Runtime" ($rts.Count -gt 0) ($rts | Select-Object -Last 1) "winget install Microsoft.DotNet.DesktopRuntime.10"

# Node.js / npm : 확장 빌드·테스트·패키징
$node = if (Get-Command node -ErrorAction SilentlyContinue) { Get-FirstLine { node -v } } else { $null }
Write-Check "Node.js" ($null -ne $node) "$node" "winget install OpenJS.NodeJS.LTS  (설치 후 새 터미널)"
$npm = if (Get-Command npm -ErrorAction SilentlyContinue) { Get-FirstLine { npm -v } } else { $null }
Write-Check "npm" ($null -ne $npm) "$npm" "Node.js 설치 시 함께 설치됩니다."

# VS Code CLI
$code = if (Get-Command code -ErrorAction SilentlyContinue) { Get-FirstLine { code --version } } else { $null }
Write-Check "VS Code (code CLI)" ($null -ne $code) "$code" "VS Code 설치 후 PATH에 code 추가"

# git
$git = if (Get-Command git -ErrorAction SilentlyContinue) { Get-FirstLine { git --version } } else { $null }
Write-Check "git" ($null -ne $git) "$git" "winget install Git.Git"

Write-Host ""
if ($script:Failed) {
    Write-Host "부족한 항목이 있습니다. 위 안내를 따라 설치한 뒤 다시 실행하세요." -ForegroundColor Red
    exit 1
} else {
    Write-Host "모든 항목이 준비되었습니다." -ForegroundColor Green
    exit 0
}
