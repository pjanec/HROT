using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Services;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-508</c> — THE one place that turns a runtime <c>NetworkIdentity.Value</c> into an
/// <c>Entity</c>.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §8 ② · 📌 <c>R-77</c> *(count corrected: there were
/// **FOUR** copies, not two)</b>.
///
/// <para>⭐⭐ <b>What it consolidates</b> — measured `2026-08-25`, graph-enumerated and grep-confirmed:
/// <list type="bullet">
///   <item><c>ReplayBrowserSubsystem</c> — ⛔ unfiltered <c>Query().Build()</c> over EVERYTHING, then
///   <c>HasComponent</c> per entity</item>
///   <item><c>EditorMissionService</c> — filtered, ⛔ but <c>GetComponent</c> *(a struct copy)* and no
///   null-repo guard</item>
///   <item><c>EditorSubsystem</c> — filtered, ⛔ <c>GetComponent</c></item>
///   <item><c>MapPickServiceBridge</c> — ⭐ the closest: filtered + <c>GetComponentRO</c> + guards</item>
/// </list>
/// ⇒ ⭐ <b>the best of the four</b>: the FILTERED query *(so the scan visits only networked entities)*,
/// <c>GetComponentRO</c> *(no copy)*, and both guards — ⛔ and none of them was the keeper as it stood.</para>
///
/// <para>⛔⛔ <b>NO INDEX, NO CACHE — and that is a DESIGN CONSTRAINT, not an oversight.</b>
/// 📌 <c>DESIGN_Variable_Watch_Pinning.md</c> §4's <b>two-clocks rule</b>: a binding resolves only on a
/// SELECTION CHANGE or a LOAD, ⛔ <b>never on the tick</b>. ⇒ the linear scan is called a handful of
/// times per user gesture, and it is exactly what makes it correct: a maintained index has to be kept in
/// step with entity creation and destruction, and a stale one silently answers with the wrong entity.
/// ⭐ A caller that genuinely needs a per-tick lookup wants <see cref="NetworkEntityMap"/>, which is
/// maintained by the replication systems — ⛔ not a cache bolted onto this.</para>
///
/// <para>⭐ <c>EntityQuery</c> is a filter SPEC, not a snapshot *(it holds masks plus the repo and walks
/// it live)*, so building one per call costs a constructor. ⇒ ⛔ no reason for a caller to hold one.</para>
/// </summary>
public static class NetworkIdResolver
{
    /// <summary>
    /// ⭐ The entity carrying <paramref name="networkId"/>, or <see cref="Entity.Null"/>.
    ///
    /// <para>⚠ <c>Entity.Null</c> for: a null repository *(a host whose world is not up)*, a
    /// non-positive id *(<c>0</c> is "no network identity", never a valid one)*, and a live id that
    /// simply is not present. ⛔ None of the three throws — every call site is a lookup that must be
    /// able to answer "not here" without the caller wrapping it.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b>The FORWARD direction — an entity's runtime network id, or <c>0</c>.</b>
    ///
    /// <para>⭐⭐ <b>Added <c>2026-08-25</c> because a THIRD caller needed it</b> *(the Axis-B change-request
    /// egress)*, and 📐 measuring found the other two: <c>CgfSubsystem.RuntimeNetworkIdOf</c> and
    /// <c>EditorSubsystem.RuntimeNetworkIdOf</c> — **two private, line-for-line identical copies**, each
    /// with a comment referring to the other. ⇒ ⛔ writing a third would have been the moment a duplicate
    /// became a convention. 📌 The same lesson as <c>R-77</c>, in the opposite direction.</para>
    ///
    /// <para>⚠ <b><c>0</c> is the "no durable identity" sentinel</b> — a dead entity, a null world, an
    /// unreplicated entity and <c>Entity.Null</c> all answer it, and every caller already treats them
    /// alike. ⛔ Deliberately not an exception: asking an unreplicated entity for its network id is a
    /// normal question with a normal answer.</para>
    /// </summary>
    public static long RuntimeNetworkIdOf(EntityRepository? repo, Entity entity)
        => repo != null
        && !entity.Equals(default(Entity))
        && repo.IsAlive(entity)
        && repo.HasComponent<NetworkIdentity>(entity)
             ? repo.GetComponentRO<NetworkIdentity>(entity).Value
             : 0;

