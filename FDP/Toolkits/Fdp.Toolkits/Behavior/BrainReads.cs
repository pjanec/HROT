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
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.Roe>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Squad.SquadLeadership>();   // a member's brain asks who leads his squad
            ParallelReadCheck.DeclareUnmanaged<Fdp.Core.CommandHierarchy.UnitRoster>();          // the squad assignment (StandardInputs.IsAssignedTarget)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Squad.SquadCognitiveState>();          // — and its squad's assigned targets
            ParallelReadCheck.DeclareUnmanaged<Fdp.Core.CommandHierarchy.UnitSubordinate>();      // the ROE through his commander (AimAndFireExecutor)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.CoverClaim>();       // a friend's cover claim (R-4208: seen a tick late)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.TkbIdentity>(); // a contact's template (the threat level's weapon count)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.StaticObstacle>();             // CE-7012 — the dozer's blocker (nobody writes it in the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.ObstacleShape>();               // — and its box (its nearest face)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CombatProficiency>(); // CE-2278 — a squad leader asks whether a man is Regular+ (nobody writes it in the phase)
            // ⭐ CE-7035 (X-1) — measured by the drills under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 (78 of 244 red): a squad play reads its MEMBERS.
            //   ⛔ WeaponState was declared only by the weapon dispatcher (another phase) — the brain phase read it undeclared wherever that phase
            //   did not run, and live on a first frame. ⭐ All as tick-start reads: several are written inside the phase (AimAndFireExecutor writes
            //   WeaponState; a man's own executors his Locomotion), and a stable read is only correct for a type nothing in the phase writes.
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponState>();          // SquadGates.Track — a member firing
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();      // RoomDrill.FindThrower — a member's grenade mount
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.WeaponSystemParams>();              // SquadDefendPlay.LiftOwnHold — a member's weapon
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();      // a member aboard
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();    // SquadEmbarkPlay.Outcome — the carrier's seats
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.EngagementArea>();     // EngagementAreas.HasSuperiorSet — the superior's sector
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.RecentSenses>();       // SquadDefendPlay.RoundAtAMan — a round at a member
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.Locomotion>();                      // Locomotions.Of — a member's locomotion
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Terrain.TerrainObjectKey>();                 // RoomDrill.OrderFor — a door / room key
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.DoorState>();                      // RoomDrill.OrderFor — the door's state
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.ActorCapabilityState>(); // an HSM condition on a member's capabilities
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CombatTemperament>();  // TemperamentOf.Has — a member's temperament
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Motion.Descent>();                         // SquadFastRopePlay — a member on the rope
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.TurretPose>();           // BatteryFireMissionPlay.Plan — a gun's pose
            ParallelReadCheck.DeclareUnmanaged<SimVelocity>();                                        // SquadAirborneExitBehavior.Plan — the carrier's velocity
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CarrierSeats>();       // an HSM action on the carrier's seats (embark / dismount)
            // ⭐ CE-3450 (D-5) — measured by the read-check survey on showcases/city-battle and by reading the readers:
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Physics.Components.PhysicsCollider>();     // Embarkation.HullRadius — the vehicle's hull (nobody writes it in the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Gesture>();                         // ThreatDanger.ShowsHandsUp — another man's gesture: written in the
                                                                                                       //   phase by CivilianBehavior (own) and AimAndFireExecutor's warning
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.Stress>();             // ⭐ CE-7015 — a platoon asks whether its men are broken (CombatEffective.IsStanding; the stress job writes it in its own phase)
        }
    }
}
