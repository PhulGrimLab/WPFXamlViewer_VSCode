using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// M5 HitMap 테스트(H-M01~M05): 렌더된 요소의 경계가 원본 줄/열과 기대 사각형으로 매핑되는지 확인한다.
/// 좌표는 결과 PNG 픽셀 기준(dpi 배율 반영)이며 목록 순서는 선위 순회(부모 → 자식, 나중 형제가 위)다.
/// </summary>
[TestClass]
public class HitMapTests
{
    private const string Ns = RenderTestHelper.PresentationNs;
    private const string XNs = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static IReadOnlyList<HitElement> Map(string xaml, double dpi = XamlRenderer.DefaultDpi)
        => RenderTestHelper.Render(xaml, dpi: dpi).Elements!;

    private static void AssertBounds(HitElement e, double x, double y, double w, double h)
    {
        Assert.AreEqual(x, e.X, 0.01, $"X of line {e.Line}");
        Assert.AreEqual(y, e.Y, 0.01, $"Y of line {e.Line}");
        Assert.AreEqual(w, e.Width, 0.01, $"W of line {e.Line}");
        Assert.AreEqual(h, e.Height, 0.01, $"H of line {e.Line}");
    }

    [TestMethod]
    public void M01_SimpleLayout_MapsEachElementToItsLineAndBounds()
    {
        var xaml = $"<Grid {Ns} Width=\"200\" Height=\"100\">\n"
            + "  <Button Width=\"80\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\"/>\n"
            + "  <Border Width=\"60\" Height=\"40\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Bottom\">\n"
            + "    <Rectangle Fill=\"Red\" Width=\"20\" Height=\"10\"/>\n"
            + "  </Border>\n"
            + "</Grid>";
        var map = Map(xaml);
        Assert.HasCount(4, map, "Grid, Button, Border, TextBlock만(Button/TextBlock 내부 템플릿 요소는 제외)");
        Assert.AreEqual((1, 1), (map[0].Line, map[0].Col));
        AssertBounds(map[0], 0, 0, 200, 100);
        Assert.AreEqual(2, map[1].Line);
        AssertBounds(map[1], 0, 0, 80, 30);
        Assert.AreEqual(3, map[2].Line);
        AssertBounds(map[2], 140, 60, 60, 40);
        Assert.AreEqual(4, map[3].Line);
        Assert.AreEqual(5, map[2].EndLine, "Border의 끝 태그는 5행");
        // Rectangle은 Border 안 가운데(60x40 안의 20x10).
        AssertBounds(map[3], 160, 75, 20, 10);
    }

