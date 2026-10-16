using XamlRenderHost.Projects;

namespace XamlRenderHost.Rendering;

/// <summary>
/// 이번 렌더가 쓴 Tier 결정(doc/01 3.3, H023). Tier 0 = 사용자 타입은 자리표시자, Tier 1 = 프로젝트 DLL을 로드해 실제 컨트롤을 그림.
/// Reason: Loaded / Untrusted / NoFilePath / NoProject / NoArtifact / LoadFailed. Reloaded/LoadMs는 이번 호출에서 DLL을 (다시) 로드했을 때만 의미가 있다(H020).
/// </summary>
public sealed record ProjectTierInfo(int Tier, string Reason, string? AssemblyName = null, string? AssemblyPath = null, bool Reloaded = false, long LoadMs = 0, string? Detail = null, System.Reflection.Assembly? Assembly = null);

/// <summary>Tier 결정 이유 문자열(로그/프로토콜이 그대로 사용).</summary>
public static class ProjectTierReasons
{
    public const string Loaded = "Loaded";
    public const string Untrusted = "Untrusted";
    public const string NoFilePath = "NoFilePath";
    public const string LoadFailed = "LoadFailed";
}

/// <summary>Tier 결정 로직. 상태 없는 정적 클래스(로드 상태는 <see cref="UserAssemblies"/>가 소유).</summary>
public static class ProjectTier
{
    /// <summary>
    /// 신뢰(allow)가 있고 파일 경로로 산출물을 찾아 로드에 성공하면 Tier 1, 아니면 Tier 0과 그 이유.
    /// 신뢰 판단은 호출자(확장: Workspace Trust)가 하고 호스트는 그 결과만 따른다 — 신뢰 없이는 사용자 코드를 절대 로드하지 않는다.
    /// </summary>
    public static ProjectTierInfo Resolve(bool allowProjectAssemblies, string? filePath, bool allowHostRestart = false)
    {
        if (!allowProjectAssemblies)
        {
            return new ProjectTierInfo(0, ProjectTierReasons.Untrusted);
        }
        else if (string.IsNullOrEmpty(filePath))
        {
            return new ProjectTierInfo(0, ProjectTierReasons.NoFilePath);
        }
        else
        {
            // 신뢰 + 경로 있음: 산출물 탐색.
        }

        var lookup = ProjectLocator.Find(filePath);
        if (lookup.Artifact == null)
        {
            return new ProjectTierInfo(0, lookup.Reason);
        }
        else
        {
            // 산출물 있음: 로드.
        }

        if (allowHostRestart && UserAssemblies.Instance.NeedsReload(lookup.Artifact))
        {
            // 이미 불러 둔 이전 버전이 있다: 같은 프로세스에서 다시 불러오면 라이브러리의 의존 속성이 중복 등록되어 렌더가 깨진다. 호스트를 새로 띄우게 한다.
            throw new XamlRenderException(RenderErrorCodes.HostRestartRequired,
                Loc.T("프로젝트가 다시 빌드되어 호스트를 다시 시작합니다.", "The project was rebuilt; restarting the host."));
        }
        else
        {
            // 처음 로드이거나 같은 버전이거나 재시작을 허용하지 않는 호출자: 그대로 진행.
        }

        var load = UserAssemblies.Instance.Ensure(lookup.Artifact);
        return load.Assembly == null
            ? new ProjectTierInfo(0, ProjectTierReasons.LoadFailed, lookup.Artifact.AssemblyName, lookup.Artifact.AssemblyPath, Detail: load.Failure)
            : new ProjectTierInfo(1, ProjectTierReasons.Loaded, lookup.Artifact.AssemblyName, lookup.Artifact.AssemblyPath, load.Reloaded, load.LoadMs, Assembly: load.Assembly);
    }

