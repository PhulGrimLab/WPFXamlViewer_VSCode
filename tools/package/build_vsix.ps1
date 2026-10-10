<#
.SYNOPSIS
    확장을 .vsix로 패키징한다(호스트를 번들). 산출물: artifacts/wpf-xaml-viewer-<버전>.vsix

.DESCRIPTION
    doc/02 M6.1/6.2. 개발 트리(extension/)를 건드리지 않도록 artifacts/vsix-stage 에 필요한 파일만 모아 거기서 패키징한다.
    (개발 트리에 bin/host 가 생기면 테스트가 개발 빌드 대신 번들 호스트를 집어 갈 수 있다 - hostLocator는 번들 위치를 먼저 본다.)
    순서: 확장 컴파일 → 스테이징 복사 → 호스트 publish(Release, win-x64, 프레임워크 종속) → vsce package.
    .NET 10 Desktop Runtime 이 사용자 PC에 필요하다(doc/00 5절). 확장은 호스트가 못 뜨면 설치 안내를 보여 준다.

.PARAMETER SkipCompile
    확장 컴파일을 건너뛴다(이미 out/ 이 최신일 때).
#>
[CmdletBinding()]
param(
    [switch]$SkipCompile
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ExtensionDir = Join-Path $RepoRoot "extension"
$HostProject = Join-Path $RepoRoot "host\XamlRenderHost\XamlRenderHost.csproj"
$Artifacts = Join-Path $RepoRoot "artifacts"
$Stage = Join-Path $Artifacts "vsix-stage"

# 설치 직후 같은 세션에서 PATH가 갱신되지 않은 경우를 대비해 머신/사용자 PATH를 다시 합친다.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

function Invoke-Native([string]$Title, [scriptblock]$Action) {
    Write-Host "== $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "실패: $Title (exit $LASTEXITCODE)"
    }
}

$package = Get-Content (Join-Path $ExtensionDir "package.json") -Raw | ConvertFrom-Json
$vsixPath = Join-Path $Artifacts ("{0}-{1}.vsix" -f $package.name, $package.version)

if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force $Stage | Out-Null

# 확장 의존성(개발 도구 포함)이 없으면 컴파일/패키징이 "Cannot find module" 로 실패한다(새 PC/새 클론에서 흔함). 먼저 설치한다.
# 컴파일은 테스트 코드까지 포함하므로 @vscode/test-electron, vsce 가 모두 있어야 한다.
$needInstall = -not (Test-Path (Join-Path $ExtensionDir "node_modules\@vscode\test-electron")) `
    -or -not (Test-Path (Join-Path $ExtensionDir "node_modules\.bin\vsce.cmd"))
if ($needInstall) {
    Invoke-Native "확장 의존성 설치 (npm ci)" { Push-Location $ExtensionDir; try { & npm ci } finally { Pop-Location } }
}

if (-not $SkipCompile) {
    Invoke-Native "확장 컴파일" { Push-Location $ExtensionDir; try { & npm run compile } finally { Pop-Location } }
}

Write-Host "== 스테이징 복사" -ForegroundColor Cyan
Copy-Item (Join-Path $ExtensionDir "package.json") $Stage
Copy-Item (Join-Path $ExtensionDir ".vscodeignore") $Stage
Copy-Item (Join-Path $ExtensionDir "README.md") $Stage
Copy-Item (Join-Path $ExtensionDir "CHANGELOG.md") $Stage
Copy-Item (Join-Path $ExtensionDir "icon.png") $Stage
Copy-Item (Join-Path $RepoRoot "LICENSE") $Stage
New-Item -ItemType Directory -Force (Join-Path $Stage "out") | Out-Null
Copy-Item (Join-Path $ExtensionDir "out\src") (Join-Path $Stage "out\src") -Recurse

Invoke-Native "호스트 publish (Release, win-x64, 프레임워크 종속)" {
    & dotnet publish $HostProject -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $Stage "bin\host") --nologo -v q
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
Write-Host "생성됨: $vsixPath ($size MB)" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
Write-Output $vsixPath
