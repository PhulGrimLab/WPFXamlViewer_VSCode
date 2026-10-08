using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace XamlRenderHost.Projects;

/// <summary>사용자 어셈블리 로드 결과. Reloaded는 이번 호출에서 새로(다시) 로드했는지, LoadMs는 그때 걸린 시간.</summary>
public sealed record UserAssemblyLoad(Assembly? Assembly, bool Reloaded, long LoadMs, string? Failure);

/// <summary>
/// 프로젝트 빌드 산출물을 호스트 프로세스 안의 수집 가능한 <see cref="AssemblyLoadContext"/>에 로드하고 관리한다(doc/01 3.3 규칙 3, M4B B.2).
///
/// Owner: 프로세스 전체에서 하나(<see cref="Instance"/>). Lifetime: 프로세스 시작~종료(종료 시 임시 복사본 삭제).
/// 스레드: 어느 스레드에서도 호출될 수 있다(렌더는 STA 스레드, 테스트는 병렬). 상태(로드 목록)는 <see cref="_gate"/> 잠금으로 보호한다.
/// 로드 방식: 산출물 폴더 전체를 **임시 폴더에 복사한 뒤** 그 복사본에서 로드한다 — 원본 DLL을 잠그지 않아 사용자가 계속 빌드할 수 있다.
/// 원본의 수정 시각/크기가 바뀌면 이전 컨텍스트를 Unload하고 다시 만든다(갱신 시 재로드). 의존성은 deps.json 기반
/// <see cref="AssemblyDependencyResolver"/>로 해석하고, WPF 등 프레임워크 어셈블리는 기본 컨텍스트에서 공유한다.
/// XamlSchemaContext가 `assembly=이름`으로 찾는 어셈블리는 기본 컨텍스트의 Resolving 이벤트로 이 클래스가 대신 돌려준다.
/// </summary>
public sealed class UserAssemblies
{
    /// <summary>프로세스 전역 인스턴스(기본 AssemblyLoadContext가 하나뿐이라 로더도 하나다).</summary>
    public static UserAssemblies Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedProject> _projects = new(StringComparer.OrdinalIgnoreCase);

    private UserAssemblies()
    {
        AssemblyLoadContext.Default.Resolving += OnDefaultResolving;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAllTempDirectories();
    }

    /// <summary>프로젝트 하나의 로드 상태.</summary>
    private sealed class LoadedProject
    {
        public required string SourcePath { get; init; }
        public required DateTime SourceWriteTimeUtc { get; init; }
        public required long SourceLength { get; init; }
        public required string TempDirectory { get; init; }
        public required UserLoadContext Context { get; init; }
        public required Assembly Assembly { get; init; }
    }

    /// <summary>사용자 어셈블리 전용 컨텍스트. 수집 가능(Unload 가능)이며 deps.json으로 의존성을 찾는다.</summary>
    private sealed class UserLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public UserLoadContext(string mainAssemblyPath) : base("XamlViewer.User:" + Path.GetFileName(mainAssemblyPath), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        /// <summary>
        /// 사용자 어셈블리가 참조하는 어셈블리 중 deps.json이 가리키는 것(프로젝트 참조/NuGet 라이브러리)을 미리 이 컨텍스트에 로드한다.
        /// XamlSchemaContext에 "이 컨텍스트의 어셈블리 전체"를 넘기려면 지연 로드 전에 목록에 들어 있어야 하기 때문이다.
        /// 프레임워크 어셈블리는 resolver가 null을 돌려주므로 건너뛴다. 로드 실패는 무시한다(실제로 쓰일 때 오류가 보고된다).
        /// </summary>
        public void PreloadDependencies(Assembly main)
        {
            foreach (var reference in main.GetReferencedAssemblies())
            {
                var path = _resolver.ResolveAssemblyToPath(reference);
                if (path == null)
                {
                    continue;
                }
                else
                {
                    try
                    {
                        LoadFromAssemblyPath(path);
                    }
                    catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
                    {
                        // 이 의존성은 지금 쓸 수 없다: 실제 사용 시점의 오류에 맡긴다.
                    }
                }
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path); // null이면 기본 컨텍스트(프레임워크)가 처리한다.
        }
    }

