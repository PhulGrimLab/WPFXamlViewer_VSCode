using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace XamlRenderHost.Rendering;

/// <summary>렌더 요청. AllowProjectAssemblies는 호출자(확장)가 Workspace Trust로 판단한 "프로젝트 DLL 로드 허용"이다(false면 사용자 코드를 실행하지 않는다). FilePath는 문서의 파일 경로로, 병합 사전의 상대 경로를 풀 때만 쓴다(없어도 렌더된다). Width/Height는 "요청 크기"(우선순위 3번)이며 루트의 명시 크기와 d:Design* 가 있으면 무시된다.</summary>
public sealed record RenderRequest(string Xaml, double? Width = null, double? Height = null, double Dpi = XamlRenderer.DefaultDpi, string? FilePath = null, bool AllowProjectAssemblies = false);

/// <summary>렌더 결과. Png는 투명 배경 PNG 바이트, 크기는 픽셀 단위.</summary>
public sealed record RenderResult(byte[] Png, int PixelWidth, int PixelHeight, IReadOnlyList<RenderWarning>? Warnings = null, ProjectTierInfo? Project = null, IReadOnlyList<HitElement>? Elements = null);

/// <summary>
/// XAML 문자열을 WPF로 실제 렌더링해 PNG로 돌려준다(doc/01 3절의 Renderer).
/// 반드시 STA 스레드에서 호출해야 한다(<see cref="StaRunner"/> 또는 STAThread 메인).
/// 상태가 없는 정적 클래스이므로 스레드 안전성은 호출한 스레드의 WPF 객체에만 의존한다.
///
/// 크기 결정 순서(차원별로 독립 적용): ① 루트의 Width/Height 명시 → ② d:DesignWidth/DesignHeight
/// → ③ 요청 크기 → ④ 콘텐츠 크기(Measure 결과).
/// 결정성(골든 테스트 전제, doc/03 2절): 96 DPI 기본, 회색조 텍스트(ClearType 끔), 투명 배경.
/// </summary>
public static class XamlRenderer
{
    /// <summary>
    /// 디자인 모드 표시(doc/01 3.3 규칙 4, M4B B.3): 사용자 코드가 `DesignerProperties.GetIsInDesignMode`로 분기할 수 있게
    /// 기본값을 true로 덮어쓴다(Visual Studio 디자이너와 같은 관례). 프로세스에서 한 번만 가능하므로 정적 초기화로 둔다.
    /// 덮어쓰기가 이미 되어 있으면(예: 같은 프로세스의 다른 초기화) 무시한다.
    /// </summary>
    private static readonly bool DesignModeEnabled = EnableDesignMode();

    private static bool EnableDesignMode()
    {
        try
        {
            System.ComponentModel.DesignerProperties.IsInDesignModeProperty.OverrideMetadata(
                typeof(DependencyObject), new FrameworkPropertyMetadata(true));
            return true;
        }
        catch (ArgumentException)
        {
            return false; // 이미 덮어써져 있음.
        }
        catch (InvalidOperationException)
        {
            return false; // 속성이 이미 사용되어 메타데이터가 확정됨.
        }
    }

    /// <summary>기본 DPI. 골든 이미지는 이 값으로 만든다.</summary>
    public const double DefaultDpi = 96.0;

    /// <summary>WPF 논리 단위(1/96 인치)의 기준 DPI.</summary>
    private const double LogicalDpi = 96.0;

    /// <summary>렌더 결과 한 변의 최대 픽셀. 거대한 비트맵으로 메모리가 폭증하는 것을 막는다.</summary>
    public const int MaxPixelsPerSide = 8192;

    /// <summary>콘텐츠가 비어 크기가 0일 때 쓰는 최소 한 변(픽셀). RenderTargetBitmap은 0 크기를 허용하지 않는다.</summary>
    private const int MinPixelsPerSide = 1;

