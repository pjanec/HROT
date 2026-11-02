using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.ScenarioRules.Tests.Actions;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>Resolves a scenario RefKey to a live entity (the PoC's stand-in for the real RefKey/GUID registry).</summary>
    public sealed class PocRefTable
    {
        private readonly Dictionary<string, Entity> _byKey = new();
        public void Set(string key, Entity e) => _byKey[key] = e;
        public bool TryResolve(string key, out Entity e) => _byKey.TryGetValue(key, out e);
    }

    /// <summary>
    /// ⭐ P5 — the <c>OnEntity {Ref, Predicate}</c> re-target, implemented AS A WRAPPER around an already-compiled child:
    /// resolve the target, evaluate the child on IT. (Real SR-95 wants this as a DTO the compiler understands — see the report.)
    /// </summary>
    public static class PocOnEntity
    {
        public static Func<EntityRepository, Entity, bool> Wrap(PocRefTable refs, string refKey, Func<EntityRepository, Entity, bool> child)
            => (w, self) => refs.TryResolve(refKey, out var t) && w.IsAlive(t) && child(w, t);

        /// <summary>A PoC-level AND over compiled conditions — what a Compound containing an OnEntity would do.</summary>
        public static Func<EntityRepository, Entity, bool> All(params Func<EntityRepository, Entity, bool>[] parts)
            => (w, self) => { foreach (var p in parts) if (!p(w, self)) return false; return true; };
    }

    /// <summary>
    /// ⭐ P5 — the minimal trigger driver: rising EDGE only, <c>delay</c> in sim seconds, <c>once</c> (or re-arm); on fire it runs
    /// <c>RunActions</c> through <see cref="ActionHost"/>. State lives in the <see cref="PocTriggerState"/> component (restore-friendly).
    /// Phase: 0 uninit · 1 armed · 2 waiting · 3 acting · 4 done.
    /// </summary>
    public sealed class PocTrigger
    {
        private readonly Func<EntityRepository, Entity, bool> _when;
        private readonly double _delay;
        private readonly bool _once;
        private readonly int _listRef;

        public PocTrigger(Func<EntityRepository, Entity, bool> compiledWhen, double delaySeconds, bool once, int listRef)
        { _when = compiledWhen; _delay = delaySeconds; _once = once; _listRef = listRef; }

        public NodeStatus Tick(EntityRepository w, Entity self, double simNow)
        {
            bool cond = _when(w, self);
            // ⚠ the ref is HELD across ActionHost.Tick below (which may append a store block = AddComponent on this entity) — measured, not assumed
            ref PocTriggerState st = ref w.GetComponentRW<PocTriggerState>(self);

            if (st.Phase == 0) { st.Prev = cond ? 1 : 0; st.Phase = 1; return NodeStatus.Running; }   // arm WITHOUT an edge: "already true at start" is not a rising edge

            if (st.Phase == 1)
            {
                if (cond && st.Prev == 0)
                {
                    st.EdgeAt = simNow;
                    st.Phase = _delay <= 0 ? 3 : 2;
                    if (st.Phase == 3) st.Fired++;
                }
                st.Prev = cond ? 1 : 0;
            }
            else if (st.Phase == 2 && simNow - st.EdgeAt >= _delay - 1e-9)
            {
                st.Phase = 3; st.Fired++;
            }

            if (st.Phase == 3)
            {
                var r = ActionHost.Tick(w, self, _listRef, out _);
                if (r != NodeStatus.Running)
                {
                    st.Result = r == NodeStatus.Success ? 1 : 2;     // ← written through the ref held since the top of the call
                    st.Phase = _once ? 4 : 1;
                    st.Prev = cond ? 1 : 0;
                }
            }
            return st.Phase == 4 ? (st.Result == 1 ? NodeStatus.Success : NodeStatus.Failure) : NodeStatus.Running;
        }
    }
}
