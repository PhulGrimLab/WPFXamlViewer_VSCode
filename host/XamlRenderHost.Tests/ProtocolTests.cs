using System.IO;
using System.Text.Json.Nodes;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// H-P01~P05 및 보강: 실제 `serve` 프로세스와 줄 단위 JSON으로 왕복한다(M2.1).
/// </summary>
[TestClass]
public class ProtocolTests
{
    private const string Ns = RenderTestHelper.PresentationNs;

    private static JsonObject RenderParams(string xaml, int? width = null, int? height = null)
    {
        var p = new JsonObject { ["xaml"] = xaml };
        if (width.HasValue) { p["width"] = width.Value; }
        if (height.HasValue) { p["height"] = height.Value; }
        return p;
    }

    [TestMethod]
    public void P01_Ping_ReturnsVersionProtocolAndPid()
    {
        using var host = new HostProcess();
        host.Send(1, "ping");
        var r = host.ReadResponse();
        Assert.AreEqual(1, (int)r["id"]!);
        Assert.IsTrue((bool)r["ok"]!);
        Assert.AreEqual(HostInfo.Version, (string)r["result"]!["version"]!);
        Assert.AreEqual(HostInfo.ProtocolVersion, (int)r["result"]!["protocol"]!);
        Assert.AreEqual(host.ProcessId, (int)r["result"]!["pid"]!);
    }

