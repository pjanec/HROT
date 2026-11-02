using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    // ⭐ TRIGGER ACTION-SEQUENCE PoC (SR-93..98) — test-project-only components. Ids 509, 492, 484, 485, 457, 458 are test-only here (425-430 became production ids: the objectives' 425/426, S2's 427-429)
    //   (FdpConfig.MAX_COMPONENT_TYPES = 512 — ⚠ the 9001/1001 ids other test projects use are OUT of range for a world that registers them).

    /// <summary>A test-visible log: <c>PocRecord</c> appends its param here.</summary>
    [ComponentId(509)]
    public struct PocLog
    {
        public int Count;
        public int V0, V1, V2, V3, V4, V5, V6, V7;

        public void Append(int v)
        {
            switch (Count) { case 0: V0 = v; break; case 1: V1 = v; break; case 2: V2 = v; break; case 3: V3 = v; break;
                             case 4: V4 = v; break; case 5: V5 = v; break; case 6: V6 = v; break; case 7: V7 = v; break; }
            Count++;
        }
        public int At(int i) => i switch { 0 => V0, 1 => V1, 2 => V2, 3 => V3, 4 => V4, 5 => V5, 6 => V6, _ => V7 };
    }

    /// <summary>A condition source for the <c>when</c> (PropertyMatch target).</summary>
    [ComponentId(492)]
    public struct PocSignal { public int Value; }

    [ComponentId(484)]
    public struct PocSignalB { public int Value; }

    /// <summary>⭐ SR-97 — ONE generic side record: the JSON of every list/arg-set the entity's actions use, indexed by an <c>int</c> ref.</summary>
    [ComponentId(485)]
    public sealed record PocActionArgs
    {
        public required IReadOnlyList<string> Items { get; init; }
    }

    /// <summary>The PoC trigger driver's state (Phase: 0 uninit · 1 armed · 2 waiting · 3 acting · 4 done).</summary>
    [ComponentId(457)]
    public struct PocTriggerState
    {
        public int Phase;
        public int Prev;          // last evaluated condition (0/1)
        public double EdgeAt;     // sim time of the rising edge
        public int Fired;         // how many times the actions STARTED
        public int Result;        // 0 none · 1 Success · 2 Failure
    }

    /// <summary>NodeStatus carries no reason — a failing action leaves its reason here by convention (PoC finding).</summary>
    [ComponentId(458)]
    public struct PocLastReason { public int Code; }
}
