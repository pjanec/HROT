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
        if (repo.HasComponent<SquadCognitiveState>(commander)) return;
        repo.AddComponent(commander, default(SquadCognitiveState));
    }
}
