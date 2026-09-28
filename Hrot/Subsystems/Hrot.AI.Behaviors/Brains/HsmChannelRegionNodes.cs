using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-400</c> / ACCEPTANCE RAIL ⑥ — two HSM activities on DIFFERENT COMMAND CHANNELS,
    /// so two orthogonal regions can drive movement and weapons at the same time.</b>
    /// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §9 ⑥.
    ///
    /// <para>🔒 <b>The user's acceptance description, verbatim:</b> <i>"multiple actions in parallel,
    /// one per channel like one action for movement, one for weapon control."</i></para>
    ///
    /// <para>🔴 <b>Why these had to be written.</b> 📐 Measured: <c>HsmOrthogonalRegions</c>' two regions
    /// bind NO activity at all (all four states carry <c>ActivityAction: null</c>), and the only
    /// channel-writing HSM actions in the tree live in <c>FDP/Examples</c>, which the game assembly
    /// does not reference. ⇒ <b>no asset could express "a region per channel"</b>, and rail ⑥ had no
    /// subject.</para>
    ///
    /// <para>⭐⭐ <b>They count their own ticks.</b> A channel write alone cannot distinguish "ran once"
    /// from "runs every frame" — the second write is idempotent. ⇒ each action bumps
    /// <c>ActionInstanceId</c> on its own channel, so the rail can assert BOTH regions advance on
    /// EVERY frame rather than merely having run at some point.</para>
    ///
    /// <para>⛔ <b>Plain <c>[HsmAction]</c> thunks, deliberately NOT DTO-bound.</b> What is under test
    /// is that two regions tick into two channels; an occurrence-scoped params DTO would add the
    /// storage machinery <c>O7</c> already proves and obscure the claim. ⚠ The blueprint-hosted
    /// version of this is <c>CE-388</c> (<c>WritesChannel</c> for hosted actions), which is filed and
    /// separate.</para>
    /// </summary>
    public static unsafe class HsmChannelRegionNodes
    {
        /// <summary>Distinct, arbitrary action ids — the rail asserts WHICH channel each landed on.</summary>
        public const ushort ActionIdDrive = 0x0D01;
        public const ushort ActionIdFire  = 0x0F01;

        /// <summary>⭐ Region 0's activity: the MOVEMENT channel.</summary>
        [HsmAction]
        public static void Activity_DriveChannel(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
            loco.ActiveAction       = ActionIdDrive;
            loco.BehaviorInstanceId = repo.GetComponent<BehaviorState>(bridge->Self).InstanceId;
            loco.ActionInstanceId++;   // ⭐ the per-frame witness
        }

        /// <summary>⭐ Region 1's activity: the WEAPON channel, concurrently.</summary>
        [HsmAction]
        public static void Activity_FireChannel(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            ref var weapon = ref repo.GetComponentRW<WeaponChannel>(bridge->Self);
            weapon.ActiveAction       = ActionIdFire;
            weapon.BehaviorInstanceId = repo.GetComponent<BehaviorState>(bridge->Self).InstanceId;
            weapon.ActionInstanceId++;
        }
    }
}
