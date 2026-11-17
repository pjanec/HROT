using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8012</c> (ST-5, SV-4 / SV-6; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §16a) — the tag on an entity
    /// a stochastic unit's split created: its unit, its slot, and whether it is still HELD (created, kept out of the fight until the handover).
    /// </summary>
    [ComponentId(GlobalComponentIds.AggregateChild)]
    [DataPolicy(DataPolicy.NoScenario)]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AggregateChild
    {
        public Entity Unit;
        public byte Slot;
        /// <summary>1 while held (Materialising), 0 after the handover.</summary>
        public byte Held;
        private ushort _pad0;
        private uint _pad1;
    }

    /// <summary>
    /// ⭐ <c>CE-8012</c> (§16b, SV-9, SV-15) — the numbers the resolution manager decides by. ⭐ The POLICY (an unmanaged World Environment
    /// component set by <c>SetResolutionPolicy</c>, detail areas) is agg-behaviors' (§23.3 ST-5); the manager reads it through
    /// <see cref="ResolutionManagerSystem.Policy"/> — these defaults are SV-9's FIXED bands until it lands.
    /// </summary>
    public readonly record struct ResolutionBands(
        float PhysicalBand = 400f,       // a physical enemy nearer than this ⇒ physical (SV-9)
        float LeadMetres = 200f,         // D-P1's lead: the process's budget × the closing speed, widened into the trigger band
        float MergeMargin = 200f,        // ⭐ F-23 (R-4286) — the hysteresis a merge needs BEYOND THE SPLIT TRIGGER (band + lead + this ≈ 800 m)
        SimDuration QuietSeconds = default,   // ⭐ S0 — a length of sim time (30 s, Default) · ⭐ F-23 — beyond it, unobserved and with no man in contact, this long before an automatic merge
        float ForcedRange = 300f,        // C-7: a physical enemy this near forces the fallback handover AT ONCE
        SimDuration DeadlineSeconds = default)   // ⭐ S0 — a length of sim time (10 s, Default) · D-P5: the process's deadline (fallback posts are used from the start until D-P3's planners land)
    {
        /// <summary>SV-9's fixed bands. ⚠ Use THIS, never <c>new ResolutionBands()</c> — a record struct's parameterless <c>new()</c> is all
        /// zeros, not the defaults above (measured: every band 0, the manager never split).</summary>
        public static ResolutionBands Default => new(400f, 200f, 200f, 30f, 300f, 10f);
    }

    /// <summary>
    /// ⭐ <c>CE-8012</c> — the seam the manager drives a split / merge through (§16a). ⭐ Production: <see cref="BusResolutionSwitch"/> (transient
    /// creation requests with derived identities, SV-4). A test may supply its own.
    /// </summary>
    public interface IUnitResolutionSwitch
    {
        /// <summary>Materialise: create the unit's live slots at <paramref name="posts"/>, HELD (SV-6, SV-7).</summary>
        void Materialise(EntityRepository repo, Entity unit, ReadOnlySpan<Vector3> posts);
        /// <summary>Has every requested child been created?</summary>
        bool AllPresent(EntityRepository repo, Entity unit);
        /// <summary>The one-tick handover (D-P6): the latest slot table mapped onto the children (X-4), the dead removed, the held released.</summary>
        void Handover(EntityRepository repo, Entity unit);
        /// <summary>The trigger was withdrawn mid-process: the held children removed (SV-7) — logged, never seen.</summary>
        void Cancel(EntityRepository repo, Entity unit);
        /// <summary>Re-aggregation (D-P7): sum the children back into the slot table, the centroid into the unit, then remove them.</summary>
        void Merge(EntityRepository repo, Entity unit);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8012</c> (X-3 / X-4 / SV-4, D-P5) — the mapping rules, written once: a child's STABLE identity, the fallback posts, a slot's
    /// health onto a soldier's and back.
    /// </summary>
    public static class UnitSplits
    {
        /// <summary>A man's health (the slot scale, <see cref="UnitSlot.Health"/>).</summary>
        public const float SlotFullHealth = 25f;

        /// <summary>⭐ SV-4 — the child of <paramref name="unitGuid"/> in <paramref name="slot"/>: the same unit split twice gives the same children.</summary>
        public static Guid ChildGuid(Guid unitGuid, int slot) => EntityGuidDerivation.Derive(unitGuid, "child:" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>The unit's identity: its <see cref="EntityGuid"/>, else one derived from nothing but its index (a unit with no scenario key).</summary>
        public static Guid UnitGuid(EntityRepository repo, Entity unit)
            => repo.IsComponentTypeRegistered<EntityGuid>() && repo.HasComponent<EntityGuid>(unit) ? repo.GetComponentRO<EntityGuid>(unit).Value
             : EntityGuidDerivation.Derive(Guid.Empty, "unit:" + unit.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>⭐ D-P5 — the FALLBACK posts: the live slots spread across the frontage, square to the heading, in rows of ≤ 12.</summary>
        public static int FallbackPosts(in AggregateUnit u, Vector3 at, in UnitSlotTable slots, Span<Vector3> into)
        {
            int n = Math.Min(slots.Present(), into.Length);
            if (n == 0) return 0;
            float rad = u.Heading.Degrees * (MathF.PI / 180f);
            var across = new Vector3(MathF.Cos(rad), -MathF.Sin(rad), 0f);
            var back = new Vector3(-MathF.Sin(rad), -MathF.Cos(rad), 0f);
            int perRow = Math.Min(12, n);
            float width = MathF.Max(u.Width, 2f * perRow);
            for (int k = 0; k < n; k++)
            {
                int row = k / perRow, col = k % perRow;
                into[k] = at + across * (-width * 0.5f + width * (col + 0.5f) / perRow) + back * (row * 10f);
            }
            return n;
        }

        /// <summary>X-4 — a slot's health onto a soldier of max <paramref name="max"/>, and back.</summary>
        public static float ToSoldier(float slotHealth, float max) => max <= 0f ? slotHealth : slotHealth / SlotFullHealth * max;
        public static float ToSlot(float current, float max) => max <= 0f ? current : MathF.Max(0f, current) / max * SlotFullHealth;

        /// <summary>⭐ CE-8013 ② — a child that counts: HELD (Constructing, SV-6) or Active; ⛔ not one already in teardown (a merge's).</summary>
        public static bool Live(EntityRepository repo, Entity c)
        {
            var l = repo.GetLifecycleState(c);
            return l == EntityLifecycle.Active || l == EntityLifecycle.Constructing;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8012</c> (ST-5, §16a / §16b, R-1 … R-7, C-7) — THE RESOLUTION MANAGER: one serial pass every second over the stochastic units
    /// (and the split ones), deciding by the bands (<see cref="ResolutionBands"/>), the instructor's pin (SV-14: instructor &gt; author &gt;
    /// automatic) and the nearest PHYSICAL enemy; driving the process stage by stage on <see cref="AggregateUnit.Splitting"/> (recorded) through
    /// an <see cref="IUnitResolutionSwitch"/>. ⛔ The unit's brain never takes part (§16b): a split is invisible to its tactics.
    /// <para>Stochastic: enemy within band + lead (or pin Physical) ⇒ Planning → (fallback posts, D-P5 — the real planners' plan/assign split
    /// is agg-behaviors' D-P3) Materialising → every child present ⇒ Handover (one tick) ⇒ Physical. ⭐ C-7: an enemy within the forced range
    /// skips straight to Materialising. Withdrawn before the handover (beyond the band + lead + margin, not pinned) ⇒ cancelled (SV-7).
    /// Physical (split): out of the band + margin and quiet for the quiet time (or pin Stochastic) ⇒ Merging ⇒ Stochastic.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ResolutionManagerSystem : IEcsModuleSystem
    {
        public const float StepSeconds = 1f;
        private readonly IUnitResolutionSwitch _switch;
        private readonly bool _gateOnAuthority;
        private EntityQuery? _units, _people;
        private EntityRepository? _queriesFor;
        private SimInstant _last = (SimInstant)(-1.0);
        private readonly List<(Vector3 At, ForceId Force)> _enemies = new();
        private Vector3[] _posts = new Vector3[UnitSlotTable.Capacity];
        // ⭐ CE-8013 ④ — per step: every entity a PHYSICAL sensor holds as acquired → the forces of those observers (bit per ForceId)
        private readonly Dictionary<long, uint> _seenBy = new();

        /// <summary>The policy in force (agg-behaviors' <c>ResolutionPolicy</c> plugs in here); default SV-9's fixed bands.</summary>
        public Func<EntityRepository, ResolutionBands> Policy { get; init; } = static _ => ResolutionBands.Default;

        public ResolutionManagerSystem(IUnitResolutionSwitch @switch, bool gateOnAuthority = false)
        {
            _switch = @switch ?? throw new ArgumentNullException(nameof(@switch));   // ⛔ never a silent default: a manager with no switch does nothing
            _gateOnAuthority = gateOnAuthority;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<EntityInfo>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if ((double)now - (double)_last < StepSeconds - 1e-6) return;
            _last = now;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<SimTransform>().With<EntityInfo>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _people = repo.Query().With<EntityInfo>().With<SimTransform>().Build();
                _queriesFor = repo;
            }
            var bands = Policy(repo);
            // the PHYSICAL entities that can see / fight a unit: everything with a side and a place that is not a unit and not hidden
            _enemies.Clear();
            bool tagged = repo.IsComponentTypeRegistered<AggregateChild>();
            foreach (var e in _people!)
            {
                if (repo.HasComponent<AggregateUnit>(e)) continue;
                if (Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(repo, e)) continue;
                if (tagged && repo.HasComponent<AggregateChild>(e) && repo.GetComponentRO<AggregateChild>(e).Held != 0) continue;
                var f = repo.GetComponentRO<EntityInfo>(e).ForceId;
                if (f == ForceId.Neutral) continue;
                _enemies.Add(((Vector3)repo.GetComponentRO<SimTransform>(e).Position, f));
            }
            CollectObserved(repo);
            foreach (var unit in _units!)
                Decide(repo, unit, now, in bands);
        }

        /// <summary>⭐ CE-8013 ④ (§16b R-6 UNOBSERVED) — who each physical sensor currently holds (acquired), by the observer's force.</summary>
        private unsafe void CollectObserved(EntityRepository repo)
        {
            _seenBy.Clear();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorContactList>()
                || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) return;
            foreach (var sensor in repo.Query().With<Fdp.Toolkit.Perception.Components.SensorContactList>().With<Fdp.Toolkit.Replication.Components.PartMetadata>().Build())
            {
                var owner = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(sensor).ParentEntity;
                if (!repo.IsAlive(owner) || !repo.HasComponent<EntityInfo>(owner)) continue;
                uint bit = 1u << ((int)repo.GetComponentRO<EntityInfo>(owner).ForceId & 31);
                ref readonly var list = ref repo.GetComponentRO<Fdp.Toolkit.Perception.Components.SensorContactList>(sensor);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list.State[i] != (byte)Fdp.Toolkit.Perception.Components.SensorContactState.Acquired) continue;
                    long id = list.EntityIds[i];
                    _seenBy[id] = _seenBy.TryGetValue(id, out uint m) ? m | bit : bit;
                }
            }
        }

        /// <summary>⭐ CE-8013 ④ — is any of <paramref name="unit"/>'s children held (acquired) by a sensor of ANOTHER force? Then an automatic
        /// merge waits: men never vanish in front of a trainee (§16b).</summary>
        private bool Observed(EntityRepository repo, Entity unit)
        {
            if (_seenBy.Count == 0 || !repo.IsComponentTypeRegistered<AggregateChild>()) return false;
            uint mine = repo.HasComponent<EntityInfo>(unit) ? 1u << ((int)repo.GetComponentRO<EntityInfo>(unit).ForceId & 31) : 0u;
            foreach (var c in repo.Query().With<AggregateChild>().Build())
                if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && _seenBy.TryGetValue((long)c.PackedValue, out uint by) && (by & ~mine) != 0)
                    return true;
            return false;
        }

        private float NearestEnemy(Vector3 at, ForceId mine)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].Force == mine) continue;
                float d = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(_enemies[i].At.X, _enemies[i].At.Y));
                if (d < best) best = d;
            }
            return best;
        }

        private void Decide(EntityRepository repo, Entity unit, SimInstant now, in ResolutionBands b)
        {
            ref var u = ref repo.GetComponentRW<AggregateUnit>(unit);
            var at = (Vector3)repo.GetComponentRO<SimTransform>(unit).Position;
            float d = NearestEnemy(at, repo.GetComponentRO<EntityInfo>(unit).ForceId);
            bool trigger = d < b.PhysicalBand + b.LeadMetres;
            bool forced = d < b.ForcedRange;
            bool withdrawn = d > b.PhysicalBand + b.LeadMetres + b.MergeMargin;
            var res = u.Effective;

            if (res == AggregateResolution.Stochastic)
            {
                switch (u.Splitting)
                {
                    case SplitStage.NotSplitting:
                        if (u.Pin == ResolutionPin.PinStochastic || u.Pin == ResolutionPin.PinKeep && !forced) return;
                        if (u.Pin == ResolutionPin.PinPhysical || trigger)
                        {
                            u.Splitting = SplitStage.Planning;
                            u.SplittingSince = now;
                            if (forced) goto case SplitStage.Planning;   // C-7: no planning time
                        }
                        return;
                    case SplitStage.Planning:
                        if (withdrawn && u.Pin != ResolutionPin.PinPhysical && !forced)
                        {
                            u.Splitting = SplitStage.NotSplitting;
                            if (HasPlan(repo, unit)) repo.RemoveComponent<PlannedPosts>(unit);   // ⭐ DP3-B — a withdrawn trigger clears the plan
                            return;
                        }
                        if (!repo.IsComponentTypeRegistered<UnitSlotTable>() || !repo.HasComponent<UnitSlotTable>(unit)) return;
                        // ⭐ DP3-B (R-4286) — a unit that IS being planned (its planner added PlannedPosts) waits for Ready or the deadline;
                        //   a forced split (C-7) never waits; a unit nobody plans for goes at once, as before.
                        bool planned = HasPlan(repo, unit);
                        if (planned && !forced && repo.GetComponentRO<PlannedPosts>(unit).Ready == 0
                            && (double)now - (double)u.SplittingSince < b.DeadlineSeconds) return;
                        // ⭐ D-P5 — the fallback posts for every live slot; ⭐ DP3-D ① — the planned ones over the first Count (the order Materialise walks)
                        int n = UnitSplits.FallbackPosts(in u, at, repo.GetComponentRO<UnitSlotTable>(unit), _posts);
                        if (planned)
                        {
                            ref readonly var plan = ref repo.GetComponentRO<PlannedPosts>(unit);
                            int m = System.Math.Min(System.Math.Min((int)plan.Count, PlannedPosts.Capacity), n);
                            for (int i = 0; i < m; i++) if (plan.TryGet(i, out var post)) _posts[i] = post.Point;   // an entry with no post keeps its fallback point (agg-behaviors' PostedFlag, unified at the R-4286 merge)
                        }
                        _switch.Materialise(repo, unit, _posts.AsSpan(0, n));
                        u.Splitting = SplitStage.Materialising;
                        return;
                    case SplitStage.Materialising:
                        if (withdrawn && u.Pin != ResolutionPin.PinPhysical && !forced)
                        {
                            _switch.Cancel(repo, unit);   // SV-7: the held soldiers removed — never seen
                            u.Splitting = SplitStage.NotSplitting;
                            return;
                        }
                        if (!_switch.AllPresent(repo, unit)) return;
                        u.Splitting = SplitStage.Handover;
                        goto case SplitStage.Handover;
                    case SplitStage.Handover:
                        _switch.Handover(repo, unit);   // D-P6: one tick — the latest slot table mapped, the held released
                        u.Resolution = AggregateResolution.Physical;
                        u.Splitting = SplitStage.NotSplitting;
                        u.SplittingSince = now;   // in Physical: when the unit last became merge-eligible (reset below while not)
                        return;
                }
                return;
            }

            if (res != AggregateResolution.Physical || !HasChildren(repo, unit)) return;
            // a split unit: re-aggregate when beyond the SPLIT TRIGGER + the margin and quiet (automatic), or at once on the instructor's pin.
            // ⭐ F-23 (R-4286) — the merge line is band + lead + margin, never inside the trigger (band + lead): with an enemy parked between
            //   the old merge line (band + margin, 500 m) and the trigger (600 m) the unit split and merged every ~63 s (measured).
            bool far = d > b.PhysicalBand + b.LeadMetres + b.MergeMargin;
            if (u.Pin == ResolutionPin.PinPhysical || u.Pin == ResolutionPin.PinKeep) return;
            if (u.Pin != ResolutionPin.PinStochastic)
            {
                // ⭐ CE-8013 ④ — UNOBSERVED too; ⭐ F-23 — and no man of it in CONTACT (automatic only)
                if (!far || Observed(repo, unit) || InContact(repo, unit)) { u.SplittingSince = now; return; }
                if ((double)now - (double)u.SplittingSince < b.QuietSeconds) return;
            }
            u.Splitting = SplitStage.Merging;
            _switch.Merge(repo, unit);
            u.Resolution = AggregateResolution.Stochastic;
            u.Splitting = SplitStage.NotSplitting;
            u.SplittingSince = now;
        }

        /// <summary>⭐ DP3-B — is the unit being planned for (its planner added <see cref="PlannedPosts"/>)?</summary>
        private static bool HasPlan(EntityRepository repo, Entity unit)
            => repo.IsComponentTypeRegistered<PlannedPosts>() && repo.HasComponent<PlannedPosts>(unit);

        /// <summary>⭐ F-23 (R-4286) — is a man of the split unit in CONTACT: a live child that saw an enemy in the last
        /// <see cref="ContactSteps"/> perception steps (its <c>TargetMemory</c>, against <c>PerceptionClock</c> — the stamp's own clock).</summary>
        private static unsafe bool InContact(EntityRepository repo, Entity unit)
        {
            if (!repo.IsComponentTypeRegistered<AggregateChild>() || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()) return false;
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(repo);
            foreach (var c in repo.Query().With<AggregateChild>().With<Fdp.Toolkit.Perception.Components.TargetMemory>().Build())
            {
                if (repo.GetComponentRO<AggregateChild>(c).Unit != unit) continue;
                ref readonly var mem = ref repo.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(c);
                for (int k = 0; k < mem.Count; k++)
                    if (now - mem.LastSeenTick[k] <= ContactSteps) return true;
            }
            return false;
        }

        /// <summary>A sighting this many perception steps old still counts as contact (the sensors re-stamp a target they hold every step).</summary>
        private const uint ContactSteps = 2;

        private static bool HasChildren(EntityRepository repo, Entity unit)
        {
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return false;
            foreach (var c in repo.Query().With<AggregateChild>().IncludeAll().Build())
                if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && UnitSplits.Live(repo, c)) return true;
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8012</c> (SV-4, SV-6, SV-7, X-4) — THE production switch: a split publishes one TRANSIENT <see cref="EntityCreationRequest"/>
    /// per live slot (the existing creation path, <c>CreateEntityRequestSystem</c>; never saved — <c>ScenarioIgnoreTag</c> by IsTransient)
    /// carrying the EXPLICIT identity <see cref="UnitSplits.ChildGuid"/>, its post, and the HELD tag; the handover maps the LATEST slot table
    /// (health, stress) onto each child and releases it, removing the slots that died meanwhile; a merge sums the children back and removes
    /// them. ⚠ <see cref="ChildType"/> (the TKB composite's slot → type) MUST be set by the host that has the TKB (the silent-default rule:
    /// none ⇒ <see cref="Materialise"/> throws). ⭐ CE-8013: the held children stay Constructing (<see cref="HeldChildParticipant"/>) until the
    /// handover; with <see cref="Lifecycle"/> set, a child leaves through the lifecycle's teardown (every node sees it go), else it is
    /// destroyed in place (a single world). ⭐ Ammunition crosses both ways (<see cref="UnitAmmunition.ToSoldier"/> / <see cref="UnitAmmunition.ToSlot"/>).
    /// </summary>
    public sealed class BusResolutionSwitch : IUnitResolutionSwitch
    {
        private readonly Dictionary<Entity, int> _requested = new();

        /// <summary>The TKB type of the unit's composite slot (host-supplied).</summary>
        public Func<EntityRepository, Entity, int, TkbTypeId>? ChildType { get; init; }

        /// <summary>⭐ CE-8013 — the node's lifecycle: a released child leaves through its teardown. Null ⇒ destroyed in place (single-world
        /// hosts, rails) — ⚠ a networked host passes it, or the children's replicas outlive the merge.</summary>
        public Func<Fdp.Toolkit.Lifecycle.EntityLifecycleModule?>? Lifecycle { get; init; }

        private void Remove(EntityRepository repo, Entity c, string why)
        {
            var elm = Lifecycle?.Invoke();
            if (elm != null && repo.GetLifecycleState(c) == EntityLifecycle.Active)
                elm.BeginDestruction(c, repo.GlobalVersion, why, ((ISimulationView)repo).GetCommandBuffer());
            else repo.DestroyEntity(c);   // a held child (Constructing) is never seen: removed at once; the participant abandons its wait
        }

        public void Materialise(EntityRepository repo, Entity unit, ReadOnlySpan<Vector3> posts)
        {
            if (ChildType == null) throw new InvalidOperationException("BusResolutionSwitch.ChildType is not set: the host must resolve the TKB composite (CE-8013)");
            ref readonly var slots = ref repo.GetComponentRO<UnitSlotTable>(unit);
            var guid = UnitSplits.UnitGuid(repo, unit);
            var force = repo.HasComponent<EntityInfo>(unit) ? repo.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;
            string unitName = repo.HasComponent<EntityInfo>(unit) ? (string)repo.GetComponentRO<EntityInfo>(unit).Name : "";
            int k = 0;
            for (int i = 0; i < slots.Count && k < posts.Length; i++)
            {
                if (!slots.IsUp(i)) continue;
                int slot = slots.Slots[i].CompositeSlot;
                repo.Bus.PublishManaged(new EntityCreationRequest
                {
                    RequestId = EntityGuidDerivation.Derive(guid, "split:" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    TkbType = ChildType(repo, unit, slot),
                    IsTransient = true,
                    InitialComponents = new List<object>
                    {
                        new EntityGuid(UnitSplits.ChildGuid(guid, slot)),
                        new SimTransform { Position = posts[k], Rotation = Quaternion.Identity },
                        new AggregateChild { Unit = unit, Slot = (byte)i, Held = 1 },
                        new EntityInfo { ForceId = force, Name = ChildName(unitName, slot) },   // ⭐ F-22 (R-4286) — checks, goldens, the UI name him
                    },
                });
                k++;
            }
            _requested[unit] = k;
        }

        /// <summary>⭐ F-22 (R-4286) — a split child's name: <c>&lt;unit name&gt;/&lt;composite slot&gt;</c> (e.g. <c>Alpha/3</c>) — stable across splits,
        /// the same leaf every time. A unit with no name gives just the slot.</summary>
        public static string ChildName(string unitName, int compositeSlot)
            => string.IsNullOrEmpty(unitName) ? compositeSlot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                               : unitName + "/" + compositeSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public bool AllPresent(EntityRepository repo, Entity unit)
            => _requested.TryGetValue(unit, out int n) && Count(repo, unit) >= n;

        public void Handover(EntityRepository repo, Entity unit)
        {
            ref readonly var slots = ref repo.GetComponentRO<UnitSlotTable>(unit);
            bool health = repo.IsComponentTypeRegistered<Health>();
            bool stress = repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.Stress>();
            foreach (var c in Children(repo, unit))
            {
                ref var tag = ref repo.GetComponentRW<AggregateChild>(c);
                int i = tag.Slot;
                if (i >= slots.Count || !slots.IsUp(i)) { Remove(repo, c, "split: died before the handover"); continue; }   // died meanwhile (SV-7)
                if (health && repo.HasComponent<Health>(c))
                {
                    ref var h = ref repo.GetComponentRW<Health>(c);
                    h.Current = UnitSplits.ToSoldier((float)slots.Slots[i].Health, h.Max);
                }
                if (stress && repo.HasComponent<Fdp.Toolkit.Behavior.Components.Stress>(c))
                    repo.GetComponentRW<Fdp.Toolkit.Behavior.Components.Stress>(c).Level = (float)slots.Slots[i].Stress;
                UnitAmmunition.ToSoldier(repo, c, slots.Slots[i].Ammo);   // ⭐ CE-8013 ③ (X-4)
                tag.Held = 0;
            }
            _requested.Remove(unit);
        }

        public void Cancel(EntityRepository repo, Entity unit)
        {
            foreach (var c in Children(repo, unit)) Remove(repo, c, "split cancelled");
            _requested.Remove(unit);
        }

        public void Merge(EntityRepository repo, Entity unit)
        {
            ref var slots = ref repo.GetComponentRW<UnitSlotTable>(unit);
            bool health = repo.IsComponentTypeRegistered<Health>();
            bool stress = repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.Stress>();
            // a slot whose child is gone (killed and removed) is down; the rest take their child's state
            Span<bool> seen = stackalloc bool[UnitSlotTable.Capacity];
            var sum = Vector3.Zero; int n = 0;
            foreach (var c in Children(repo, unit))
            {
                int i = repo.GetComponentRO<AggregateChild>(c).Slot;
                if (i < slots.Count)
                {
                    seen[i] = true;
                    if (health && repo.HasComponent<Health>(c))
                    {
                        var h = repo.GetComponentRO<Health>(c);
                        slots.Slots[i].Health = (Half)UnitSplits.ToSlot(h.Current, h.Max);
                    }
                    if (stress && repo.HasComponent<Fdp.Toolkit.Behavior.Components.Stress>(c))
                        slots.Slots[i].Stress = (Half)repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.Stress>(c).Level;
                    int rounds = UnitAmmunition.ToSlot(repo, c);   // ⭐ CE-8013 ③ (X-4) — −1: he carries no weapon state, the slot keeps its count
                    if (rounds >= 0) slots.Slots[i].Ammo = (ushort)rounds;
                }
                if (repo.HasComponent<SimTransform>(c)) { sum += (Vector3)repo.GetComponentRO<SimTransform>(c).Position; n++; }
                Remove(repo, c, "merged");
            }
            for (int i = 0; i < slots.Count; i++) if (!seen[i] && slots.IsUp(i)) slots.Slots[i].Health = (Half)0f;
            if (n > 0 && repo.HasComponent<SimTransform>(unit)) repo.GetComponentRW<SimTransform>(unit).Position = sum / n;   // the centroid (X-4)
        }

        private static int Count(EntityRepository repo, Entity unit)
        {
            int n = 0;
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return 0;
            foreach (var c in repo.Query().With<AggregateChild>().IncludeAll().Build()) if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && UnitSplits.Live(repo, c)) n++;
            return n;
        }

        private static List<Entity> Children(EntityRepository repo, Entity unit)
        {
            var list = new List<Entity>();   // a split / merge is rare: allocation is fine here (R-2084: not every frame)
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return list;
            foreach (var c in repo.Query().With<AggregateChild>().IncludeAll().Build()) if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && UnitSplits.Live(repo, c)) list.Add(c);
            return list;
        }
    }
}
