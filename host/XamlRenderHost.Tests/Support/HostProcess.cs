using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace XamlRenderHost.Tests.Support;

/// <summary>
/// 실제 산출물 `XamlRenderHost.exe serve`를 서브프로세스로 띄워 줄 단위 JSON을 주고받는 테스트 도우미.
/// 확장(HostClient)이 쓰는 경로와 같은 방식이다. Owner: 테스트 메서드(using으로 Dispose). Lifetime: 테스트 하나.
/// Dispose 때 프로세스가 남아 있으면 강제 종료한다.
/// </summary>
public sealed class HostProcess : IDisposable
{
    /// <summary>응답/종료를 기다리는 기본 제한 시간.</summary>
    public const int DefaultTimeoutMs = 20_000;

    private readonly Process _process;

    public HostProcess(IEnumerable<string>? extraArgs = null, IDictionary<string, string>? env = null)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "XamlRenderHost.exe");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false),
        };
        psi.ArgumentList.Add("serve");
        foreach (var a in extraArgs ?? Array.Empty<string>())
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var kv in env ?? new Dictionary<string, string>())
        {
            psi.Environment[kv.Key] = kv.Value;
        }
        _process = Process.Start(psi)!;
    }

    public int ProcessId => _process.Id;

    /// <summary>요청 한 줄을 보낸다.</summary>
    public void SendLine(string line)
    {
        _process.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    /// <summary>JSON 요청을 만들어 보낸다.</summary>
    public void Send(int id, string method, JsonObject? parameters = null)
    {
        var req = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters != null)
        {
            req["params"] = parameters;
        }
        else
        {
            // params 없는 요청.
        }
        SendLine(req.ToJsonString());
    }

    /// <summary>응답 한 줄을 JSON으로 읽는다. 시간 내에 오지 않으면 예외.</summary>
    public JsonObject ReadResponse(int timeoutMs = DefaultTimeoutMs)
    {
        var line = ReadRawLine(timeoutMs) ?? throw new InvalidOperationException("호스트가 응답 없이 stdout을 닫았습니다.");
        return JsonNode.Parse(line)!.AsObject();
    }

    /// <summary>stdout 한 줄을 그대로 읽는다. EOF면 null, 시간 초과면 예외.</summary>
    public string? ReadRawLine(int timeoutMs = DefaultTimeoutMs)
    {
        var task = _process.StandardOutput.ReadLineAsync();
        if (!task.Wait(timeoutMs))
        {
            throw new TimeoutException($"{timeoutMs}ms 안에 응답이 오지 않았습니다.");
        }
        else
        {
            return task.Result;
        }
    }

    /// <summary>stdin을 닫아 EOF를 알린다.</summary>
    public void CloseInput() => _process.StandardInput.Close();

    /// <summary>프로세스가 끝나길 기다려 종료 코드를 돌려준다. 시간 초과면 예외.</summary>
    public int WaitForExit(int timeoutMs = DefaultTimeoutMs)
    {
        if (!_process.WaitForExit(timeoutMs))
        {
            throw new TimeoutException($"{timeoutMs}ms 안에 호스트가 종료되지 않았습니다.");
        }
        else
        {
            return _process.ExitCode;
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }
        else
        {
            // 이미 종료됨.
        }
        _process.Dispose();
    }
}
