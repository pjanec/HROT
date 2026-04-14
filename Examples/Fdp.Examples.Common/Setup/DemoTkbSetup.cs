using CarKinem.Core;
using Fdp.Examples.Common.Constants;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb;

namespace Fdp.Examples.Common.Setup
{
    /// <summary>
    /// TKB blueprint registration for the DistributedTank scenario entity types.
    ///
    /// <para><b>Usage (Muscle node composition root):</b></para>
    /// <code>
    /// var tkb = new TkbDatabase();
    /// DemoTkbSetup.RegisterAll(tkb);
    /// var replicationModule = new ReplicationLogicModule(entityMap, tkb, lifecycleModule);
    /// </code>
    ///
    /// <para><b>Registered templates:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="DemoTemplateIds.CommandTank"/> (100) — physics-capable tank hull for Muscle ghost promotion.</item>
    /// </list>
    ///
    /// <para><b>Design note:</b> Only the Muscle-side template is registered here.  The Brain node
    /// spawns <c>CommandTank</c> and <c>TankTurret</c> entities manually (no TKB promotion needed on
    /// the authoritative side).  The <c>TankTurret</c> (ID 101) template is not registered because the
    /// Turret entity is Brain-only and never ghost-promoted on the Muscle node.</para>
    /// </summary>
    public static class DemoTkbSetup
    {
        /// <summary>Default arrival radius for tank hull navigation (metres).</summary>
        private const float CommandTankArrivalRadius = 2f;

        /// <summary>
        /// Registers all DistributedTank entity templates with <paramref name="tkb"/>.
        /// Call once at kernel setup, before <c>ModuleHostKernel.Initialize()</c>.
        /// </summary>
        /// <param name="tkb">The TKB database that will own the registered templates.</param>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            RegisterCommandTank(tkb);
        }

        // ── Template: CommandTank (ID 100) ───────────────────────────────────────────

        /// <summary>
        /// Muscle-side blueprint for the CommandTank hull entity (TKB type 100).
        /// Applied by <c>GhostPromotionSystem</c> when the ghost's
        /// <c>TkbIdentity.TkbType == <see cref="DemoTemplateIds.CommandTank"/></c>.
        /// </summary>
        private static void RegisterCommandTank(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("CommandTank", tkbType: DemoTemplateIds.CommandTank);

            // Universal spatial primitives
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            // Vehicle kinematics (required by CarKinematicsSystem)
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Tank));

            // Navigation target  — populated via DemoLocomotionMsg translation at runtime
            t.AddComponent(new NavState
            {
                Mode             = KinematicsMode.None,
                TrajectoryId     = -1,
                CurrentSegmentId = -1,
                ArrivalRadius    = CommandTankArrivalRadius,
                TargetSpeed      = 0f,
            });

            // Locomotion channel — written by DemoLocomotionMsg translator, read by
            // LocomotionDispatcherSystem (if registered) or used for split-authority checks.
            t.AddComponent(new LocomotionChannel());

            tkb.Register(t);
        }
    }
}
