using System.Runtime.InteropServices;
using Fdp.Core;

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

        /// <summary>Seconds to wait between successive shots.</summary>
        public float CooldownSeconds;

        /// <summary>
        /// ⭐ <c>CE-3089</c> (G7) — which weapon mount fires: 0 = the primary (what every zero-filled writer gets — today's
        /// behaviour), <see cref="MountAuto"/> = choose per shot (<see cref="WeaponChoice"/>), else that mount index.
        /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §12 W3/W4.
        /// </summary>
        public byte Mount;

        /// <summary>The <see cref="Mount"/> value that asks the executor to choose the weapon per shot.</summary>
        public const byte MountAuto = 255;
    }
}
