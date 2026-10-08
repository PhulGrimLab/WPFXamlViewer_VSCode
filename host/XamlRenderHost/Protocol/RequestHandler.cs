using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using XamlRenderHost.Logging;
using XamlRenderHost.Projects;
using XamlRenderHost.Rendering;

namespace XamlRenderHost.Protocol;

/// <summary>프로토콜 상수. 문자열 값은 확장(HostClient)과의 계약이므로 바꾸면 <see cref="HostInfo.ProtocolVersion"/>을 올려야 한다.</summary>
public static class ProtocolConstants
{
    public const string MethodPing = "ping";
    public const string MethodRender = "render";
    public const string MethodShutdown = "shutdown";

    /// <summary>장애 주입 전용 메서드(XAMLVIEWER_TEST_HOOKS=1 일 때만 동작, doc/03 5절).</summary>
    public const string MethodDebugHang = "debug.hang";
    public const string MethodDebugCrash = "debug.crash";

    /// <summary>테스트 훅을 켜는 환경 변수 이름.</summary>
    public const string TestHooksEnvVar = "XAMLVIEWER_TEST_HOOKS";

    /// <summary>debug.crash가 사용하는 종료 코드(테스트가 비정상 종료를 구분하기 위한 값).</summary>
    public const int CrashExitCode = 99;
}

/// <summary>프로토콜 수준 오류 코드(렌더 오류 코드는 <see cref="RenderErrorCodes"/>).</summary>
public static class ProtocolErrorCodes
{
    public const string InvalidRequest = "InvalidRequest";
    public const string InvalidParams = "InvalidParams";
    public const string UnknownMethod = "UnknownMethod";
}

/// <summary>요청 하나의 처리 결과: 응답 JSON 한 줄과 "처리 후 종료해야 하는가".</summary>
public readonly record struct HandleResult(string ResponseJson, bool Shutdown);

/// <summary>
/// 요청 한 줄(JSON)을 받아 응답 한 줄(JSON)을 만든다(doc/01 3.1). 입출력 스레드와 무관한 순수 처리 단계라 단위 테스트가 쉽다.
/// Owner: <see cref="ProtocolLoop"/>(또는 테스트). Lifetime: 호스트 프로세스 동안 하나.
/// 스레드: 반드시 WPF STA 메인 스레드에서만 호출된다(렌더가 STA를 요구). 내부 상태는 없고 로거(스레드 안전)만 공유한다.
/// 보안/개인정보: XAML 본문은 로그에 남기지 않고 길이만 남긴다.
/// </summary>
public sealed class RequestHandler
{
    private readonly HostLogger _logger;
    private readonly bool _testHooksEnabled;
    private long _handledCount;

    public RequestHandler(HostLogger logger, bool testHooksEnabled)
    {
        _logger = logger;
        _testHooksEnabled = testHooksEnabled;
    }

    /// <summary>지금까지 처리한 요청 수(H002 로그용).</summary>
    public long HandledCount => Interlocked.Read(ref _handledCount);

    /// <summary>
    /// 요청 한 줄을 처리한다. 입력: JSON 한 줄. 출력: 응답 JSON 한 줄과 종료 여부.
    /// 잘못된 JSON/형식은 예외 대신 오류 응답으로 돌려준다(루프가 죽지 않도록).
    /// </summary>
    public HandleResult Handle(string line)
    {
        Interlocked.Increment(ref _handledCount);

        JsonNode? id = null;
        string method;
        JsonNode? parameters;
        try
        {
            var node = JsonNode.Parse(line) as JsonObject
                ?? throw new JsonException("요청은 JSON 객체여야 합니다.");
            id = node["id"]?.DeepClone();
            method = node["method"]?.GetValue<string>() ?? throw new JsonException("method가 없습니다.");
            parameters = node["params"];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new HandleResult(Error(id, ProtocolErrorCodes.InvalidRequest, ex.Message), false);
        }

        _logger.Log(LogLevel.Debug, LogIds.RequestReceived, $"id={id?.ToJsonString() ?? "null"} method={method} length={line.Length}");

        switch (method)
        {
            case ProtocolConstants.MethodPing:
                return new HandleResult(Ok(id, new JsonObject
                {
                    ["version"] = HostInfo.Version,
                    ["protocol"] = HostInfo.ProtocolVersion,
                    ["pid"] = Environment.ProcessId,
                }), false);

            case ProtocolConstants.MethodShutdown:
                return new HandleResult(Ok(id, new JsonObject()), true);

            case ProtocolConstants.MethodRender:
                return new HandleResult(HandleRender(id, parameters as JsonObject), false);

            case ProtocolConstants.MethodDebugHang when _testHooksEnabled:
                // 장애 주입: 렌더가 끝나지 않는 상황을 흉내 낸다(확장의 타임아웃/kill/재시작 검증용).
                Thread.Sleep(Timeout.Infinite);
                return new HandleResult(Ok(id, new JsonObject()), false);

            case ProtocolConstants.MethodDebugCrash when _testHooksEnabled:
                // 장애 주입: 응답 없이 비정상 종료한다.
                Environment.Exit(ProtocolConstants.CrashExitCode);
                return new HandleResult(Ok(id, new JsonObject()), false);

            default:
                // 테스트 훅이 꺼져 있을 때의 debug.* 도 알 수 없는 메서드로 취급한다.
                return new HandleResult(Error(id, ProtocolErrorCodes.UnknownMethod, $"알 수 없는 method: {method}"), false);
        }
    }

