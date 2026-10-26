using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.IG.Components;
using SelectionStateComponent = Hrot.IG.Components.SelectionState;

namespace Hrot.ScenarioEditor.Selection;

/// <summary>
/// The <see cref="ISelectionState"/> view over the ECS truth — <c>Hrot.IG.Components.SelectionState</c>.
///
/// <para>⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-1</c></b>. 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.1
/// names this class; §2.7.4 lists what it replaces. 🔴 <b>The defect it closes, measured
/// <c>2026-09-20</c>:</b> the editor and CGF each held a <c>DefaultSelectionState</c> — a
/// <see cref="HashSet{T}"/> with no connection to the world — while
/// <c>SelectionInteractionSystem</c> wrote the component. ⇒ two stores, one concept, synchronised by
/// nobody: a map click moved the ring (component) and not the Mission Editor
/// (<c>EditorSubsystem.Update</c> reads the hash set every frame), and an inspector click moved
/// neither. ⚠ The divergence was known and written down — see <c>ScenarioMissionView</c>'s remarks,
/// which say in as many words <em>"the two selection models converge under UXI-11"</em>.</para>
///
/// <para>⭐⭐ <b>It is a HANDLE, not a store, and that is the property that makes it safe to have
/// several.</b> Everything it reports is derived from the world on demand, so two instances over one
/// <see cref="EntityRepository"/> can never disagree — unlike the two hash sets they replace. The
/// only per-instance state is <see cref="HoveredEntity"/> (view-local by nature: no component backs
/// hover) and the <see cref="Version"/> observation, which is per-observer on purpose.</para>
///
/// <para>⛔ <b>It does NOT pin a world across a scenario reload.</b> The host nulls and re-creates it
/// beside its repository adapter, for the same reason: ⇒ a cached instance would hold a dead
/// <see cref="EntityRepository"/>.</para>
///
/// <para>⚠ <b>Writes go straight to the repository, not through a command buffer</b> — a deliberate
/// deviation from §2.5, recorded there. §2.5's own analysis shows the corruption risk it guards
/// against does not exist here (<c>EntityQuery</c> is an index scan, not a chunk walk), and the
/// deferral would put a one-frame lag inside <c>SyncSelection2D3D</c>'s read-back. 📌 <c>S-2</c>
/// removes the question entirely: every caller publishes a request and
/// <c>SelectionRequestSystem</c> writes during its own tick, which §2.5 already blesses.</para>
///
/// <para>⭐⭐ <b><c>CE-4049</c> (the coordinator's replay-UI-state handoff ①)</b> — the TRUTH is now <see cref="SelectionStore"/>, a
/// world singleton keyed by network id and never recorded; the component is its per-entity copy for the readers. Every write here goes
/// through <see cref="Write"/> (store + component together), and <see cref="Sync"/> rewrites the components from the store each frame, so
/// a replay keyframe or a seek — which clear and refill the component tables — never changes what the operator selected.</para>
/// </summary>
public sealed class EcsSelectionState : ISelectionState
{
    private readonly System.Func<EntityRepository> _worldOf;
    private EntityRepository? _queryWorld;
    private EntityQuery?      _query;

    // The last observation, kept so that Version bumps on a real change and only on a real change.
    private readonly List<Entity> _observed     = new();
    private readonly List<Entity> _scratch      = new();
    private Entity?               _observedPrimary;
    private int                   _version;

    public EcsSelectionState(EntityRepository world)
    {
        if (world is null) throw new System.ArgumentNullException(nameof(world));
        _worldOf = () => world;
    }

    /// <summary>
    /// ⭐ CE-3327 RB-7 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c> §7.4) — a view over the world a host SHOWS, resolved per call: the
    /// Replay Browser switches its shown world (a node's sandbox, the merged world) under one map, and a view pinned to the world it was
    /// built over selected in a world nobody looks at. <paramref name="world"/> must never return <see langword="null"/>.
    /// </summary>
    public EcsSelectionState(System.Func<EntityRepository> world)
        => _worldOf = world ?? throw new System.ArgumentNullException(nameof(world));

    private EntityRepository _world => _worldOf();

