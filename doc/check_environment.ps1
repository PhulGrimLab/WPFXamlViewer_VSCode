<#
.SYNOPSIS
    WPF XAML Live Preview 개발 환경을 점검한다 (doc/00_Environment_Setup_Guide.md 2절의 표).

.DESCRIPTION
    빠진 항목은 설치 방법만 안내하고 자동으로 설치하지 않는다
    (대용량 다운로드/관리자 권한이 필요한 작업을 스크립트가 몰래 실행하지 않기 위함).
    모두 만족하면 종료 코드 0, 하나라도 부족하면 1.

    -PackagingOnly : ".vsix만 만들 때" 필요한 것(Windows, .NET SDK 10, Node 20+, npm)만 필수로 본다.
                     Desktop Runtime / VS Code / git 은 없어도 통과한다(있으면 표시만). 테스트까지 하려면 옵션 없이 실행한다.
#>
[CmdletBinding()]
param(
    [switch]$PackagingOnly
)

$script:Failed = $false

function Write-Check([string]$Name, [bool]$Ok, [string]$Detail, [string]$Hint, [bool]$Optional = $false) {
    if ($Ok) {
        Write-Host ("  [OK]   {0,-34} {1}" -f $Name, $Detail) -ForegroundColor Green
    } elseif ($Optional) {
        # 이 모드에서는 필수가 아니다: 안내만 하고 실패로 세지 않는다.
        Write-Host ("  [선택] {0,-34} {1}" -f $Name, "없음 (이 모드에서는 필요 없음)") -ForegroundColor DarkYellow
        Write-Host ("         -> 필요하면: {0}" -f $Hint) -ForegroundColor DarkGray
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
Write-Check ".NET 10 Desktop Runtime" ($rts.Count -gt 0) ($rts | Select-Object -Last 1) "winget install Microsoft.DotNet.DesktopRuntime.10" $PackagingOnly

# Node.js / npm : 확장 빌드·테스트·패키징
$node = if (Get-Command node -ErrorAction SilentlyContinue) { Get-FirstLine { node -v } } else { $null }
# 패키징(.vsix)은 Node 20 이상(@vscode/vsce 3.x), 테스트까지 하려면 Node 22 이상(@vscode/test-electron)을 권장한다.
$nodeMajor = 0
if ($node) { $nodeMajor = [int](($node.TrimStart('v') -split '\.')[0]) }
$nodeNeeded = if ($PackagingOnly) { 20 } else { 22 }
Write-Check "Node.js (>= $nodeNeeded)" ($null -ne $node -and $nodeMajor -ge $nodeNeeded) "$node" "winget install OpenJS.NodeJS.LTS  (설치 후 새 터미널)"
$npm = if (Get-Command npm -ErrorAction SilentlyContinue) { Get-FirstLine { npm -v } } else { $null }
Write-Check "npm" ($null -ne $npm) "$npm" "Node.js 설치 시 함께 설치됩니다."

# VS Code CLI
$code = if (Get-Command code -ErrorAction SilentlyContinue) { Get-FirstLine { code --version } } else { $null }
Write-Check "VS Code (code CLI)" ($null -ne $code) "$code" "VS Code 설치 후 PATH에 code 추가" $PackagingOnly

# git
$git = if (Get-Command git -ErrorAction SilentlyContinue) { Get-FirstLine { git --version } } else { $null }
Write-Check "git" ($null -ne $git) "$git" "winget install Git.Git" $PackagingOnly

Write-Host ""
if ($script:Failed) {
    Write-Host "부족한 항목이 있습니다. 위 안내를 따라 설치한 뒤 다시 실행하세요." -ForegroundColor Red
    exit 1
} else {
    Write-Host "모든 항목이 준비되었습니다." -ForegroundColor Green
    exit 0
}
