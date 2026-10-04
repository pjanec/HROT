using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-511</c> — the two-way bridge between an <c>Entity</c> a designer can see and the
/// <b>AUTHORED</b> id a pin can outlive a reload with.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 *(restart survival BY TRANSLATION)</b> · §8a.
///
/// <para>⭐⭐ <b>Why ONE type and not three delegates.</b> Restart survival needs three separate facts —
/// the load's id table, <c>Entity → runtime id</c>, and <c>runtime id → Entity</c> — and only the first
/// is pure. ⛔ Handing the Watch three <c>Func</c>s made every call site remember all three, which is the
/// silent-default shape one argument at a time. ⭐ One object: a host either has the bridge or it does
/// not, and <c>AiWatchWindow.HasEntityIdentity</c> is a single rail surface.</para>
///
/// <para>⛔⛔ <b>The two ECS halves are DELEGATES, deliberately.</b> The production implementations are
/// <c>NetworkIdResolver.FindEntityByNetworkId</c> and a <c>NetworkIdentity</c> read over the live world —
/// both in assemblies <c>Hrot.Editor.AiShared</c> does not reference. ⭐ Same host-installed shape as
/// <c>SetRunStateSource</c> / <c>WatchEntityPicker</c>.</para>
///
/// <para>⚠ <b>Every method answers rather than throws.</b> <c>0</c> and <c>default(Entity)</c> are real
/// answers: an entity spawned at RUNTIME has no authored ancestor, and an authored id from a previous
/// scenario is simply absent from this load. ⭐ The callers turn those into "within-session pin" and
/// "stale row" — ⛔ never into an exception a UI gesture would have to catch.</para>
/// </summary>
public sealed class WatchEntityIdentity
{
    private readonly Func<long, Entity> _entityByRuntimeId;
    private readonly Func<Entity, long> _runtimeIdOf;

    /// <param name="remap">⭐ The table the current load published *(shared, not copied — it is replaced
    /// in place on each load and every reader must see the same one)</param>
    /// <param name="entityByRuntimeId">⭐ Production: <c>NetworkIdResolver.FindEntityByNetworkId</c>.</param>
    /// <param name="runtimeIdOf">⭐ Production: read <c>NetworkIdentity.Value</c> off the entity, or <c>0</c>.</param>
    public WatchEntityIdentity(
        StagingRemapView   remap,
        Func<long, Entity> entityByRuntimeId,
        Func<Entity, long> runtimeIdOf)
    {
        Remap              = remap             ?? throw new ArgumentNullException(nameof(remap));
        _entityByRuntimeId = entityByRuntimeId ?? throw new ArgumentNullException(nameof(entityByRuntimeId));
        _runtimeIdOf       = runtimeIdOf       ?? throw new ArgumentNullException(nameof(runtimeIdOf));
    }

    /// <summary>⭐ The load's id table. ⚠ Its <c>Generation</c> is how a host knows a reload happened.</summary>
    public StagingRemapView Remap { get; }

    /// <summary>
    /// ⭐⭐ <b>The durable key for a live entity</b> — <c>Entity → runtime id → AUTHORED id</c>.
    /// ⚠ <c>0</c> for the sentinel entity, for an entity with no <c>NetworkIdentity</c>, and for one
    /// spawned at runtime. ⭐ All three mean the same thing to a pin: within-session only.
    /// </summary>
    public long StagingIdOf(Entity entity)
    {
        if (entity.Equals(default(Entity))) return 0;

        long runtimeId = _runtimeIdOf(entity);
        return runtimeId == 0 ? 0 : Remap.ToStaging(runtimeId);
    }

    /// <summary>
    /// ⭐⭐ <b>This load's entity for an authored id</b> — <c>AUTHORED id → runtime id → Entity</c>.
    /// ⚠ <c>default</c> when the id is not in this load's table *(a different scenario, or the entity was
    /// removed from it)* or when the translated id resolves to nothing.
    /// ⛔ It never falls back to treating the authored id AS a runtime id — 📌 the two are drawn from one
    /// numeric space, so that would silently resolve to the wrong entity.
    /// </summary>
    public Entity EntityForStagingId(long stagingId)
    {
        if (stagingId == 0) return default;

        long runtimeId = Remap.ToRuntime(stagingId);
        return runtimeId == 0 ? default : _entityByRuntimeId(runtimeId);
    }

    /// <summary>
    /// ⭐⭐ <b>The Watch's "pin on a picked entity" (<c>AQ55</c>), built ONCE for every host</b> — a map pick's runtime
    /// network id turned into a CONCRETE binding keyed by the AUTHORED id (<c>BP-511</c>: the runtime id is renumbered
    /// on every load, the authored one survives a reload). 📄 <c>DESIGN_Map_Picking_Unification.md</c>.
    /// <para>⛔ Answers <c>null</c> — never a half-built binding — when the pick yields nothing (no map, cancelled, an
    /// entity with no identity) or the id resolves to no live entity.</para>
    /// <para>⚠ An authored id of <c>0</c> is legitimate (a runtime-spawned entity): the pin is then within-session.</para>
    /// </summary>
    /// <param name="pickRuntimeId">The host's map pick — production: <c>IMapPickService.PickEntityAsync</c>, read at
    /// call time so a host whose adapter is built (or torn down) after this is composed still works.</param>
    public WatchEntityPicker PickerOver(Func<CancellationToken, Task<int>> pickRuntimeId)
    {
        if (pickRuntimeId == null) throw new ArgumentNullException(nameof(pickRuntimeId));
        return async ct =>
        {
            int runtimeId = await pickRuntimeId(ct).ConfigureAwait(false);
            if (runtimeId <= 0) return null;

            var entity = _entityByRuntimeId(runtimeId);
            if (entity.IsNull || entity.Equals(default(Entity))) return null;

            return EntityBinding.Concrete(Remap.ToStaging(runtimeId), entity);
        };
    }
}
