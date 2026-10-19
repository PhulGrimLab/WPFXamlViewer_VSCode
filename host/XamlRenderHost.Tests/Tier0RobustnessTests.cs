using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// 프로젝트 DLL을 불러오지 않은(미신뢰/빌드 전) 렌더에서, 만들 수 없는 프로젝트/라이브러리 타입을 참조하는 흔한 XAML 패턴 하나 때문에 미리보기 전체가 실패하지 않는지 확인한다(H-R0).
/// 각 패턴은 실제 앱에서 자주 쓰이는 형태다. 문서는 프로젝트(.csproj)에 속하게 만들어 "프로젝트 문맥"의 처리가 적용되게 한다.
/// </summary>
[TestClass]
public class Tier0RobustnessTests
{
    private const string Head = "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" "
        + "xmlns:local=\"clr-namespace:Nope.App\" xmlns:i=\"http://schemas.microsoft.com/xaml/behaviors\" xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\" Width=\"200\">";

    private const string CsProj = "<Project Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\"/>";

    private static RenderResult RenderInProject(string body)
        => PreprocessorTests.RenderWithFiles(new() { ["P.csproj"] = CsProj, ["Main.xaml"] = Head + body + "</StackPanel>" }, "Main.xaml");

    [TestMethod]
    [DataRow("<StackPanel.Resources><local:Conv x:Key=\"C\"/></StackPanel.Resources><TextBlock><TextBlock.Text><MultiBinding Converter=\"{StaticResource C}\"><Binding Path=\"A\"/></MultiBinding></TextBlock.Text></TextBlock>", DisplayName = "MultiBinding 속성 컨버터 리소스")]
    [DataRow("<TextBlock><TextBlock.Text><MultiBinding><MultiBinding.Converter><local:MyMulti/></MultiBinding.Converter><Binding Path=\"A\"/></MultiBinding></TextBlock.Text></TextBlock>", DisplayName = "MultiBinding 요소 컨버터")]
    [DataRow("<TextBlock><TextBlock.Text><Binding Path=\"A\"><Binding.Converter><local:Conv/></Binding.Converter></Binding></TextBlock.Text></TextBlock>", DisplayName = "Binding 요소 컨버터")]
    [DataRow("<StackPanel.Resources><local:Sel x:Key=\"S\"/></StackPanel.Resources><ListBox ItemTemplateSelector=\"{StaticResource S}\"/>", DisplayName = "ItemTemplateSelector 리소스")]
    [DataRow("<StackPanel.Resources><local:MyBrush x:Key=\"B\"/></StackPanel.Resources><Border Background=\"{StaticResource B}\" Height=\"10\"/>", DisplayName = "사용자 브러시 리소스")]
    [DataRow("<TextBlock Text=\"{local:Loc Title}\"/>", DisplayName = "사용자 마크업 확장(속성)")]
    [DataRow("<TextBlock Text=\"{Binding A, Converter={local:Conv}, FallbackValue={local:Loc X}}\"/>", DisplayName = "바인딩 안의 사용자 마크업 확장")]
    [DataRow("<Grid><local:Att.Value>5</local:Att.Value></Grid>", DisplayName = "사용자 첨부 속성 요소")]
    [DataRow("<Button><i:Interaction.Triggers><i:EventTrigger EventName=\"Click\"><i:InvokeCommandAction Command=\"{Binding C}\"/></i:EventTrigger></i:Interaction.Triggers></Button>", DisplayName = "Blend 동작(라이브러리 첨부 속성 요소)")]
    [DataRow("<ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><local:MyPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>", DisplayName = "사용자 ItemsPanel")]
    [DataRow("<StackPanel.Resources><x:Array x:Key=\"A\" Type=\"local:VM\"><local:VM/></x:Array></StackPanel.Resources>", DisplayName = "사용자 타입 x:Array")]
    [DataRow("<Border Background=\"{StaticResource MissingBrush}\" Height=\"10\"/>", DisplayName = "찾을 수 없는 StaticResource")]
    [DataRow("<Button><Button.Triggers><EventTrigger RoutedEvent=\"Button.Click\"><BeginStoryboard Storyboard=\"{StaticResource MissingStory}\"/></EventTrigger></Button.Triggers></Button>", DisplayName = "찾을 수 없는 Storyboard")]
    [DataRow("<ui:Card><TextBlock Text=\"a\"/></ui:Card>", DisplayName = "라이브러리 컨트롤(외부 xmlns)")]
    [DataRow("<Button ui:Foo.Bar=\"1\" Content=\"a\"/>", DisplayName = "라이브러리 첨부 속성")]
    public void R01_CommonUserTypePatterns_DoNotFailTheWholeRender(string body)
    {
        var result = RenderInProject(body);
        Assert.AreEqual(200, result.PixelWidth, "나머지 XAML은 그려져야 한다");
        CollectionAssert.Contains((result.Warnings ?? []).Select(w => w.Code).ToList(), WarningCodes.PlaceholderUsed, "바꾸거나 제거한 내용은 경고로 알려야 한다");
    }

    [TestMethod]
    public void R02_WithoutProject_UnknownNamespaceStillReportsAnError()
    {
        // 프로젝트에 속하지 않은 낱개 XAML의 오타/미선언은 가리지 않고 오류로 보고한다(외부 xmlns 자리표시자는 프로젝트 문맥에서만).
        var ex = Assert.Throws<XamlRenderException>(() => Tier0Render($"{Head}<ui:Card/></StackPanel>"));
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
    }

    [TestMethod]
    public void R03_ResolvableResourcesAndConvertersAreNotTouched()
    {
        // 해석되는 리소스/컨버터는 건드리지 않는다: 존재하는 키의 StaticResource와 WPF 컨버터는 그대로 적용된다.
        var xaml = Head + "<StackPanel.Resources><BooleanToVisibilityConverter x:Key=\"B\"/><SolidColorBrush x:Key=\"Red\" Color=\"Red\"/></StackPanel.Resources>"
            + "<Border Background=\"{StaticResource Red}\" Visibility=\"{Binding Shown, Converter={StaticResource B}}\" Height=\"10\"/></StackPanel>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["P.csproj"] = CsProj, ["Main.xaml"] = xaml }, "Main.xaml");
        Assert.AreEqual(200, result.PixelWidth);
        CollectionAssert.DoesNotContain((result.Warnings ?? []).Select(w => w.Code).ToList(), WarningCodes.PlaceholderUsed);
    }

    private static RenderResult Tier0Render(string xaml)
        => StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml)));
}
