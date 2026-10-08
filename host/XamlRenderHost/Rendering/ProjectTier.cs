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
    public static ProjectTierInfo Resolve(bool allowProjectAssemblies, string? filePath)
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

        var load = UserAssemblies.Instance.Ensure(lookup.Artifact);
        return load.Assembly == null
            ? new ProjectTierInfo(0, ProjectTierReasons.LoadFailed, lookup.Artifact.AssemblyName, lookup.Artifact.AssemblyPath, Detail: load.Failure)
            : new ProjectTierInfo(1, ProjectTierReasons.Loaded, lookup.Artifact.AssemblyName, lookup.Artifact.AssemblyPath, load.Reloaded, load.LoadMs, Assembly: load.Assembly);
    }

    /// <summary>
    /// Tier 1용 XamlSchemaContext를 **렌더마다 새로** 만든다. 참조 목록 = 기본 컨텍스트의 어셈블리(WPF/프레임워크) + 이번에 로드한
    /// 사용자 어셈블리와 그 컨텍스트의 의존 어셈블리. 이렇게 하는 이유: ① WPF의 공유 스키마 컨텍스트는 어셈블리를 이름으로 찾고 결과를
    /// 캐시하므로 DLL을 다시 로드해도 옛 타입을 계속 쓴다 ② 서로 다른 프로젝트/버전이 같은 어셈블리 이름을 가져도 섞이지 않는다.
    /// Tier 0이면 null(WPF 기본 경로 사용).
    /// </summary>
    public static System.Xaml.XamlSchemaContext? CreateSchemaContext(ProjectTierInfo info)
    {
        if (info.Assembly == null)
        {
            return null;
        }
        else
        {
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a) == System.Runtime.Loader.AssemblyLoadContext.Default)
                .Concat(UserAssemblies.GetContextAssemblies(info.Assembly))
                .Distinct();
            return new System.Xaml.XamlSchemaContext(references);
        }
    }

    /// <summary>Tier 0일 때 사용자에게 보여 줄 이유 문장(자리표시자가 생긴 경우의 경고 메시지).</summary>
    public static string DescribeTier0(ProjectTierInfo info) => info.Reason switch
    {
        ProjectTierReasons.Untrusted => "신뢰되지 않은 폴더라 프로젝트 DLL을 불러오지 않았습니다. 폴더를 신뢰하면 사용자 컨트롤이 실제로 그려집니다.",
        ProjectTierReasons.NoFilePath => "저장되지 않은 문서라 프로젝트를 찾을 수 없습니다.",
        ProjectLookupReasons.NoProject => "가까운 .csproj를 찾지 못해 사용자 컨트롤을 자리표시자로 표시합니다.",
        ProjectLookupReasons.NoArtifact => "빌드 산출물(bin)이 없습니다. 먼저 `dotnet build` 하세요.",
        ProjectTierReasons.LoadFailed => $"프로젝트 DLL을 불러오지 못했습니다: {info.Detail}",
        _ => info.Reason,
    };
}
