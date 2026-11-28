using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ N-11 (CE-4473; docs/DESIGN_Parallel_Simulation.md §18.2, D-6) — the component types brain code (the library behaviours, the helpers
    /// generated code calls, and the generated code itself) reads on ANOTHER unit: inside the brains' per-entity phase such a read is served
    /// from the last-tick view, so the view copies exactly these. Seeded from the entity-access census (§18.1a) and completed by the
    /// determinism rail run with the checks on (<c>HROT_READ_CHECK=1</c>: an undeclared cross read throws, naming the type).
    /// ⚠ The design's target is the GENERATOR emitting each asset's set (§18.2) — this hand list stands for it until then.
    /// </summary>
    public static class BrainReads
    {
        /// <summary>Declares the set (idempotent).</summary>
        public static void Declare()
        {
            // measured by ParallelBrainsDeterminismTests under HROT_READ_CHECK=2 (the survey) — who is in the fight, what side, whose part
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.Health>();
            ParallelReadCheck.DeclareUnmanaged<EntityInfo>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.PartMetadata>();
            // measured on the scale rig (CGF, 300 men, squads marching — the survey, HROT_READ_CHECK=2): a squad play reads its members
            ParallelReadCheck.DeclareUnmanaged<SimTransform>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.BehaviorState>();   // ⭐ every unit writes its own in the phase
            // ⭐ CE-4612 — a commander asks whether its member still runs ITS order (CurrentOrders.Of: the member's start record / paused task)
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Behavior.Components.PausedTask>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.Roe>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkAuthority>();   // ⭐ CE-3470 — WeaponDeployment.Owns: does this node own the gunner's weapon? (OwnershipApplier writes it, outside the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Squad.SquadLeadership>();   // a member's brain asks who leads his squad
            ParallelReadCheck.DeclareUnmanaged<Fdp.Core.CommandHierarchy.UnitRoster>();          // the squad assignment (StandardInputs.IsAssignedTarget)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Squad.SquadCognitiveState>();          // — and its squad's assigned targets
            ParallelReadCheck.DeclareUnmanaged<Fdp.Core.CommandHierarchy.UnitSubordinate>();      // the ROE through his commander (AimAndFireExecutor)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.CoverClaim>();       // a friend's cover claim (R-4208: seen a tick late)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.TkbIdentity>(); // a contact's template (the threat level's weapon count)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.StaticObstacle>();             // CE-7012 — the dozer's blocker (nobody writes it in the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.ObstacleShape>();               // — and its box (its nearest face)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Animals.HerdState>();               // ⭐ CE-4623 AB-4 — an animal's herd (the HerdSystem writes it serially, before the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Creatures.Group>();                 // ⭐ CE-4628 — the herd's header (count, centre, lead): the HerdSystem writes it serially, before the phase, with the HerdState
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Animals.AnimalState>();             // ⭐ CE-4623 AB-5 — "is that animal attacking?" (ThreatDanger.IsNeutral), and a guard dog's own; tick-start copy
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CombatProficiency>(); // CE-2278 — a squad leader asks whether a man is Regular+ (nobody writes it in the phase)
            // ⭐ CE-7035 (X-1) — measured by the drills under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 (78 of 244 red): a squad play reads its MEMBERS.
            //   ⛔ WeaponState was declared only by the weapon dispatcher (another phase) — the brain phase read it undeclared wherever that phase
            //   did not run, and live on a first frame. ⭐ All as tick-start reads: several are written inside the phase (AimAndFireExecutor writes
            //   WeaponState; a man's own executors his Locomotion), and a stable read is only correct for a type nothing in the phase writes.
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponState>();          // SquadGates.Track — a member firing
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();      // RoomDrill.FindThrower — a member's grenade mount
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.WeaponSystemParams>();              // SquadDefendPlay.LiftOwnHold — a member's weapon
            // ⭐ S1d-3 (devices D-63 / R4-3) — the body's brain asks whether he carries a device and whether a view shows it (CarriedDevices): DeviceInfo
            //   is written once at creation, DeviceInput by the device tick's serial pre-pass (its own system, after the brains) — never in this phase
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Devices.DeviceInfo>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Devices.DeviceInput>();
            // ⭐ devices SD-S2c (§3.13c D-104 / D-106) — the drone flown through a ground station reads its station's command (RemoteControl: written by the
            //   device tick, its own system; the station's place is its SimTransform); the station reads its drone's link + telemetry (DroneLink: written by
            //   the drone's brain on itself) — tick-start reads across the brain and device phases. ⚠ Both exist from CREATION (DeviceTkbTranslator /
            //   FlightTkbTranslator): a component another phase ADDED this frame is not in the tick-start view, and the read throws (measured, SD-S2c)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Devices.RemoteControl>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.DroneLink>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Mountable>();                       // ⭐ C1 — is a carried thing an item (Items.IsMountable: checkpoint / stop-at-signal, carry stations, operator use)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponPosture>();            // CE-7035 — a civilian reads how a forces man holds his weapon (covered / aimed at: CE-2239, CE-2241)
            ParallelReadCheck.DeclareUnmanaged<global::CarKinem.Core.VehicleParams>();               // CE-7035 — a civilian / rioter tells a vehicle from a man (its params)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.AmmoLoadout>();              // CE-7035 — what a forces man's weapon is loaded with (a baton round is no cover, CE-2239)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.BallisticProjectile>();      // CE-7035 — a lying round / stone a rioter may pick up (a live grenade is not a stone, CE-2238)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.FighterPilotState>();   // ⭐ CE-10003 (air combat RV-6) — a bandit's live "inbound" (one missile at a time); the MOTION node writes it, never the brain phase
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.AirArena>();            // ⭐ CE-10003 — the arena (AirArenaSystem writes it serially, before the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.EffectState>();                         // CE-7035 — an effect on a man (a rioter's target choice, CE-2238)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponAmmo>();               // CE-4610 — the SITREP ammo colour (StaffComposerSystem.AmmoOf): a man's rounds, a tick old
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();      // a member aboard
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();    // SquadEmbarkPlay.Outcome — the carrier's seats
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.EngagementArea>();     // EngagementAreas.HasSuperiorSet — the superior's sector
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.RecentSenses>();       // SquadDefendPlay.RoundAtAMan — a round at a member
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.Locomotion>();                      // Locomotions.Of — a member's locomotion
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Perception.Radar.RadarSignature>();    // ⭐ CE-4617 — the radar SAM's salvo reads a target's cross-section (AmmoChoice.InDomain's lock floor); stamped at spawn, written by nothing in the phase
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Terrain.TerrainObjectKey>();                 // RoomDrill.OrderFor — a door / room key
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.DoorState>();                      // RoomDrill.OrderFor — the door's state
            // ⭐ CE-3490 — the rest of what the world query's door table carries (DoorStates.Of → BuildingDamageTable, CraterTable, FelledTrees,
            //   PlaceKnowledgeTable, WindowTable): any brain that asks the ground height or a line of sight (WorldQuery.Of) reads them. Each is written
            //   only by its own system (BuildingDamageSystem, GroundDamageSystem, PlaceKnowledgeSystem), the weapon phase or network ingress — never in
            //   the brain phase ⇒ tick-start reads. 📐 dr-dragon-village under HROT_READ_CHECK THREW in the air attack's missile defence (latent for any
            //   brain near a scenario-placed building)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.BuildingDamage>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.WindowDamage>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.GroundDamage>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.TreeDamage>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.MinefieldKnowledge>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.ActorCapabilityState>(); // an HSM condition on a member's capabilities
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CombatTemperament>();  // TemperamentOf.Has — a member's temperament
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.Descent>();                         // SquadFastRopePlay — a member on the rope
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.TurretPose>();           // BatteryFireMissionPlay.Plan — a gun's pose
            ParallelReadCheck.DeclareUnmanaged<SimVelocity>();                                        // SquadAirborneExitBehavior.Plan — the carrier's velocity
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CarrierSeats>();       // an HSM action on the carrier's seats (embark / dismount)
            // ⭐ CE-3450 (D-5) — measured by the read-check survey on showcases/city-battle and by reading the readers:
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Physics.Components.PhysicsCollider>();     // Embarkation.HullRadius — the vehicle's hull (nobody writes it in the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Creatures.Demeanour>();                         // ThreatDanger.ShowsHandsUp — another man's gesture: written in the
                                                                                                       //   phase by CivilianBehavior (own) and AimAndFireExecutor's warning
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.Stress>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Perception.Components.SensorMount>();   // ⭐ CE-4591 (terrain lane, cross-lane) — ThreatDanger → EqsTerrainSight.Mount: a contact's eye point (nobody writes it in the phase); found red by wreck-blocks-street under HROT_READ_CHECK=1             // ⭐ CE-7015 — a platoon asks whether its men are broken (CombatEffective.IsStanding; the stress job writes it in its own phase)
        }
    }
}
