using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// 골든 비교기 자체의 검증. 비교기가 항상 "같다"고 답하면 골든 테스트 전체가 무의미해지므로,
/// 다른 이미지를 실제로 걸러내는지 확인한다.
/// </summary>
[TestClass]
public class ImageComparerTests
{
    private const string Ns = RenderTestHelper.PresentationNs;

    private static byte[] RenderRect(string fill, int size = 20)
        => RenderTestHelper.Render($"<Rectangle {Ns} Width=\"{size}\" Height=\"{size}\" Fill=\"{fill}\"/>").Png;

    [TestMethod]
    public void SameImage_HasNoMismatch()
    {
        var png = RenderRect("Red");
        var diff = StaRunner.Run(() => ImageComparer.Compare(png, png));
        Assert.IsFalse(diff.SizeMismatch);
        Assert.AreEqual(0, diff.MismatchedPixels);
    }

    [TestMethod]
    public void DifferentColor_IsDetectedAcrossWholeImage()
    {
        var diff = StaRunner.Run(() => ImageComparer.Compare(RenderRect("Red"), RenderRect("Blue")));
        Assert.IsFalse(diff.SizeMismatch);
        Assert.AreEqual(diff.TotalPixels, diff.MismatchedPixels);
        Assert.IsNotNull(diff.DiffPng);
    }

    [TestMethod]
    public void DifferentSize_IsReportedAsSizeMismatch()
    {
        var diff = StaRunner.Run(() => ImageComparer.Compare(RenderRect("Red", 20), RenderRect("Red", 21)));
        Assert.IsTrue(diff.SizeMismatch);
        Assert.AreEqual(1.0, diff.Ratio);
    }

    [TestMethod]
    public void SmallChannelDifference_WithinToleranceIsIgnored()
    {
        // #FF0000 vs #FE0000: 차이 1 — 기본 허용오차(2) 이내이므로 같은 이미지로 본다.
        var diff = StaRunner.Run(() => ImageComparer.Compare(RenderRect("#FF0000"), RenderRect("#FE0000")));
        Assert.AreEqual(0, diff.MismatchedPixels);
    }
}
