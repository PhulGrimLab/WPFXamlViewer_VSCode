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
/// 규칙: 구성/TFM이 여러 개면 **수정 시각이 가장 최신인 DLL**을 고른다. 빌드는 하지 않는다. 상태 없는 정적 클래스(스레드 안전).
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
    /// 입력: XAML 파일의 절대 경로. 출력: 산출물 또는 실패 이유. 입출력 예외는 "없음"으로 취급한다(읽을 수 없는 폴더 등).
    /// </summary>
    public static ProjectLookup Find(string xamlFilePath)
    {
        var projectFile = FindNearestProjectFile(xamlFilePath);
        if (projectFile == null)
        {
            return new ProjectLookup(null, ProjectLookupReasons.NoProject);
        }
        else
        {
            // 프로젝트 있음: 산출물 탐색.
        }

        var assemblyName = ReadAssemblyName(projectFile);
        var binDirectory = Path.Combine(Path.GetDirectoryName(projectFile)!, BinDirectoryName);
        var newest = FindNewestAssembly(binDirectory, assemblyName + ".dll");
        return newest == null
            ? new ProjectLookup(null, ProjectLookupReasons.NoArtifact)
            : new ProjectLookup(new ProjectArtifact(projectFile, assemblyName, newest), ProjectLookupReasons.Found);
    }

    private static string? FindNearestProjectFile(string xamlFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(xamlFilePath));
        for (var level = 0; level <= MaxProjectSearchLevels && directory != null; level++)
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

    /// <summary>`&lt;AssemblyName&gt;` 속성이 있으면 그 값, 없거나 읽을 수 없으면 프로젝트 파일 이름(확장자 제외).</summary>
    private static string ReadAssemblyName(string projectFile)
    {
        try
        {
            var declared = XDocument.Load(projectFile).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value.Trim();
            if (!string.IsNullOrEmpty(declared))
            {
                return declared;
            }
            else
            {
                // 선언 없음: 기본값 사용.
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // 읽기/파싱 실패: 기본값 사용.
        }
        return Path.GetFileNameWithoutExtension(projectFile);
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
