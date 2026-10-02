using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// A4 / <c>O0</c> (<c>PLAN_Occurrence_Storage_Build</c>) — <b>how the Instance-Blueprint runtime is
/// composed into a host's Simulation phase</b>, in the assembly that owns the systems.
///
/// <para>⭐⭐⭐ <b>Why it moved here, and it is the SAME defect as <c>CE-161</c> one level up.</b>
/// 📐 <c>CE-161</c> (`2026-09-03`) measured a `--mode all` cluster aborting with
/// <i>"Component BlueprintBlackboard1024 is not registered"</i> on CGF, because the only code
/// registering the tiers lived in <c>Hrot.Blueprints.Editor</c>, whose sole production caller is the
/// Editor. Its fix moved the tier LIST down beside the tier TYPES. ⛔ <b>The components moved; the
/// SCHEDULING did not</b> — <c>BlueprintTickSystem</c> and <c>BlueprintMaintenanceSystem</c> were
/// still wired only by <c>EditorSubsystem</c>, so no other host ever ticked a blueprint Instance.</para>
///
/// <para>⚠ <b>Why the splice cannot simply be a global system.</b> <c>BlueprintTickSystem</c> is
/// <c>[UpdateInPhase(Simulation)]</c>, which <c>RegisterGlobalSystem</c> rejects, so it has to reach
/// the kernel inside a module's Simulation list. ⭐ That is a scheduling constraint, not a reason for
/// each host to hand-roll the placement: <c>CgfLogicPack</c> now performs this splice once, and every
/// host carrying the Brain capability inherits it.</para>
///
/// <para>⛔ <b>Not a second list.</b> <c>Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring</c>
/// forwards here, exactly as its <c>RegisterTierComponents</c> already forwards to
/// <c>BlueprintBlackboardTiers</c>.</para>
/// </summary>
public static class BlueprintRuntimeComposition
{
    /// <summary>
    /// Inserts <paramref name="bpTick"/> into <paramref name="simulationSystems"/> at the position its
    /// own <c>[UpdateBefore]</c> declarations require: immediately BEFORE the first system named by one
    /// of <see cref="BlueprintTickSystem"/>'s <c>[UpdateBefore]</c> attributes (the Locomotion / Weapon /
    /// Interaction dispatchers).
    ///
    /// <para>🔴 <b>Module-group execution order is ARRAY POSITION</b> — the kernel does not re-apply
    /// ordering attributes inside a module's system list. Both real compositions used to APPEND the tick
    /// after the dispatchers, silently downgrading the architect-approved <c>Q#16-B</c> <i>"intent is
    /// read the same tick"</i> contract to write-visible-next-tick. This helper makes the declared
    /// contract hold BY CONSTRUCTION.</para>
    ///
    /// <para>⭐ Targets are read off the attributes rather than hard-coded, so a future
    /// <c>[UpdateBefore]</c> on <see cref="BlueprintTickSystem"/> re-positions the splice automatically.
    /// ⚠ A list containing NO target (a degenerate or test composition with no dispatchers) appends the
    /// tick — exactly the old behaviour, where no ordering contract exists to honour.</para>
    /// </summary>
    public static List<IEcsModuleSystem> SpliceIntoSimulation(
        IEnumerable<IEcsModuleSystem> simulationSystems, BlueprintTickSystem bpTick)
    {
        if (simulationSystems is null) throw new ArgumentNullException(nameof(simulationSystems));
        if (bpTick is null)            throw new ArgumentNullException(nameof(bpTick));

        var targets = typeof(BlueprintTickSystem)
            .GetCustomAttributes<UpdateBeforeAttribute>()
            .Select(a => a.Target)
            .Where(t => t is not null)
            .ToArray();

        var result = new List<IEcsModuleSystem>(simulationSystems);
        int firstTarget = result.FindIndex(s => targets.Contains(s.GetType()));
        if (firstTarget >= 0)
            result.Insert(firstTarget, bpTick);
        else
            result.Add(bpTick);
        return result;
    }
}
