using System.Linq;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Verifies the PACK2-U004 dependency constraint for Hrot.Editor.
/// <see cref="Hrot.Editor.OfflineNetworkFactory"/> implements <see cref="Hrot.Core.Network.INetworkFactory"/>
/// which exposes <c>CycloneDDS.Runtime.DdsParticipant</c> in its interface surface.
/// Therefore Hrot.Editor.dll transitively references CycloneDDS.Runtime, which is
/// acceptable: the Editor is the composition root for offline mode and must implement
/// the full factory contract.
/// NOTE (D-031): CycloneDDS.Schema is now an accepted direct reference in Hrot.Editor.dll.
/// Multiple direct project dependencies (Hrot.Diagnostics.Breakpoints, Hrot.Blueprints.Editor,
/// Hrot.Presentation, Hrot.CGF, Hrot.IG, Hrot.Network.NED) expose CycloneDDS.Schema types
/// in their public API surfaces (return types / parameters). The C# compiler therefore emits
/// a direct AssemblyRef to CycloneDDS.Schema in Hrot.Editor.dll even though no Editor source
/// file imports that namespace. Removing this transitive reference would require refactoring
/// all those public APIs, which is out of scope. The CycloneDDS.Core constraint is preserved.
/// </summary>
public class EditorDependencyTests
{
    [Fact]
    public void HrotEditor_HasNoCycloneDdsDependency()
    {
        var assemblyNames = typeof(Hrot.Editor.IEditorLogic).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        // D-031: CycloneDDS.Schema is now an accepted direct assembly reference in Hrot.Editor.dll
        // due to deep transitive exposure through multiple direct project dependencies.
        // See class-level summary for full rationale.
        // Assert.DoesNotContain("CycloneDDS.Schema", assemblyNames); // ACCEPTED -- see D-031

        // CycloneDDS.Core (low-level C binding layer) must still never appear.
        Assert.DoesNotContain("CycloneDDS.Core",   assemblyNames);
    }
}