    public static Entity FindEntityByNetworkId(EntityRepository? repo, long networkId)
    {
        if (repo == null || networkId <= 0) return Entity.Null;

        foreach (var e in repo.Query().With<NetworkIdentity>().Build())
            if (repo.GetComponentRO<NetworkIdentity>(e).Value == networkId)
                return e;

        return Entity.Null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§6.7, 2026-09-11 — the PER-TICK-SAFE resolve: the world's maintained
    /// <see cref="NetworkEntityMap"/> first, the linear scan only as a correctness floor.</b>
    /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7.
    ///
    /// <para>⭐⭐ <b>Why this exists and <see cref="FindEntityByNetworkId"/> was not enough.</b> The type
    /// header above is right that a SELECTION-CHANGE resolve wants no index. ⛔ But a gizmo DRAG resolves
    /// its anchor on <b>every frame</b> of the drag, which is precisely the *"per-tick lookup"* that header
    /// sends to <see cref="NetworkEntityMap"/>. ⇒ this is that referral, made callable — ⛔ NOT a cache
    /// bolted onto the resolver: it owns no state and adds no second index. It reads the ONE index the
    /// replication systems already maintain.</para>
    ///
    /// <para>⭐⭐⭐ <b>The fallback is a FLOOR, not an exception.</b> 🔒 User, <c>2026-09-11</c>:
    /// *"replaybrowser is ecs module like any else. i do not want such exceptions."* ⇒ every ECS module
    /// gets the map, and this answers correctly in a world that does not have one yet (a fresh rail, a
    /// half-composed host) instead of silently reporting "not here". ⛔ The floor is O(n) — a module that
    /// relies on it in a drag loop is a defect to fix by giving THAT module the map, not by widening this.</para>
    ///
    /// <para>⭐⭐⭐ <b>THE MAP ANSWER IS VERIFIED AGAINST THE ENTITY, and that is what makes this safe to
    /// offer at all.</b> ⚠ The type header above is right that *"a stale index silently answers with the
    /// wrong entity"* — so the hit is confirmed by reading the entity's OWN
    /// <see cref="NetworkIdentity"/> (one component read, no query). ⇒ a stale or time-travelled map
    /// degrades to <b>slow</b> (the scan runs) and never to <b>wrong</b>. ⛔ Without this check the
    /// fast path would be trading correctness for speed, which the header correctly refuses.
    /// 📌 It is not hypothetical: a replay re-materialises the same network id as a DIFFERENT live handle
    /// after a seek, and the old entry can still be alive.</para>
    /// </summary>
    public static Entity ResolveNetworkId(EntityRepository? repo, long networkId)
    {
        if (repo == null || networkId <= 0) return Entity.Null;

        if (repo.HasSingletonManaged<NetworkEntityMap>())
        {
            var map = repo.GetSingletonManaged<NetworkEntityMap>();
            if (map != null
                && map.TryGetEntity(networkId, out var mapped)
                && RuntimeNetworkIdOf(repo, mapped) == networkId)
                return mapped;
        }

        return FindEntityByNetworkId(repo, networkId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>"This node KNEW this anchor and it is now gone"</b> — the one thing
    /// <see cref="ResolveNetworkId"/> cannot express, because it collapses <i>unknown</i> and
    /// <i>dead</i> into <c>Entity.Null</c>. 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7.
    ///
    /// <para>⛔⛔ <b>Why the distinction is load-bearing.</b> <c>GizmoInteractionIngressTranslator</c>
    /// turns a <c>DragUpdate</c>/<c>Commit</c> into a <b>Cancel</b> when the anchored entity died
    /// mid-drag — a real safety behaviour, not a fallback. ⚠ But *"an anchor I do not host"* must NOT
    /// synthesise a cancel: on a multi-node cluster most anchors belong to somebody else, and every one
    /// of them would cancel the local focus holder's drag. ⇒ only a <b>known-and-dead</b> anchor counts.</para>
    ///
    /// <para>⭐⭐ Both "known" signals are read: a live map entry whose entity is dead, and the map's
    /// GRAVEYARD (<c>Unregister</c> parks an id there for <c>graveyardDurationFrames</c>) — which is the
    /// case once the replication systems have processed the destruction. ⛔ Without a map this is
    /// <see langword="false"/>: a mapless node cannot know, and answering <i>"dead"</i> on a guess is the
    /// error that matters.</para>
    /// </summary>
    public static bool IsKnownDeadAnchor(EntityRepository? repo, long networkId)
    {
        if (repo == null || networkId <= 0) return false;
        if (!repo.HasSingletonManaged<NetworkEntityMap>()) return false;

        var map = repo.GetSingletonManaged<NetworkEntityMap>();
        if (map == null) return false;

        if (map.TryGetEntity(networkId, out var mapped) && !repo.IsAlive(mapped)) return true;
        return map.IsGraveyard(networkId);
    }
}
