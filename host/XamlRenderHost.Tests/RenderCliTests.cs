using System.Diagnostics;
using System.IO;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// `XamlRenderHost render --in --out` 을 실제 산출물 exe로 실행해 검증한다(M1.1, 실사용 경로).
/// 같은 XAML을 라이브러리로 직접 렌더한 결과와 CLI가 만든 PNG가 같아야 한다.
/// </summary>
[TestClass]
public class RenderCliTests
{
    private const int ProcessTimeoutMs = 30_000;

    private static (int ExitCode, string StdOut, string StdErr) RunHost(params string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "XamlRenderHost.exe");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        Assert.IsTrue(p.WaitForExit(ProcessTimeoutMs), "호스트 프로세스가 시간 내에 종료되지 않음");
        return (p.ExitCode, stdout, stderr);
    }

    private static string TempFile(string ext) => Path.Combine(Path.GetTempPath(), $"xrh-{Guid.NewGuid():N}{ext}");

    [TestMethod]
    public void Render_ValidXaml_WritesPngMatchingLibraryResult()
    {
        var input = Path.Combine(TestPaths.XamlFixtures, "R07_Border.xaml");
        var output = TempFile(".png");
        try
        {
            var (exit, stdout, stderr) = RunHost("render", "--in", input, "--out", output);
            Assert.AreEqual(0, exit, stderr);
            Assert.AreEqual(string.Empty, stdout, "stdout은 프로토콜 전용이므로 비어 있어야 한다");

            var fromCli = File.ReadAllBytes(output);
            var diff = XamlRenderHost.Rendering.StaRunner.Run(() =>
                ImageComparer.Compare(fromCli, RenderTestHelper.Render(File.ReadAllText(input)).Png));
            Assert.IsFalse(diff.SizeMismatch);
            Assert.AreEqual(0, diff.MismatchedPixels);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [TestMethod]
    public void Render_InvalidXaml_ExitsOneWithStructuredErrorOnStderr()
    {
        var input = TempFile(".xaml");
        var output = TempFile(".png");
        File.WriteAllText(input, $"<Grid {RenderTestHelper.PresentationNs}>\n  <NoSuchElement/>\n</Grid>");
        try
        {
            var (exit, stdout, stderr) = RunHost("render", "--in", input, "--out", output);
            Assert.AreEqual(1, exit);
            Assert.AreEqual(string.Empty, stdout);
            Assert.StartsWith("ERROR XamlParse line=2", stderr);
            Assert.IsFalse(File.Exists(output), "실패 시 출력 파일을 만들면 안 된다");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [TestMethod]
    public void Render_MissingInputFile_ExitsOneWithIoError()
    {
        var (exit, _, stderr) = RunHost("render", "--in", TempFile(".xaml"), "--out", TempFile(".png"));
        Assert.AreEqual(1, exit);
        Assert.StartsWith("ERROR IoFailed", stderr);
    }

    [TestMethod]
    public void Render_MissingRequiredArgument_ExitsWithUsageCode()
    {
        var (exit, stdout, stderr) = RunHost("render", "--in", "x.xaml");
        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, stdout);
        Assert.Contains("usage", stderr);
    }
}
