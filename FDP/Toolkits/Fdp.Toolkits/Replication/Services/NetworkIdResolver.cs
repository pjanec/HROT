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
    public static Entity FindEntityByNetworkId(EntityRepository? repo, long networkId)
    {
        if (repo == null || networkId <= 0) return Entity.Null;

        foreach (var e in repo.Query().With<NetworkIdentity>().Build())
            if (repo.GetComponentRO<NetworkIdentity>(e).Value == networkId)
                return e;

        return Entity.Null;
    }
}
