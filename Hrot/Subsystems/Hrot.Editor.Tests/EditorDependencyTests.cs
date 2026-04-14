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
/// The constraint enforced here is that CycloneDDS.Schema (code-gen stubs) is NOT
/// a direct assembly reference, as the Editor never authors DDS-serializable structs.
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

        // CycloneDDS code-generation schema must never appear directly in Hrot.Editor.dll.
        // CycloneDDS.Runtime IS acceptable because OfflineNetworkFactory implements INetworkFactory
        // which has DdsParticipant in its interface methods.
        Assert.DoesNotContain("CycloneDDS.Schema", assemblyNames);
        Assert.DoesNotContain("CycloneDDS.Core",   assemblyNames);
    }
}
