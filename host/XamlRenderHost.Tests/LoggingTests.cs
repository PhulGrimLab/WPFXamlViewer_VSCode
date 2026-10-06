using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using XamlRenderHost.Logging;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>H-L01~L04: 호스트 로그(doc/01 5절) 검증 — 로그 ID 순서, 본문 미기록, 큐 포화 시 비차단, 회전(M2.2).</summary>
[TestClass]
public class LoggingTests
{
    private const string Ns = RenderTestHelper.PresentationNs;

    /// <summary>테스트용 sink: 줄을 메모리에 모으고, 게이트가 열릴 때까지 쓰기를 막을 수 있다.</summary>
    private sealed class GatedSink : ILogSink
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public readonly ManualResetEventSlim Gate = new(initialState: true);
        public void WriteLine(string line) { Gate.Wait(); Lines.Enqueue(line); }
        public void Dispose() { }
    }

    private static string NewLogDir() => Path.Combine(Path.GetTempPath(), "xrh-log-" + Guid.NewGuid().ToString("N"));

    /// <summary>serve로 ping/성공 렌더/실패 렌더를 수행하고 shutdown한 뒤 host.log 내용을 돌려준다.</summary>
    private static string RunScenarioAndReadLog(string logDir, string level, string okXaml, string badXaml)
    {
        using (var host = new HostProcess(new[] { "--log-dir", logDir, "--log-level", level }))
        {
            host.Send(1, "ping"); host.ReadResponse();
            host.Send(2, "render", new JsonObject { ["xaml"] = okXaml }); host.ReadResponse();
            host.Send(3, "render", new JsonObject { ["xaml"] = badXaml }); host.ReadResponse();
            host.Send(4, "shutdown"); host.ReadResponse();
            host.WaitForExit();
        }
        return File.ReadAllText(Path.Combine(logDir, "host.log"));
    }

    [TestMethod]
    public void L01_InfoLevel_LogsStartRenderOkRenderFailStopInOrder()
    {
        var dir = NewLogDir();
        try
        {
            var log = RunScenarioAndReadLog(dir, "Info",
                $"<Grid {Ns} Width=\"10\" Height=\"10\"/>", $"<Grid {Ns}><NoSuch/></Grid>");
            var ids = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split(' ')[2]).ToList();
            CollectionAssert.AreEqual(new[] { "H001", "H011", "H012", "H002" }, ids, log);
            StringAssert.Contains(log, "reason=shutdown");
            StringAssert.Contains(log, "code=XamlParse");
            StringAssert.Contains(log, "size=10x10");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void L01b_DebugLevel_AlsoLogsRequestReceived()
    {
        var dir = NewLogDir();
        try
        {
            var log = RunScenarioAndReadLog(dir, "Debug", $"<Grid {Ns}/>", $"<Grid {Ns}><NoSuch/></Grid>");
            Assert.AreEqual(4, log.Split('\n').Count(l => l.Contains(" H010 ")), "요청 4개 모두 H010이어야 한다");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void L02_XamlBodyIsNeverWrittenToLog()
    {
        var dir = NewLogDir();
        try
        {
            const string secret = "SECRET_MARKER_31337";
            var log = RunScenarioAndReadLog(dir, "Debug",
                $"<TextBlock {Ns} Text=\"{secret}\"/>", $"<Grid {Ns}><{secret}/></Grid>");
            Assert.DoesNotContain(secret, log, "XAML 본문(요소/텍스트)이 로그에 남으면 안 된다");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void L03_FullQueue_DoesNotBlockCallers_AndReportsDroppedCount()
    {
        var sink = new GatedSink();
        sink.Gate.Reset(); // 쓰기 스레드를 막아 큐를 가득 채운다.
        const int capacity = 8;
        const int calls = 10_000;
        using (var logger = new HostLogger(sink, LogLevel.Debug, capacity))
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < calls; i++)
            {
                logger.Log(LogLevel.Info, "T001", "line " + i);
            }
            sw.Stop();
            Assert.IsLessThan(2000, sw.ElapsedMilliseconds, "로그 호출이 막혔다");

            sink.Gate.Set(); // 풀어 주면 쓰기 스레드가 큐를 비우고 버린 개수를 H090으로 알린다.
        }
        Assert.IsTrue(sink.Lines.Any(l => l.Contains(" H090 dropped=")), "버려진 로그 개수(H090)가 기록되어야 한다");
        Assert.IsLessThanOrEqualTo(capacity + 1, sink.Lines.Count(l => l.Contains(" T001 ")), "큐 용량을 크게 넘는 줄이 기록됨");
    }

    [TestMethod]
    public void L03b_BelowMinimumLevel_IsIgnored_AndNullSinkIsHarmless()
    {
        var sink = new GatedSink();
        using (var logger = new HostLogger(sink, LogLevel.Warn))
        {
            logger.Log(LogLevel.Info, "T002", "ignored");
            logger.Log(LogLevel.Warn, "T003", "kept");
        }
        Assert.HasCount(1, sink.Lines);
        Assert.Contains("T003", sink.Lines.Single());

        using var disabled = new HostLogger(null, LogLevel.Debug);
        disabled.Log(LogLevel.Error, "T004", "no sink, no crash");
    }

    [TestMethod]
    public void L04_Rotation_KeepsAtMostMaxFilesAndNewestInHostLog()
    {
        var dir = NewLogDir();
        try
        {
            const int maxFiles = 3;
            using (var sink = new FileLogSink(dir, "host", maxBytes: 200, maxFiles: maxFiles))
            {
                for (var i = 0; i < 100; i++)
                {
                    sink.WriteLine($"line-{i:D3} " + new string('x', 30));
                }
            }
            var files = Directory.GetFiles(dir, "host*.log");
            Assert.HasCount(maxFiles, files, string.Join(", ", files.Select(Path.GetFileName)));
            Assert.Contains("line-099", File.ReadAllText(Path.Combine(dir, "host.log")), "가장 최신 줄은 host.log에 있어야 한다");
            Assert.IsTrue(files.All(f => new FileInfo(f).Length <= 200 + 64), "회전 한도를 크게 넘는 파일이 있다");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
