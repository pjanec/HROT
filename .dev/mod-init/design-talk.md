
the codebase is supposed to implement the ideas from the .dev\eyes-and-muscle\DESIGN.md  PLs check how close it is.

The codebase  implements the first three phases of the `.dev\eyes-and-muscle\DESIGN.md` workstream highly accurately, but **Phase 4 remains incomplete due to architectural tech debt**.

Here is a detailed breakdown of how closely the codebase matches the specific design goals:

Phase 1: DRY Initialization Infrastructure (Implemented)

The codebase successfully extracts the repeated FDP/Hrot node bootstrap sequence into reusable builders to fulfill the DRY (Don't Repeat Yourself) promise.

-   **HrotNodeBuilder** **and** **HrotNodeContext****:** The codebase features a functional `HrotNodeBuilder` that constructs a generic engine stack (like `EntityRepository`, `ModuleHostKernel`, and `FdpEventBus`) alongside application-layer elements (`DdsParticipant` and `ClusterSlave`). It outputs an immutable `HrotNodeContext`.-   **Inline Handlers:** As architecturally constrained, standard handlers (`ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, `ReferenceLiveLoadHandler`) are wired directly inline during the builder phase to prevent domain logic from bleeding into shared infrastructure.

Phase 2: NedReplicationModule (Implemented)

The `NedReplicationModule` successfully encapsulates NED translators and their architecturally coupled ECS systems to serve as an Anti-Corruption Layer.

-   **Synchronous Execution:** As mandated by the design constraints, the module strictly utilizes `ExecutionPolicy.Synchronous()` to ensure safe CycloneDDS memory polling.-   **Role-Based Granularity & Collision Guards:** The module correctly activates translators based on the active `NodeRole`. Crucially, it implements the `driveFromNetwork` flag for the `DeadReckoningSyncSystem`. If the node has local physics authority (e.g., `NodeRole.AllInOne` or `NodeRole.MuscleGround`), the system filters queries to skip locally-owned entities, preventing Dead Reckoning from fighting authoritative local physics.

Phase 3: EyesAndMuscle Subsystem (Implemented)

The codebase successfully introduces the `EyesAndMuscleSubsystem` as a tracer bullet to prove the Snapshot-on-Demand (SoD) pattern.

-   **Combined Logic:** The subsystem operates without an inner `App` class, standing up directly on `HrotNodeBuilder`. It registers the `NedReplicationModule` under `NodeRole.AllInOne`.-   **Asynchronous SoD Execution:** The `EyesAndMuscleModule` uses `ExecutionPolicy.SlowBackground(60)` to run asynchronously on a background thread against an immutable simulation snapshot, successfully proving thread safety across the brain-muscle-eyes boundary.

Phase 4: Migrate Existing Subsystems (Incomplete / Tech Debt)

Phase 4 dictated that the migration must happen universally in a single pass to prevent temporary architectural fragmentation. **The codebase fails this constraint.**

-   **CgfSubsystem** **Migrated:** The CGF node successfully implements the clean Phase 3 pattern, utilizing both `HrotNodeBuilder` and `NedReplicationModule`.-   **SimHostApp** **and** **IgApplication** **Fragmented:** The legacy monolithic nodes have not been fully migrated. `SimHostApp` still manually registers dozens of ingress and egress translators individually in its `OnLoad` method.-   **The Blocker:** The codebase explicitly documents this failure via a comment in `SimHostApp`: `// TODO (P2 debt): wire NedReplicationModule once it moves to Hrot.Common so SimHostApp can reference it.`. Because `NedReplicationModule` currently lives in `Hrot.ClusterRunner`, `SimHost` cannot reference it without creating a circular dependency. Consequently, legacy structures like `EntityStatesIngressPack` still exist to serve these unmigrated applications.

pls suggest how to complete phase 4. I need the NedReplicationModule to live outside of the clusterrunner, for example in Hrot.Common. Think of it as if we needed to create separate CGF and IG and SimHost applications without clusterrunner. I do not want separate executables now, i just need the architecture to allow it easily by proper modularizing. Pls divide the solution into stages. Then we weill detail one by one.

To achieve true physical decoupling of our node applications (CGF, IG, and SimHost) from `Hrot.ClusterRunner`, we must resolve the inverted dependency graph. Currently, `NedReplicationModule` acts as a monolithic composition tier inside `Hrot.ClusterRunner` because it directly references domains that sit higher in the architecture, such as `Hrot.SimHost` (for kinematic/cognitive translators) and `Hrot.IG` (for dead reckoning).

This violates clean architecture principles and is the exact reason `SimHostApp` cannot currently reference the module to complete Phase 4 without creating a circular dependency.

To pull `NedReplicationModule` down into a shared infrastructure layer like `Hrot.Common` and allow independent executables, we should execute this migration in four distinct architectural stages:

Stage 1: Push Down Architecturally Coupled Systems

Before we can move the module, we must push its dependencies down into the common layer.

-   **Relocate** **DeadReckoningSyncSystem****:** This system currently resides in `Hrot.IG.Systems`. Because `NedReplicationModule` is responsible for orchestrating dead reckoning on remote ghosts, this system must be moved to `Hrot.Common` (or `Hrot.Map.Common`) so the replication module can reference it without depending on the presentation layer.-   **Relocate Translator Packs:** The `SharedTranslatorPack`, `KinematicTranslatorPack`, and `CognitiveTranslatorPack` currently live in `Hrot.SimHost.Network`. Since these simply map `Hrot.NED` network descriptors to `FDP` toolkit components, they contain no host-specific logic and must be extracted downward to `Hrot.Common`. (Note that `EntityStatesIngressPack` is already decently positioned in `Hrot.Map.Common.Translators`).

Stage 2: Relocate and Wire `NedReplicationModule`

With the dependencies pushed down, the Anti-Corruption Layer (ACL) is no longer fragmented.

-   **Move the Module:** Relocate `NedReplicationModule` from `Hrot.ClusterRunner.Replication` to `Hrot.Common`.-   **Validate the Boundaries:** Ensure `Hrot.Common` does not accidentally acquire references to `Hrot.SimHost` or `Hrot.IG`. The module will now cleanly bundle the role-appropriate NED translators and their coupled smoothing/lifecycle systems (like `GhostCreationSystem` and `SmartEgressSystem`) purely from shared libraries.

Stage 3: Eradicate Legacy Boilerplate in Monolithic Apps

This is the core execution of Phase 4. We can now fulfill the DRY initialization promise across all existing nodes.

-   **Refactor** **SimHostApp.OnLoad****:** Strip out the ~300 lines of manual ECS/DDS translation boilerplate. Replace it with `HrotNodeBuilder` and register the newly shared `NedReplicationModule` under `NodeRole.AllInOne` (or `NodeRole.MuscleGround` depending on configuration).-   **Refactor** **IgApplication.InitializeEmbedded****:** Similarly, gut the manual network setups and consolidate `EntityStatesIngressPack` and `DeadReckoningSyncSystem` by registering `NedReplicationModule` initialized with `NodeRole.ImageGenerator`.

Stage 4: Decouple the CGF Subsystem & Prove Isolation

-   **Update** **CgfSubsystem****:** The CGF node already utilizes `HrotNodeBuilder`, but currently pulls `NedReplicationModule` from the `ClusterRunner` namespace. Redirect this to the new `Hrot.Common` namespace.-   **Extract Executables:** At this stage, `SimHostApp`, `IgApplication`, and `CgfApplication` are completely self-sufficient. They rely only on `Hrot.Common` for their bootstrapping and network replication. We can now trivially create independent `.exe` wrappers (e.g., `SimHost.exe`, `IG.exe`) that instantiate their respective apps, achieving absolute modularity without relying on `ClusterRunner` to glue them together.

Let me know which stage you would like to detail and attack first. Stage 1 is the critical unblocker for the rest of the architecture.

pls detail stage 1

Excellent. Let's tackle Stage 1, which is all about resolving our inverted dependencies and strictly enforcing the Dependency Inversion Principle. Right now, our infrastructure layer is improperly bleeding into our application domains, which is a classic clean architecture violation.

To prepare `NedReplicationModule` for its relocation down the stack, we need to push its dependencies down into a shared library like `Hrot.Map.Common` or `Hrot.Common`. Here is the detailed technical execution plan for Stage 1:

1\. Extract `DeadReckoningSyncSystem` from the Presentation Layer

Currently, the `DeadReckoningSyncSystem` resides inside the `Hrot.IG.Systems` namespace, which tightly couples it to the Image Generator (IG) presentation application.

-   **The Problem:** Because `NedReplicationModule` orchestrates dead reckoning for remote ghosts across multiple roles (like `NodeRole.ImageGenerator` and `NodeRole.AllInOne`), having it reference the `Hrot.IG` assembly creates a massive architectural bottleneck.-   **The Fix:** We must physically move `DeadReckoningSyncSystem.cs` down to `Hrot.Map.Common` (or `Hrot.Common`).-   **Why this is structurally sound:** This system reads `NetworkTransform` and `NetworkVelocity` components to smoothly interpolate `SimTransform`. These components are written exclusively by our network translators. Therefore, dead reckoning is fundamentally an Anti-Corruption Layer (ACL) smoothing mechanism, not a rendering concern, and belongs alongside the network infrastructure.

2\. Push Down the SimHost Translator Packs

We have several translator packs currently trapped in the `Hrot.SimHost.Network` namespace. They must be relocated downwards to sit alongside our existing `EntityStatesIngressPack`, which is already correctly positioned in `Hrot.Map.Common.Translators`.

-   **Move** **SharedTranslatorPack****:** Extract this from `Hrot.SimHost.Network`. This pack handles universal lifecycle synchronization (like `EntityMasterEgressTranslator` and `EntityInfoEgressTranslator`) which every node requires regardless of its specialization.-   **Move** **KinematicTranslatorPack****:** Extract this from `Hrot.SimHost.Network`. This pack handles muscle-tier network synchronization (e.g., `GeoSpatialEgressTranslator` and `NavigationStatusEgressTranslator`), mapping pure physics and navigation states without relying on host-specific domain logic.-   **Move** **CognitiveTranslatorPack****:** Extract this from `Hrot.SimHost.Network`. It publishes intent data like `NavigationIntentEgressTranslator` and `EntityMissionEgressTranslator`.

3\. Validate the Layer Boundaries

Once these systems and factories are moved into `Hrot.Map.Common` (or `Hrot.Common`), we must verify our dependency graph.

-   Ensure that `Hrot.Map.Common` compiles cleanly without holding any references to `Hrot.SimHost` or `Hrot.IG`.-   The explicitly stated technical debt—where `Hrot.ClusterRunner` relies on `Hrot.IG` just to reach the dead reckoning system—will be officially resolved.

By completing this stage, we isolate the network-to-ECS bridging logic entirely within the lower infrastructure layers. Let me know when you are ready to discuss Stage 2, where we will actually relocate the `NedReplicationModule` itself to finalize the ACL boundary.

now pls detail stage 2

Let's execute Stage 2. This stage physically relocates the `NedReplicationModule` to finalize our Anti-Corruption Layer (ACL) boundary and enforce the Dependency Inversion Principle.

By completing Stage 1, we removed the architectural roadblocks. Now, we will lift and shift the module itself so that it serves as a shared, application-agnostic infrastructure component.

Here is the technical execution plan for Stage 2:

1\. Physical Relocation and Namespace Update

The `NedReplicationModule` was explicitly placed in `Hrot.ClusterRunner.Replication` as a temporary measure because it was the only project high enough in the dependency graph to reference both `Hrot.SimHost` and `Hrot.IG` simultaneously. Now that we have inverted those dependencies, we can move it.

-   **Move the File:** Physically move `NedReplicationModule.cs` from the `Hrot.ClusterRunner/Replication/` directory to `Hrot.Common/Replication/`.-   **Update the Namespace:** Change the namespace from `Hrot.ClusterRunner.Replication` to `Hrot.Common.Replication`.

2\. Purge Application-Layer Dependencies (The "Clean Architecture" Guard)

Because `Hrot.Common.csproj` strictly sits below the application layer, it does not and must not contain `<ProjectReference>` elements pointing to `Hrot.SimHost` or `Hrot.IG`. This natural boundary will enforce our isolation.

-   **Remove using directives:** In the newly relocated `NedReplicationModule.cs`, delete the following directives which bleed into the application layer:
    -   `using Hrot.IG.Systems;`-   `using Hrot.SimHost;`-   `using Hrot.SimHost.Network;`-   **Resolve References:** Redirect the module to use the newly shared namespaces established in Stage 1.
    -   `DeadReckoningSyncSystem` will now resolve from `Hrot.Common` or `Hrot.Map.Common`.-   `SharedTranslatorPack`, `KinematicTranslatorPack`, and `CognitiveTranslatorPack` will now resolve from their new lower-level home.

3\. Verify the Anti-Corruption Contract

The core logic of `NedReplicationModule` will remain entirely intact, but we must verify its role-based guards still compile cleanly in the new location:

-   **Synchronous Execution:** Ensure `Policy => ExecutionPolicy.Synchronous();` remains enforced, as CycloneDDS memory polling and ECS smoothing must happen on the main thread.-   **Dead Reckoning Collision Guard:** Verify the `driveFromNetwork` flag logic remains intact for the `DeadReckoningSyncSystem`. If the node role is `AllInOne`, the system must be instantiated with `driveFromNetwork: false` to ensure it only smooths remote ghost entities and skips locally-owned entities, preventing it from fighting the local ground kinematics.

4\. Update the Node Context (Optional but Recommended)

Currently, `HrotNodeBuilder` outputs an `HrotNodeContext` that holds a nullable `GhostCreationSystem` which is populated _after_ `NedReplicationModule` is built.

-   Now that `NedReplicationModule` lives next to `HrotNodeBuilder` in `Hrot.Common`, you can optionally have `HrotNodeBuilder` instantiate `NedReplicationModule` internally and output it directly in the `HrotNodeContext`. This further reduces the boilerplate in the downstream application composition roots.

Once this compiles, `Hrot.Common` is entirely self-sufficient for network replication. At this point, we will be completely unblocked to move to Stage 3, where we will gut the ~300 lines of legacy boilerplate from `SimHostApp` and `IgApplication` and replace them with our clean builder. Let me know when you are ready to tackle Stage 3.

now pls detail Stage 3: Eradicate Legacy Boilerplate in Monolithic Apps

Let's execute Stage 3 and systematically eradicate the monolithic composition root debt. This is where we pay off the architectural refactoring by fulfilling the DRY promise and cleanly decoupling our application layers from the network replication infrastructure.

By this point, we assume `NedReplicationModule` safely lives in `Hrot.Common` (completed in Stage 2). We will now migrate `SimHostApp` and `IgApplication` one by one to ensure isolated validation.

1\. Gutting `SimHostApp.OnLoad`

`SimHostApp` currently contains massive boilerplate to manually wire its CycloneDDS network dependencies. We will eliminate the explicit instantiation of individual translators (like `EntityMasterEgressTranslator`, `EntityInfoEgressTranslator`, and the cognitive/kinematic packs).

-   **Remove the manual pipeline:** Delete the manual instantiations of `GhostCreationSystem`, `SimulationSystemGroup`, and `NetworkLifecycleSystemGroup` that currently precede the orchestration build.-   **Wire the Module:** Replace the ~300 lines of manual translator lists and `CycloneNetworkModule` registrations with a single instantiation of `NedReplicationModule`. We will construct it passing the `_role` field (usually `NodeRole.AllInOne` for the monolithic host).-   **Retain Domain Specifics:** Per clean architecture, do not move domain-bound initializations. `SimHostApp` must continue to explicitly register its `DoctrineRegistry`, `RoadNetworkBlob`, `CheckpointIOWorker`, and scenario serializers.-   **State Tracker Pattern:** Assign the instantiated `NedReplicationModule` to the `_nedReplicationModule` private field that currently sits empty with a `// TODO (P2 debt)` comment. Retaining this reference is architecturally mandated so the `SubsystemOrchestrator` can cleanly hot-swap or uninstall the module at runtime without relying on kernel reflection.

2\. Gutting `IgApplication.InitializeEmbedded`

`IgApplication` suffers from the same fragmentation, explicitly constructing its entire replication layer including the `EntityStatesIngressPack` and manually registering the `DeadReckoningSyncSystem` to the global kernel.

-   **Consolidate the ACL:** Strip out the manual DDS/time/cluster setups and translator instantiations. Replace them by building the `HrotNodeContext` via `HrotNodeBuilder` and registering the `NedReplicationModule` configured strictly with `NodeRole.ImageGenerator`.-   **Architectural Guard Validation:** By initializing it with the `ImageGenerator` role, the module inherently bundles the `SharedTranslatorPack` with the `EntityStatesIngressPack`. Crucially, it automatically registers the `DeadReckoningSyncSystem` with the `driveFromNetwork: true` flag to ensure it smooths all entities, enforcing the structural rule that dead reckoning belongs exclusively to the replication boundary.

3\. Validating the Boundaries (Zero Behavioral Drift)

Because this is a pure structural refactor, we must strictly enforce the "no behavioral change" constraint.

After migrating `SimHostApp`, run the entire `Hrot.SimHost.Integration.Tests` suite. Do not touch `IgApplication` until SimHost passes 100%. Once both applications are migrated, we will have successfully isolated our network-to-ECS bridging logic into a single modular Anti-Corruption Layer, leaving our domain apps beautifully lean.

Once the tests turn green, we will have a unified, DRY application boot path and we can trivially knock out Stage 4 (verifying the CGF subsystem and proving isolated executables). Ready for Stage 4 when you are.

and now pls detail Stage 4: Decouple the CGF Subsystem & Prove Isolation

Let’s bring this architectural refactor to the finish line with Stage 4. This is where we validate our strict Dependency Inversion and prove that our applications are completely decoupled from the monolithic `Hrot.ClusterRunner`.

Because you don't want to create the physical `.exe` wrappers just yet, our goal here is to establish absolute structural readiness so that creating those executables later requires zero refactoring.

Here is the technical execution plan for Stage 4:

1\. Update the CGF Boundary (`CgfSubsystem` / `CgfApplication`)

The CGF node was our tracer bullet and already utilizes the `HrotNodeBuilder`, but it is currently holding a dirty reference to the old module location.

-   **Fix the Namespace:** In `CgfSubsystem.cs` (and `CgfApplication.cs` if applicable), remove the `using Hrot.ClusterRunner.Replication;` directive.-   **Wire to Common:** Point it to our newly relocated `NedReplicationModule` in the common infrastructure layer.-   **Validate the Role Contract:** Ensure it continues to instantiate the module strictly with `NodeRole.Brain`. This guarantees the CGF node securely loads the `CognitiveTranslatorPack` and isolates itself from ground kinematics and rendering logic.

2\. Sever the Upward Dependencies (The Clean Architecture Guard)

To prove that our modules are truly decoupled, we must enforce a strict, downward-only dependency graph.

-   Inspect `Hrot.SimHost.csproj`, `Hrot.IG.csproj`, and `Hrot.CGF.csproj`.-   Verify that **none** of these application layer projects contain a `<ProjectReference>` pointing up to `Hrot.ClusterRunner`.-   They must solely depend on our shared lower layers (e.g., `Hrot.Common`, `Hrot.Map.Common`, and the `FDP` toolkits) to resolve `HrotNodeBuilder` and `NedReplicationModule`.

3\. Prove Executable Isolation (Conceptual Validation)

Currently, `Hrot.ClusterRunner/Program.cs` acts as a monolithic switchboard, parsing command-line arguments like `-m simhost` or `-m ig` to instantiate the respective subsystems.

Because of the work we completed in Stages 1 through 3, this switchboard is now merely a convenience rather than an architectural requirement. We have proven isolation because:

-   The `SimHostApp`, `IgApplication`, and `CgfApplication` classes now completely own their network bootstrapping via `HrotNodeBuilder`.-   They inject their own domain-specific systems (like `DoctrineRegistry` or `RoadNetworkBlob`) directly into the generic builder output without relying on the orchestrator to glue them together.

Whenever you are ready to ship separate executables, you will only need to create an empty Console App (e.g., `SimHost.exe`), reference `Hrot.SimHost.csproj`, and write a `static void Main()` that calls `new SimHostApp().Run()`. The domain logic, ECS bridging, and CycloneDDS networking will seamlessly stand themselves up without any further architectural surgery.

Run the full integration test suite one last time. If everything is green, we have successfully eradicated the monolithic tech debt and achieved a pristine, loosely coupled node architecture. Let me know if you want to review the test output or inspect any specific translation boundaries!

