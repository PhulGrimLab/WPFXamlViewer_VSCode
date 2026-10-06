using System.IO;
using XamlRenderHost.Logging;
using XamlRenderHost.Protocol;
using XamlRenderHost.Rendering;

namespace XamlRenderHost;

/// <summary>
/// 진입점. 지원 명령: `--version`, `render --in a.xaml --out a.png [--width N] [--height N] [--dpi N]`(진단용 CLI),
/// `serve [--log-dir D] [--log-level L]`(확장이 사용하는 stdin/stdout JSON 프로토콜 모드).
/// stdout은 프로토콜 전용이므로(doc/01 3.1) 사람이 읽는 출력은 `--version` 같은 진단 명령에서만 쓰고,
/// 오류/사용법은 stderr로만 쓴다.
/// </summary>
public static class Program
{
    private const int ExitOk = 0;
    private const int ExitRenderFailed = 1;
    private const int ExitUsage = 2;

    private const string UsageText =
        "usage: XamlRenderHost --version | render --in <file.xaml> --out <file.png> [--width N] [--height N] [--dpi N] | serve [--log-dir <dir>] [--log-level Debug|Info|Warn|Error]";

    /// <summary>
    /// 인자를 해석해 종료 코드를 돌려준다. WPF 렌더링을 위해 STA 스레드에서 실행된다.
    /// 입력: 명령줄 인자. 출력: 종료 코드(0 성공, 1 렌더 실패, 2 사용법 오류).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // 결정성(골든 이미지 전제, doc/01 3.2): GPU 대신 소프트웨어 렌더링. 어떤 렌더링보다 먼저 설정해야 한다.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine($"XamlRenderHost {HostInfo.Version} protocol={HostInfo.ProtocolVersion}");
            return ExitOk;
        }
        else if (args.Length > 0 && args[0] == "render")
        {
            return RunRenderCommand(args);
        }
        else if (args.Length > 0 && args[0] == "serve")
        {
            return RunServeCommand(args);
        }
        else
        {
            // 알 수 없는 인자: stderr로만 안내한다(stdout 오염 방지).
            Console.Error.WriteLine(UsageText);
            return ExitUsage;
        }
    }

    /// <summary>
    /// `serve` 명령: stdin/stdout 줄 단위 JSON 프로토콜 루프를 실행한다(확장이 이 모드로 호스트를 띄운다).
    /// 옵션: `--log-dir &lt;폴더&gt;`(없으면 파일 로그 없음), `--log-level Debug|Info|Warn|Error`(기본 Info).
    /// 종료 코드: 정상 종료(shutdown/EOF) 0, 사용법 오류 2.
    /// </summary>
    private static int RunServeCommand(string[] args)
    {
        var options = ParseOptions(args.Skip(1).ToArray());
        if (options == null)
        {
            Console.Error.WriteLine(UsageText);
            return ExitUsage;
        }
        else
        {
            // 옵션 형식 정상.
        }

        var level = LogLevel.Info;
        if (options.TryGetValue("--log-level", out var levelText) && !Enum.TryParse(levelText, ignoreCase: true, out level))
        {
            Console.Error.WriteLine($"알 수 없는 --log-level: {levelText}");
            return ExitUsage;
        }
        else
        {
            // 지정하지 않았거나 올바른 값.
        }

        ILogSink? sink = options.TryGetValue("--log-dir", out var logDir) ? new FileLogSink(logDir) : null;
        using var logger = new HostLogger(sink, level);
        var testHooks = Environment.GetEnvironmentVariable(ProtocolConstants.TestHooksEnvVar) == "1";
        logger.Log(LogLevel.Info, LogIds.HostStarted,
            $"version={HostInfo.Version} protocol={HostInfo.ProtocolVersion} pid={Environment.ProcessId} clr={Environment.Version} level={level} testHooks={testHooks}");

        // stdout은 프로토콜 전용: BOM 없는 UTF-8, 줄바꿈은 \n 고정.
        using var input = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
        using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        var handler = new RequestHandler(logger, testHooks);
        var reason = new ProtocolLoop(input, output, handler, logger).Run();

        logger.Log(LogLevel.Info, LogIds.HostStopped, $"reason={reason} requests={handler.HandledCount}");
        return ExitOk;
    }

    /// <summary>`render` 명령: 파일을 읽어 렌더하고 PNG 파일로 저장한다. 실패 시 한 줄 오류를 stderr에 쓴다.</summary>
    private static int RunRenderCommand(string[] args)
    {
        var options = ParseOptions(args.Skip(1).ToArray());
        if (options == null || !options.TryGetValue("--in", out var inPath) || !options.TryGetValue("--out", out var outPath))
        {
            Console.Error.WriteLine(UsageText);
            return ExitUsage;
        }
        else
        {
            // 필수 인자 확인 완료.
        }

        try
        {
            var request = new RenderRequest(
                File.ReadAllText(inPath),
                TryGetDouble(options, "--width"),
                TryGetDouble(options, "--height"),
                TryGetDouble(options, "--dpi") ?? XamlRenderer.DefaultDpi);
            var result = XamlRenderer.Render(request);
            File.WriteAllBytes(outPath, result.Png);
            return ExitOk;
        }
        catch (XamlRenderException ex)
        {
            Console.Error.WriteLine($"ERROR {ex.Code} line={ex.Line?.ToString() ?? "-"} col={ex.Column?.ToString() ?? "-"} {ex.Message}");
            return ExitRenderFailed;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ERROR IoFailed line=- col=- {ex.Message}");
            return ExitRenderFailed;
        }
    }

    /// <summary>`--key value` 쌍을 사전으로 만든다. 값이 빠졌거나 `--`로 시작하지 않는 토큰이 있으면 null.</summary>
    private static Dictionary<string, string>? ParseOptions(string[] tokens)
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < tokens.Length; i += 2)
        {
            if (!tokens[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= tokens.Length)
            {
                return null;
            }
            else
            {
                map[tokens[i]] = tokens[i + 1];
            }
        }
        return map;
    }

    private static double? TryGetDouble(Dictionary<string, string> options, string key)
    {
        if (options.TryGetValue(key, out var text)
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        else
        {
            return null;
        }
    }
}
