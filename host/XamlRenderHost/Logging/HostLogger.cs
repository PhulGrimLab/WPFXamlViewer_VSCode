using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace XamlRenderHost.Logging;

/// <summary>로그 수준. 값이 클수록 중요하다.</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>로그 한 줄을 어딘가에 쓰는 출력 대상. 구현체는 <see cref="HostLogger"/>의 쓰기 스레드 하나에서만 호출된다.</summary>
public interface ILogSink : IDisposable
{
    void WriteLine(string line);
}

/// <summary>호스트 로그 ID(doc/01 5절). 문자열 값은 로그 검증 테스트와 문서가 그대로 사용한다.</summary>
public static class LogIds
{
    public const string HostStarted = "H001";
    public const string HostStopped = "H002";
    public const string RequestReceived = "H010";
    public const string RenderSucceeded = "H011";
    public const string RenderFailed = "H012";
    public const string PlaceholderUsed = "H013";

    /// <summary>큐가 가득 차 로그를 버렸음을 알리는 내부 로그(doc/01 5절 정책 보강).</summary>
    public const string LogsDropped = "H090";
}

/// <summary>
/// 비동기 큐 + 전용 쓰기 스레드로 로그를 남긴다(CLAUDE.md 규칙 8: 렌더/프로토콜 경로가 파일 I/O를 기다리지 않는다).
///
/// Owner: 호스트 진입점(Program/ProtocolLoop)이 생성하고 Dispose한다. Lifetime: 프로세스 시작~종료.
/// 스레드: 호출 스레드들은 <see cref="Log"/>로 큐에 넣기만 하고, 전용 쓰기 스레드 하나가 sink에 쓴다.
/// 공유자원/동기화: 유계 <see cref="BlockingCollection{T}"/>(내부 동기화), 버린 개수는 Interlocked.
/// 큐가 가득 차면 **호출자를 막지 않고** 새 로그를 버린 뒤 개수를 세어 H090으로 한 줄 남긴다.
/// sink가 null이면 아무것도 하지 않는다(로그 디렉터리를 지정하지 않은 실행).
/// XAML 본문은 로그에 남기지 않는다 — 호출자가 길이/해시만 넘긴다(doc/01 5절).
/// </summary>
public sealed class HostLogger : IDisposable
{
    /// <summary>쓰기 큐 기본 용량(줄 수).</summary>
    public const int DefaultQueueCapacity = 1024;

    /// <summary>종료 시 쓰기 스레드가 큐를 비우길 기다리는 최대 시간.</summary>
    private const int DrainTimeoutMs = 3000;

    private readonly ILogSink? _sink;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string>? _queue;
    private readonly Thread? _writerThread;
    private long _droppedCount;

    public HostLogger(ILogSink? sink, LogLevel minimumLevel, int queueCapacity = DefaultQueueCapacity)
    {
        _sink = sink;
        _minimumLevel = minimumLevel;
        if (sink == null)
        {
            // 로그 비활성: 큐/스레드를 만들지 않는다.
            return;
        }
        else
        {
            _queue = new BlockingCollection<string>(queueCapacity);
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "HostLogger.Writer" };
            _writerThread.Start();
        }
    }

    /// <summary>로그 한 줄을 큐에 넣는다. 최소 수준 미만이거나 로그가 비활성이면 무시. 절대 블록하지 않는다.</summary>
    public void Log(LogLevel level, string id, string message)
    {
        if (_queue == null || level < _minimumLevel)
        {
            return;
        }
        else
        {
            // 기록 대상: 아래로 진행.
        }

        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [{level.ToString().ToUpperInvariant()}] {id} {message.ReplaceLineEndings(" ")}");
        if (_queue.IsAddingCompleted || !_queue.TryAdd(line))
        {
            // 큐가 가득 참(또는 종료 중): 호출자를 막지 않기 위해 버리고 개수만 센다.
            Interlocked.Increment(ref _droppedCount);
        }
        else
        {
            // 큐에 들어감.
        }
    }

    /// <summary>쓰기 스레드 본체: 큐가 완료될 때까지 한 줄씩 sink에 쓰고, 버려진 로그가 있으면 H090으로 알린다.</summary>
    private void WriterLoop()
    {
        foreach (var line in _queue!.GetConsumingEnumerable())
        {
            _sink!.WriteLine(line);
            ReportDroppedIfAny();
        }
        ReportDroppedIfAny();
    }

    private void ReportDroppedIfAny()
    {
        var dropped = Interlocked.Exchange(ref _droppedCount, 0);
        if (dropped > 0)
        {
            _sink!.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [WARN] {LogIds.LogsDropped} dropped={dropped}"));
        }
        else
        {
            // 버려진 로그 없음.
        }
    }

    /// <summary>큐를 비우고(최대 <see cref="DrainTimeoutMs"/>) sink를 닫는다.</summary>
    public void Dispose()
    {
        if (_queue == null)
        {
            return;
        }
        else
        {
            // 아래에서 정리.
        }

        _queue.CompleteAdding();
        if (!_writerThread!.Join(DrainTimeoutMs))
        {
            // sink가 멈춰 있어 시간 내에 비우지 못함: 종료를 막지 않고 포기한다(쓰기 스레드는 백그라운드).
            return;
        }
        else
        {
            _sink!.Dispose();
        }
    }
}
