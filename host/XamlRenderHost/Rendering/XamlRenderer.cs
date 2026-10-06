using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace XamlRenderHost.Rendering;

/// <summary>렌더 요청. Width/Height는 "요청 크기"(우선순위 3번)이며 루트의 명시 크기와 d:Design* 가 있으면 무시된다.</summary>
public sealed record RenderRequest(string Xaml, double? Width = null, double? Height = null, double Dpi = XamlRenderer.DefaultDpi);

/// <summary>렌더 결과. Png는 투명 배경 PNG 바이트, 크기는 픽셀 단위.</summary>
public sealed record RenderResult(byte[] Png, int PixelWidth, int PixelHeight);

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
    /// 주의: 렌더 전에 x:Class/이벤트 핸들러가 있으면 XamlReader가 실패한다 — 제거는 M4(XamlPreprocessor)에서 한다.
    /// </summary>
    public static RenderResult Render(RenderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Xaml))
        {
            throw new XamlRenderException(RenderErrorCodes.EmptyInput, "XAML 입력이 비어 있습니다.");
        }
        else
        {
            // 정상 입력: 계속 진행.
        }

        var root = ParseRoot(request.Xaml);
        var (designWidth, designHeight) = ReadDesignSize(request.Xaml);
        var width = ResolveDimension(root.Width, designWidth, request.Width);
        var height = ResolveDimension(root.Height, designHeight, request.Height);

        try
        {
            return RenderToPng(root, width, height, request.Dpi);
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

    /// <summary>XamlReader로 파싱하고 FrameworkElement 루트인지 확인한다. 파싱 오류는 코드/줄/열로 변환한다.</summary>
    private static FrameworkElement ParseRoot(string xaml)
    {
        object parsed;
        try
        {
            parsed = XamlReader.Parse(xaml);
        }
        catch (XamlParseException ex)
        {
            throw ConvertParseException(ex);
        }
        catch (XmlException ex)
        {
            throw new XamlRenderException(RenderErrorCodes.XmlMalformed, ex.Message, ex.LineNumber, ex.LinePosition, ex);
        }

        if (parsed is Window)
        {
            // Window는 Show 없이 렌더할 수 없다. 콘텐츠 호스팅은 M4.5에서 지원한다.
            throw new XamlRenderException(RenderErrorCodes.UnsupportedRoot, "Window 루트는 아직 지원하지 않습니다(M4.5 예정).");
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
    private static RenderResult RenderToPng(FrameworkElement root, double? width, double? height, double dpi)
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

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new RenderResult(stream.ToArray(), pixelWidth, pixelHeight);
    }
}
