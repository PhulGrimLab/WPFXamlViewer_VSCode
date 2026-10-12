using System.IO;
using XamlRenderHost.Projects;

namespace XamlRenderHost.Tests;

/// <summary>공유 프로젝트(`.projitems`)의 XAML은 그것을 Import하는 앱 프로젝트의 산출물로 찾는다.</summary>
[TestClass]
public sealed class SharedProjectLocatorTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "xv-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Shared", "Demo_Shared", "Views"));
        File.WriteAllText(Path.Combine(root, "src", "Shared", "Demo_Shared", "Demo_Shared.projitems"), "<Project/>");
        File.WriteAllText(Path.Combine(root, "src", "Shared", "Demo_Shared", "Views", "A.xaml"), "<Grid/>");
        return root;
    }

    private static string AddApp(string root, string name, bool importsShared, bool withBuild)
    {
        var dir = Path.Combine(root, "src", "Apps", name);
        Directory.CreateDirectory(dir);
        var import = importsShared ? "<Import Project=\"..\\..\\Shared\\Demo_Shared\\Demo_Shared.projitems\" Label=\"Shared\" />" : string.Empty;
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), "<Project>" + import + "</Project>");
        if (withBuild)
        {
            var output = Path.Combine(dir, "bin", "Debug");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, name + ".dll"), "x");
        }
        return dir;
    }

    [TestMethod]
    public void S01_SharedXaml_UsesImportingAppsArtifact()
    {
        var root = NewRoot();
        try
        {
            AddApp(root, "Other", importsShared: false, withBuild: true);
            AddApp(root, "AppA", importsShared: true, withBuild: false);
            var appB = AddApp(root, "AppB", importsShared: true, withBuild: true);

            var lookup = ProjectLocator.Find(Path.Combine(root, "src", "Shared", "Demo_Shared", "Views", "A.xaml"));
            Assert.AreEqual(ProjectLookupReasons.Found, lookup.Reason);
            Assert.AreEqual(Path.Combine(appB, "bin", "Debug", "AppB.dll"), lookup.Artifact!.AssemblyPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void S02_SharedXaml_NoBuiltImporter_ReportsNoArtifact_AndNoImporter_ReportsNoProject()
    {
        var root = NewRoot();
        try
        {
            var xaml = Path.Combine(root, "src", "Shared", "Demo_Shared", "Views", "A.xaml");
            Assert.AreEqual(ProjectLookupReasons.NoProject, ProjectLocator.Find(xaml).Reason);

            AddApp(root, "AppA", importsShared: true, withBuild: false);
            Assert.AreEqual(ProjectLookupReasons.NoArtifact, ProjectLocator.Find(xaml).Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