    [TestMethod]
    public void M02_Nested_ParentComesBeforeChildAndEndsContainChildren()
    {
        var xaml = $"<StackPanel {Ns} Width=\"100\">\n<StackPanel Margin=\"10\">\n<Rectangle Height=\"20\" Fill=\"Red\"/>\n</StackPanel>\n</StackPanel>";
        var map = Map(xaml);
        Assert.HasCount(3, map);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, map.Select(e => e.Line).ToArray());
        // 안쪽 StackPanel은 바깥 안에서 마진 10, 사각형은 그 안.
        AssertBounds(map[1], 10, 10, 80, 20);
        AssertBounds(map[2], 10, 10, 80, 20);
        Assert.IsTrue(map[0].EndLine >= map[1].EndLine && map[1].EndLine >= map[2].Line, "끝 위치가 중첩 구조와 맞아야 한다");
    }

    [TestMethod]
    public void M03_TemplateGeneratedElements_AreAttributedToTheTaggedAncestor()
    {
        var xaml = $"<Grid {Ns}>\n<Button Width=\"90\" Height=\"30\">\n<Button.Template>\n<ControlTemplate TargetType=\"Button\">\n"
            + "<Border Background=\"Gray\"><ContentPresenter/></Border>\n</ControlTemplate>\n</Button.Template>\nOK\n</Button>\n</Grid>";
        var map = Map(xaml);
        Assert.IsTrue(map.Any(e => e.Line == 2 && Math.Abs(e.Width - 90) < 0.01), "Button(2행)이 있어야 한다");
        // 템플릿 정의 안의 Border(5행)는 Uid가 붙어 Button의 시각 트리 안에서 같은 Button 영역으로 나타난다(템플릿 생성 요소 귀속).
        Assert.IsTrue(map.Where(e => e.Line == 5).All(e => Math.Abs(e.Width - 90) < 0.01), "템플릿 Border는 버튼 크기여야 한다");
    }

    [TestMethod]
    public void M04_OverlappingChildren_LaterSiblingComesLaterInList()
    {
        var xaml = $"<Grid {Ns} Width=\"100\" Height=\"100\">\n<Rectangle Width=\"60\" Height=\"60\" Fill=\"Red\"/>\n<Rectangle Width=\"40\" Height=\"40\" Fill=\"Blue\"/>\n</Grid>";
        var map = Map(xaml);
        var firstIndex = map.ToList().FindIndex(e => e.Line == 2);
        var secondIndex = map.ToList().FindIndex(e => e.Line == 3);
        Assert.IsLessThan(secondIndex, firstIndex,"나중 형제가 위에 그려지므로 목록에서도 뒤여야 한다(마지막 일치가 최상단)");
        AssertBounds(map[firstIndex], 20, 20, 60, 60);
        AssertBounds(map[secondIndex], 30, 30, 40, 40);
    }

    [TestMethod]
    public void M05_Dpi_ScalesCoordinatesToPixels()
    {
        var xaml = $"<Grid {Ns} Width=\"100\" Height=\"50\"><Rectangle Width=\"20\" Height=\"10\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Fill=\"Red\"/></Grid>";
        var map = Map(xaml, dpi: 192);
        AssertBounds(map[0], 0, 0, 200, 100);
        AssertBounds(map[1], 0, 0, 40, 20);
    }

    [TestMethod]
    public void M06_WindowRoot_MapsHostBorderToWindowLine()
    {
        var xaml = $"<Window {Ns} {XNs} Width=\"200\" Height=\"100\">\n<Button Width=\"50\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\"/>\n</Window>";
        var map = Map(xaml);
        Assert.AreEqual((1, 1), (map[0].Line, map[0].Col), "Window 요소의 위치");
        AssertBounds(map[0], 0, 0, 200, 100);
        Assert.AreEqual(2, map[1].Line);
    }

    [TestMethod]
    public void M07_PlaceholderAndRemovedAttributes_KeepOriginalPositions()
    {
        var xaml = $"<StackPanel {Ns} {XNs} xmlns:l=\"clr-namespace:Nope;assembly=Nope\" x:Class=\"A.B\">\n"
            + "<Button Click=\"A\" Width=\"40\" Height=\"20\"/>\n<l:Thing Width=\"70\" Height=\"30\"/>\n</StackPanel>";
        var map = Map(xaml);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, map.Select(e => e.Line).ToArray(), "자리표시자도 원본 3행으로 매핑");
        Assert.AreEqual(1, map[2].Col, "열은 '<' 위치(1)");
        AssertBounds(map[2], 0, 20, 70, 30);
    }

    [TestMethod]
    public void M08_ErrorColumns_AreNotShiftedByTagging()
    {
        // 태그(Uid)를 붙이면 같은 줄의 열이 밀린다. 오류는 태그 없는 재파싱으로 원본 열을 보고해야 한다.
        var xaml = $"<Grid {Ns}><Button NoSuchProperty=\"1\"/></Grid>";
        var ex = RenderTestHelper.RenderExpectingFailure(xaml);
        Assert.AreEqual(1, ex.Line);
        var expectedColumn = xaml.IndexOf("NoSuchProperty", StringComparison.Ordinal);
        Assert.IsLessThanOrEqualTo(8, Math.Abs(ex.Column!.Value - expectedColumn),$"열 {ex.Column} 은 원본 위치({expectedColumn}) 근처여야 한다(태그 길이만큼 밀리면 안 됨)");
    }

    [TestMethod]
    public void M09_UserSuppliedUid_IsRespectedAndNotMapped()
    {
        var xaml = $"<Grid {Ns} Width=\"50\" Height=\"50\"><Button Uid=\"mine\" Width=\"20\" Height=\"20\"/></Grid>";
        var map = Map(xaml);
        Assert.HasCount(1, map, "사용자가 Uid를 지정한 요소는 덮어쓰지 않으므로 Grid만 매핑된다");
    }

    [TestMethod]
    public void M10_ManyElements_AreCappedAndReportedWithWarning()
    {
        var body = string.Concat(Enumerable.Repeat("<Rectangle Width=\"1\" Height=\"1\"/>", HitMap.MaxElements + 50));
        var result = RenderTestHelper.Render($"<StackPanel {Ns} Orientation=\"Horizontal\">{body}</StackPanel>");
        Assert.HasCount(HitMap.MaxElements, result.Elements!);
        CollectionAssert.Contains(result.Warnings!.Select(w => w.Code).ToList(), WarningCodes.HitMapTruncated);
    }
}
