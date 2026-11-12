using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ N-11 (CE-4473; docs/DESIGN_Parallel_Simulation.md §18) — what brain code needs done SERIALLY before the brains' per-entity phase:
    /// <list type="bullet">
    /// <item>⭐ <b>type registration</b> — the types brain code registers lazily ("a world that never faults pays nothing"): a registration creates a
    /// type table every batch thread reads without a lock, so it is a structural change and never happens inside the phase (with the checks on the
    /// repository throws if one does). Run once per world, and again when a contribution changed;</item>
    /// <item>⭐ <b>frame preparation</b> — a per-world index or table brain code builds lazily "on first use this frame" (the rule tree, the
    /// decision cadence's wake table): built here, read-only inside the phase.</item>
    /// </list>
    /// Each owner contributes under ONE key (a hot-reloaded assembly re-contributes under the same key and replaces its entry). The lazy paths in
    /// the owners stay for the serial walk — after the preparation they are no-ops.
    /// </summary>
    public static class BrainPhase
    {
        private readonly struct Entry
        {
            public readonly Action<EntityRepository>? Types;
            public readonly Action<EntityRepository>? Frame;
            public readonly Action? Reads;
            public Entry(Action<EntityRepository>? types, Action<EntityRepository>? frame, Action? reads) { Types = types; Frame = frame; Reads = reads; }
        }

        private static readonly object s_lock = new();
        private static readonly Dictionary<string, Entry> s_byKey = new();
        private static Entry[] s_all = Array.Empty<Entry>();
        private static int s_version;
        private static readonly ConditionalWeakTable<EntityRepository, StrongBox<int>> s_typesDoneFor = new();

        static BrainPhase()
        {
            Contribute("Fdp.Toolkits", RegisterToolkitTypes, PrepareToolkitFrame, BrainReads.Declare);
        }

        private static int s_readsDone = -1;

        /// <summary>Adds (or replaces, by <paramref name="key"/>) an owner's preparation. Thread-safe; called at start-up / assembly load.</summary>
        /// <param name="declareReads">Declares the component types this owner's brain code reads on OTHER entities (process-wide,
        /// <see cref="ParallelReadCheck.DeclareUnmanaged{T}"/>): the last-tick view copies exactly the declared types.</param>
        public static void Contribute(string key, Action<EntityRepository>? registerTypes, Action<EntityRepository>? prepareFrame, Action? declareReads = null)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            lock (s_lock)
            {
                s_byKey[key] = new Entry(registerTypes, prepareFrame, declareReads);
                var all = new Entry[s_byKey.Count];
                s_byKey.Values.CopyTo(all, 0);
                s_all = all;
                s_version++;
            }
        }

        /// <summary>The brains' serial pre-pass: the types (once per world and contribution set), then every frame preparation.</summary>
        public static void Prepare(EntityRepository repo)
        {
            var all = s_all;
            var done = s_typesDoneFor.GetValue(repo, static _ => new StrongBox<int>(-1));
            int version = System.Threading.Volatile.Read(ref s_version);
            if (s_readsDone != version)
            {
                for (int i = 0; i < all.Length; i++) all[i].Reads?.Invoke();
                s_readsDone = version;
            }
            if (done.Value != version)
            {
                for (int i = 0; i < all.Length; i++) all[i].Types?.Invoke(repo);
                done.Value = version;
            }
            for (int i = 0; i < all.Length; i++) all[i].Frame?.Invoke(repo);
        }

        private static void RegisterToolkitTypes(EntityRepository repo)
        {
            Fdp.Toolkit.Behavior.Events.BehaviorFault.RegisterTypes(repo);
            Fdp.Toolkit.Behavior.Components.OperatorOrderOutcome.RegisterTypes(repo);
            Fdp.Toolkit.Behavior.Components.SubordinateOrders.RegisterTypes(repo);
            Fdp.Toolkit.Behavior.Components.DesignatedTarget.RegisterTypes(repo);
            Fdp.Toolkit.Perception.Survey.WorldSurveyChildSensor.Register(repo);
            if (!repo.TryGetTable(typeof(Fdp.Toolkit.Squad.SquadDrillTrace), out _)) repo.RegisterManagedComponent<Fdp.Toolkit.Squad.SquadDrillTrace>();   // SquadManeuverRunner
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadDrillState>()) repo.RegisterComponent<Fdp.Toolkit.Squad.SquadDrillState>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.ManualDrive>()) repo.RegisterComponent<Fdp.Toolkit.Navigation.ManualDrive>();              // OperatorControlBehavior
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>()) repo.RegisterComponent<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>();   // OperatorControlBehavior
        }

        private static void PrepareToolkitFrame(EntityRepository repo)
            => Fdp.Toolkit.Utility.DecisionCadence.Of(repo).PrepareFrame(repo);
    }
}
