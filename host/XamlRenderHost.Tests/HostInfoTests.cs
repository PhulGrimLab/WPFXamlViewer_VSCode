using System.Diagnostics;
using System.IO; // WPF 프로젝트는 암시적 using에서 System.IO가 제외된다.
using System.Reflection;

namespace XamlRenderHost.Tests;

/// <summary>
/// M0 스모크 테스트: 호스트 어셈블리 버전과 HostInfo 상수의 일치, 실제 exe의 `--version` 동작.
/// </summary>
[TestClass]
public class HostInfoTests
{
    private const int ProcessTimeoutMs = 10_000;

    /// <summary>HostInfo.Version이 csproj Version(어셈블리 정보 버전)과 어긋나면 배포 때 혼동되므로 일치를 강제한다.</summary>
    [TestMethod]
    public void Version_MatchesAssemblyVersion()
    {
        var info = typeof(HostInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.StartsWith(HostInfo.Version, info);
    }

    /// <summary>실제 산출물 exe를 서브프로세스로 실행해 `--version` 출력과 종료 코드를 확인한다(실사용 경로 검증).</summary>
    [TestMethod]
    public void Exe_VersionFlag_PrintsVersionAndExitsZero()
    {
        var (exit, stdout, _) = RunHost("--version");
        Assert.AreEqual(0, exit);
        Assert.AreEqual($"XamlRenderHost {HostInfo.Version} protocol={HostInfo.ProtocolVersion}", stdout.Trim());
    }

    /// <summary>알 수 없는 인자는 종료 코드 2이고 stdout에는 아무것도 쓰지 않는다(stdout은 프로토콜 전용).</summary>
    [TestMethod]
    public void Exe_UnknownArgument_ExitsWithUsageCodeAndKeepsStdoutEmpty()
    {
        var (exit, stdout, stderr) = RunHost("--bogus");
        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, stdout);
        Assert.Contains("usage", stderr);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunHost(string arg)
    {
        // 테스트 프로젝트가 ProjectReference로 호스트를 빌드하므로 exe는 테스트 출력 폴더에 함께 복사된다.
        var exe = Path.Combine(AppContext.BaseDirectory, "XamlRenderHost.exe");
        Assert.IsTrue(File.Exists(exe), $"호스트 exe 없음: {exe}");
        var psi = new ProcessStartInfo(exe, arg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        Assert.IsTrue(p.WaitForExit(ProcessTimeoutMs), "호스트 프로세스가 시간 내에 종료되지 않음");
        return (p.ExitCode, stdout, stderr);
    }
}
