using XamlRenderHost.Rendering;

namespace XamlRenderHost.Tests.Support;

/// <summary>렌더 테스트 공통 도우미: STA 실행과 자주 쓰는 XAML 조각.</summary>
public static class RenderTestHelper
{
    /// <summary>WPF 기본 네임스페이스 선언(루트 요소에 붙여 쓴다).</summary>
    public const string PresentationNs = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    /// <summary>STA 스레드에서 렌더한다. 실패 시 XamlRenderException이 그대로 전파된다.</summary>
    public static RenderResult Render(string xaml, double? width = null, double? height = null, double dpi = XamlRenderer.DefaultDpi)
        => StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml, width, height, dpi)));

    /// <summary>렌더가 XamlRenderException으로 실패하길 기대하고 그 예외를 돌려준다. 성공하면 단언 실패.</summary>
    public static XamlRenderException RenderExpectingFailure(string xaml)
    {
        try
        {
            Render(xaml);
        }
        catch (XamlRenderException ex)
        {
            return ex;
        }
        throw new AssertFailedException("렌더가 실패해야 하는데 성공했습니다.");
    }
}
