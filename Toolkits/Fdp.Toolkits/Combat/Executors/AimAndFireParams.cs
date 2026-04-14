using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>
    /// Parameters packed into <see cref="Fdp.Toolkit.Behavior.Components.WeaponChannel.Params"/>
    /// for the AimAndFire action.
    /// Must fit within <see cref="Fdp.Toolkit.Behavior.BehaviorConstants.ActionParamsByteSize"/> bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AimAndFireParams
    {
        /// <summary>The entity to aim at and fire upon.</summary>
        public Entity Target;

        /// <summary>Number of ticks to wait between successive shots.</summary>
        public int CooldownTicks;
    }
}