    /// <summary>render 요청: params를 검증하고 렌더해 PNG(base64)와 크기를 돌려준다. 결과 로그는 H011/H012.</summary>
    private string HandleRender(JsonNode? id, JsonObject? p)
    {
        string xaml;
        string? filePath;
        bool allowProjectAssemblies;
        double? width, height;
        double dpi;
        try
        {
            xaml = p?["xaml"]?.GetValue<string>() ?? throw new JsonException("params.xaml(문자열)이 필요합니다.");
            filePath = p["filePath"]?.GetValue<string>();
            allowProjectAssemblies = p["allowProjectAssemblies"]?.GetValue<bool>() ?? false;
            width = p["width"]?.GetValue<double>();
            height = p["height"]?.GetValue<double>();
            dpi = p["dpi"]?.GetValue<double>() ?? XamlRenderer.DefaultDpi;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Error(id, ProtocolErrorCodes.InvalidParams, ex.Message);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = XamlRenderer.Render(new RenderRequest(xaml, width, height, dpi, filePath, allowProjectAssemblies));
            var warnings = result.Warnings ?? Array.Empty<RenderWarning>();
            _logger.Log(LogLevel.Info, LogIds.RenderSucceeded,
                $"id={id?.ToJsonString() ?? "null"} ms={stopwatch.ElapsedMilliseconds} size={result.PixelWidth}x{result.PixelHeight} elements={result.Elements?.Count ?? 0} warnings={warnings.Count}");
            LogPlaceholders(id, warnings);
            LogProjectTier(id, result.Project, warnings);
            return Ok(id, new JsonObject
            {
                ["png"] = Convert.ToBase64String(result.Png),
                ["width"] = result.PixelWidth,
                ["height"] = result.PixelHeight,
                ["elements"] = ToJson(result.Elements),
                ["warnings"] = ToJson(warnings),
                ["project"] = result.Project == null ? null : new JsonObject
                {
                    ["tier"] = result.Project.Tier,
                    ["reason"] = result.Project.Reason,
                },
            });
        }
        catch (XamlRenderException ex)
        {
            _logger.Log(LogLevel.Warn, LogIds.RenderFailed,
                $"id={id?.ToJsonString() ?? "null"} code={ex.Code} line={ex.Line?.ToString() ?? "-"} col={ex.Column?.ToString() ?? "-"}");
            return Error(id, ex.Code, ex.Message, ex.Line, ex.Column);
        }
    }

    /// <summary>H013 로그에 남기는 자리표시자 타입 최대 개수(로그 폭주 방지).</summary>
    private const int MaxPlaceholderLogEntries = 5;

    /// <summary>자리표시자로 대체한 타입을 H013으로 기록한다(최대 <see cref="MaxPlaceholderLogEntries"/>개). 타입 이름만 남기고 본문은 남기지 않는다.</summary>
    private void LogPlaceholders(JsonNode? id, IReadOnlyList<RenderWarning> warnings)
    {
        foreach (var w in warnings.Where(w => w.Code == WarningCodes.PlaceholderUsed).Take(MaxPlaceholderLogEntries))
        {
            _logger.Log(LogLevel.Warn, LogIds.PlaceholderUsed, $"id={id?.ToJsonString() ?? "null"} {w.Message}");
        }
    }

