using System.IO;
using System.Xml.Linq;

namespace XamlRenderHost.Projects;

/// <summary>프로젝트 빌드 산출물 정보. AssemblyPath는 `bin` 아래의 실제 DLL 경로다.</summary>
public sealed record ProjectArtifact(string ProjectFile, string AssemblyName, string AssemblyPath);

/// <summary>산출물 탐색 결과. Artifact가 null이면 Reason이 이유(H023 로그/경고에 쓴다).</summary>
public sealed record ProjectLookup(ProjectArtifact? Artifact, string Reason);

/// <summary>탐색 실패 이유 문자열(로그/프로토콜 경고가 그대로 사용한다).</summary>
public static class ProjectLookupReasons
{
    public const string Found = "Found";

    /// <summary>문서 위쪽에 .csproj가 없다.</summary>
    public const string NoProject = "NoProject";

    /// <summary>.csproj는 있으나 `bin` 아래에 빌드 산출물(DLL)이 없다 — "먼저 dotnet build" 안내 대상.</summary>
    public const string NoArtifact = "NoArtifact";
}

/// <summary>
/// XAML 파일에서 가장 가까운 `.csproj`와 그 빌드 산출물(`bin/&lt;Config&gt;/&lt;TFM&gt;/&lt;AssemblyName&gt;.dll`)을 찾는다(doc/01 3.3 규칙 2, M4B B.1).
/// 규칙: 구성/TFM이 여러 개면 **수정 시각이 가장 최신인 DLL**을 고른다(DLL이 하나도 없으면 `&lt;AssemblyName&gt;.exe`). 빌드는 하지 않는다. 상태 없는 정적 클래스(스레드 안전).
/// </summary>
public static class ProjectLocator
{
    /// <summary>XAML 폴더에서 위로 올라가며 .csproj를 찾는 최대 단계.</summary>
    private const int MaxProjectSearchLevels = 12;

    /// <summary>`bin` 아래에서 DLL을 찾을 때 내려가는 최대 깊이(bin/Config/TFM/RID 정도).</summary>
    private const int MaxArtifactDepth = 4;

    private const string ProjectFilePattern = "*.csproj";
    private const string BinDirectoryName = "bin";

    /// <summary>
    /// 입력: XAML 파일의 절대 경로, (선택) 워크스페이스 루트. 루트를 주면 그 **안**의 `.csproj`/`.projitems`/산출물만 찾는다(루트 밖 폴더의 프로젝트 DLL을 불러오지 않게).
    /// 출력: 산출물 또는 실패 이유. 입출력 예외는 "없음"으로 취급한다(읽을 수 없는 폴더 등).
    /// </summary>
    public static ProjectLookup Find(string xamlFilePath, string? workspaceRoot = null)
    {
        var projectFile = FindNearestProjectFile(xamlFilePath, workspaceRoot);
        if (projectFile == null)
        {
            // 공유 프로젝트(`.projitems`)의 XAML이면 그것을 가져가는(Import) 앱 프로젝트의 산출물을 쓴다.
            return FindViaSharedProject(xamlFilePath, workspaceRoot) ?? new ProjectLookup(null, ProjectLookupReasons.NoProject);
        }
        else
        {
            // 프로젝트 있음: 산출물 탐색.
        }

        var artifact = FindArtifact(projectFile);
        return artifact == null
            ? new ProjectLookup(null, ProjectLookupReasons.NoArtifact)
            : new ProjectLookup(artifact, ProjectLookupReasons.Found);
    }

    private static ProjectArtifact? FindArtifact(string projectFile)
    {
        var assemblyName = ReadAssemblyName(projectFile);
        var binDirectory = Path.Combine(Path.GetDirectoryName(projectFile)!, BinDirectoryName);
        // DLL 우선. WPF/WinForms 앱 프로젝트(OutputType=WinExe/Exe)는 DLL이 없고 <AssemblyName>.exe만 나오므로 그때만 EXE를 쓴다.
        var newest = FindNewestAssembly(binDirectory, assemblyName + ".dll")
            ?? FindNewestAssembly(binDirectory, assemblyName + ".exe");
        return newest == null ? null : new ProjectArtifact(projectFile, assemblyName, newest);
    }

    private const string SharedItemsPattern = "*.projitems";

    /// <summary>공유 프로젝트 폴더에서 위로 올라가 `.csproj`를 찾는 최대 단계(`src/Shared/X` 기준 저장소 루트 근처까지).</summary>
    private const int MaxSharedSearchLevels = 3;

    /// <summary>`.csproj`를 찾을 때 내려가는 최대 깊이와 건너뛸 폴더.</summary>
    private const int MaxSharedProjectDepth = 4;
    private static readonly string[] SkippedDirectories = { "bin", "obj", "node_modules", ".git", ".vs" };