    // ⭐ Built once per world. Safe across frames: EntityEnumerator snapshots MaxIssuedIndex in ITS OWN
    //   constructor (EntityQuery.cs:120), i.e. per foreach -- not at Build() -- so entities created later are still seen.
    private EntityQuery _withSelectionState
    {
        get
        {
            var world = _world;
            if (_query is null || !ReferenceEquals(world, _queryWorld))
            {
                _query = world.Query().With<SelectionStateComponent>().WithLifecycle(EntityLifecycle.All).Build();
                _queryWorld = world;
            }
            return _query;
        }
    }

    /// <inheritdoc/>
    public Entity? HoveredEntity { get; set; }

    /// <inheritdoc/>
    public bool IsSelected(Entity entity)
    {
        if (entity.IsNull || !_world.IsAlive(entity)) return false;
        if (!_world.HasComponent<SelectionStateComponent>(entity)) return false;
        ref readonly var s = ref _world.GetComponentRO<SelectionStateComponent>(entity);
        return s.IsSelected || s.IsPrimarySelection;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Entity> SelectedEntities
    {
        get { Observe(); return _observed; }
    }

    /// <inheritdoc/>
    public Entity? PrimarySelected
    {
        get { Observe(); return _observedPrimary; }
        set
        {
            // ⚠ The long-standing contract: assigning primary REPLACES the selection.
            ClearCore();
            if (value.HasValue && !value.Value.IsNull && _world.IsAlive(value.Value))
                SetSelectedCore(value.Value, isPrimary: true);
        }
    }

    /// <inheritdoc/>
    public int Version
    {
        get { Observe(); return _version; }
    }

    /// <inheritdoc/>
    public void Add(Entity entity)
    {
        if (entity.IsNull || !_world.IsAlive(entity)) return;
        // Demote the incumbent primary to a secondary, then promote this one.
        foreach (var e in _withSelectionState)
        {
            if (e == entity || !_world.IsAlive(e)) continue;
            ref readonly var s = ref _world.GetComponentRO<SelectionStateComponent>(e);
            if (!s.IsPrimarySelection) continue;
            Write(_world, e, selected: true, primary: false);
        }
        SetSelectedCore(entity, isPrimary: true);
    }

    /// <inheritdoc/>
    public void Remove(Entity entity)
    {
        if (entity.IsNull || !_world.IsAlive(entity)) return;
        if (!_world.HasComponent<SelectionStateComponent>(entity)) return;

        bool wasPrimary = _world.GetComponentRO<SelectionStateComponent>(entity).IsPrimarySelection;
        Write(_world, entity, selected: false, primary: false);
        if (!wasPrimary) return;

        // ⚠ Removing the primary must leave a primary behind when anything is still selected --
        //   acceptance 11.3 ("exactly one entity has IsPrimarySelection after any selecting
        //   operation") is violated by a selection with none.
        foreach (var e in _withSelectionState)
        {
            if (!_world.IsAlive(e)) continue;
            ref readonly var s = ref _world.GetComponentRO<SelectionStateComponent>(e);
            if (!s.IsSelected) continue;
            Write(_world, e, selected: true, primary: true);
            return;
        }
    }

    /// <inheritdoc/>
    public void SetMultiple(IReadOnlyCollection<Entity> entities)
    {
        ClearCore();
        if (entities == null) return;
        bool anyPrimary = false;
        foreach (var e in entities)
        {
            if (e.IsNull || !_world.IsAlive(e)) continue;
            SetSelectedCore(e, isPrimary: !anyPrimary);
            anyPrimary = true;
        }
    }

    /// <inheritdoc/>
    public void Clear() => ClearCore();

    // ── the component writes -- the ONE implementation ───────────────────────────
    // ⭐ SelectionInteractionSystem delegates here rather than keeping its own copies, so there is a
    //   single place that decides what "selected" looks like on the component (ruling 9).

    /// <summary>Clears every <c>SelectionState</c> component in the world.</summary>
    public void ClearCore()
    {
        SelectionStore.Peek(_world)?.Clear();
        foreach (var e in _withSelectionState)
        {
            if (!_world.IsAlive(e)) continue;
            _world.SetComponent(e, new SelectionStateComponent
            {
                IsSelected         = false,
                IsPrimarySelection = false,
            });
        }
    }

    /// <summary>Marks one entity selected, adding the component when it is absent.</summary>
    public void SetSelectedCore(Entity entity, bool isPrimary) => Write(_world, entity, selected: true, primary: isPrimary);

    /// <summary>⭐ <c>CE-4049</c> — THE write: the store (when the entity has a network id) and its component copy, together.</summary>
    public static void Write(EntityRepository world, Entity entity, bool selected, bool primary)
    {
        long net = NetOf(world, entity);
        if (net > 0) SelectionStore.Of(world).Set(net, selected, primary);
        if (!world.HasComponent<SelectionStateComponent>(entity))
        {
            if (!selected && !primary) return;
            world.AddComponent(entity, new SelectionStateComponent());
        }
        world.SetComponent(entity, new SelectionStateComponent { IsSelected = selected, IsPrimarySelection = primary });
    }

    /// <summary>
    /// ⭐ <c>CE-4049</c> — rewrites every entity's component from the <see cref="SelectionStore"/>: the selected ones (re)marked, any
    /// other entity WITH a network id unmarked. Run each frame (<c>SelectionRequestSystem</c>), so a replay keyframe, a seek or a
    /// respawn is followed on the next frame. An entity with no network id is left as its direct write put it.
    /// </summary>
    public static void Sync(EntityRepository world)
    {
        if (SelectionStore.Peek(world) is not { } store) return;
        if (!world.IsComponentTypeRegistered<SelectionStateComponent>() || !world.IsComponentTypeRegistered<NetworkIdentity>()) return;
        foreach (long net in store.Selected)
        {
            var e = NetworkIdResolver.ResolveNetworkId(world, net);
            if (e.IsNull || !world.IsAlive(e)) continue;   // not in this frame (a seek before he existed): kept, marked when he is
            bool primary = store.Primary == net;
            if (world.HasComponent<SelectionStateComponent>(e))
            {
                ref readonly var c = ref world.GetComponentRO<SelectionStateComponent>(e);
                if (c.IsSelected && c.IsPrimarySelection == primary) continue;
            }
            else world.AddComponent(e, new SelectionStateComponent());
            world.SetComponent(e, new SelectionStateComponent { IsSelected = true, IsPrimarySelection = primary });
        }
        foreach (var e in world.Query().With<SelectionStateComponent>().Build())
        {
            ref readonly var c = ref world.GetComponentRO<SelectionStateComponent>(e);
            if (!c.IsSelected && !c.IsPrimarySelection) continue;
            long net = NetOf(world, e);
            if (net > 0 && !store.Contains(net))
                world.SetComponent(e, new SelectionStateComponent { IsSelected = false, IsPrimarySelection = false });
        }
    }

    private static long NetOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<NetworkIdentity>() ? NetworkIdResolver.RuntimeNetworkIdOf(world, e) : 0;

    // ── observation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the world and bumps <see cref="Version"/> when what we would report has changed.
    ///
    /// <para>⭐ The comparison is an exact element-wise one against the previous observation, not a
    /// hash: both lists come from the same ascending index scan, so their order is deterministic and
    /// a sequence compare cannot miss a change the way an order-independent digest can.</para>
    /// </summary>
    private void Observe()
    {
        _scratch.Clear();
        Entity? primary = null;

        foreach (var e in _withSelectionState)
        {
            if (!_world.IsAlive(e)) continue;
            ref readonly var s = ref _world.GetComponentRO<SelectionStateComponent>(e);
            if (!s.IsSelected && !s.IsPrimarySelection) continue;
            _scratch.Add(e);
            if (s.IsPrimarySelection && primary == null) primary = e;
        }

        // A selection with no explicit primary still reports one -- callers assigning through
        // SetSelectedCore always set it, but a component written by other code may not.
        if (primary == null && _scratch.Count > 0) primary = _scratch[0];

        if (primary == _observedPrimary && SameAsObserved()) return;

        _observed.Clear();
        _observed.AddRange(_scratch);
        _observedPrimary = primary;
        _version++;
    }

    private bool SameAsObserved()
    {
        if (_scratch.Count != _observed.Count) return false;
        for (int i = 0; i < _scratch.Count; i++)
            if (_scratch[i] != _observed[i]) return false;
        return true;
    }
}
