<#
.SYNOPSIS
    소스에서 확장 설치 파일(.vsix)을 만든다(호스트 번들 포함). 산출물: artifacts/wpf-xaml-live-preview-<버전>.vsix + artifacts/SHA256SUMS.txt

.DESCRIPTION
    "소스를 내려받은 어떤 Windows PC에서든" 이 스크립트 하나로 .vsix를 만들 수 있게 하는 것이 목표다(doc/02 M6, README "소스에서 .vsix 만들기").
    VS Code와 Visual Studio는 필요 없다. 필요한 것: Windows, .NET SDK 10, Node.js 20 이상(npm 포함), 인터넷(처음 한 번 npm/NuGet 복원).

    순서: 전제 조건 점검 → 확장 의존성 설치(없을 때만 npm ci) → 확장 컴파일(src만) → 스테이징 복사 → 호스트 publish → vsce package → SHA256.
    개발 트리(extension/)를 건드리지 않도록 artifacts/vsix-stage 에서 패키징한다(개발 트리에 bin/host 가 생기면 테스트가 낡은 번들 호스트를 집어 갈 수 있다).
    저장소 안 어디서 실행해도(현재 폴더와 무관하게), 경로에 공백/한글이 있어도 동작한다.

    실행 정책 때문에 .ps1 실행이 막히면:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\package\build_vsix.ps1

.PARAMETER SkipCompile
    확장 컴파일을 건너뛴다(이미 out/src 가 최신일 때).

.PARAMETER SkipInstall
    npm ci 를 건너뛴다(의존성이 이미 설치되어 있고 확실할 때).
#>
[CmdletBinding()]
param(
    [switch]$SkipCompile,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ExtensionDir = Join-Path $RepoRoot "extension"
$HostProject = Join-Path $RepoRoot "host\XamlRenderHost\XamlRenderHost.csproj"
$Artifacts = Join-Path $RepoRoot "artifacts"
$Stage = Join-Path $Artifacts "vsix-stage"

# 요구 버전(README/doc 00과 같은 값).
$MinDotNetMajor = 10
$MinNodeMajor = 20   # @vscode/vsce 3.x 가 Node 20 이상을 요구한다.

# 설치 직후 같은 세션에서 PATH가 갱신되지 않은 경우를 대비해 머신/사용자 PATH를 다시 합친다.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

function Write-Step([string]$Text) { Write-Host "== $Text" -ForegroundColor Cyan }

function Invoke-Native([string]$Title, [scriptblock]$Action) {
    Write-Step $Title
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "'$Title' 단계가 실패했습니다 (종료 코드 $LASTEXITCODE). 위 출력의 첫 오류 메시지를 확인하세요."
    }
}

<#
  전제 조건을 모두 점검하고, 부족한 항목을 **한 번에** 알려 준다(하나 고치고 다시 실행하며 하나씩 발견하는 일을 막는다).
  자동 설치는 하지 않는다(관리자 권한/대용량 다운로드를 몰래 실행하지 않기 위해). 부족하면 예외를 던진다.
#>
function Assert-Prerequisites {
    Write-Step "전제 조건 점검"
    $missing = New-Object System.Collections.Generic.List[string]

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $missing.Add("Windows 가 아닙니다. 렌더 호스트가 WPF(Windows 전용)라 이 확장은 Windows 에서만 만들 수 있습니다.")
    }

    # .NET SDK: global.json(repo 루트)이 10 이상을 요구한다.
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $sdkOk = $false
    if ($dotnet) {
        $sdks = @(& dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] })
        $majors = @($sdks | ForEach-Object { [int](($_ -split '\.')[0]) })
        $sdkOk = @($majors | Where-Object { $_ -ge $MinDotNetMajor }).Count -gt 0
        Write-Host ("  .NET SDK : {0}" -f ($(if ($sdks.Count -gt 0) { $sdks -join ', ' } else { '(없음)' })))
    }
    if (-not $sdkOk) {
        $missing.Add(".NET SDK $MinDotNetMajor 이상이 없습니다.  설치: winget install Microsoft.DotNet.SDK.10   (또는 https://dotnet.microsoft.com/download/dotnet/10.0)  설치 후 새 터미널에서 다시 실행하세요.")
    }

    # Node.js / npm
    $node = Get-Command node -ErrorAction SilentlyContinue
    $nodeOk = $false
    if ($node) {
        $nodeVersion = (& node -v 2>$null).Trim()
        $nodeMajor = [int](($nodeVersion.TrimStart('v') -split '\.')[0])
        $nodeOk = $nodeMajor -ge $MinNodeMajor
        Write-Host ("  Node.js  : {0}" -f $nodeVersion)
    }
    if (-not $nodeOk) {
        $missing.Add("Node.js $MinNodeMajor 이상이 없습니다(LTS 권장).  설치: winget install OpenJS.NodeJS.LTS   (또는 https://nodejs.org)  설치 후 새 터미널에서 다시 실행하세요.")
    }
    if ($node -and -not (Get-Command npm -ErrorAction SilentlyContinue)) {
        $missing.Add("npm 을 찾을 수 없습니다. Node.js 를 다시 설치하세요(npm 이 함께 설치됩니다).")
    }

    if ($missing.Count -gt 0) {
        $list = ($missing | ForEach-Object { "  - $_" }) -join "`n"
        throw "필요한 도구가 부족합니다:`n$list"
    }
}