    /// <summary>Blend 디자인 타임 네임스페이스(d:DesignWidth/DesignHeight가 속한다).</summary>
    private static readonly XNamespace DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>
    /// XAML을 렌더해 PNG를 만든다.
    /// 입력: <paramref name="request"/>. 출력: PNG와 픽셀 크기.
    /// 예외: <see cref="XamlRenderException"/> — 코드/줄/열을 가진 구조화된 오류(빈 입력, XML/XAML 오류, 지원하지 않는 루트, 크기 초과, 렌더 실패).
    /// 렌더 전에 <see cref="XamlPreprocessor"/>가 x:Class/이벤트/x:Code 제거, 해석 불가 타입 자리표시자, 병합 사전 인라인을 수행하며
    /// 그 변경은 결과의 Warnings로 돌려준다. 전처리는 줄 번호를 보존하므로 오류 위치는 원본 기준이다.
    /// </summary>
    public static RenderResult Render(RenderRequest request)
    {
        _ = DesignModeEnabled; // 정적 초기화를 확실히 수행한다(어떤 WPF 객체도 만들기 전).
        if (string.IsNullOrWhiteSpace(request.Xaml))
        {
            throw new XamlRenderException(RenderErrorCodes.EmptyInput, "XAML 입력이 비어 있습니다.");
        }
        else
        {
            // 정상 입력: 계속 진행.
        }

        var tier = ProjectTier.Resolve(request.AllowProjectAssemblies, request.FilePath);
        var schema = ProjectTier.CreateSchemaContext(tier);
        try
        {
            return RenderCore(request, tier, schema, tagElements: true);
        }
        catch (XamlRenderException ex) when (ex.Code == RenderErrorCodes.XamlParse && ex.Line.HasValue)
        {
            // HitMap용 Uid 태그가 같은 줄의 열 위치를 밀어 놓았다. 오류 위치(특히 열)를 원본 기준으로 정확히 보고하려고
            // 태그 없이 한 번 더 파싱한다(오류 경로에서만 드는 비용). 뜻밖에 성공하면 그 결과(HitMap 없음)를 돌려준다.
            return RenderCore(request, tier, schema, tagElements: false);
        }
    }

    /// <summary>전처리 → 파싱 → 레이아웃 → PNG/HitMap의 본체. tagElements=true면 HitMap용 Uid 태그를 붙인다.</summary>
    private static RenderResult RenderCore(RenderRequest request, ProjectTierInfo tier, System.Xaml.XamlSchemaContext? schema, bool tagElements)
    {
        var preprocessed = XamlPreprocessor.Process(request.Xaml, request.FilePath, tier.Tier == 1 ? tier.AssemblyName : null, schema, tagElements);
        var warnings = new List<RenderWarning>(preprocessed.Warnings);
        var (root, finalXaml) = ParseWithUserFailureRecovery(preprocessed.Xaml, schema, warnings);
        if (tier.Tier == 0 && warnings.Any(w => w.Code == WarningCodes.PlaceholderUsed))
        {
            // 자리표시자가 생겼고 그 이유가 Tier 0라면 사용자에게 이유를 알린다(미신뢰/산출물 없음 등).
            warnings.Add(new RenderWarning(WarningCodes.ProjectTier0, ProjectTier.DescribeTier0(tier), null, null));
        }
        else
        {
            // 알릴 필요 없음.
        }
        var (designWidth, designHeight) = ReadDesignSize(finalXaml);
        var width = ResolveDimension(root.Width, designWidth, request.Width);
        var height = ResolveDimension(root.Height, designHeight, request.Height);

        try
        {
            var result = RenderToPng(root, width, height, request.Dpi, preprocessed.Elements);
            if (result.Elements is { Count: >= HitMap.MaxElements })
            {
                warnings.Add(new RenderWarning(WarningCodes.HitMapTruncated, $"요소가 많아 클릭 매핑을 {HitMap.MaxElements}개로 제한했습니다.", null, null));
            }
            else
            {
                // 한도 이내.
            }
            return result with { Warnings = warnings, Project = tier };
        }
        catch (XamlRenderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new XamlRenderException(RenderErrorCodes.RenderFailed, $"렌더 중 오류: {ex.Message}", inner: ex);
        }
    }

