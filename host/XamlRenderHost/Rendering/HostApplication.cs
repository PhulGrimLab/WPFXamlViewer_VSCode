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
    /// 앞서 적용한 앱 리소스를 비운다(적용한 적이 없으면 아무 일도 하지 않는다). 신뢰하지 않은 렌더(Tier 0) 직전에 호출한다:
    /// `Application.Resources`에는 신뢰된 프로젝트의 사용자 타입 인스턴스(변환기 등)가 들어 있어, 비우지 않으면 신뢰하지 않은 문서가
    /// `{StaticResource …}`로 그 인스턴스를 얻어 사용자 코드를 실행하게 될 수 있다. 같은 프로세스에서 신뢰 상태가 바뀌는 경우의 심층 방어다.
    /// </summary>
    public static void ClearResources()
    {
        if (_appliedKey == null)
        {
            return;
        }
        else
        {
            // 적용한 앱 리소스가 있음: 비운다.
        }

        try
        {
            if (Application.Current != null)
            {
                Application.Current.Resources = new ResourceDictionary();
            }
            else
            {
                // Application이 없다: 비울 것이 없다.
            }
        }
        catch (Exception)
        {
            // 다른 스레드에서 만든 Application 등으로 접근이 막혔다: 다음 Tier 1 렌더가 다시 덮어쓴다.
        }
        _appliedKey = null;
    }

    /// <summary>앱 리소스 파싱에서 실패한 요소를 건너뛰며 다시 시도하는 최대 횟수(무한 반복 방지).</summary>
    private const int MaxSkippedElements = 20;

    /// <summary>
    /// 앱 리소스 사전을 파싱한다. 사전 안의 요소 하나가 실패하면(예: public이 아닌 변환기 타입) 그 요소만 건너뛰고 다시 파싱한다 —
    /// 문서에 주입한 같은 사전에 적용하는 복구(<see cref="XamlPreprocessor.RemoveFailedInlinedElement"/>)와 같은 방식이다. 전체가 인라인 구간이라 모든 요소가 대상이다.
    /// </summary>
    private static object ParseSkippingFailingElements(string xml, System.Xaml.XamlSchemaContext? schema)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return XamlRenderer.ParseObject(xml, schema);
            }
            catch (System.Windows.Markup.XamlParseException ex) when (attempt < MaxSkippedElements)
            {
                var removal = XamlPreprocessor.RemoveFailedInlinedElement(xml, ex.LineNumber, ex.LinePosition);
                if (removal == null)
                {
                    throw;
                }
                else
                {
                    xml = removal.Xaml;
                }
            }
        }
    }

    /// <summary>
    /// 라이브러리별 "앱 시작 때 하는 일" 훅(어셈블리 이름, 형식, 정적 매개변수 없는 메서드). 일부 라이브러리는 XAML 사전만으로는 값이 다 채워지지 않고
    /// 앱 시작 코드가 `Application.Resources`에 값을 써 넣는다. 호스트는 앱을 실행하지 않으므로 알려진 라이브러리에 한해 그 호출을 대신한다.
    /// - WPF UI: `ApplicationAccentColorManager.ApplySystemAccent()`가 시스템 강조색을 `SystemAccentColor*` 키로 써 넣는다.
    ///   없으면 `AccentButtonBackground` 등 강조색 브러시가 비어 Accent/Primary 버튼이 안 보인다.
    ///   그 전에 `UiApplication`의 스레드 정적 캐시(`_uiApplication`)를 비운다: 앱 사전을 파싱하는 동안 컨트롤이 `UiApplication.Current`를 먼저 건드리면
    ///   그때는 `Application`에 WPF UI 리소스가 아직 없어서 `UiApplication`이 `Application`이 아닌 자기만의 임시 사전을 쓰는 상태로 굳는다
    ///   (그러면 훅이 쓴 `AccentFillColorDefault` 같은 키가 `Application.Resources`에 없어 `AccentButtonBackground`가 투명이 된다).
    /// 훅은 신뢰되어 이미 로드된 사용자 컨텍스트의 어셈블리에서만 찾는다. 실패는 렌더를 막지 않고 경고로 알린다.
    /// </summary>
    private static readonly (string Assembly, string ResetType, string ResetStaticField, string Type, string Method)[] LibraryBootstraps =
    {
        ("Wpf.Ui", "Wpf.Ui.UiApplication", "_uiApplication", "Wpf.Ui.Appearance.ApplicationAccentColorManager", "ApplySystemAccent"),
    };

    /// <summary>알려진 라이브러리 훅을 실행한다. 로드된 어셈블리에 없으면 건너뛴다. 반환: 첫 실패 사유(없으면 null).</summary>
    private static string? RunLibraryBootstraps()
    {
        string? failure = null;
        foreach (var (assemblyName, resetType, resetField, typeName, methodName) in LibraryBootstraps)
        {
            // 현재 로드된 프로젝트의 컨텍스트에서 찾는다. 다시 불러온 뒤에는 이전 복사본이 AppDomain에 남아 있어, AppDomain의 첫 일치를 쓰면 낡은 복사본을 집는다.
            var assembly = Projects.UserAssemblies.Instance.FindCurrentAssembly(assemblyName);
            var method = assembly?.GetType(typeName)?.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, Type.EmptyTypes);
            if (method == null)
            {
                continue; // 이 라이브러리를 쓰지 않는 프로젝트.
            }
            else
            {
                // 훅 실행.
            }

            try
            {
                // 스레드 정적 필드는 이 스레드(렌더 스레드)의 값만 지운다. 이 스레드가 UiApplication을 쓰는 스레드와 같다.
                assembly!.GetType(resetType)?.GetField(resetField, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, null);
                method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                failure ??= $"{typeName}.{methodName}: {(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}";
            }
        }
        return failure;
    }

    /// <summary>
    /// Tier 1에서 App.xaml 리소스 사전(<paramref name="resourcesXml"/>)을 `Application.Resources`로 적용한다. 이미 같은 내용이면 건너뛴다.
    /// 입력: 주입에 쓴 사전 XML, 스키마 컨텍스트(문서와 같은 것), 로드한 프로젝트 어셈블리 경로. 반환: 실패 사유(성공/불필요면 null).
    /// 실패해도 렌더는 계속된다(문서 안에 이미 같은 사전이 주입되어 있다).
    /// </summary>
    public static string? ApplyResources(string? resourcesXml, System.Xaml.XamlSchemaContext? schema, string? assemblyPath)
    {
        if (resourcesXml == null)
        {
            // 이 프로젝트는 App.xaml 리소스가 없다: 앞선 다른 프로젝트가 올려 둔 것이 남아 이 문서에 새지 않게 비운다.
            ClearResources();
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
            var parsed = ParseSkippingFailingElements(resourcesXml, schema) as ResourceDictionary
                ?? throw new InvalidOperationException(Loc.T("App.xaml 리소스가 ResourceDictionary가 아닙니다.", "The App.xaml resources are not a ResourceDictionary."));
            application.Resources = parsed;
            _appliedKey = key;
            return RunLibraryBootstraps();
        }
        catch (Exception ex)
        {
            _appliedKey = null; // 다음 렌더에서 다시 시도한다.
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
