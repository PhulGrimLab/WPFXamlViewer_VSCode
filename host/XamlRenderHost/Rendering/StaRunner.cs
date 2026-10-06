using System.Threading;

namespace XamlRenderHost.Rendering;

/// <summary>
/// WPF 객체를 만들고 렌더하는 작업을 STA 스레드에서 실행해 주는 도우미.
/// WPF는 STA에서만 동작하는데 테스트/라이브러리 호출자는 보통 MTA이기 때문에 필요하다.
/// Owner: 호출자(호출마다 새 스레드를 만들고 끝나면 종료). Lifetime: 호출 하나 동안만.
/// 공유자원 없음(결과/예외만 호출 스레드로 넘긴다). 장기 실행 호스트(M2)는 자체 STA 메인 스레드를 쓰므로 이 클래스를 쓰지 않는다.
/// </summary>
public static class StaRunner
{
    /// <summary>
    /// <paramref name="work"/>를 새 STA 스레드에서 실행하고 결과를 돌려준다.
    /// 작업 중 던져진 예외는 원래 예외 그대로 호출 스레드에서 다시 던진다(스택 정보 보존).
    /// </summary>
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (error != null)
        {
            error.Throw();
        }
        else
        {
            // 정상 종료: 예외 전달 없음.
        }
        return result;
    }
}
