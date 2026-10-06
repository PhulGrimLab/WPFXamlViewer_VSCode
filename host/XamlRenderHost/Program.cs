namespace XamlRenderHost;

/// <summary>
/// 진입점. M0 단계에서는 `--version`만 처리한다(렌더/프로토콜 루프는 M1~M2에서 추가).
/// stdout은 프로토콜 전용이므로(doc/01 3.1) 사람이 읽는 출력은 `--version` 같은 진단 명령에서만 쓴다.
/// </summary>
public static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 2;

    /// <summary>
    /// 인자를 해석해 종료 코드를 돌려준다.
    /// 입력: 명령줄 인자. 출력: 종료 코드(0 성공, 2 사용법 오류).
    /// </summary>
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine($"XamlRenderHost {HostInfo.Version} protocol={HostInfo.ProtocolVersion}");
            return ExitOk;
        }
        else
        {
            // 알 수 없는 인자: stderr로만 안내한다(stdout 오염 방지).
            Console.Error.WriteLine("usage: XamlRenderHost --version");
            return ExitUsage;
        }
    }
}