try {
    Assert-Prerequisites

    $package = Get-Content (Join-Path $ExtensionDir "package.json") -Raw | ConvertFrom-Json
    $vsixPath = Join-Path $Artifacts ("{0}-{1}.vsix" -f $package.name, $package.version)

    # 스테이징 폴더 초기화. 이전 실행의 파일을 다른 프로그램(VS Code 등)이 잡고 있으면 삭제가 실패할 수 있다.
    if (Test-Path $Stage) {
        try { Remove-Item $Stage -Recurse -Force }
        catch { throw "이전 스테이징 폴더를 지울 수 없습니다: $Stage`n그 폴더를 열고 있는 프로그램(탐색기/VS Code/터미널)을 닫고 다시 실행하세요. ($($_.Exception.Message))" }
    }
    New-Item -ItemType Directory -Force $Stage | Out-Null

    # 확장 의존성(npm ci). 빌드에 필요한 것: typescript(컴파일), @vscode/vsce(패키징). 없을 때만 설치한다(새 PC/새 클론).
    $needInstall = (-not (Test-Path (Join-Path $ExtensionDir "node_modules\.bin\vsce.cmd"))) `
        -or (-not (Test-Path (Join-Path $ExtensionDir "node_modules\.bin\tsc.cmd")))
    if ($needInstall -and -not $SkipInstall) {
        Invoke-Native "확장 의존성 설치 (npm ci)" { Push-Location $ExtensionDir; try { & npm ci } finally { Pop-Location } }
    }
    elseif ($needInstall) {
        throw "의존성이 설치되어 있지 않은데 -SkipInstall 이 지정되었습니다. extension 폴더에서 'npm ci' 를 먼저 실행하세요."
    }

    if (-not $SkipCompile) {
        # 패키징에는 확장 본체(src)만 필요하다. 테스트 코드는 컴파일하지 않는다(개발 도구 의존성 감소).
        Invoke-Native "확장 컴파일 (src)" { Push-Location $ExtensionDir; try { & npm run compile:build } finally { Pop-Location } }
    }
    if (-not (Test-Path (Join-Path $ExtensionDir "out\src\extension.js"))) {
        throw "컴파일 결과(extension/out/src/extension.js)가 없습니다. -SkipCompile 없이 다시 실행하세요."
    }

    Write-Step "스테이징 복사"
    Copy-Item (Join-Path $ExtensionDir "package.json") $Stage
    Copy-Item (Join-Path $ExtensionDir ".vscodeignore") $Stage
    Copy-Item (Join-Path $ExtensionDir "README.md") $Stage
    Copy-Item (Join-Path $ExtensionDir "CHANGELOG.md") $Stage
    Copy-Item (Join-Path $ExtensionDir "icon.png") $Stage
    Copy-Item (Join-Path $RepoRoot "LICENSE") $Stage
    New-Item -ItemType Directory -Force (Join-Path $Stage "out") | Out-Null
    Copy-Item (Join-Path $ExtensionDir "out\src") (Join-Path $Stage "out\src") -Recurse

    # 처음 한 번 NuGet 복원(인터넷)이 필요할 수 있다. 회사 프록시 환경이면 NuGet/npm 프록시 설정을 확인한다.
    Invoke-Native "호스트 publish (Release, win-x64, 프레임워크 종속)" {
        & dotnet publish $HostProject -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $Stage "bin\host") --nologo -v q
    }
    if (-not (Test-Path (Join-Path $Stage "bin\host\XamlRenderHost.exe"))) {
        throw "호스트 publish 결과에 XamlRenderHost.exe 가 없습니다."
    }

    Invoke-Native "vsce package" {
        Push-Location $Stage
        try { & (Join-Path $ExtensionDir "node_modules\.bin\vsce.cmd") package --no-dependencies --out $vsixPath } finally { Pop-Location }
    }

    # 배포 확인용 체크섬: 내려받은 사람이 `Get-FileHash`로 같은 값인지 비교할 수 있게 같은 폴더에 SHA256SUMS.txt를 만든다.
    $hash = (Get-FileHash $vsixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sumsPath = Join-Path $Artifacts "SHA256SUMS.txt"
    Set-Content -Path $sumsPath -Value ("{0}  {1}" -f $hash, (Split-Path -Leaf $vsixPath)) -Encoding ascii

    $size = [math]::Round((Get-Item $vsixPath).Length / 1MB, 2)
    Write-Host ""
    Write-Host "생성 완료: $vsixPath ($size MB)" -ForegroundColor Green
    Write-Host "SHA256   : $hash" -ForegroundColor Green
    Write-Host ""
    Write-Host "설치해 보기:  code --install-extension `"$vsixPath`"" -ForegroundColor Yellow
    Write-Output $vsixPath
}
catch {
    Write-Host ""
    Write-Host "패키징 실패: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "문제 해결 표: README.md 의 '소스에서 .vsix 만들기' 절" -ForegroundColor Yellow
    exit 1
}
