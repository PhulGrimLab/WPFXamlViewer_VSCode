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

/// <summary>생성자가 끝나지 않는 컨트롤(호스트 타임아웃/kill/재시작 검증용, B.6).</summary>
public class Hanging : FrameworkElement
{
    public Hanging()
    {
        Thread.Sleep(Timeout.Infinite);
    }
}
