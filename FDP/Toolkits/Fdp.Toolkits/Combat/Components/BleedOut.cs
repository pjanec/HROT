using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — the bleeding an incapacitated man has gathered since the last whole step <c>BleedOutSystem</c> took off his health.
    /// On the man, on the node owning his Health, while he bleeds; recorded, so a checkpoint restored into a fresh process carries the bleed
    /// on (it was a field of the system: lost there). 📄 docs/DESIGN_System_State_Inventory.md §6 #2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BleedOut)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BleedOut
    {
        /// <summary>Bleeding time gathered toward the next step (s), below <c>BleedOutSystem.StepSeconds</c>.</summary>
        public float Pending;
    }
}