    /// <summary>
    /// 산출물을 로드한다(이미 같은 버전이 로드되어 있으면 재사용). 실패는 예외 대신 Failure 문자열로 돌려준다(사용자 코드/파일 상태 문제라서).
    /// </summary>
    public UserAssemblyLoad Ensure(ProjectArtifact artifact)
    {
        lock (_gate)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(artifact.AssemblyPath);
                if (!info.Exists)
                {
                    return new UserAssemblyLoad(null, false, 0, "산출물 파일이 없습니다.");
                }
                else
                {
                    // 파일 있음.
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new UserAssemblyLoad(null, false, 0, ex.Message);
            }

            if (_projects.TryGetValue(artifact.ProjectFile, out var existing))
            {
                if (existing.SourcePath == artifact.AssemblyPath
                    && existing.SourceWriteTimeUtc == info.LastWriteTimeUtc && existing.SourceLength == info.Length)
                {
                    return new UserAssemblyLoad(existing.Assembly, false, 0, null);
                }
                else
                {
                    // 산출물이 바뀜: 이전 컨텍스트를 버리고 다시 로드한다.
                    Unload(artifact.ProjectFile, existing);
                }
            }
            else
            {
                // 처음 로드.
            }

            var started = System.Diagnostics.Stopwatch.StartNew();
            string? tempDirectory = null;
            try
            {
                tempDirectory = CopyOutputDirectory(Path.GetDirectoryName(artifact.AssemblyPath)!);
                var copiedPath = Path.Combine(tempDirectory, Path.GetFileName(artifact.AssemblyPath));
                var context = new UserLoadContext(copiedPath);
                var assembly = context.LoadFromAssemblyPath(copiedPath);
                context.PreloadDependencies(assembly);
                _projects[artifact.ProjectFile] = new LoadedProject
                {
                    SourcePath = artifact.AssemblyPath,
                    SourceWriteTimeUtc = info.LastWriteTimeUtc,
                    SourceLength = info.Length,
                    TempDirectory = tempDirectory,
                    Context = context,
                    Assembly = assembly,
                };
                return new UserAssemblyLoad(assembly, true, started.ElapsedMilliseconds, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or FileLoadException)
            {
                TryDeleteDirectory(tempDirectory);
                return new UserAssemblyLoad(null, false, started.ElapsedMilliseconds, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 지정한 사용자 어셈블리가 속한 컨텍스트의 어셈블리 전체(자신 + 미리 로드한 의존성)를 돌려준다.
    /// XamlSchemaContext의 참조 목록에 넣어 "같은 이름의 다른 복사본"과 섞이지 않게 하는 데 쓴다.
    /// </summary>
    public static IReadOnlyList<Assembly> GetContextAssemblies(Assembly userAssembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(userAssembly);
        return context == null ? new[] { userAssembly } : context.Assemblies.ToList();
    }

    /// <summary>
    /// 기본 컨텍스트가 못 찾은 어셈블리 요청(XamlSchemaContext의 `assembly=이름`)을 로드된 사용자 컨텍스트에서 찾아 돌려준다.
    /// 일치하는 것이 없으면 null(다른 해석기가 이어서 시도). 사용자 어셈블리의 의존 라이브러리도 그 컨텍스트가 찾는다.
    /// </summary>
    private Assembly? OnDefaultResolving(AssemblyLoadContext _, AssemblyName name)
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                if (string.Equals(project.Assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return project.Assembly;
                }
                else
                {
                    try
                    {
                        var dependency = project.Context.LoadFromAssemblyName(name);
                        if (dependency != null)
                        {
                            return dependency;
                        }
                        else
                        {
                            // 이 컨텍스트에는 없음.
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // 이 컨텍스트에는 없음: 다음 프로젝트 확인.
                    }
                }
            }
            return null;
        }
    }

    private void Unload(string projectFile, LoadedProject project)
    {
        _projects.Remove(projectFile);
        project.Context.Unload();
        TryDeleteDirectory(project.TempDirectory); // 잠금이 남아 실패해도 프로세스 종료 시 다시 시도한다.
    }

    /// <summary>산출물 폴더 전체를 새 임시 폴더로 복사한다(하위 폴더 포함). 원본은 읽기만 한다.</summary>
    private static string CopyOutputDirectory(string sourceDirectory)
    {
        var target = Path.Combine(Path.GetTempPath(), "xamlviewer-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(sourceDirectory, directory)));
        }
        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(sourceDirectory, file)), overwrite: true);
        }
        return target;
    }

    private void DeleteAllTempDirectories()
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                TryDeleteDirectory(project.TempDirectory);
            }
        }
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (directory == null)
        {
            return;
        }
        else
        {
            // 삭제 시도.
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 아직 잠겨 있다(컨텍스트가 완전히 수집되기 전). 임시 폴더라 치명적이지 않다.
        }
    }
}
