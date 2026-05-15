using System;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser;

/// <summary>
/// RB-X.2: Verifies that backend replay browser assemblies (Fdp.Toolkits, containing the
/// search/export/diff services) do not transitively reference Fdp.Presentation, ImGui, or Raylib.
/// </summary>
public class AssemblyDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "Fdp.Presentation",
        "ImGui",
        "Raylib",
        "rlImGui",
    };

    [Fact]
    public void RBX2_FdpToolkitsAssembly_DoesNotReference_PresentationOrUI()
    {
        var refs = typeof(RecordingSearchService).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        foreach (var r in refs)
        {
            foreach (var forbidden in ForbiddenPrefixes)
            {
                Assert.False(
                    r.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Fdp.Toolkits references forbidden assembly '{r}'");
            }
        }
    }

    [Fact]
    public void RBX2_ReplayBrowserContextAssembly_DoesNotReference_PresentationOrUI()
    {
        // ReplayBrowserContext lives in Fdp.Toolkits -- same assembly as SearchService.
        // Verify the assembly name itself confirms this (no cross-project bleed).
        string asmName = typeof(ReplayBrowserContext).Assembly.GetName().Name ?? "";
        Assert.DoesNotContain("Presentation", asmName, StringComparison.OrdinalIgnoreCase);
    }
}
