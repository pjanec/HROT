// NAV-P0-T1: Assembly Placement Policy — Navigation Subsystem v2
//
// DECISION: All new navigation production code lives in the EXISTING Fdp.Toolkits assembly.
// No new production .csproj files are created for navigation subsystem v2.
//
// RATIONALE (DSC-5): The design documents (DD-Tests-Nav §2.1) named Hrot.Navigation.* assemblies
// that do not exist. Creating them would introduce circular dependency risks since Fdp.Toolkits
// already holds NavigationIntent, NavigationStatus, INavmeshProvider, PathfindingSolverSystem,
// NavigationIntentBridgeSystem, and NavigationExecutionSystem with no circular-dep issues.
//
// NAMESPACE PLAN:
//   Fdp.Toolkit.Navigation
//       Provider interfaces:   INavmeshProvider, IDtCrowdProvider, IVolumetricPathProvider, IPathRegistry
//       Value types:           NavWaypoint, TraversalKind, SurfaceType, NavAgentProfile
//       Components:            NavigationCorridorMuscle, NavigationCorridorPreview, NavigationPathDetailsBuffer
//       Tags:                  CrowdAgent
//       Allocator:             NavigationHandleAllocator
//       New systems:           (future NAV-P1..P7 work)
//
//   Fdp.Toolkit.Navigation.Fake
//       Fake providers:        FakeNavmeshProvider, FakeDtCrowdProvider, FakeVolumetricPathProvider
//       Test map:              NavTestMap
//
//   Fdp.Toolkit.Navigation.EngineBacked
//       Engine-backed providers + module (DD-EngineBacked-Nav; wraps TrajectoryPoolManager / RoadNetworkBlob)
//
// UI/EDITOR BOUNDARY:
//   ImGui inspector windows and path gizmos live in the existing Hrot.Editor.AiShared / Hrot.Editor
//   assemblies since Fdp.Toolkits must remain UI-free (no ImGui dependency).
//
// TEST PLACEMENT:
//   Layer-1 and Layer-2 nav tests: FDP/Toolkits/Fdp.Toolkits.Tests/ (Navigation/ and Eqs/ sub-folders)
//   Integration test project: Hrot.ClusterRunner.Integration.Tests (existing, already suitable)

namespace Fdp.Toolkit.Navigation
{
    // This file is intentionally empty beyond the namespace declaration.
    // Its purpose is to document the assembly placement policy (NAV-P0-T1).
}
