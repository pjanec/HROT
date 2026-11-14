using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Weather;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐ CE-4239 (CE-3365 + CE-4152; docs/DESIGN_Entity_Hierarchy_Views.md §6) — the ONE answer to "does this entity have a PHYSICAL PRESENCE — is it something you
    /// could see on the map?". The map's per-entity projectors (symbol, anchor, pick box, name card, heading arrow) and the ORBAT's Command / Structure views ask THIS, so a
    /// rule, a score or an evaluation is never drawn as a label and never listed as a unit — ⛔ not two filters that agree today and drift tomorrow.
    /// <para><b>Not seeable</b> (measured: <c>showcase-crossfire</c> drew <i>game/score/Default #1781</i> at 31 men and listed ~150 score entities): ① what <see cref="NotAUnit"/> says (the camera, a round, a cloud —
    /// drawn by their own layers) · ② a RULE entity — an objective / trigger / measure (TKB <c>8860</c> / <c>8861</c>, or one that carries <see cref="ObjectiveStatus"/>): a brain with no body · ③ the World Environment
    /// (the entity that carries <see cref="ScenarioClock"/> — a data holder) · ④ a MACRO INSTANCE that expanded into nothing seeable (an evaluation macro: only objectives) — its members decide, nested instances too.</para>
    /// <para>⭐ An instance that has not finished expanding (<c>None</c> / <c>Expanding</c>) or <c>Failed</c> stays seeable: a macro just placed must show where, and a failed one shows the reason over its marker.</para>
    /// <para>⭐ The Scenario view of the ORBAT and the rules view do NOT ask this — they list the virtual ones by design (HV-3, RV-3).</para>
    /// </summary>
    public static class Seeable
    {
        /// <summary>
        /// ⭐ CE-1480 G5 — the <see cref="GizmoInputs"/> of a rule that asks <see cref="OnMap"/> and reads <paramref name="own"/> itself: the components whose VALUES the
        /// presence answer reads (<c>TkbIdentity</c>'s type, a macro instance's expansion state and its members' <c>MacroOrigin</c>) added to the rule's own.
        /// Presence / absence of any component is a structural change and always redraws. Not view-dependent, no setting.
        /// </summary>
        public static GizmoInputs Inputs(params System.Type[] own) => Inputs(null, own);

        /// <summary>The same with an extra external version (a setting, a name table).</summary>
        public static GizmoInputs Inputs(System.Func<long>? version, params System.Type[] own) => Build(false, version, own);

        /// <summary>The same for a rule that also depends on the map view (zoom / what is in view).</summary>
        public static GizmoInputs InputsWithView(System.Func<long>? version, params System.Type[] own) => Build(true, version, own);

        private static GizmoInputs Build(bool dependsOnView, System.Func<long>? version, System.Type[] own)
        {
            var all = new System.Type[own.Length + 3];
            own.CopyTo(all, 0);
            all[own.Length] = typeof(TkbIdentity); all[own.Length + 1] = typeof(MacroExpansionStatus); all[own.Length + 2] = typeof(MacroOrigin);
            return new GizmoInputs(dependsOnView, version, all);
        }

        /// <summary>True when <paramref name="entity"/> has a physical presence on the map.</summary>
        public static bool OnMap(ISimulationView view, Entity entity)
        {
            if (view is not EntityRepository world || entity.Index < 0) return Compute(view, entity);
            // ⭐ CE-2394 — answered ONCE per entity per frame: the map's five per-entity projectors (and the ORBAT) each asked it, and each
            //   answer is several component reads. Cached per world version like ④ below; a reused slot is told apart by its generation.
            var memo = Memo.GetValue(world, static _ => new FrameMemo());
            uint version = world.GlobalVersion;
            int i = entity.Index;
            if (memo.Version != version)
            {
                memo.Version = version;
                System.Array.Clear(memo.Answer, 0, memo.Answer.Length);
            }
            if (i >= memo.Answer.Length)
            {
                int size = System.Math.Max(i + 1, memo.Answer.Length * 2);
                System.Array.Resize(ref memo.Answer, size);
                System.Array.Resize(ref memo.Generation, size);
                System.Array.Resize(ref memo.Mask, size);
            }
            // ⭐ CE-2405 — the entity's component set is part of the key: GlobalVersion does not move on an add / remove inside a frame, so a
            //   component removed after the first ask (a cloud tag dropped, the same entity now a unit) kept the stale answer (CE1165 rail).
            int mask = world.GetComponentMask(i).GetHashCode();
            byte a = memo.Answer[i];
            if (a != 0 && memo.Generation[i] == entity.Generation && memo.Mask[i] == mask) return a == 1;
            bool on = Compute(view, entity);
            memo.Answer[i] = on ? (byte)1 : (byte)2;
            memo.Generation[i] = entity.Generation;
            memo.Mask[i] = mask;
            return on;
        }

        private sealed class FrameMemo { public uint Version = uint.MaxValue; public byte[] Answer = new byte[256]; public ushort[] Generation = new ushort[256]; public int[] Mask = new int[256]; }
        private static readonly ConditionalWeakTable<EntityRepository, FrameMemo> Memo = new();

        private static bool Compute(ISimulationView view, Entity entity)
        {
            if (!HasPresenceItself(view, entity)) return false;
            if (!view.HasManagedComponent<MacroInstance>(entity)) return true;
            if (Has<MacroExpansionStatus>(view, entity) && view.GetComponentRO<MacroExpansionStatus>(entity).State != MacroExpansionState.Done) return true;   // still expanding, or failed: show it
            return view is not EntityRepository repo || InstancesWithMembers(repo).Contains(entity.Index);
        }

        /// <summary>The entity on its own — everything but the macro-instance rule (④).</summary>
        internal static bool HasPresenceItself(ISimulationView view, Entity entity)
        {
            if (NotAUnit.Is(view, entity)) return false;
            if (Has<ObjectiveStatus>(view, entity) || Has<ScenarioClock>(view, entity)) return false;
            if (Has<TkbIdentity>(view, entity))
            {
                long type = view.GetComponentRO<TkbIdentity>(entity).TkbType;
                if (type == Hrot.Map.Common.TkbEntityTypes.Objective || type == Hrot.Map.Common.TkbEntityTypes.Trigger) return false;
            }
            return true;
        }

        // ── ④ the macro instances that have a seeable member: one pass over the members, cached per world version (the ORBAT asks per row, the projectors per entity per frame)
        private sealed class Cache { public uint Version = uint.MaxValue; public readonly HashSet<int> Instances = new(); public readonly List<(Entity Member, Entity Instance)> Pairs = new(); }
        private static readonly ConditionalWeakTable<EntityRepository, Cache> ByWorld = new();

        private static HashSet<int> InstancesWithMembers(EntityRepository world)
        {
            var cache = ByWorld.GetValue(world, _ => new Cache());
            if (cache.Version == world.GlobalVersion) return cache.Instances;
            cache.Instances.Clear();
            cache.Version = world.GlobalVersion;
            if (!world.IsComponentTypeRegistered<MacroOrigin>()) return cache.Instances;

            // (member, instance) pairs once; then mark instances to a fixpoint — a nested instance counts only when IT has a seeable member
            var pairs = cache.Pairs;   // ⭐ CE-2394 — reused: it was a new list every frame
            pairs.Clear();
            foreach (var member in world.Query().With<MacroOrigin>().Build())
            {
                var instance = world.GetComponentRO<MacroOrigin>(member).Instance.Resolve(world);
                if (!instance.IsNull && world.IsAlive(instance)) pairs.Add((member, instance));
            }
            for (int pass = 0; pass < 8; pass++)
            {
                bool grew = false;
                foreach (var (member, instance) in pairs)
                {
                    if (cache.Instances.Contains(instance.Index) || !HasPresenceItself(world, member)) continue;
                    if (world.HasManagedComponent<MacroInstance>(member) && !cache.Instances.Contains(member.Index)
                        && !(Has<MacroExpansionStatus>(world, member) && world.GetComponentRO<MacroExpansionStatus>(member).State != MacroExpansionState.Done)) continue;
                    cache.Instances.Add(instance.Index); grew = true;
                }
                if (!grew) break;
            }
            return cache.Instances;
        }

        private static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);
    }
}
