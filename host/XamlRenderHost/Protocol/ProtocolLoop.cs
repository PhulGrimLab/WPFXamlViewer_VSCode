using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using XamlRenderHost.Logging;

namespace XamlRenderHost.Protocol;

/// <summary>
/// stdin/stdout 줄 단위 JSON 루프(doc/01 3.1, 4절 스레드 모델).
///
/// Owner: <c>Program.Main</c>(STA 메인 스레드)이 생성하고 <see cref="Run"/>을 호출한다. Lifetime: 호스트 프로세스 전체.
/// 스레드:
///  - 호출한 STA 메인 스레드: 요청 큐에서 꺼내 <see cref="RequestHandler"/>로 처리(WPF 렌더는 여기서만 한다).
///  - StdinReader(백그라운드): 입력 줄을 읽어 요청 큐에 넣는다. EOF에서 큐를 완료시킨다. 종료 때 ReadLine에 걸려 있을 수 있어 백그라운드로 둔다.
///  - StdoutWriter: 응답 큐에서 꺼내 출력에 쓰고 flush한다. 출력 핸들은 이 스레드만 사용한다.
/// 공유자원: 요청 큐(<see cref="BlockingCollection{T}"/>, 무제한), 응답 큐(유계 — 확장이 읽지 않으면 처리 쪽이 막혀 자연스러운 배압이 된다).
/// stdout은 프로토콜 전용이다. 사람이 읽는 로그는 절대 여기에 쓰지 않는다.
/// </summary>
public sealed class ProtocolLoop
{
    /// <summary>요청 한 줄의 최대 길이(문자). 넘는 줄은 읽어서 버리고 오류 응답을 보낸다(끝없는 줄로 메모리가 차는 것을 막는다). 32M 문자(약 64MB)는 정상 XAML에 충분히 크다.</summary>
    public const int DefaultMaxRequestChars = 32 * 1024 * 1024;

    /// <summary>요청 큐 용량(줄 수). 처리가 밀리면 읽기 스레드가 막혀 입력 파이프가 가득 차고, 보내는 쪽이 자연히 기다린다.</summary>
    private const int RequestQueueCapacity = 8;

    /// <summary>너무 긴 줄을 큐에 알리는 표식(요청 본문으로는 나올 수 없는 NUL 문자로 시작).</summary>
    private const string TooLongMarker = "\0TooLong";

    /// <summary>응답 큐 용량(줄 수).</summary>
    private const int ResponseQueueCapacity = 64;

    /// <summary>종료 시 쓰기 스레드가 응답을 모두 내보내길 기다리는 최대 시간.</summary>
    private const int WriterDrainTimeoutMs = 3000;

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly RequestHandler _handler;
    private readonly HostLogger _logger;

    /// <summary>stdout 쓰기가 끊겼다(확장이 파이프를 닫음). writer 스레드가 설정하고 메인 루프가 읽는다.</summary>
    private volatile bool _outputClosed;

    private readonly int _maxRequestChars;

    public ProtocolLoop(TextReader input, TextWriter output, RequestHandler handler, HostLogger logger, int maxRequestChars = DefaultMaxRequestChars)
    {
        _maxRequestChars = maxRequestChars;
        _input = input;
        _output = output;
        _handler = handler;
        _logger = logger;
    }

    /// <summary>
    /// 입력이 끝나거나 shutdown 요청을 처리할 때까지 요청을 처리한다. 호출 스레드(STA)를 점유한다.
    /// 반환: 종료 사유("shutdown" 또는 "EOF") — H002 로그에 쓰인다.
    /// </summary>
    public string Run()
    {
        var requests = new BlockingCollection<string>(RequestQueueCapacity);
        var responses = new BlockingCollection<string>(ResponseQueueCapacity);

        var reader = new Thread(() => ReadLoop(requests)) { IsBackground = true, Name = "StdinReader" };
        var writer = new Thread(() => WriteLoop(responses)) { Name = "StdoutWriter" };
        reader.Start();
        writer.Start();

        var reason = "EOF";
        foreach (var line in requests.GetConsumingEnumerable())
        {
            if (_outputClosed)
            {
                reason = "stdout closed"; // 응답을 내보낼 곳이 없다: 계속 처리해도 소용없다.
                break;
            }
            else
            {
                // 출력이 살아 있다.
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                // 빈 줄은 무시한다(응답 없음).
                continue;
            }
            else
            {
                // 요청 줄: 처리로 진행.
            }

            var result = line == TooLongMarker
                ? new HandleResult(RequestHandler.RequestTooLargeResponse(_maxRequestChars), false)
                : _handler.Handle(line);
            responses.Add(result.ResponseJson);
            if (result.Shutdown)
            {
                reason = "shutdown";
                break;
            }
            else
            {
                // 계속 처리.
            }
        }

        responses.CompleteAdding();
        if (!writer.Join(WriterDrainTimeoutMs))
        {
            // 확장이 stdout을 읽지 않아 응답이 막혀 있음: 종료를 막지 않는다.
            _logger.Log(LogLevel.Warn, LogIds.HostStopped, "writer did not drain in time");
        }
        else
        {
            // 응답을 모두 내보냄.
        }
        return reason;
    }

    /// <summary>
    /// 한 줄을 읽는다(줄 끝 `\n`, 앞의 `\r`은 제거). 최대 길이를 넘으면 그 줄의 나머지를 읽어 버리고 <see cref="TooLongMarker"/>를 돌려준다(메모리에 쌓지 않는다). EOF면 null.
    /// </summary>
    private string? ReadBoundedLine()
    {
        var builder = new System.Text.StringBuilder();
        var tooLong = false;
        int next;
        while ((next = _input.Read()) >= 0)
        {
            var c = (char)next;
            if (c == '\n')
            {
                break;
            }
            else if (tooLong)
            {
                continue; // 넘친 줄의 나머지: 버린다.
            }
            else if (builder.Length >= _maxRequestChars)
            {
                tooLong = true;
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }
        if (next < 0 && builder.Length == 0 && !tooLong)
        {
            return null; // EOF
        }
        else if (tooLong)
        {
            return TooLongMarker;
        }
        else
        {
            return builder.Length > 0 && builder[^1] == '\r' ? builder.ToString(0, builder.Length - 1) : builder.ToString();
        }
    }

    private void ReadLoop(BlockingCollection<string> requests)
    {
        try
        {
            string? line;
            while ((line = ReadBoundedLine()) != null)
            {
                requests.Add(line);
            }
        }
        catch (IOException)
        {
            // 입력 파이프가 깨짐: EOF와 같게 취급한다.
        }
        finally
        {
            requests.CompleteAdding();
        }
    }

    private void WriteLoop(BlockingCollection<string> responses)
    {
        try
        {
            foreach (var line in responses.GetConsumingEnumerable())
            {
                _output.WriteLine(line);
                _output.Flush();
            }
        }
        catch (IOException)
        {
            // 확장이 파이프를 닫음: 더 쓸 곳이 없으므로 종료한다. 남은 응답은 버려서 큐가 가득 차 메인 루프가 영원히 기다리지 않게 한다.
            _outputClosed = true;
            foreach (var _ in responses.GetConsumingEnumerable())
            {
            }
        }
    }
}
