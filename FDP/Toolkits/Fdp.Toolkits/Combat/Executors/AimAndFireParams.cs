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

        /// <summary>
        /// ⭐ AQ89 A (R-269) — 1 = SUPPRESS: when the target is not seen (and nothing can be thrown), fire slow bursts at where the
        /// unit's memory last placed it instead of holding. 0 = hold (every zero-filled writer — today's behaviour). Sits in the
        /// padding after <see cref="Mount"/>, so <see cref="Rounds"/> keeps its offset.
        /// </summary>
        public byte BlindFire;

        /// <summary>
        /// ⭐ <c>CE-3172</c> S4d (M-8, C-2; docs/DESIGN_Entity_Control.md §8b) — 1 = OPERATOR fire: a possessing station aims at
        /// <see cref="Point"/> (no target entity) and fires while <see cref="Trigger"/> is held — no seen-now gate, no aim time, and
        /// the unit's ROE does not hold it (the operator overrides it). 0 = the AI's aimed fire (every zero-filled writer). Sits in
        /// the padding after <see cref="BlindFire"/>, so <see cref="Rounds"/> keeps its offset.
        /// </summary>
        public byte Operator;

        /// <summary>
        /// ⭐ <c>CE-4143</c> (docs/DESIGN_Entity_Control.md §8r R-3/R-4) — the <see cref="Operator"/> value of a SIGHT shot on a TURRET: the
        /// aim is along the player's look ray at the zero range and the muscle fires every cooldown along the barrel as it points
        /// (<see cref="Components.WeaponTask.AlongBarrel"/>), with no on-target gate. ⚠ Deviation from §8r ("+AlongBarrel byte"): this
        /// struct is exactly its 32 bytes, so the flag rides in the operator byte (every reader asks <c>Operator != 0</c>), not a new field.
        /// </summary>
        public const byte OperatorAlongBarrel = 2;

        /// <summary>⭐ CE-4143 — 1 when this is an operator shot along the barrel (<see cref="OperatorAlongBarrel"/>).</summary>
        public readonly byte AlongBarrel => Operator == OperatorAlongBarrel ? (byte)1 : (byte)0;

        /// <summary>⭐ <c>CE-3172</c> S4d — operator fire only: 1 = the trigger is held (fire), 0 = aim only (a turret tracks
        /// <see cref="Point"/>).</summary>
        public byte Trigger;

        /// <summary>The <see cref="Mount"/> value that asks the executor to choose the weapon per shot.</summary>
        public const byte MountAuto = 255;

        /// <summary>
        /// ⭐ <c>CE-3136</c> B7 (peek-and-fire §8.1) — aimed rounds to fire, then Success; <b>0 = until stopped</b> (every
        /// zero-filled writer — today's behaviour). The same rule as <see cref="FireAtPointParams.Rounds"/>, so both fire
        /// actions end the same way and no behaviour counts intents.
        /// </summary>
        public int Rounds;

        /// <summary>⭐ <c>CE-3172</c> S4d — operator fire only: where the station aims (world metres). Fills the struct to its 32
        /// bytes (<see cref="Fdp.Toolkit.Behavior.BehaviorConstants.ActionParamsByteSize"/>).</summary>
        public System.Numerics.Vector3 Point;
    }
}