    /// <summary>
    /// XAML이 공유 프로젝트(`.projitems`가 있는 폴더 트리) 안에 있으면, 그 `.projitems`를 Import하는 `.csproj` 중 산출물이 있는 가장 최신 것을 돌려준다.
    /// 공유 프로젝트 자체는 빌드 산출물이 없어서 가장 가까운 `.csproj`가 없다. 못 찾으면 null.
    /// </summary>
    private static ProjectLookup? FindViaSharedProject(string xamlFilePath, string? workspaceRoot)
    {
        var sharedItems = FindNearestSharedItems(xamlFilePath);
        if (sharedItems != null && !IsUnderRoot(sharedItems, workspaceRoot))
        {
            return null; // 워크스페이스 밖의 공유 프로젝트.
        }
        else
        {
            // 경계 안(또는 경계 없음).
        }
        if (sharedItems == null)
        {
            return null;
        }
        else
        {
            // 공유 프로젝트 안의 XAML: 가져가는 앱 프로젝트 탐색.
        }

        var sharedName = Path.GetFileName(sharedItems);
        var root = Path.GetDirectoryName(sharedItems);
        for (var level = 0; level < MaxSharedSearchLevels && root != null; level++)
        {
            var parent = Path.GetDirectoryName(root);
            if (parent == null || !IsUnderRoot(parent, workspaceRoot))
            {
                break; // 워크스페이스 루트 위로는 훑지 않는다(디스크 전체 스캔과 밖의 프로젝트 선택을 막는다).
            }
            root = parent;
        }
        var budget = new ScanBudget();

        ProjectArtifact? best = null;
        var bestTime = DateTime.MinValue;
        var sawProject = false;
        foreach (var project in EnumerateProjectFiles(root, depth: 0, budget))
        {
            if (!ImportsSharedItems(project, sharedName))
            {
                continue;
            }
            else
            {
                sawProject = true;
            }

            var artifact = FindArtifact(project);
            var time = artifact == null ? DateTime.MinValue : File.GetLastWriteTimeUtc(artifact.AssemblyPath);
            if (artifact != null && time > bestTime)
            {
                best = artifact;
                bestTime = time;
            }
            else
            {
                // 산출물이 없거나 더 오래된 것.
            }
        }

        return best != null
            ? new ProjectLookup(best, ProjectLookupReasons.Found)
            : sawProject ? new ProjectLookup(null, ProjectLookupReasons.NoArtifact) : null;
    }

