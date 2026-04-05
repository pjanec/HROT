using System.Linq;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Verifies the revised PACK2-U004 constraint: Hrot.Editor must not carry a direct DDS
/// (CycloneDDS) dependency. A <c>Hrot.NED</c> reference is now acceptable because
/// <see cref="Hrot.Editor.Adapters.EditorMissionService"/> implements
/// <c>IMissionEditorService</c> whose API surfaces <c>MissionPlan</c> from
/// <c>Hrot.NED.Descriptors</c>. The critical constraint is that CycloneDDS runtime
/// assemblies are NOT referenced directly by Hrot.Editor (DDS is only used by Hrot.ExCon
/// and Hrot.SimHost translators, never by the offline Editor).
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

        // CycloneDDS runtime must never appear directly in Hrot.Editor.dll.
        Assert.DoesNotContain("CycloneDDS.Runtime", assemblyNames);
        Assert.DoesNotContain("CycloneDDS.Core",    assemblyNames);
    }
}
