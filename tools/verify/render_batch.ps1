param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$ProjectDir,
    [Parameter(Mandatory)][string]$OutCsv,
    [int]$TimeoutMs = 60000,
    [int]$Max = 400,
    [switch]$Untrusted
)
# 한 호스트 프로세스에 프로젝트의 .xaml 파일들을 차례로 렌더 요청하고 결과(ok/오류/tier/경고)를 CSV로 남긴다. 호스트가 죽으면 다시 띄운다.
$allow = -not $Untrusted
$files = Get-ChildItem $ProjectDir -Recurse -Filter *.xaml -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Select-Object -First $Max
$rows = New-Object System.Collections.Generic.List[object]

function Start-Host {
    $psi = New-Object Diagnostics.ProcessStartInfo $Exe
    $psi.Arguments = 'serve'
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false; $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $p = [Diagnostics.Process]::Start($psi)
    $null = $p.StandardError.ReadToEndAsync()
    return $p
}

$p = Start-Host
$id = 0
foreach ($f in $files) {
    $id++
    $xaml = [IO.File]::ReadAllText($f.FullName)
    $req = @{ id = $id; method = 'render'; params = @{ xaml = [string]$xaml; filePath = [string]$f.FullName; allowProjectAssemblies = $allow } } | ConvertTo-Json -Depth 5 -Compress
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $status = ''; $detail = ''; $tier = ''; $warn = 0; $codes = ''
    try {
        $p.StandardInput.WriteLine($req); $p.StandardInput.Flush()
        $task = $p.StandardOutput.ReadLineAsync()
        if ($task.Wait($TimeoutMs)) {
            $j = $task.Result | ConvertFrom-Json
            if ($j.ok) {
                $status = 'OK'; $tier = $j.result.project.tier
                $warn = @($j.result.warnings).Count
                $codes = (@($j.result.warnings) | Group-Object code | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ';'
            } else {
                $status = 'ERROR'; $detail = "$($j.error.code): $($j.error.message)" -replace '[\r\n]+', ' '
            }
        } else {
            $status = 'TIMEOUT'
            try { $p.Kill() } catch {}
            $p = Start-Host
        }
    } catch {
        $status = 'HOSTDEAD'; $detail = $_.Exception.Message
        $p = Start-Host
    }
    $rows.Add([pscustomobject]@{
        File = $f.FullName.Substring($ProjectDir.Length).TrimStart('\'); Status = $status; Tier = $tier
        Ms = $sw.ElapsedMilliseconds; Warnings = $warn; WarnCodes = $codes; Detail = $detail
    })
}
try { $p.StandardInput.WriteLine('{"id":999999,"method":"shutdown"}'); $p.WaitForExit(5000) | Out-Null } catch {}
$rows | Export-Csv $OutCsv -NoTypeInformation -Encoding UTF8
$rows | Group-Object Status | ForEach-Object { "{0}: {1}" -f $_.Name, $_.Count }