    /// <summary>마지막으로 로그에 남긴 Tier 결정("tier/reason"). 같은 결정을 매 렌더마다 반복해 남기지 않기 위한 상태(STA 스레드에서만 접근).</summary>
    private string _lastTierLogged = string.Empty;

    /// <summary>
    /// Tier 관련 로그: DLL을 (다시) 로드했으면 H020, 로드 실패면 H021, 결정(tier/이유)이 바뀌었으면 H023, 사용자 컨트롤 예외마다 H022.
    /// 경로는 남기되 XAML 본문/예외 메시지 전문은 남기지 않는다(메시지는 길이가 제한된 요약만).
    /// </summary>
    private void LogProjectTier(JsonNode? id, ProjectTierInfo? tier, IReadOnlyList<RenderWarning> warnings)
    {
        if (tier != null)
        {
            if (tier.Reloaded)
            {
                _logger.Log(LogLevel.Info, LogIds.UserAssemblyLoaded, $"assembly={tier.AssemblyName} path={tier.AssemblyPath} ms={tier.LoadMs}");
            }
            else if (tier.Reason == ProjectTierReasons.LoadFailed)
            {
                _logger.Log(LogLevel.Warn, LogIds.UserAssemblyUnavailable, $"assembly={tier.AssemblyName} reason={tier.Detail}");
            }
            else
            {
                // 이미 로드되어 있음 / Tier 0 사유는 H023로 충분.
            }

            var decision = $"{tier.Tier}/{tier.Reason}";
            if (decision != _lastTierLogged)
            {
                _lastTierLogged = decision;
                _logger.Log(LogLevel.Info, LogIds.TierDecided, $"tier={tier.Tier} reason={tier.Reason}");
            }
            else
            {
                // 결정이 그대로: 반복 기록하지 않는다.
            }
        }
        else
        {
            // Tier 정보 없음(호출 경로상 발생하지 않음).
        }

        foreach (var w in warnings.Where(w => w.Code == WarningCodes.UserControlFailed).Take(MaxPlaceholderLogEntries))
        {
            _logger.Log(LogLevel.Warn, LogIds.UserControlFailed, $"id={id?.ToJsonString() ?? "null"} {w.Message}");
        }
    }

    /// <summary>HitMap 요소를 프로토콜의 elements 배열로 바꾼다: 번호/원본 줄·열·끝 + 픽셀 경계(x,y,w,h).</summary>
    private static JsonArray ToJson(IReadOnlyList<HitElement>? elements)
    {
        var array = new JsonArray();
        foreach (var e in elements ?? Array.Empty<HitElement>())
        {
            array.Add(new JsonObject
            {
                ["id"] = $"e{e.Index}",
                ["line"] = e.Line,
                ["col"] = e.Col,
                ["endLine"] = e.EndLine,
                ["endCol"] = e.EndCol,
                ["x"] = e.X,
                ["y"] = e.Y,
                ["w"] = e.Width,
                ["h"] = e.Height,
            });
        }
        return array;
    }

    /// <summary>경고 목록을 프로토콜의 warnings 배열(code/message/line/col)로 바꾼다.</summary>
    private static JsonArray ToJson(IReadOnlyList<RenderWarning> warnings)
    {
        var array = new JsonArray();
        foreach (var w in warnings)
        {
            var item = new JsonObject { ["code"] = w.Code, ["message"] = w.Message };
            if (w.Line.HasValue)
            {
                item["line"] = w.Line.Value;
            }
            else
            {
                // 줄 정보 없음: 생략.
            }
            if (w.Column.HasValue)
            {
                item["col"] = w.Column.Value;
            }
            else
            {
                // 열 정보 없음: 생략.
            }
            array.Add(item);
        }
        return array;
    }

    private static string Ok(JsonNode? id, JsonObject result)
        => new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, string code, string message, int? line = null, int? column = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (line.HasValue)
        {
            error["line"] = line.Value;
        }
        else
        {
            // 줄 정보 없음: 필드를 생략한다.
        }
        if (column.HasValue)
        {
            error["col"] = column.Value;
        }
        else
        {
            // 열 정보 없음: 필드를 생략한다.
        }
        return new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = error }.ToJsonString();
    }
}
