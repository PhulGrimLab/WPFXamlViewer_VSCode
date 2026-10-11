using System.IO;
using System.Windows;

namespace XamlRenderHost.Rendering;

/// <summary>
/// 호스트 프로세스의 `System.Windows.Application`(실제 앱의 App.xaml이 하는 일을 흉내 낸다).
/// 왜 필요한가: MaterialDesign 같은 라이브러리의 컴파일된 테마 사전은 `Application.Current.Resources`에서 키를 찾는다
/// (예: `MaterialDesignColors.StaticResourceExtension`). 문서 안에 App.xaml 리소스를 병합해 주입해도 앱 수준 조회는 보지 못하므로,
/// Tier 1에서는 같은 사전을 `Application.Resources`에도 얹는다.
///
/// Owner: 프로세스 전체에서 하나(정적). Lifetime: 첫 사용~프로세스 종료. 스레드: 렌더를 수행하는 STA 스레드에서만 호출한다
/// (serve 모드는 STA 메인 스레드 하나, 테스트는 호출마다 새 STA 스레드). 상태(<see cref="_appliedKey"/>)는 렌더가 직렬이라 잠금 없이 접근한다.
/// 주의: Application은 프로세스에 하나만 만들 수 있고 만든 스레드의 Dispatcher에 묶인다. 이미 있으면 그대로 쓴다.
/// </summary>
public static class HostApplication
{
    /// <summary>마지막으로 `Application.Resources`에 적용한 내용의 키(어셈블리 경로+수정 시각+사전 XML). 같으면 다시 파싱하지 않는다.</summary>
    private static string? _appliedKey;

    /// <summary>
    /// Tier 1에서 App.xaml 리소스 사전(<paramref name="resourcesXml"/>)을 `Application.Resources`로 적용한다. 이미 같은 내용이면 건너뛴다.
    /// 입력: 주입에 쓴 사전 XML, 스키마 컨텍스트(문서와 같은 것), 로드한 프로젝트 어셈블리 경로. 반환: 실패 사유(성공/불필요면 null).
    /// 실패해도 렌더는 계속된다(문서 안에 이미 같은 사전이 주입되어 있다).
    /// </summary>
    public static string? ApplyResources(string? resourcesXml, System.Xaml.XamlSchemaContext? schema, string? assemblyPath)
    {
        if (resourcesXml == null)
        {
            return null;
        }
        else
        {
            // 적용할 앱 리소스 있음.
        }

        var key = $"{assemblyPath}|{(assemblyPath != null && File.Exists(assemblyPath) ? File.GetLastWriteTimeUtc(assemblyPath).Ticks : 0)}|{resourcesXml}";
        if (key == _appliedKey)
        {
            return null;
        }
        else
        {
            // 처음이거나 앱 리소스/산출물이 바뀜: 적용한다.
        }

        try
        {
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var parsed = XamlRenderer.ParseObject(resourcesXml, schema) as ResourceDictionary
                ?? throw new InvalidOperationException("App.xaml 리소스가 ResourceDictionary가 아닙니다.");
            application.Resources = parsed;
            _appliedKey = key;
            return null;
        }
        catch (Exception ex)
        {
            _appliedKey = null; // 다음 렌더에서 다시 시도한다.
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
