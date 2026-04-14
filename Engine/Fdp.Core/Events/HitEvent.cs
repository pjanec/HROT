// DEBT-031: HitEvent has been moved to FDP.Toolkit.Combat.Contracts.HitEvent
// to restore kernel purity.  The kernel must not contain game-domain event types.
//
// Before (BATCH-10): HitEvent was in Fdp.Kernel to break a circular project dependency.
// After  (DEBT-031): HitEvent is in FDP.Toolkit.Combat.Contracts, which references only
//                    Fdp.Kernel.  Both FDP.Toolkit.Physics and FDP.Toolkit.Combat reference
//                    FDP.Toolkit.Combat.Contracts.
//
// Consumers: add  using FDP.Toolkit.Combat.Contracts;  instead of  using Fdp.Kernel;
//            for HitEvent access.

