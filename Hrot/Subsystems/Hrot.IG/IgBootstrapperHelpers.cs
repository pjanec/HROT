using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG;

// ⛔⛔⛔ GhostDestructionSystem was DELETED here on 2026-09-03 (CE-144, option A).
//
// It consumed DestroyEntityCommand and destroyed the entity immediately, WITHOUT the ELM. Measured
// consequence: the wire dispose is written by CycloneNetworkCleanupSystem (phase Export) on a
// DestructionOrder event, and only EntityLifecycleModule.BeginDestruction publishes that — so a destroy
// that skipped the ELM emitted no dispose sample at all, and peers kept the instance forever for any
// entity IG owned. It failed silently and in the worse direction.
//
// It was written when IG had no NetworkSpawningSystem ("so the IG no longer acts as an authoritative
// spawner"); IG now composes EntityCreationPack and schedules the shared spawn system, whose
// ProcessDestroy is the ONE consumer on every host. Map un-registration is handled by the shared
// DisposalMonitoringSystem, which is what this system was doing eagerly.
//
// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4c.

// B2: the one-system wrapper that used to live here is now the shared
// Fdp.ModuleHost.Scheduling.SingleSystemModule -- it carried no IG-specific logic, and
// SimHostModule was a byte-for-byte twin of it under a different Name.
// (RegisterGlobalSystem still rejects SystemPhase.Simulation, so a lone simulation-phase
// system must still reach the kernel via RegisterModule; only the wrapper is shared now.)