    private static string? FindNearestSharedItems(string xamlFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(xamlFilePath));
        for (var level = 0; level <= MaxProjectSearchLevels && directory != null; level++)
        {
            try
            {
                var items = Directory.GetFiles(directory, SharedItemsPattern);
                if (items.Length > 0)
                {
                    return items.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
                }
                else
                {
                    // 이 폴더에는 없음: 위로.
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 읽을 수 없는 폴더: 위로 계속.
            }
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    /// <summary>공유 프로젝트 탐색의 방문 폴더 수 상한(렌더마다 큰 디스크를 훑어 응답이 늦어지는 것을 막는다).</summary>
    private sealed class ScanBudget
    {
        public int RemainingDirectories = 3000;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string? directory, int depth, ScanBudget budget)
    {
        if (directory == null || budget.RemainingDirectories-- <= 0)
        {
            yield break;
        }
        else
        {
            // 탐색 시작 폴더 있음.
        }

        string[] projects;
        string[] subdirectories = Array.Empty<string>();
        try
        {
            projects = Directory.GetFiles(directory, ProjectFilePattern);
            if (depth < MaxSharedProjectDepth)
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            else
            {
                // 최대 깊이.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var project in projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            yield return project;
        }

        foreach (var subdirectory in subdirectories.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(subdirectory), StringComparer.OrdinalIgnoreCase) || IsReparsePoint(subdirectory))
            {
                continue; // 건너뛸 폴더, 또는 링크(정션/심볼릭 링크: 밖의 큰 폴더나 순환으로 이어질 수 있다)
            }
            else
            {
                foreach (var found in EnumerateProjectFiles(subdirectory, depth + 1, budget))
                {
                    yield return found;
                }
            }
        }
    }

    /// <summary>프로젝트 파일이 `…\이름.projitems`를 Import하는가(경로 구분자와 대소문자 무시).</summary>
    private static bool ImportsSharedItems(string projectFile, string sharedItemsFileName)
    {
        try
        {
            var text = File.ReadAllText(projectFile);
            return text.Replace('/', '\\').Contains("\\" + sharedItemsFileName, StringComparison.OrdinalIgnoreCase)
                && text.Contains("Import", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>경로가 워크스페이스 루트 안(같은 폴더 포함)인가. 루트가 없으면 항상 true. 대소문자 무시, 구분자 정규화.</summary>
    public static bool IsUnderRoot(string path, string? workspaceRoot)
    {
        if (string.IsNullOrEmpty(workspaceRoot))
        {
            return true;
        }
        else
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // 읽을 수 없으면 따라가지 않는다.
        }
    }

    private static string? FindNearestProjectFile(string xamlFilePath, string? workspaceRoot)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(xamlFilePath));
        for (var level = 0; level <= MaxProjectSearchLevels && directory != null && IsUnderRoot(directory, workspaceRoot); level++)
        {
            try
            {
                var projects = Directory.GetFiles(directory, ProjectFilePattern);
                if (projects.Length > 0)
                {
                    return projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
                }
                else
                {
                    // 이 폴더에는 없음: 위로.
                }
            }
            catch (IOException)
            {
                // 읽을 수 없는 폴더: 위로 계속.
            }
            catch (UnauthorizedAccessException)
            {
                // 권한 없음: 위로 계속.
            }
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    /// <summary>어셈블리 이름에 들어가면 안 되는 문자: XAML 속성/URI에 이름을 끼워 넣을 때 따옴표·꺾쇠·구분자가 구조를 깨거나 경로를 바꿀 수 있다.</summary>
    private static readonly char[] UnsafeAssemblyNameCharacters = { '"', '\'', '<', '>', '&', ';', '=', ',', '\\', '/', ':', '*', '?', '|', '\r', '\n', '\t', '\0' };

    /// <summary>
    /// 어셈블리 이름이 XAML 텍스트/pack URI/로드 경로에 그대로 쓰여도 안전한가(비어 있지 않고, 길이 제한 이내이며, 위험 문자가 없다).
    /// 프로젝트 파일(`&lt;AssemblyName&gt;`)이나 XAML(`assembly=…`, `;component/`)이 준 문자열이라 검증 없이 쓰지 않는다.
    /// </summary>
    public static bool IsSafeAssemblyName(string? name)
        => !string.IsNullOrWhiteSpace(name) && name.Length <= MaxAssemblyNameLength && name.IndexOfAny(UnsafeAssemblyNameCharacters) < 0 && !name.Contains("..", StringComparison.Ordinal);

    /// <summary>어셈블리 이름 최대 길이(비정상적으로 긴 입력 차단).</summary>
    private const int MaxAssemblyNameLength = 256;

    /// <summary>`&lt;AssemblyName&gt;` 속성이 있으면 그 값(안전할 때만), 없거나 읽을 수 없거나 안전하지 않으면 프로젝트 파일 이름(확장자 제외).</summary>
    private static string ReadAssemblyName(string projectFile)
    {
        try
        {
            // 프로젝트 파일에는 DTD가 필요 없다: 엔티티 확장 폭주(billion laughs)를 막으려고 DTD를 금지하고 읽는다.
            using var reader = System.Xml.XmlReader.Create(projectFile, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
            var declared = XDocument.Load(reader).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value.Trim();
            if (IsSafeAssemblyName(declared))
            {
                return declared!;
            }
            else
            {
                // 선언 없음 또는 위험 문자 포함: 기본값 사용.
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // 읽기/파싱 실패: 기본값 사용.
        }
        var fallback = Path.GetFileNameWithoutExtension(projectFile);
        return IsSafeAssemblyName(fallback) ? fallback : "UnsafeAssemblyName";
    }

    private static string? FindNewestAssembly(string binDirectory, string fileName)
    {
        if (!Directory.Exists(binDirectory))
        {
            return null;
        }
        else
        {
            // bin 있음: 재귀 탐색.
        }

        string? newest = null;
        var newestTime = DateTime.MinValue;
        foreach (var candidate in EnumerateFiles(binDirectory, fileName, depth: 0))
        {
            var time = File.GetLastWriteTimeUtc(candidate);
            if (time > newestTime)
            {
                newest = candidate;
                newestTime = time;
            }
            else
            {
                // 더 오래된 산출물.
            }
        }
        return newest;
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string fileName, int depth)
    {
        var direct = Path.Combine(directory, fileName);
        if (File.Exists(direct))
        {
            yield return direct;
        }
        else
        {
            // 이 폴더에는 없음.
        }

        if (depth >= MaxArtifactDepth)
        {
            yield break;
        }
        else
        {
            // 더 내려갈 수 있음.
        }

        string[] subdirectories;
        try
        {
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var subdirectory in subdirectories)
        {
            foreach (var found in EnumerateFiles(subdirectory, fileName, depth + 1))
            {
                yield return found;
            }
        }
    }
}
