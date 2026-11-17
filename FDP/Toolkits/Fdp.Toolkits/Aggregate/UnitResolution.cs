using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
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
    /// ⭐⭐ <c>CE-8307</c> (R-8382 / R-8385; DESIGN_Split_One_Level.md) — ONE DIRECT subordinate of a unit's TKB composite, what its split creates
    /// (X-5 / D-P7: one level at a time): <paramref name="Key"/> its 1-based index among the direct subordinates (the child key
    /// <c>child:&lt;Key&gt;</c> and the name <c>&lt;unit&gt;/&lt;Key&gt;</c>), its type and designation, the composite LEAVES under it
    /// (<paramref name="FirstSlot"/> 1-based, <paramref name="Slots"/> of them — what <see cref="UnitSlot.CompositeSlot"/> numbers), whether it is
    /// itself a composite (<paramref name="IsUnit"/> ⇒ it is created as a STOCHASTIC unit with its share of the slot table, else as a man) and,
    /// for a unit, its <paramref name="Echelon"/>. For a squad every direct subordinate is a man and <c>Key == FirstSlot</c>.
    /// </summary>
    public readonly record struct SplitChild(int Key, TkbTypeId Type, TacticalDesignation Designation, int FirstSlot, int Slots, bool IsUnit, byte Echelon)
    {
        /// <summary>Does composite leaf <paramref name="compositeSlot"/> (1-based) lie under this child?</summary>
        public bool Holds(int compositeSlot) => compositeSlot >= FirstSlot && compositeSlot < FirstSlot + Slots;
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

        /// <summary>⭐⭐ <c>CE-8307</c> (R-8389; DESIGN_Split_One_Level.md §6 Q-h; supersedes X-2's <c>RostersItsMen</c>, R-8372) — the scope of the roster
        /// and of the order re-resolution: a unit whose split's DIRECT children (<paramref name="directChildren"/>: a squad's men, a platoon's squads, a
        /// company's platoons) fit its <see cref="UnitRoster"/>. ONE rule: the switch counts its <see cref="BusResolutionSwitch.Composition"/>, the
        /// behaviours side the composite's direct count (<c>StochasticUnitFill.RostersItsChildren</c>). ⛔ The echelon is not read — an authored table
        /// never sets it (📌 <c>agg-merge-back</c>, R-8372).</summary>
        public static bool RostersItsChildren(int directChildren) => directChildren <= UnitRoster.Capacity;

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

        /// <summary>
        /// ⭐⭐ <c>X-2</c> X2-D (R-8357, agg-behaviors; DESIGN_X2_Order_Reresolution.md §7) — why an AUTOMATIC switch of <c>unit</c> to the target
        /// resolution must WAIT (non-null = the reason): the unit's current order has no play at the target (an untyped order, or a kind with no
        /// row there — SV-10). ⭐ Only the automatic manager consults it: the instructor's pin (SV-14) always goes (the order is then cleared and
        /// its commander hears TaskFailed — the behaviours' reconciler does that). Null (the default) ⇒ nothing ever waits. ⛔ The manager learns
        /// nothing about tactics: the host plugs the behaviours' answer in (<c>CgfLogicPack</c>: <c>OrderReissue.BlockReason</c>).
        /// </summary>
        public Func<EntityRepository, Entity, AggregateResolution, string?>? SwitchBlocked { get; init; }
        // ⭐ X2-D — the last reason each unit waited for (logged once per change, at Debug); a unit is added on its first block only (rare)
        private readonly Dictionary<Entity, string> _blockedWhy = new();

        /// <summary>⭐ X2-D — does <see cref="SwitchBlocked"/> hold the automatic switch of <paramref name="unit"/> to <paramref name="target"/>?</summary>
        private bool Blocked(EntityRepository repo, Entity unit, AggregateResolution target)
        {
            var why = SwitchBlocked?.Invoke(repo, unit, target);
            if (why == null)
            {
                if (_blockedWhy.Count > 0) _blockedWhy.Remove(unit);
                return false;
            }
            if (!_blockedWhy.TryGetValue(unit, out var last) || last != why)
            {
                _blockedWhy[unit] = why;
                Fdp.Core.Logging.FdpLog<ResolutionManagerSystem>.Debug("[ResolutionManager] unit #{0} waits to go " + target + ": " + why, unit.Index);
            }
            return true;
        }

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
            {
                // ⭐ CE-8307 — a HELD unit child (a platoon's squad before its handover) is not in the fight yet: never decided (SV-6; in a host
                //   with the lifecycle it is Constructing and not even visited)
                if (tagged && repo.HasComponent<AggregateChild>(unit) && repo.GetComponentRO<AggregateChild>(unit).Held != 0) continue;
                Decide(repo, unit, now, in bands);
            }
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
            // ⭐ CE-8307 (R-8395, review F4) — the instructor's PinPhysical on a parent holds for its unit children too (SV-14: the instructor
            //   wins always): DERIVED, never copied, so lifting the parent's pin lifts it below
            var pin = EffectivePin(repo, unit, u.Pin);

            if (res == AggregateResolution.Stochastic)
            {
                switch (u.Splitting)
                {
                    case SplitStage.NotSplitting:
                        if (pin == ResolutionPin.PinStochastic || pin == ResolutionPin.PinKeep && !forced) return;
                        if (pin == ResolutionPin.PinPhysical || trigger)
                        {
                            // ⭐ X2-D (R-8357) — an AUTOMATIC split waits while the unit's order has no physical play; the pin goes regardless (SV-14);
                            //   ⭐ CE-8307 (R-8392, review F1) — and a FORCED split (C-7) is never held: contact is imminent
                            if (pin != ResolutionPin.PinPhysical && !forced && Blocked(repo, unit, AggregateResolution.Physical)) return;
                            // ⭐ CE-8307 (R-8397, review F5) — a just-released UNIT child (a platoon's squad) on the AUTOMATIC path waits until it holds its
                            //   parent's order (its Defend ⇒ DP3 plans its posts) or the deadline passes
                            if (pin != ResolutionPin.PinPhysical && !forced && AwaitsParentsOrder(repo, unit, in u, now, in b)) return;
                            u.Splitting = SplitStage.Planning;
                            u.SplittingSince = now;
                            if (forced) goto case SplitStage.Planning;   // C-7: no planning time
                        }
                        return;
                    case SplitStage.Planning:
                        if (withdrawn && pin != ResolutionPin.PinPhysical && !forced)
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
                        if (withdrawn && pin != ResolutionPin.PinPhysical && !forced)
                        {
                            _switch.Cancel(repo, unit);   // SV-7: the held soldiers removed — never seen (a held squad's held men with it)
                            u.Splitting = SplitStage.NotSplitting;
                            return;
                        }
                        if (!_switch.AllPresent(repo, unit)) return;
                        // ⭐⭐ CE-8307 (R-8393, review F2; X-5 "before contact") — a FORCED split materialises the WHOLE subtree while held (a platoon's
                        //   squads create their men, still held), then every level hands over in ONE tick: no stochastic squad ever stands in contact
                        if (DeepMaterialise(repo, unit, forced, now)) return;
                        u = ref repo.GetComponentRW<AggregateUnit>(unit);
                        u.Splitting = SplitStage.Handover;
                        goto case SplitStage.Handover;
                    case SplitStage.Handover:
                        _switch.Handover(repo, unit);   // D-P6: one tick — the latest slot table mapped, the held released
                        u = ref repo.GetComponentRW<AggregateUnit>(unit);
                        u.Resolution = AggregateResolution.Physical;
                        u.Splitting = SplitStage.NotSplitting;
                        u.SplittingSince = now;   // in Physical: when the unit last became merge-eligible (reset below while not)
                        DeepHandover(repo, unit, now);   // ⭐ CE-8307 — the held subtree a forced split materialised, released in the same tick
                        return;
                }
                return;
            }

            if (res != AggregateResolution.Physical) return;
            if (!HasChildren(repo, unit))
            {
                // ⭐ CE-8307 (R-8394, review F3) — a SPLIT unit (only the handover writes an explicit Physical) whose every child is gone (killed)
                //   merges at once — its slots down — so its parent's leaves-first merge is never held by it; the pins keep it as they would
                if (u.Resolution == AggregateResolution.Physical && pin != ResolutionPin.PinPhysical && pin != ResolutionPin.PinKeep
                    && repo.IsComponentTypeRegistered<UnitSlotTable>() && repo.HasComponent<UnitSlotTable>(unit))
                {
                    _switch.Merge(repo, unit);
                    u = ref repo.GetComponentRW<AggregateUnit>(unit);
                    u.Resolution = AggregateResolution.Stochastic;
                    u.Splitting = SplitStage.NotSplitting;
                    u.SplittingSince = now;
                }
                return;
            }
            // a split unit: re-aggregate when beyond the SPLIT TRIGGER + the margin and quiet (automatic), or at once on the instructor's pin.
            // ⭐ F-23 (R-4286) — the merge line is band + lead + margin, never inside the trigger (band + lead): with an enemy parked between
            //   the old merge line (band + margin, 500 m) and the trigger (600 m) the unit split and merged every ~63 s (measured).
            bool far = d > b.PhysicalBand + b.LeadMetres + b.MergeMargin;
            if (pin == ResolutionPin.PinPhysical || pin == ResolutionPin.PinKeep) return;
            if (pin != ResolutionPin.PinStochastic)
            {
                // ⭐ CE-8013 ④ — UNOBSERVED too; ⭐ F-23 — and no man of it in CONTACT (automatic only)
                if (!far || Observed(repo, unit) || InContact(repo, unit)) { u.SplittingSince = now; return; }
                if ((double)now - (double)u.SplittingSince < b.QuietSeconds) return;
                // ⭐⭐ CE-8307 (R-8390; D-P7 "the reverse") — LEAVES FIRST: a unit child (a squad of the split platoon) that is itself split or
                //   splitting merges by its OWN rule first; the parent waits (its quiet clock keeps running, so it follows its last child at once)
                if (UnitChildBusy(repo, unit)) return;
                if (Blocked(repo, unit, AggregateResolution.Stochastic)) return;   // ⭐ X2-D (R-8357) — the order has no stochastic play: the automatic merge waits
            }
            else MergeUnitChildren(repo, unit, now);   // ⭐ CE-8307 — the PIN (SV-14) cascades in one step: a split child merged, a splitting one cancelled
            // re-read: the cascade above may have created / removed components (the ref into the AggregateUnit table stays valid — no structural
            // change to THIS entity — but take it again for clarity)
            u = ref repo.GetComponentRW<AggregateUnit>(unit);
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

        /// <summary>⭐ CE-8307 (R-8395) — the pin in force: the unit's own, else (none) the nearest parent unit's <see cref="ResolutionPin.PinPhysical"/>.</summary>
        private static ResolutionPin EffectivePin(EntityRepository repo, Entity unit, ResolutionPin own)
        {
            if (own != ResolutionPin.NoPin || !repo.IsComponentTypeRegistered<AggregateChild>()) return own;
            var e = unit;
            for (int depth = 0; depth < 6 && repo.HasComponent<AggregateChild>(e); depth++)
            {
                e = repo.GetComponentRO<AggregateChild>(e).Unit;
                if (!repo.IsAlive(e) || !repo.HasComponent<AggregateUnit>(e)) break;
                var p = repo.GetComponentRO<AggregateUnit>(e).Pin;
                if (p == ResolutionPin.PinPhysical) return p;
                if (p != ResolutionPin.NoPin) break;
            }
            return own;
        }

        /// <summary>⭐ CE-8307 (R-8397, review F5) — a released UNIT child whose parent's order has not reached it yet (no superior task), within the
        /// deadline since its release: its automatic split waits, so the order (a Defend ⇒ DP3's planned posts) lands first.</summary>
        private static bool AwaitsParentsOrder(EntityRepository repo, Entity unit, in AggregateUnit u, SimInstant now, in ResolutionBands b)
        {
            if (!repo.IsComponentTypeRegistered<AggregateChild>() || !repo.HasComponent<AggregateChild>(unit)) return false;
            if ((double)now - (double)u.SplittingSince >= b.DeadlineSeconds) return false;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.BehaviorState>()
                || !repo.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit)) return true;
            ref readonly var bs = ref repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit);
            return !(bs.ActiveBehaviorHash != 0 && bs.Origin == Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior);
        }

        /// <summary>The live UNIT children of <paramref name="unit"/> (a split / merge / forced cascade is rare: a list is fine, R-2084).</summary>
        private static List<Entity> UnitChildren(EntityRepository repo, Entity unit)
        {
            var list = new List<Entity>();
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return list;
            foreach (var c in repo.Query().With<AggregateChild>().With<AggregateUnit>().IncludeAll().Build())
                if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && UnitSplits.Live(repo, c)) list.Add(c);
            return list;
        }

        /// <summary>⭐⭐ CE-8307 (R-8393, review F2) — the FORCED cascade: every HELD unit child of <paramref name="unit"/> materialises its own children
        /// (held) at its fallback posts, recursively. True while anything below is still being created (the handover waits for the whole subtree).
        /// Not forced and nothing started below ⇒ false at once (the automatic one-level path).</summary>
        private bool DeepMaterialise(EntityRepository repo, Entity unit, bool forced, SimInstant now)
        {
            bool waiting = false;
            foreach (var c in UnitChildren(repo, unit))
            {
                if (repo.GetComponentRO<AggregateChild>(c).Held == 0) continue;
                ref var cu = ref repo.GetComponentRW<AggregateUnit>(c);
                if (cu.Splitting == SplitStage.NotSplitting)
                {
                    if (!forced || !repo.HasComponent<UnitSlotTable>(c) || !repo.HasComponent<SimTransform>(c)) continue;
                    var at = (Vector3)repo.GetComponentRO<SimTransform>(c).Position;
                    int n = UnitSplits.FallbackPosts(in cu, at, repo.GetComponentRO<UnitSlotTable>(c), _posts);
                    _switch.Materialise(repo, c, _posts.AsSpan(0, n));
                    cu = ref repo.GetComponentRW<AggregateUnit>(c);
                    cu.Splitting = SplitStage.Materialising;
                    cu.SplittingSince = now;
                    waiting = true;
                    continue;
                }
                if (cu.Splitting != SplitStage.Materialising) continue;
                if (!_switch.AllPresent(repo, c) || DeepMaterialise(repo, c, forced, now)) waiting = true;
            }
            return waiting;
        }

        /// <summary>⭐ CE-8307 — after <paramref name="unit"/>'s handover: each unit child that materialised in the forced cascade hands over too (same
        /// tick, top-down); each other released unit child starts its "await the parent's order" clock (R-8397).</summary>
        private void DeepHandover(EntityRepository repo, Entity unit, SimInstant now)
        {
            foreach (var c in UnitChildren(repo, unit))
            {
                ref var cu = ref repo.GetComponentRW<AggregateUnit>(c);
                if (cu.Splitting != SplitStage.Materialising) { cu.SplittingSince = now; continue; }
                _switch.Handover(repo, c);
                cu = ref repo.GetComponentRW<AggregateUnit>(c);
                cu.Resolution = AggregateResolution.Physical;
                cu.Splitting = SplitStage.NotSplitting;
                cu.SplittingSince = now;
                DeepHandover(repo, c, now);
            }
        }

        /// <summary>⭐ CE-8307 (R-8390) — is a live UNIT child of <paramref name="unit"/> (a stochastic squad its split created) itself split
        /// (Physical) or mid-process? Then the parent's automatic merge waits: leaves first.</summary>
        private static bool UnitChildBusy(EntityRepository repo, Entity unit)
        {
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return false;
            foreach (var c in repo.Query().With<AggregateChild>().With<AggregateUnit>().IncludeAll().Build())
            {
                if (repo.GetComponentRO<AggregateChild>(c).Unit != unit || !UnitSplits.Live(repo, c)) continue;
                ref readonly var cu = ref repo.GetComponentRO<AggregateUnit>(c);
                if (cu.Effective != AggregateResolution.Stochastic || cu.Splitting != SplitStage.NotSplitting) return true;
            }
            return false;
        }

        /// <summary>⭐ CE-8307 (R-8390, SV-14) — the instructor's pin merges the whole tree in ONE step: each live unit child that is split is merged
        /// (recursively — its own unit children first), one mid-process is cancelled; then the caller merges <paramref name="unit"/>.</summary>
        private void MergeUnitChildren(EntityRepository repo, Entity unit, SimInstant now)
        {
            if (!repo.IsComponentTypeRegistered<AggregateChild>()) return;
            List<Entity>? kids = null;   // the pin is rare: a list is fine (R-2084: not every frame)
            foreach (var c in repo.Query().With<AggregateChild>().With<AggregateUnit>().IncludeAll().Build())
                if (repo.GetComponentRO<AggregateChild>(c).Unit == unit && UnitSplits.Live(repo, c)) (kids ??= new List<Entity>()).Add(c);
            if (kids == null) return;
            foreach (var c in kids)
            {
                ref var cu = ref repo.GetComponentRW<AggregateUnit>(c);
                if (cu.Effective == AggregateResolution.Stochastic)
                {
                    if (cu.Splitting is SplitStage.Planning or SplitStage.Materialising or SplitStage.Handover)
                    {
                        _switch.Cancel(repo, c);
                        cu = ref repo.GetComponentRW<AggregateUnit>(c);
                        cu.Splitting = SplitStage.NotSplitting;
                        if (HasPlan(repo, c)) repo.RemoveComponent<PlannedPosts>(c);
                    }
                    continue;
                }
                MergeUnitChildren(repo, c, now);
                _switch.Merge(repo, c);
                cu = ref repo.GetComponentRW<AggregateUnit>(c);
                cu.Resolution = AggregateResolution.Stochastic;
                cu.Splitting = SplitStage.NotSplitting;
                cu.SplittingSince = now;
            }
        }

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

        /// <summary>⭐ <c>X-2</c> X2-B (R-8354, agg-behaviors) — the designation of the unit's composite slot (host-supplied, from the same TKB leaf
        /// as <see cref="ChildType"/>): what the released man is assigned under in the unit's roster. Null ⇒ <see cref="TacticalDesignation.Undefined"/>.</summary>
        public Func<EntityRepository, Entity, int, TacticalDesignation>? ChildDesignation { get; init; }

        /// <summary>
        /// ⭐⭐ <c>CE-8307</c> (R-8382; DESIGN_Split_One_Level.md) — the unit's DIRECT subordinates (host-supplied from the TKB composite:
        /// <c>StochasticUnitFill.Composition</c>), so a split goes ONE LEVEL (X-5 / D-P7): a platoon creates its SQUADS — each a stochastic unit
        /// carrying its share of the slot table — never its flattened men. Null ⇒ every slot is a man of its own leaf (<see cref="ChildType"/>,
        /// <see cref="ChildDesignation"/>; the single-level rails).
        /// </summary>
        public Func<EntityRepository, Entity, IReadOnlyList<SplitChild>?>? Composition { get; init; }

        /// <summary>The direct child holding composite leaf <paramref name="compositeSlot"/>: from <paramref name="comp"/>, else the leaf itself as a man.</summary>
        private SplitChild ChildFor(EntityRepository repo, Entity unit, IReadOnlyList<SplitChild>? comp, int compositeSlot)
        {
            if (comp != null)
                for (int k = 0; k < comp.Count; k++) if (comp[k].Holds(compositeSlot)) return comp[k];
            return new SplitChild(compositeSlot, ChildType != null ? ChildType(repo, unit, compositeSlot) : default,
                                  ChildDesignation?.Invoke(repo, unit, compositeSlot) ?? TacticalDesignation.Undefined, compositeSlot, 1, false, 0);
        }

        /// <summary>⭐ CE-8307 (R-8383) — the share of <paramref name="parent"/> that <paramref name="child"/> carries: every slot (up or down) whose leaf
        /// lies under it, in table order, its <see cref="UnitSlot.CompositeSlot"/> renumbered to the child's OWN composite (leaf − first + 1).</summary>
        public static UnitSlotTable ShareOf(in UnitSlotTable parent, in SplitChild child)
        {
            var t = new UnitSlotTable();
            for (int i = 0; i < parent.Count && t.Count < UnitSlotTable.Capacity; i++)
            {
                var s = parent.Slots[i];
                if (!child.Holds(s.CompositeSlot)) continue;
                s.CompositeSlot = (byte)(s.CompositeSlot - child.FirstSlot + 1);
                t.Slots[t.Count++] = s;
            }
            return t;
        }

        /// <summary>The composition as the host gives it (null when none, or when it holds no unit child — then the single-level path is exact).</summary>
        private IReadOnlyList<SplitChild>? CompositionOf(EntityRepository repo, Entity unit)
            => Composition?.Invoke(repo, unit) is { Count: > 0 } c ? c : null;

        private void Remove(EntityRepository repo, Entity c, string why)
        {
            // ⭐ CE-8307 (R-8393) — a UNIT child removed takes its own children with it (a held squad's held men in a cancelled forced cascade)
            if (repo.HasComponent<AggregateUnit>(c))
            {
                foreach (var g in Children(repo, c)) Remove(repo, g, why);
                _requested.Remove(c);
            }
            var elm = Lifecycle?.Invoke();
            if (elm != null && repo.GetLifecycleState(c) == EntityLifecycle.Active)
                elm.BeginDestruction(c, repo.GlobalVersion, why, ((ISimulationView)repo).GetCommandBuffer());
            else repo.DestroyEntity(c);   // a held child (Constructing) is never seen: removed at once; the participant abandons its wait
        }

        public void Materialise(EntityRepository repo, Entity unit, ReadOnlySpan<Vector3> posts)
        {
            if (ChildType == null && Composition == null) throw new InvalidOperationException("BusResolutionSwitch.ChildType is not set: the host must resolve the TKB composite (CE-8013)");
            ref readonly var slots = ref repo.GetComponentRO<UnitSlotTable>(unit);
            var comp = CompositionOf(repo, unit);
            var guid = UnitSplits.UnitGuid(repo, unit);
            var force = repo.HasComponent<EntityInfo>(unit) ? repo.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;
            string unitName = repo.HasComponent<EntityInfo>(unit) ? (string)repo.GetComponentRO<EntityInfo>(unit).Name : "";
            // ⭐⭐ CE-8307 (R-8382) — a UNIT child (a platoon's squad) is ONE request for all its live slots: placed at the centroid of their posts
            int n = comp?.Count ?? 0;
            Span<Vector3> at = n > 0 ? stackalloc Vector3[n] : Span<Vector3>.Empty;
            Span<int> men = n > 0 ? stackalloc int[n] : Span<int>.Empty;
            int requests = 0, k = 0;
            for (int i = 0; i < slots.Count && k < posts.Length; i++)
            {
                if (!slots.IsUp(i)) continue;
                int slot = slots.Slots[i].CompositeSlot;
                var child = ChildFor(repo, unit, comp, slot);
                if (child.IsUnit)
                {
                    int g = IndexOf(comp!, child.Key);
                    at[g] += posts[k]; men[g]++;
                    k++;
                    continue;
                }
                repo.Bus.PublishManaged(new EntityCreationRequest
                {
                    RequestId = EntityGuidDerivation.Derive(guid, "split:" + child.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    TkbType = child.Type,
                    IsTransient = true,
                    InitialComponents = new List<object>
                    {
                        new EntityGuid(UnitSplits.ChildGuid(guid, child.Key)),
                        new SimTransform { Position = posts[k], Rotation = Quaternion.Identity },
                        new AggregateChild { Unit = unit, Slot = (byte)i, Held = 1 },
                        new EntityInfo { ForceId = force, Name = ChildName(unitName, child.Key) },   // ⭐ F-22 (R-4286) — checks, goldens, the UI name him
                    },
                });
                k++; requests++;
            }
            if (n > 0)
            {
                ref readonly var parent = ref repo.GetComponentRO<AggregateUnit>(unit);
                int units = 0;
                for (int g = 0; g < n; g++) if (men[g] > 0) units++;
                for (int g = 0; g < n; g++)
                {
                    if (men[g] == 0) continue;   // a child with no man up is not created (the dead are never re-created)
                    var child = comp![g];
                    repo.Bus.PublishManaged(new EntityCreationRequest
                    {
                        RequestId = EntityGuidDerivation.Derive(guid, "split:" + child.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        TkbType = child.Type,
                        IsTransient = true,
                        InitialComponents = new List<object>
                        {
                            new EntityGuid(UnitSplits.ChildGuid(guid, child.Key)),
                            new SimTransform { Position = at[g] / men[g], Rotation = Quaternion.Identity },
                            new AggregateChild { Unit = unit, Slot = (byte)child.Key, Held = 1 },   // ⭐ a UNIT child's Slot is its KEY (it carries its own table)
                            new EntityInfo { ForceId = force, Name = ChildName(unitName, child.Key) },
                            ChildUnit(in parent, in child, units),
                            ShareOf(in slots, in child),   // rewritten with the LATEST share at the handover (D-P4)
                        },
                    });
                    requests++;
                }
            }
            _requested[unit] = requests;
        }

        private static int IndexOf(IReadOnlyList<SplitChild> comp, int key)
        {
            for (int g = 0; g < comp.Count; g++) if (comp[g].Key == key) return g;
            return -1;
        }

        /// <summary>⭐ CE-8307 (R-8382) — a unit child's <see cref="AggregateUnit"/>: STOCHASTIC, its own echelon, the parent's shape and state
        /// (formation, posture, prepared, heading, the last contact — the stress calm-down), its share of the frontage.</summary>
        public static AggregateUnit ChildUnit(in AggregateUnit parent, in SplitChild child, int unitChildren) => new()
        {
            Resolution = AggregateResolution.Stochastic,
            Echelon = child.Echelon,
            Formation = parent.Formation,
            Posture = parent.Posture,
            Prepared = parent.Prepared,
            PreparedSince = parent.PreparedSince,
            Heading = parent.Heading,
            Width = MathF.Max(10f, parent.Width / Math.Max(1, unitChildren)),
            Depth = parent.Depth,
            LastContactAt = parent.LastContactAt,
            LastFiredAt = parent.LastFiredAt,
        };

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
            var comp = CompositionOf(repo, unit);
            // ⭐ CE-8307 (R-8396, review F9) — in KEY order (a play picks its children by roster POSITION: PlatoonDefend's squads), and the roster
            //   counts only the children actually RELEASED (R-8398, review F12: a vehicle leaf is never created)
            var kids = Children(repo, unit);
            var keys = new int[kids.Count];
            for (int k = 0; k < kids.Count; k++) keys[k] = KeyOf(repo, kids[k], in slots);
            var ordered = kids.ToArray();
            Array.Sort(keys, ordered);
            kids = new List<Entity>(ordered);
            var released = new List<(Entity Child, TacticalDesignation As)>(kids.Count);
            foreach (var c in kids)
            {
                ref var tag = ref repo.GetComponentRW<AggregateChild>(c);
                if (repo.HasComponent<AggregateUnit>(c))
                {
                    // ⭐⭐ CE-8307 (R-8383, D-P4) — a UNIT child takes its LATEST share of the table (the losses while it was held count);
                    //   no man of it left up ⇒ it never enters the fight
                    int g = comp == null ? -1 : IndexOf(comp, tag.Slot);
                    var share = g < 0 ? default : ShareOf(in slots, comp![g]);
                    if (share.Present() == 0) { Remove(repo, c, "split: died before the handover"); continue; }
                    repo.GetComponentRW<UnitSlotTable>(c) = share;
                    tag = ref repo.GetComponentRW<AggregateChild>(c);
                    tag.Held = 0;
                    released.Add((c, comp![g].Designation));
                    continue;
                }
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
                tag = ref repo.GetComponentRW<AggregateChild>(c);
                tag.Held = 0;
                released.Add((c, ChildFor(repo, unit, comp, slots.Slots[i].CompositeSlot).Designation));
            }
            // ⭐⭐ X-2 X2-B (R-8354) / CE-8307 (R-8389) — the released DIRECT children join the unit's ROSTER through the ONE hierarchy path
            //   (UnitHierarchySystem) when they fit it (a squad's men, a platoon's squads); the roster lands a frame later
            if (UnitSplits.RostersItsChildren(released.Count))
                foreach (var (c, des) in released)
                    repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = c, Commander = unit, FireTeam = FireTeam.None, Designation = des });
            _requested.Remove(unit);
        }

        /// <summary>A child's key — its direct-subordinate index (a unit child's <see cref="AggregateChild.Slot"/>, a man's composite leaf).</summary>
        private static int KeyOf(EntityRepository repo, Entity c, in UnitSlotTable parent)
        {
            int slot = repo.GetComponentRO<AggregateChild>(c).Slot;
            if (repo.HasComponent<AggregateUnit>(c)) return slot;
            return slot < parent.Count ? parent.Slots[slot].CompositeSlot : 1000 + slot;
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
            IReadOnlyList<SplitChild>? comp = null;
            // ⭐ CE-8307 (R-8399, review F8; X-4 "sums and the centroid") — the unit-level state of UNIT children folds back: the latest contact and
            //   fire, the LEAST prepared (a fresh squad un-prepares the whole), the MOST suppressed (the worst-off part sets the whole's state)
            bool anyUnit = false;
            SimInstant lastContact = default, lastFired = default; PreparedLevel prepared = default; float suppressed = 0f;
            foreach (var c in Children(repo, unit))
            {
                int i = repo.GetComponentRO<AggregateChild>(c).Slot;
                if (repo.HasComponent<AggregateUnit>(c))
                {
                    // ⭐⭐ CE-8307 (R-8383; D-P7 "sum, then remove") — a UNIT child's table back into the parent, slot for slot by its leaf:
                    //   health, stress and ammunition exactly as the child holds them (the manager merges a parent only once its children are stochastic)
                    comp ??= CompositionOf(repo, unit);
                    ref readonly var cu = ref repo.GetComponentRO<AggregateUnit>(c);
                    if (!anyUnit) { lastContact = cu.LastContactAt; lastFired = cu.LastFiredAt; prepared = cu.Prepared; suppressed = cu.SuppressedShare; anyUnit = true; }
                    else
                    {
                        if ((double)cu.LastContactAt > (double)lastContact) lastContact = cu.LastContactAt;
                        if ((double)cu.LastFiredAt > (double)lastFired) lastFired = cu.LastFiredAt;
                        if (cu.Prepared < prepared) prepared = cu.Prepared;
                        if (cu.SuppressedShare > suppressed) suppressed = cu.SuppressedShare;
                    }
                    int g = comp == null ? -1 : IndexOf(comp, i);
                    if (g >= 0 && repo.HasComponent<UnitSlotTable>(c))
                    {
                        var child = comp![g];
                        ref readonly var share = ref repo.GetComponentRO<UnitSlotTable>(c);
                        for (int p = 0; p < slots.Count; p++)
                        {
                            int leaf = slots.Slots[p].CompositeSlot;
                            if (!child.Holds(leaf)) continue;
                            int local = leaf - child.FirstSlot + 1;
                            for (int j = 0; j < share.Count; j++)
                            {
                                if (share.Slots[j].CompositeSlot != local) continue;
                                slots.Slots[p].Health = share.Slots[j].Health;
                                slots.Slots[p].Stress = share.Slots[j].Stress;
                                slots.Slots[p].Ammo = share.Slots[j].Ammo;
                                seen[p] = true;
                                break;
                            }
                        }
                    }
                    if (repo.HasComponent<SimTransform>(c)) { sum += (Vector3)repo.GetComponentRO<SimTransform>(c).Position; n++; }
                    Remove(repo, c, "merged");
                    continue;
                }
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
            if (anyUnit)
            {
                ref var pu = ref repo.GetComponentRW<AggregateUnit>(unit);
                if ((double)lastContact > (double)pu.LastContactAt) pu.LastContactAt = lastContact;
                if ((double)lastFired > (double)pu.LastFiredAt) pu.LastFiredAt = lastFired;
                pu.Prepared = prepared;
                pu.SuppressedShare = suppressed;
            }
            // ⭐⭐ X-2 X2-B (R-8354, F-X2-4) — the men are gone, so is the unit's ROSTER: a stochastic unit that kept one (even empty — the
            //   DestructionOrder cascade empties it, never removes it) would drop out of the staff composer's stochastic arm (no SITREP / Set / TaskFailed)
            if (repo.IsComponentTypeRegistered<UnitRoster>() && repo.HasComponent<UnitRoster>(unit)) repo.RemoveComponent<UnitRoster>(unit);
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