    [TestMethod]
    public void P02_Render_ReturnsDecodablePngWithRequestedSize()
    {
        using var host = new HostProcess();
        host.Send(7, "render", RenderParams($"<Rectangle {Ns} Fill=\"Red\"/>", 40, 20));
        var r = host.ReadResponse();
        Assert.IsTrue((bool)r["ok"]!, r.ToJsonString());
        Assert.AreEqual(40, (int)r["result"]!["width"]!);
        Assert.AreEqual(20, (int)r["result"]!["height"]!);
        var elements = r["result"]!["elements"]!.AsArray();
        Assert.HasCount(1, elements, "루트 Rectangle 하나가 HitMap에 있어야 한다");
        Assert.AreEqual("e0", (string)elements[0]!["id"]!);
        Assert.AreEqual(1, (int)elements[0]!["line"]!);
        Assert.AreEqual(1, (int)elements[0]!["col"]!);
        Assert.AreEqual(40.0, (double)elements[0]!["w"]!);
        Assert.AreEqual(20.0, (double)elements[0]!["h"]!);

        var png = Convert.FromBase64String((string)r["result"]!["png"]!);
        Assert.IsTrue(png.Length > 8 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G', "PNG 시그니처가 아님");
    }

    [TestMethod]
    public void P02b_Render_ProtocolResultEqualsDirectRender()
    {
        // 프로토콜을 거쳐도 라이브러리 직접 렌더와 같은 픽셀이어야 한다.
        var xaml = File.ReadAllText(Path.Combine(TestPaths.XamlFixtures, "R09_StyleSetter.xaml"));
        using var host = new HostProcess();
        host.Send(1, "render", RenderParams(xaml));
        var viaHost = Convert.FromBase64String((string)host.ReadResponse()["result"]!["png"]!);
        var direct = RenderTestHelper.Render(xaml).Png;
        var diff = XamlRenderHost.Rendering.StaRunner.Run(() => ImageComparer.Compare(direct, viaHost));
        Assert.IsFalse(diff.SizeMismatch);
        Assert.AreEqual(0, diff.MismatchedPixels);
    }

    [TestMethod]
    public void P03_RenderFailure_ReturnsStructuredErrorWithLineAndColumn()
    {
        using var host = new HostProcess();
        host.Send(2, "render", RenderParams($"<Grid {Ns}>\n  <NoSuchElement/>\n</Grid>"));
        var r = host.ReadResponse();
        Assert.IsFalse((bool)r["ok"]!);
        Assert.AreEqual("XamlParse", (string)r["error"]!["code"]!);
        Assert.AreEqual(2, (int)r["error"]!["line"]!);
        Assert.IsNotNull(r["error"]!["col"]);
    }

    [TestMethod]
    public void P04_InvalidJson_ReturnsInvalidRequestWithNullIdAndKeepsServing()
    {
        using var host = new HostProcess();
        host.SendLine("this is not json");
        var bad = host.ReadResponse();
        Assert.IsFalse((bool)bad["ok"]!);
        Assert.AreEqual("InvalidRequest", (string)bad["error"]!["code"]!);
        Assert.IsNull(bad["id"]);

        // 루프가 살아 있어야 한다.
        host.Send(3, "ping");
        Assert.IsTrue((bool)host.ReadResponse()["ok"]!);
    }

    [TestMethod]
    public void P04b_ParamsAndMethodErrors()
    {
        using var host = new HostProcess();
        host.Send(1, "no.such.method");
        Assert.AreEqual("UnknownMethod", (string)host.ReadResponse()["error"]!["code"]!);

        host.Send(2, "render", new JsonObject()); // xaml 없음
        Assert.AreEqual("InvalidParams", (string)host.ReadResponse()["error"]!["code"]!);

        host.SendLine("{\"id\":3}"); // method 없음
        Assert.AreEqual("InvalidRequest", (string)host.ReadResponse()["error"]!["code"]!);
    }

    [TestMethod]
    public void P04c_BlankLinesAreIgnored()
    {
        using var host = new HostProcess();
        host.SendLine("");
        host.SendLine("   ");
        host.Send(1, "ping");
        Assert.AreEqual(1, (int)host.ReadResponse()["id"]!);
    }

    [TestMethod]
    public void P05_Shutdown_RespondsThenExitsZero()
    {
        using var host = new HostProcess();
        host.Send(9, "shutdown");
        var r = host.ReadResponse();
        Assert.IsTrue((bool)r["ok"]!);
        Assert.AreEqual(0, host.WaitForExit());
    }

    [TestMethod]
    public void P05b_StdinEof_ExitsZero()
    {
        using var host = new HostProcess();
        host.Send(1, "ping");
        host.ReadResponse();
        host.CloseInput();
        Assert.AreEqual(0, host.WaitForExit());
    }

    [TestMethod]
    public void P06_ResponsesKeepRequestOrderAndIds()
    {
        using var host = new HostProcess();
        for (var i = 1; i <= 5; i++)
        {
            host.Send(i, "ping");
        }
        for (var i = 1; i <= 5; i++)
        {
            Assert.AreEqual(i, (int)host.ReadResponse()["id"]!);
        }
    }

    [TestMethod]
    public void P07_DebugMethods_AreUnknownWithoutTestHooks()
    {
        using var host = new HostProcess();
        host.Send(1, "debug.crash");
        var r = host.ReadResponse();
        Assert.AreEqual("UnknownMethod", (string)r["error"]!["code"]!);
    }

    [TestMethod]
    public void P08_DebugCrash_WithTestHooks_ExitsWithCrashCodeAndNoResponse()
    {
        using var host = new HostProcess(env: new Dictionary<string, string> { ["XAMLVIEWER_TEST_HOOKS"] = "1" });
        host.Send(1, "debug.crash");
        Assert.AreEqual(99, host.WaitForExit());
        Assert.IsNull(host.ReadRawLine(), "크래시한 호스트는 응답을 보내면 안 된다");
    }

    [TestMethod]
    public void P09_OnlyJsonLinesOnStdout()
    {
        // stdout은 프로토콜 전용: 여러 요청 뒤에도 모든 줄이 JSON 객체여야 한다.
        using var host = new HostProcess();
        host.Send(1, "ping");
        host.Send(2, "render", RenderParams($"<Grid {Ns} Width=\"10\" Height=\"10\"/>"));
        host.Send(3, "shutdown");
        string? line;
        var count = 0;
        while ((line = host.ReadRawLine()) != null)
        {
            Assert.IsInstanceOfType<JsonObject>(JsonNode.Parse(line));
            count++;
        }
        Assert.AreEqual(3, count);
    }
}
