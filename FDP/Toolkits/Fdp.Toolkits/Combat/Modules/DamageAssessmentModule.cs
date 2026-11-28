using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Systems;

namespace Fdp.Toolkit.Combat.Modules
{
    /// <summary>
    /// Registers the systems required for damage assessment on the authority node.
    ///
    /// <para><b>Systems registered (in execution order):</b></para>
    /// <list type="number">
    ///   <item>
    ///     <b>Simulation phase</b> — <see cref="DamageCalculationSystem"/>: consumes
    ///     <c>DetonationNotification</c> events, computes flat HP loss (POC), and publishes
    ///     <c>DamageAssessedEvent</c> for the egress translator.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// The companion translator pair (<c>MunitionDetonationIngressTranslator</c> and
    /// <c>DamageAssessedEgressTranslator</c>) live in <c>Hrot.SimHost/Network</c> and are
    /// registered separately in <c>SimHostApp</c> / <c>NodeBootstrapper</c>.
    /// </para>
    /// </summary>
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public sealed class DamageAssessmentModule
    {
        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; } =
            new IEcsModuleSystem[] { new DamageCalculationSystem(), new AreaEffectSystem(),   // ⭐ CE-1032 W-5 — the area effect beside the direct hit
                                     new NapalmSystem(),
                                     new StructureFlameSystem(),   // ⭐ CE-3488 (flame FL-4) — a flame jet on a burnable wall sets its building alight (before the record writer below)   // ⭐ CE-3487 (flame FL-3) — a napalm burst: the fireball (men alight) and the ground fire's spots, from the same bursts
                                     new Fdp.Toolkit.Squad.DangerArea.ShelledAreaSystem(),   // ⭐ CE-4583 — the shelled areas, from the same bursts
                                     new Fdp.Toolkit.Terrain.BuildingDamageSystem(),   // ⭐ CE-4391 S-1 — the breaches it decided, written into the building's damage entity (after it: B-5)
                                     new Fdp.Toolkit.Terrain.GroundDamageSystem(),   // ⭐ CE-4591 (P-4) — the craters it decided, written into the cell's ground-damage entity
                                     new Fdp.Toolkit.Terrain.CraterRegroundSystem(),   // ⭐ CE-4591 (G-11) — an owned man standing where a crater opened takes the new ground
                                     new Fdp.Toolkit.Terrain.DozerCutSystem(),   // ⭐ CE-4601 (B-4) — a bulldozer's blade cuts the wall / fells the trunk its order marked (before the writers)
                                     new Fdp.Toolkit.Terrain.BridgeWorkSystem(),   // ⭐ CE-4693 (BL-S3 ⑥) — a bridge engineer at its stand lays / recovers the bridge its brain ordered (the dozer's node)
                                     new Fdp.Toolkit.Terrain.TreeDamageSystem(),
                                     new Fdp.Toolkit.Terrain.GroundFireSystem(),   // ⭐ CE-3487 (flame FL-3, TD-6) — the ground fire NapalmSystem laid, written into the cell's fire record; a spot gone out leaves a scorch
                                     new Fdp.Toolkit.Terrain.GroundHeatMarkSystem(),   // ⭐ CE-4664 (TD-14) — the warm stands HeatSourceSystem (CE-4658) reports here, written into the cell's heat-marks entity
                                     new Fdp.Toolkit.Terrain.MinefieldStrikesSystem(),   // ⭐ CE-4624 MS-T1 — the mines that went off, written into the field's strikes record
                                     new Fdp.Toolkit.Terrain.PlaceKnowledgeSystem(),
                                     new Fdp.Toolkit.Terrain.UnderFireWriter(),   // ⭐ CE-2468 UF-2 — the places under fire each side learned (shelling here; broken contacts on the CGF node), aged and retired
                                     new Fdp.Toolkit.Terrain.UnderFireSightSystem(),   // ⭐ CE-2468 UF-4 (K5) — the ground each known enemy sees + the route-cost maps, rebuilt serially only when the table changed
                                     new Fdp.Toolkit.Terrain.MinefieldLanesSystem() };   // ⭐ CE-4624 MS-T4 — the lanes cleared, written into the field's lanes record   // ⭐ CE-4624 MS-T2 — what each side learned (strikes here; recon on the CGF node)   // ⭐ CE-4603 (B-6) — the trunks it (or a player's hull) felled   // ⭐ CE-4591 (G-11) — an owned man standing where a crater opened takes the new ground   // ⭐ CE-4591 (P-4) — the craters it decided, written into the cell's ground-damage entity   // ⭐ CE-4391 S-1 — the breaches it decided, written into the building's damage entity (after it: B-5)
    }
}
