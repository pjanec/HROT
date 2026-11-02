using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    // ⭐ TRIGGER ACTION-SEQUENCE PoC (SR-93..98) — test-project-only components. Ids 492, 484, 457 are test-only here (509 moved to ActionTestLog)
    //   (FdpConfig.MAX_COMPONENT_TYPES = 512 — ⚠ the 9001/1001 ids other test projects use are OUT of range for a world that registers them).

    /// <summary>A condition source for the <c>when</c> (PropertyMatch target).</summary>
    [ComponentId(492)]
    public struct PocSignal { public int Value; }

    [ComponentId(484)]
    public struct PocSignalB { public int Value; }

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

}
