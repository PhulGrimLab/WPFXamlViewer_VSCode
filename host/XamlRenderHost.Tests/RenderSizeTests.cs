using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// H-S01: 크기 결정 규칙(doc/02 1.2) — 차원별로 ① 루트 Width/Height 명시 → ② d:DesignWidth/Height → ③ 요청 크기 → ④ 콘텐츠 크기.
/// 렌더 결과의 픽셀 크기로 어느 규칙이 적용됐는지 확인한다.
/// </summary>
[TestClass]
public class RenderSizeTests
{
    private const string Ns = RenderTestHelper.PresentationNs;
    private const string DesignNs =
        "xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"d\"";

    [TestMethod]
    public void ExplicitSize_WinsOverDesignAndRequest()
    {
        var r = RenderTestHelper.Render($"<Grid {Ns} {DesignNs} Width=\"100\" Height=\"50\" d:DesignWidth=\"300\" d:DesignHeight=\"120\"/>", 400, 400);
        Assert.AreEqual((100, 50), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void DesignSize_WinsOverRequest()
    {
        var r = RenderTestHelper.Render($"<Grid {Ns} {DesignNs} d:DesignWidth=\"300\" d:DesignHeight=\"120\"/>", 400, 400);
        Assert.AreEqual((300, 120), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void RequestSize_UsedWhenNoExplicitOrDesign()
    {
        var r = RenderTestHelper.Render($"<Grid {Ns}/>", 200, 80);
        Assert.AreEqual((200, 80), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void ContentSize_UsedWhenNothingElseGiven()
    {
        var r = RenderTestHelper.Render($"<StackPanel {Ns}><Rectangle Width=\"64\" Height=\"32\" Fill=\"Red\"/></StackPanel>");
        Assert.AreEqual((64, 32), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void Dimensions_AreResolvedIndependently()
    {
        // 폭은 루트에 명시(100), 높이는 명시/디자인이 없으므로 요청 크기(70)를 쓴다.
        var r = RenderTestHelper.Render($"<Grid {Ns} Width=\"100\"/>", 400, 70);
        Assert.AreEqual((100, 70), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void Dpi_ScalesPixelSizeButNotLayout()
    {
        var r = RenderTestHelper.Render($"<Grid {Ns} Width=\"100\" Height=\"50\"/>", dpi: 192);
        Assert.AreEqual((200, 100), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void EmptyContent_ProducesMinimumOnePixelImage()
    {
        var r = RenderTestHelper.Render($"<Grid {Ns}/>");
        Assert.AreEqual((1, 1), (r.PixelWidth, r.PixelHeight));
    }

    [TestMethod]
    public void OverMaxSize_FailsWithTooLarge()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns} Width=\"{XamlRenderer.MaxPixelsPerSide + 1}\" Height=\"10\"/>");
        Assert.AreEqual(RenderErrorCodes.TooLarge, ex.Code);
    }
}
