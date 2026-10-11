using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SampleControls;

/// <summary>고정 크기 붉은 사각형. VARIANT_B 빌드에서는 폭이 80이라 DLL 교체 후 재로드 여부를 폭으로 구분할 수 있다.</summary>
public class RedBox : FrameworkElement
{
#if VARIANT_B
    private const double BoxWidth = 80;
#else
    private const double BoxWidth = 40;
#endif

    protected override Size MeasureOverride(Size availableSize) => new(BoxWidth, 20);

    protected override void OnRender(DrawingContext drawingContext)
        => drawingContext.DrawRectangle(Brushes.Red, null, new Rect(0, 0, BoxWidth, 20));
}

/// <summary>
/// **internal** 컨트롤과 변환기. 컴파일된 앱에서는 흔하지만(예: WPF UI Gallery의 `internal sealed class NullToVisibilityConverter`) 컴파일되지 않은 XAML을 읽는
/// XamlReader는 기본적으로 public이 아닌 타입을 만들지 못한다("알 수 없는 형식"). 신뢰된 프로젝트에서는 만들 수 있어야 한다.
/// </summary>
internal class InternalBox : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(33, 20);

    protected override void OnRender(DrawingContext drawingContext)
        => drawingContext.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 33, 20));
}

/// <summary>internal 값 변환기(리소스 사전에 인스턴스로 선언된다).</summary>
internal sealed class InternalConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value;
}

/// <summary>디자인 모드이면 폭 60, 아니면 30. 호스트가 IsInDesignMode=true를 켜는지 확인한다.</summary>
public class DesignAware : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(DesignerProperties.GetIsInDesignMode(this) ? 60 : 30, 10);

    protected override void OnRender(DrawingContext drawingContext)
        => drawingContext.DrawRectangle(Brushes.Green, null, new Rect(0, 0, DesiredSize.Width, 10));
}

/// <summary>생성자가 예외를 던지는 컨트롤(오류 자리표시자로 대체되어야 한다).</summary>
public class Throwing : FrameworkElement
{
    public Throwing()
    {
        throw new InvalidOperationException("boom from ctor");
    }
}

/// <summary>
/// 델리게이트 타입의 첨부 **속성**(이벤트가 아님). MaterialDesign의 `DialogHost.DialogClosingAttached`와 같은 구조: XAML에서 `s:AttachedHandlers.Closing="핸들러이름"`처럼
/// 코드 비하인드 메서드 이름을 쓰는데, 코드 비하인드가 없으면 이름 문자열을 델리게이트로 만들 수 없어 로드가 실패한다.
/// </summary>
public static class AttachedHandlers
{
    public static readonly DependencyProperty ClosingProperty =
        DependencyProperty.RegisterAttached("Closing", typeof(EventHandler), typeof(AttachedHandlers), new PropertyMetadata(null));

    public static void SetClosing(DependencyObject element, EventHandler value) => element.SetValue(ClosingProperty, value);

    public static EventHandler GetClosing(DependencyObject element) => (EventHandler)element.GetValue(ClosingProperty);

    /// <summary>델리게이트 타입이 아닌 첨부 속성(제거되면 안 된다).</summary>
    public static readonly DependencyProperty CountProperty =
        DependencyProperty.RegisterAttached("Count", typeof(double), typeof(AttachedHandlers), new PropertyMetadata(0.0));

    public static void SetCount(DependencyObject element, double value) => element.SetValue(CountProperty, value);

    public static double GetCount(DependencyObject element) => (double)element.GetValue(CountProperty);
}

/// <summary>생성자가 끝나지 않는 컨트롤(호스트 타임아웃/kill/재시작 검증용, B.6).</summary>
public class Hanging : FrameworkElement
{
    public Hanging()
    {
        Thread.Sleep(Timeout.Infinite);
    }
}
