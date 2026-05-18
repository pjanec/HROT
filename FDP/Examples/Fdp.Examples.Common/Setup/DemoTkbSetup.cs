using Fdp.Examples.Common.Constants;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

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
        /// <summary>
        /// Registers all DistributedTank entity templates with <paramref name="tkb"/>.
        /// Call once at kernel setup, before <c>ModuleHostKernel.Initialize()</c>.
        /// Idempotent: safe to call multiple times, skips if template is already registered.
        /// </summary>
        /// <param name="tkb">The TKB database that will own the registered templates.</param>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (!tkb.TryGetByType(DemoTemplateIds.CommandTank, out _))
            {
                RegisterCommandTank(tkb);
            }
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

            t.AddDescriptor(new TkbMasterDto { CustomName = "CommandTank" });

            // TKB-014 (Phase 6): ECS components (SimTransform, SimVelocity, VehicleState,
            // VehicleParams, NavState, LocomotionChannel) will be injected by translators.
            t.AddDescriptor(new VehicleParametersDto
            {
                Length      = 7.0f,
                Width       = 3.5f,
                MaxSpeedFwd = 12.0f,
                MaxSpeedRev = 8.0f,
                MaxAccel    = 2.0f
            });

            tkb.Register(t);
        }
    }
}
