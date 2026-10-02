using Xunit;

// ⛔⛔ DISABLE PARALLEL TEST EXECUTION ACROSS THIS ASSEMBLY — and the reason is MEASURED, not
//    precautionary. 📄 DESIGN_Occurrence_Scoped_Storage.md §33.12.6.
//
// 🔴 The failure, captured 2026-09-27:
//
//    System.InvalidOperationException : Operations that change non-concurrent collections must
//    have exclusive access. A concurrent update was performed on this collection and corrupted
//    its state.
//      at System.Collections.Generic.Dictionary`2.TryInsert
//      at Fhsm.Kernel.HsmActionDispatcher.RegisterAction (HsmActionDispatcher.cs:41)
//      at BlueprintRegistrar_HsmTwoRegionParamsDemo_1434647B_Bp.Register(BlueprintRegistryStaging)
//
// ⭐⭐ THE HAZARD IS PRE-EXISTING AND STRUCTURAL: several test classes here INVOKE REAL GENERATED
//    REGISTRARS, and a registrar writes to process-wide, NON-thread-safe statics —
//    HsmActionDispatcher's plain Dictionary being the one that actually corrupted. xUnit runs
//    distinct test CLASSES in parallel, so any two such classes can race.
//
// ⚠ It stayed latent only because the timing never lined up; adding E6_R9/E6_R10 (which invoke a
//   real hosting registrar and load assets) widened the window and it fired immediately —
//   measured as 4 failures without those rails and 5 with them, while the newly-red test passes
//   in isolation.
//
// ⛔ THIS IS A TEST-HARNESS FIX, NOT A PRODUCT FIX. In production the registrar scan runs ONCE,
//    single-threaded, at boot (BlueprintRegistrarScanner.Scan), so the dispatcher's plain
//    Dictionary is correct there. Making it concurrent would add a lock to a startup-only path to
//    satisfy a test runner — the wrong trade. 📋 If registrars ever run concurrently in
//    production, THAT is when the dispatcher needs to change.
//
// ⭐ Precedent: FDP/Toolkits/Fdp.Toolkits.Tests/AssemblyInfo.cs does the same, for the same class
//   of reason (a static ComponentTypeRegistry raced between two suites).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
