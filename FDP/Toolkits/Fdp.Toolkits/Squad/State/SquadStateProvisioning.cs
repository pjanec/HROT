using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Fdp.Toolkit.Squad;

/// <summary>
/// `O1` — the ONE place that decides when an entity acquires its <see cref="SquadCognitiveState"/>.
///
/// <para>⭐⭐⭐ <b>Hook the FACT, not a write path.</b> 📐 Measured <c>2026-09-20</c>, and it cost a live
/// regression first: the state was initially provisioned inside <c>UnitHierarchySystem</c>'s assign
/// handler, which looked like "the one system that establishes the commander relationship". It is not —
/// <c>GenesisMaterializationSystem</c> builds a commander's <see cref="UnitRoster"/> independently for
/// scenario-loaded hierarchies. ⇒ in a <c>--mode all</c> run of <c>hill-attack-close</c> the commander
/// came up with NO squad state, and because every squad system now gates on the component's presence,
/// squad behaviour was silently OFF — ⛔ <b>with the golden test still green</b>.</para>
///
/// <para>⭐ <b>The invariant is simple and path-independent: an entity that owns a <see cref="UnitRoster"/>
/// is a commander, and a commander has squad state.</b> Both roster creators call this, so a third one
/// has exactly one thing to remember — and this doc to find.</para>
///
/// <para>⚠ <b>Add-only.</b> An existing commander keeps its accumulated state when another subordinate
/// is assigned; only the absence is filled.</para>
/// </summary>
public static class SquadStateProvisioning
{
    /// <summary>
    /// Ensures <paramref name="commander"/> carries a <see cref="SquadCognitiveState"/>.
    /// ⭐ Idempotent, and cheap when already present — one <c>HasComponent</c>.
    /// </summary>
    public static void EnsureForCommander(EntityRepository repo, Entity commander)
    {
        if (repo is null) return;
        // ⭐ CE-1319 (BV-60) — the report and order histories ride the same fact: a commander has them. Only where the brain's registry
        //   registered them (a node without a brain keeps none).
        if (repo.IsComponentTypeRegistered<Command.CommandReportHistory>() && !repo.HasComponent<Command.CommandReportHistory>(commander))
            repo.AddComponent(commander, default(Command.CommandReportHistory));
        if (repo.IsComponentTypeRegistered<Command.CommandOrderHistory>() && !repo.HasComponent<Command.CommandOrderHistory>(commander))
            repo.AddComponent(commander, default(Command.CommandOrderHistory));
        // ⭐ N-11 (CE-4473) — the drill trace and its recorded headline ride the same fact: provisioned here, so the commander's brain only
        //   ever REPLACES them (an in-place value) — a first add mid-tick is a structural change, deferred in the brains' parallel phase, and a
        //   second write in the same tick would have built from the empty trace
        if (repo.TryGetTable(typeof(SquadDrillTrace), out _) && !repo.HasManagedComponent<SquadDrillTrace>(commander))
            repo.SetManagedComponent(commander, SquadDrillTrace.Empty);
        if (repo.IsComponentTypeRegistered<SquadDrillState>() && !repo.HasComponent<SquadDrillState>(commander))
            repo.AddComponent(commander, default(SquadDrillState));
        // ⭐ F-11 (CE-4474) — the leadership record rides the same fact: provisioned empty (no leader yet — Succession names the first designated
        //   one on its first pass, the value it would have added), so the squad's step only ever writes it in place (a first add inside the
        //   per-squad phase is deferred, and MemberDown would miss it for a tick)
        if (repo.IsComponentTypeRegistered<SquadLeadership>() && !repo.HasComponent<SquadLeadership>(commander))
            repo.AddComponent(commander, default(SquadLeadership));
        if (repo.HasComponent<SquadCognitiveState>(commander)) return;
        repo.AddComponent(commander, default(SquadCognitiveState));
    }
}
