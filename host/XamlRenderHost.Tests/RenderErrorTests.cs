using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// H-E01~E08: 잘못된 XAML이 코드/줄/열을 가진 구조화 오류로 변환되는지 확인한다(M1.4).
/// 줄 번호는 1-base이며, 오류를 일으킨 요소/속성이 있는 줄이어야 한다.
/// </summary>
[TestClass]
public class RenderErrorTests
{
    private const string Ns = RenderTestHelper.PresentationNs;

    [TestMethod]
    public void E01_UnclosedTag_IsXmlMalformedAtClosingLine()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns}>\n  <Button>\n</Grid>");
        Assert.AreEqual(RenderErrorCodes.XmlMalformed, ex.Code);
        Assert.AreEqual(3, ex.Line);
    }

    [TestMethod]
    public void E02_UnknownElement_IsXamlParseAtThatLine()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns}>\n  <NoSuchElement/>\n</Grid>");
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
        Assert.AreEqual(2, ex.Line);
    }

    [TestMethod]
    public void E03_UnknownAttribute_IsXamlParseAtThatLine()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns}>\n  <Button NoSuchProperty=\"1\"/>\n</Grid>");
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
        Assert.AreEqual(2, ex.Line);
    }

    [TestMethod]
    public void E04_InvalidValue_IsXamlParseAtThatLine()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns}>\n  <Button Width=\"abc\"/>\n</Grid>");
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
        Assert.AreEqual(2, ex.Line);
    }

    [TestMethod]
    public void E05_MissingNamespace_IsReportedWithAnErrorCode()
    {
        // 네임스페이스가 없으면 Grid가 WPF 요소로 해석되지 않는다. 어떤 범주로 분류되든 코드와 줄은 있어야 한다.
        var ex = RenderTestHelper.RenderExpectingFailure("<Grid>\n  <Button/>\n</Grid>");
        Assert.IsTrue(ex.Code is RenderErrorCodes.XamlParse or RenderErrorCodes.XmlMalformed, $"예상 밖 코드: {ex.Code}");
        Assert.IsNotNull(ex.Line);
    }

    [TestMethod]
    public void E06_EmptyInput_IsEmptyInput()
    {
        Assert.AreEqual(RenderErrorCodes.EmptyInput, RenderTestHelper.RenderExpectingFailure("").Code);
        Assert.AreEqual(RenderErrorCodes.EmptyInput, RenderTestHelper.RenderExpectingFailure("  \n\t ").Code);
    }

    [TestMethod]
    public void E07_NonXamlText_IsXmlMalformedAtFirstLine()
    {
        var ex = RenderTestHelper.RenderExpectingFailure("hello world");
        Assert.AreEqual(RenderErrorCodes.XmlMalformed, ex.Code);
        Assert.AreEqual(1, ex.Line);
    }

    [TestMethod]
    public void E08_TwoRoots_IsXmlMalformedAtSecondRoot()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns}/>\n<Grid {Ns}/>");
        Assert.AreEqual(RenderErrorCodes.XmlMalformed, ex.Code);
        Assert.AreEqual(2, ex.Line);
    }

    [TestMethod]
    public void NonElementRoot_IsUnsupported()
    {
        // FrameworkElement도 Window도 아닌 객체(브러시)가 루트면 렌더할 수 없다. (Window 루트는 M4.5부터 지원한다.)
        var ex = RenderTestHelper.RenderExpectingFailure($"<SolidColorBrush {Ns} Color=\"Red\"/>");
        Assert.AreEqual(RenderErrorCodes.UnsupportedRoot, ex.Code);
    }
}
