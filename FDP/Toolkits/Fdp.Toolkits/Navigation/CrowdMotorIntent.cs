#nullable enable
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Engine-agnostic steering output written by <c>CrowdAgentUpdateSystem</c> (P2-T4)
    /// and read by <c>BulletCharacterMotor</c> (P1-T3) to drive the Bullet
    /// <c>CharacterComponent</c> (design §5.3, §6.2).
    ///
    /// <para>
    /// <b>Rationale (design §5.3):</b> Under split authority, <see cref="SimVelocity"/> is a
    /// <em>result</em> of the physics step (written by the reverse-sync); it must NOT be used
    /// as an input to the character controller. A dedicated intent component cleanly separates
    /// the steering request from the resolved velocity.
    /// </para>
    ///
    /// <para>
    /// <b>Coordinate space:</b> <see cref="Velocity"/> is in FDP world space (right-handed,
    /// X=East, Y=North, Z=Up). <c>BulletCharacterMotor</c> converts it to Stride space via
    /// <c>FdpStrideTransform.ToStrideVelocity</c> before passing it to
    /// <c>CharacterComponent.SetVelocity</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Component ID:</b> <see cref="NavigationContractsComponentIds.CrowdMotorIntent"/> (265).
    /// Allocated in the navigation contracts block next to <see cref="CrowdAgent"/> (261).
    /// </para>
    ///
    /// <para>
    /// <b>Phase 2 note:</b> In P1 the component is written directly by test/bootstrap code;
    /// the real writer (<c>CrowdAgentUpdateSystem</c> refactored in P2-T4) will write it
    /// instead of mutating <see cref="SimVelocity"/>.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.CrowdMotorIntent)]
    public struct CrowdMotorIntent
    {
        /// <summary>
        /// Desired steering velocity in FDP world space (X=East, Y=North, Z=Up), metres/second.
        ///
        /// <para>
        /// The magnitude encodes the desired speed; the direction encodes the steering heading.
        /// A zero vector means "stop". The <c>BulletCharacterMotor</c> applies the entity's
        /// current stance speed multiplier and passes the result to
        /// <c>CharacterComponent.SetVelocity</c>.
        /// </para>
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// When <see langword="true"/>, the character should jump this frame — provided
        /// <c>CharacterComponent.IsGrounded</c> is true at the time the motor runs.
        ///
        /// <para>
        /// The motor gate (<c>IsGrounded</c> check) prevents mid-air double-jumps.
        /// </para>
        /// </summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool Jump;
    }
}
