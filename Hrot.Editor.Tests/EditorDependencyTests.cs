using System.Linq;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Verifies PACK2-U004 constraint: Hrot.Editor has no transitive dependency on Hrot.NED.
/// </summary>
public class EditorDependencyTests
{
    [Fact]
    public void HrotEditor_HasNoTransitiveNedDependency()
    {
        var assemblyNames = typeof(Hrot.Editor.IEditorLogic).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Hrot.NED", assemblyNames);
    }
}
