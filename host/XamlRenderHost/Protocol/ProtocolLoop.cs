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
    /// <summary>응답 큐 용량(줄 수).</summary>
    private const int ResponseQueueCapacity = 64;

    /// <summary>종료 시 쓰기 스레드가 응답을 모두 내보내길 기다리는 최대 시간.</summary>
    private const int WriterDrainTimeoutMs = 3000;

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly RequestHandler _handler;
    private readonly HostLogger _logger;

    public ProtocolLoop(TextReader input, TextWriter output, RequestHandler handler, HostLogger logger)
    {
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
        var requests = new BlockingCollection<string>();
        var responses = new BlockingCollection<string>(ResponseQueueCapacity);

        var reader = new Thread(() => ReadLoop(requests)) { IsBackground = true, Name = "StdinReader" };
        var writer = new Thread(() => WriteLoop(responses)) { Name = "StdoutWriter" };
        reader.Start();
        writer.Start();

        var reason = "EOF";
        foreach (var line in requests.GetConsumingEnumerable())
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // 빈 줄은 무시한다(응답 없음).
                continue;
            }
            else
            {
                // 요청 줄: 처리로 진행.
            }

            var result = _handler.Handle(line);
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

    private void ReadLoop(BlockingCollection<string> requests)
    {
        try
        {
            string? line;
            while ((line = _input.ReadLine()) != null)
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
            // 확장이 파이프를 닫음: 더 쓸 곳이 없으므로 종료한다.
        }
    }
}
