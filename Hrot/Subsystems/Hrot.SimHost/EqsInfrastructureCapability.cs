#nullable enable
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// EQS result ingestion as cross-role infrastructure — <c>CE-221</c>, the sibling of
/// <see cref="CoreInfrastructureCapabilities.UnitHierarchy"/>.
/// </summary>
/// <remarks>
/// <para><c>EqsResultUpdateSystem</c> was a member of <c>CgfLogicPack</c>, <c>SimHostCoreLogicPack</c>
/// and <c>StrideMuscleModuleSet</c> alike — the same mis-assignment, and the second of the two types
/// that made the Stride editor's <c>[SingleInstance]</c> boot failure fatal. See
/// <see cref="CoreInfrastructureCapabilities"/> for the full reasoning; it is not repeated here.</para>
///
/// <para>⚠ <b>Why it lives in <c>Hrot.SimHost</c> and not beside its sibling in <c>Hrot.Common</c>.</b>
/// 📐 Measured: <c>EqsResultUpdateSystem</c> is declared in <c>Hrot.SimHost.Systems</c>, and
/// <c>Hrot.IG</c> references only <c>Hrot.Common</c>. Homing this capability in <c>Hrot.Common</c>
/// would need either a new IG → SimHost project reference or a symbol move. IG never carried this
/// system, so the split costs nothing today and keeps every host's system set byte-for-byte what it
/// was — which is what makes <c>CE-221</c>'s fix purely structural and cheap to gate.</para>
///
/// <para>⭐ The end-state worth doing later, separately: move <c>EqsResultUpdateSystem</c> into
/// <c>Hrot.Common.Systems</c> beside <c>UnitHierarchySystem</c> and merge the two capabilities. That
/// is a Roslyn symbol move (⛔ never a text replace) and is deliberately not bundled here.</para>
/// </remarks>
public sealed class EqsResultUpdateCapability : INodeCapability
{
    private readonly EqsResultUpdateSystem _system = new();

    public string Key => CapabilityKeys.EqsResultUpdate;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    public void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> simulation,
        List<IEcsModuleSystem> postSimulation)
    {
        if (simulation is null) throw new ArgumentNullException(nameof(simulation));
        simulation.Add(_system);
    }
}
