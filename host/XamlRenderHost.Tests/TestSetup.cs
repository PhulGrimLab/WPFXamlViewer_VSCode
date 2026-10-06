using System.Windows.Interop;
using System.Windows.Media;

namespace XamlRenderHost.Tests;

/// <summary>
/// 어셈블리 단위 초기화. 골든 이미지의 결정성을 위해 호스트 exe(Program.Main)와 같은 렌더 모드를 쓴다
/// (소프트웨어 렌더링). 어떤 렌더링보다 먼저 설정해야 한다.
/// </summary>
[TestClass]
public static class TestSetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }
}
