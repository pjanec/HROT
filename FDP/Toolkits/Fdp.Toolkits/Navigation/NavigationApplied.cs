using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — what <c>NavigationIntentBridgeSystem</c> already applied to this mover: the <see cref="NavigationIntent.IntentId"/>
    /// and the locomotion channel's action instance. On the mover, on the node that runs his muscle; recorded, so a checkpoint restored into a
    /// fresh process does not apply every intent again (📐 as a field of the system it was lost there: each route restarted from its first
    /// corner, <c>HasArrived</c> cleared, crowd registration re-run). ⛔ Not <c>NavState</c> (a man on a crowd path has none) and not
    /// <c>NavigationIntent</c> (it is egressed: a write would republish it). 📄 docs/DESIGN_System_State_Inventory.md §6 #5.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.NavigationApplied)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct NavigationApplied
    {
        /// <summary>The intent applied, valid when <see cref="HasIntent"/> is 1.</summary>
        public uint IntentId;
        /// <summary>The locomotion action instance applied, valid when <see cref="HasAction"/> is 1.</summary>
        public uint ActionInstanceId;
        public byte HasIntent;
        public byte HasAction;
    }
}
