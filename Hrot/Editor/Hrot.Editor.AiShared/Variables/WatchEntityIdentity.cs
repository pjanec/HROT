using System;
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
}
