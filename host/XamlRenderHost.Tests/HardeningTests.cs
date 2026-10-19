using System.IO;
using XamlRenderHost.Logging;
using XamlRenderHost.Projects;
using XamlRenderHost.Protocol;
using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>2차 보안 점검에서 보류했던 낮은 위험 항목의 개선(H-S): 로그 회전 실패, 요청 줄 길이 상한, 임시 복사 제외, 의존 DLL 변경 감지.</summary>
[TestClass]
public class HardeningTests
{
    [TestMethod]
    public void S01_LogRotationFailure_DoesNotDropLines()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var sink = new FileLogSink(directory, maxBytes: 200, maxFiles: 3);
            sink.WriteLine("first line");
            // 다른 창의 호스트가 host.log를 잡고 있는 상황: 삭제/이동을 허용하지 않는 핸들(FileShare.Delete 없음)이 있으면 회전(Move)이 실패한다.
            using var holder = new FileStream(Path.Combine(directory, "host.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            for (var i = 0; i < 30; i++)
            {
                sink.WriteLine($"line-{i:D2} ................................................");
            }
            sink.Dispose();
            var text = File.ReadAllText(Path.Combine(directory, "host.log"));
            StringAssert.Contains(text, "line-29", "회전에 실패해도 마지막 줄까지 현재 파일에 남아야 한다");
            StringAssert.Contains(text, "line-00");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static List<string> RunLoop(string input, int maxRequestChars)
    {
        using var logger = new HostLogger(null, LogLevel.Error);
        var output = new StringWriter { NewLine = "\n" };
        var loop = new ProtocolLoop(new StringReader(input), output, new RequestHandler(logger, testHooksEnabled: false), logger, maxRequestChars);
        loop.Run();
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    [TestMethod]
    public void S02_TooLongRequestLine_IsRejected_AndNextRequestStillWorks()
    {
        var tooLong = new string('x', 5000);
        var lines = RunLoop($"{tooLong}\n{{\"id\":7,\"method\":\"ping\"}}\n", maxRequestChars: 1000);
        Assert.HasCount(2, lines);
        StringAssert.Contains(lines[0], "\"ok\":false");
        StringAssert.Contains(lines[0], "InvalidRequest");
        StringAssert.Contains(lines[0], "exceeds 1000");
        StringAssert.Contains(lines[1], "\"id\":7");
        StringAssert.Contains(lines[1], "\"ok\":true");
    }

    [TestMethod]
    public void S03_NormalAndCrLfRequestLines_AreStillHandled()
    {
        var lines = RunLoop("{\"id\":1,\"method\":\"ping\"}\r\n{\"id\":2,\"method\":\"ping\"}", maxRequestChars: 1000);
        Assert.HasCount(2, lines);
        StringAssert.Contains(lines[0], "\"id\":1");
        StringAssert.Contains(lines[1], "\"id\":2"); // 마지막 줄은 줄바꿈 없이 끝나도 처리한다.
    }

    [TestMethod]
    public void S04_TempCopy_NeverCopiesSymbolsOrKeyMaterial()
    {
        var method = typeof(UserAssemblies).GetMethod("ShouldSkipCopy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        bool Skips(string name) => (bool)method.Invoke(null, [@"C:\out", Path.Combine(@"C:\out", name)])!;
        foreach (var name in new[] { "App.pdb", "cert.pfx", "key.p12", "id.pem", "private.key", "strong.snk" })
        {
            Assert.IsTrue(Skips(name), name);
        }
        foreach (var name in new[] { "App.dll", "App.deps.json", "App.runtimeconfig.json", "appsettings.json", "native.dll" })
        {
            Assert.IsFalse(Skips(name), name);
        }
    }

    [TestMethod]
    public void S05_ChangedDependencyDll_IsDetectedAsReload()
    {
        // 주 DLL은 그대로인데 출력 폴더의 다른 DLL만 다시 빌드된 경우도 "바뀜"으로 본다.
        using var project = UserProjectFixture.Create();
        var xaml = $"<StackPanel {RenderTestHelper.PresentationNs} xmlns:s=\"clr-namespace:SampleControls;assembly=SampleControls\"><s:RedBox/></StackPanel>";
        File.WriteAllText(project.XamlPath, xaml);
        StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml, FilePath: project.XamlPath, AllowProjectAssemblies: true)));
        var artifact = ProjectLocator.Find(project.XamlPath).Artifact!;
        Assert.IsFalse(UserAssemblies.Instance.NeedsReload(artifact), "아무것도 바뀌지 않았다");

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project.DllPath)!, "Dependency.Library.dll"), "new build of a dependency");
        Assert.IsTrue(UserAssemblies.Instance.NeedsReload(artifact), "의존 DLL이 추가/변경되면 다시 불러와야 한다");
    }
}