    /// <summary>한 렌더에서 사용자 컨트롤 예외로 대체를 시도하는 최대 횟수(무한 반복 방지).</summary>
    private const int MaxUserFailureReplacements = 20;

    /// <summary>오류 자리표시자에 싣는 예외 요약의 최대 길이.</summary>
    private const int MaxErrorSummaryLength = 120;

    /// <summary>
    /// 파싱하되, 사용자 코드(프로젝트 어셈블리)가 던진 예외로 실패하면 그 요소만 오류 자리표시자로 바꾸고 다시 파싱한다(M4B B.3).
    /// 사용자 코드가 던진 것인지는 예외의 스택에 기본 컨텍스트가 아닌(= 사용자 컨텍스트) 어셈블리 프레임이 있는지로 판단한다.
    /// 그 외 실패(문법/값 오류 등)는 그대로 던진다. 반환: 루트와 최종 XAML(디자인 크기 읽기용).
    /// </summary>
    private static (FrameworkElement Root, string Xaml) ParseWithUserFailureRecovery(string xaml, System.Xaml.XamlSchemaContext? schema, List<RenderWarning> warnings)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return (ParseRoot(xaml, schema), xaml);
            }
            catch (XamlRenderException ex) when (attempt < MaxUserFailureReplacements
                && ex.Code == RenderErrorCodes.XamlParse && ex.Line.HasValue && ex.Column.HasValue
                && ex.InnerException != null && ThrownByUserCode(ex.InnerException))
            {
                var summary = Summarize(ex.InnerException);
                var replacement = XamlPreprocessor.ReplaceFailedUserElement(xaml, ex.Line.Value, ex.Column.Value, summary);
                if (replacement == null)
                {
                    throw; // 사용자 타입 요소를 특정하지 못함: 원래 오류를 보고한다.
                }
                else
                {
                    xaml = replacement.Xaml;
                    warnings.Add(new RenderWarning(WarningCodes.UserControlFailed, $"{replacement.TypeName}: {summary}", replacement.Line, replacement.Column));
                }
            }
        }
    }

    /// <summary>예외(와 내부 예외들)의 스택에 사용자 컨텍스트에서 로드한 어셈블리의 프레임이 있는가.</summary>
    private static bool ThrownByUserCode(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            foreach (var frame in new System.Diagnostics.StackTrace(current).GetFrames())
            {
                var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                var context = assembly == null ? null : System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly);
                if (context != null && context != System.Runtime.Loader.AssemblyLoadContext.Default)
                {
                    return true;
                }
                else
                {
                    // 프레임워크/호스트 코드.
                }
            }
        }
        return false;
    }

    /// <summary>가장 안쪽 예외의 "형식: 메시지"를 한 줄 요약(길이 제한)으로 만든다.</summary>
    private static string Summarize(Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException != null)
        {
            innermost = innermost.InnerException;
        }
        var text = $"{innermost.GetType().Name}: {innermost.Message}".Replace("\r", " ").Replace("\n", " ");
        return text.Length <= MaxErrorSummaryLength ? text : text[..MaxErrorSummaryLength] + "…";
    }

    /// <summary>XamlReader로 파싱하고 FrameworkElement 루트인지 확인한다. 파싱 오류는 코드/줄/열로 변환한다.</summary>
    private static FrameworkElement ParseRoot(string xaml, System.Xaml.XamlSchemaContext? schema)
    {
        object parsed;
        try
        {
            parsed = ParseObject(xaml, schema);
        }
        catch (XamlParseException ex)
        {
            throw ConvertParseException(ex);
        }
        catch (XmlException ex)
        {
            throw new XamlRenderException(RenderErrorCodes.XmlMalformed, ex.Message, ex.LineNumber, ex.LinePosition, ex);
        }

        if (parsed is Window window)
        {
            return HostWindowContent(window);
        }
        else if (parsed is FrameworkElement element)
        {
            return element;
        }
        else
        {
            throw new XamlRenderException(
                RenderErrorCodes.UnsupportedRoot,
                $"루트가 FrameworkElement가 아닙니다: {parsed?.GetType().Name ?? "null"}");
        }
    }

    /// <summary>
    /// Window 루트를 렌더 가능한 Border로 바꾼다(창 크롬 제외, M4.5). Window는 Show 없이 그릴 수 없으므로 콘텐츠를 떼어
    /// Border에 호스팅하고, 크기/배경/리소스/글꼴(로컬로 지정된 것만)을 Window에서 옮긴다. Window는 표시된 적이 없어 별도 정리가 필요 없다.
    /// </summary>
    private static FrameworkElement HostWindowContent(Window window)
    {
        var content = window.Content;
        window.Content = null; // 콘텐츠의 논리 부모를 Window에서 떼어야 다른 부모에 붙일 수 있다.
        var resources = window.Resources;
        window.Resources = new ResourceDictionary();

        var host = new System.Windows.Controls.Border
        {
            Width = window.Width,
            Height = window.Height,
            Background = window.Background,
            Uid = window.Uid, // HitMap: Window 요소의 위치를 호스트 Border가 대신 가진다.
            Resources = resources,
            Child = content switch
            {
                null => null,
                UIElement element => element,
                _ => new System.Windows.Controls.ContentPresenter { Content = content },
            },
        };
        CopyLocalValue(window, host, System.Windows.Controls.Control.FontFamilyProperty, System.Windows.Documents.TextElement.FontFamilyProperty);
        CopyLocalValue(window, host, System.Windows.Controls.Control.FontSizeProperty, System.Windows.Documents.TextElement.FontSizeProperty);
        CopyLocalValue(window, host, System.Windows.Controls.Control.ForegroundProperty, System.Windows.Documents.TextElement.ForegroundProperty);
        return host;
    }

    /// <summary>
    /// Window에 로컬 값이 있을 때만 호스트 Border에 옮긴다. Border는 Control이 아니라 글꼴 DP를 직접 갖지 않으므로
    /// 자식에게 상속되는 TextElement 쪽 속성으로 설정한다.
    /// </summary>
    private static void CopyLocalValue(DependencyObject from, FrameworkElement to, DependencyProperty source, DependencyProperty inheritableTarget)
    {
        var value = from.ReadLocalValue(source);
        if (value != DependencyProperty.UnsetValue)
        {
            to.SetValue(inheritableTarget, value);
        }
        else
        {
            // 로컬 값 없음: 기본값 유지.
        }
    }

    /// <summary>
    /// schema가 없으면 WPF 기본 경로(XamlReader.Parse), 있으면(Tier 1) 그 스키마 컨텍스트로 XamlXmlReader를 만들어 로드한다.
    /// 후자는 줄/열 정보를 켜서 오류가 같은 위치 정보를 갖게 한다.
    /// </summary>
    private static object ParseObject(string xaml, System.Xaml.XamlSchemaContext? schema)
    {
        if (schema == null)
        {
            return XamlReader.Parse(xaml);
        }
        else
        {
            using var stringReader = new StringReader(xaml);
            using var xamlReader = new System.Xaml.XamlXmlReader(stringReader, schema, new System.Xaml.XamlXmlReaderSettings { ProvideLineInfo = true });
            return XamlReader.Load(xamlReader);
        }
    }

    /// <summary>XamlParseException을 XML 오류/XAML 오류로 구분해 줄/열과 함께 변환한다.</summary>
    private static XamlRenderException ConvertParseException(XamlParseException ex)
    {
        if (ex.InnerException is XmlException xml)
        {
            return new XamlRenderException(RenderErrorCodes.XmlMalformed, xml.Message, xml.LineNumber, xml.LinePosition, ex);
        }
        else
        {
            return new XamlRenderException(
                RenderErrorCodes.XamlParse, ex.Message,
                ex.LineNumber > 0 ? ex.LineNumber : null,
                ex.LinePosition > 0 ? ex.LinePosition : null,
                ex);
        }
    }

    /// <summary>루트 요소의 d:DesignWidth / d:DesignHeight를 읽는다. 없거나 숫자가 아니면 null.</summary>
    private static (double? Width, double? Height) ReadDesignSize(string xaml)
    {
        // 이미 XamlReader가 올바른 XML임을 확인했으므로 파싱 실패는 정상 경로에서 발생하지 않는다.
        var rootElement = XDocument.Parse(xaml).Root;
        return (ReadDouble(rootElement, "DesignWidth"), ReadDouble(rootElement, "DesignHeight"));
    }

    private static double? ReadDouble(XElement? element, string designAttributeName)
    {
        var text = element?.Attribute(DesignNamespace + designAttributeName)?.Value;
        if (text != null && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }
        else
        {
            return null;
        }
    }

    /// <summary>한 차원의 크기를 우선순위(명시 → 디자인 → 요청)로 결정한다. 모두 없으면 null(= 콘텐츠 크기).</summary>
    private static double? ResolveDimension(double explicitValue, double? design, double? requested)
    {
        if (!double.IsNaN(explicitValue))
        {
            return explicitValue;
        }
        else if (design.HasValue)
        {
            return design;
        }
        else
        {
            return requested;
        }
    }

    /// <summary>레이아웃(Measure/Arrange)을 수행하고 RenderTargetBitmap으로 그려 PNG로 인코딩한다.</summary>
    private static RenderResult RenderToPng(FrameworkElement root, double? width, double? height, double dpi, IReadOnlyList<SourceElement> sources)
    {
        // 결정성: ClearType 대신 회색조 텍스트. 사용자 XAML이 직접 지정했다면 그 값이 우선하도록 로컬 값이 없을 때만 설정한다.
        if (root.ReadLocalValue(System.Windows.Media.TextOptions.TextRenderingModeProperty) == DependencyProperty.UnsetValue)
        {
            System.Windows.Media.TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        }
        else
        {
            // 사용자가 지정한 값을 존중한다.
        }

        root.Measure(new Size(width ?? double.PositiveInfinity, height ?? double.PositiveInfinity));
        var finalWidth = width ?? root.DesiredSize.Width;
        var finalHeight = height ?? root.DesiredSize.Height;
        root.Arrange(new Rect(0, 0, finalWidth, finalHeight));
        root.UpdateLayout();

        // 템플릿 적용/로드 이벤트 등 지연 작업을 비운다.
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        var scale = dpi / LogicalDpi;
        var pixelWidth = Math.Max(MinPixelsPerSide, (int)Math.Ceiling(finalWidth * scale));
        var pixelHeight = Math.Max(MinPixelsPerSide, (int)Math.Ceiling(finalHeight * scale));
        if (pixelWidth > MaxPixelsPerSide || pixelHeight > MaxPixelsPerSide)
        {
            throw new XamlRenderException(
                RenderErrorCodes.TooLarge,
                $"렌더 크기 {pixelWidth}x{pixelHeight}px 가 최대 {MaxPixelsPerSide}px 를 넘습니다.");
        }
        else
        {
            // 허용 범위: 계속 진행.
        }

        // 클릭 매핑은 레이아웃이 끝난 지금 계산한다(PNG와 같은 픽셀 좌표계: dpi 배율 반영).
        var elements = HitMap.Collect(root, sources, scale, out _);

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new RenderResult(stream.ToArray(), pixelWidth, pixelHeight, Elements: elements);
    }
}