    /// <summary>
    /// Tier 1용 XamlSchemaContext를 **렌더마다 새로** 만든다. 이렇게 하는 이유: ① WPF의 공유 스키마 컨텍스트는 어셈블리를 이름으로 찾고
    /// 결과를 캐시하므로 DLL을 다시 로드해도 옛 타입을 계속 쓴다 ② 서로 다른 프로젝트/버전이 같은 어셈블리 이름을 가져도 섞이지 않는다.
    /// 일반 <see cref="System.Xaml.XamlSchemaContext"/>는 WPF 규칙이 없어 ControlTemplate의 VisualTree 설정이 실패하므로(FrameworkElementFactory
    /// 변환 오류), WPF 내부 `WpfSharedXamlSchemaContext`를 새 인스턴스로 만든다(어셈블리는 기본 컨텍스트의 Resolving → <see cref="UserAssemblies"/>가 찾는다).
    /// 그 타입을 못 만들면 참조 목록(기본 컨텍스트의 어셈블리 + 사용자 컨텍스트의 어셈블리) 방식으로 되돌린다. Tier 0이면 null(WPF 기본 경로 사용).
    /// </summary>
    public static System.Xaml.XamlSchemaContext? CreateSchemaContext(ProjectTierInfo info)
    {
        if (info.Assembly == null)
        {
            return null;
        }
        else
        {
            return CreateWpfSchemaContext()
                ?? new System.Xaml.XamlSchemaContext(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a) == System.Runtime.Loader.AssemblyLoadContext.Default)
                    .Concat(UserAssemblies.GetContextAssemblies(info.Assembly))
                    .Distinct());
        }
    }

    /// <summary>WPF 내부 `WpfSharedXamlSchemaContext(XamlSchemaContextSettings, bool useV3Rules)`를 새로 만든다. 없으면 null.</summary>
    private static System.Xaml.XamlSchemaContext? CreateWpfSchemaContext()
    {
        var type = System.Windows.Markup.XamlReader.GetWpfSchemaContext().GetType();
        var constructor = type.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            [typeof(System.Xaml.XamlSchemaContextSettings), typeof(bool)]);
        // 같은 개수의 인자를 받는 생성자가 여럿인 마크업 확장(예: WPF UI의 `{ui:SymbolIcon ...}`)도 허용한다(기본값 false면 파싱 오류).
        var settings = new System.Xaml.XamlSchemaContextSettings { SupportMarkupExtensionsWithDuplicateArity = true };
        return constructor?.Invoke([settings, false]) as System.Xaml.XamlSchemaContext;
    }

    /// <summary>Tier 0일 때 사용자에게 보여 줄 이유 문장(자리표시자가 생긴 경우의 경고 메시지).</summary>
    public static string DescribeTier0(ProjectTierInfo info) => info.Reason switch
    {
        ProjectTierReasons.Untrusted => Loc.T("신뢰되지 않은 폴더라 프로젝트 DLL을 불러오지 않았습니다. 폴더를 신뢰하면 사용자 컨트롤이 실제로 그려집니다.", "The folder is not trusted, so the project DLL was not loaded. Trust the folder to render user controls for real."),
        ProjectTierReasons.NoFilePath => Loc.T("저장되지 않은 문서라 프로젝트를 찾을 수 없습니다.", "The document is unsaved, so its project cannot be found."),
        ProjectLookupReasons.NoProject => Loc.T("가까운 .csproj를 찾지 못해 사용자 컨트롤을 자리표시자로 표시합니다.", "No nearby .csproj was found, so user controls are shown as placeholders."),
        ProjectLookupReasons.NoArtifact => Loc.T("빌드 산출물(bin)이 없습니다. 먼저 `dotnet build` 하세요.", "There is no build output (bin). Run `dotnet build` first."),
        ProjectTierReasons.LoadFailed => Loc.T($"프로젝트 DLL을 불러오지 못했습니다: {info.Detail}", $"Could not load the project DLL: {info.Detail}"),
        _ => info.Reason,
    };
}
