you are working on applying the .dev\modular-2\DESIGN.md

you are not yet done.


You have made absolutely incredible progress. Looking at `FDP_49` and `HROT_49`, you have executed some of the most difficult and high-risk structural changes required by the `DESIGN.md` blueprint. 

You are standing right at the finish line of a textbook Hexagonal Architecture transformation. 

### The Massive Wins
1. **The FDP Monolith is Shattered (Rule 1):** You successfully executed Phase 1. The 20+ legacy `FDP.Toolkit.*` and `FDP.Framework.*` project files have been physically deleted from the repository. The engine is now cleanly consolidated into `Fdp.Engine`, `Fdp.Core`, and `Fdp.Presentation`. 
2. **The App Domain is Unified (Phase 2.1):** You successfully absorbed `Hrot.Map.Common` and `Hrot.Map.Definitions` into `Hrot.Core` and deleted their `.csproj` files. We now have a single, unified domain model.
3. **True Plugin Discovery (Rule 5):** Your `LoadReferencedAssemblies()` implementation in `Program.cs` correctly scans the physical `bin` directory for `*.dll` files. The engine is now a true plugin architecture—you can drop a new subsystem DLL into the folder and the orchestrator will find it without a single code change.
4. **CGF/SimHost Decoupling:** You extracted the AI behavior nodes (`CgfNodes.cs`) out of the Muscle node!

### The Final Boss: Lingering Infrastructure Inversions
While the abstractions (`INetworkFactory`) exist, the **physical enforcement** of Rule 3 (Protocol Ignorance) is failing. The application domains are still physically bound to the concrete NED network infrastructure. 

Here are the specific architectural violations you must fix to complete the design:

**1. Subsystems still reference `Hrot.Network.NED`**
The entire purpose of Phase 4 is to delete the physical project references to the network implementations, forcing developers to use the `INetworkFactory` Anti-Corruption Layer. Yet, your subsystem projects still hold hard references to NED:
*   `Hrot.CGF.csproj` contains `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />` (with a comment noting it needs it for `NedEntityCreationRequestSource`).
*   `Hrot.IG.csproj` contains the same reference.

*The Fix:* You must physically delete these `<ProjectReference>` lines from the `.csproj` files. If deleting them causes a compiler error (e.g., CGF trying to instantiate `NedEntityCreationRequestSource`), it means that specific instantiation logic needs to be pushed down into `NedNetworkFactory` and exposed via an interface (like `INetworkFactory.CreateExConIngressHandlers`).

**2. Rogue `DdsParticipant` Instantiation (Rule 3)**
Rule 3 mandates that subsystems never call `new DdsParticipant()`; they must accept it from the Composition Root via the network factory. 
*   In `CgfApplication.cs`, the code still explicitly calls `_participant = new DdsParticipant((uint)domainId);` instead of using the injected `_networkFactory?.Participant`.

*The Fix:* Strip this out. The `CgfApplication` should rely entirely on the `Participant` provided by `HrotNodeConfig` / `INetworkFactory`.

**3. Lingering Lateral Coupling (CGF -> SimHost)**
You successfully moved `CgfNodes.cs` to `Hrot.CGF`, but `CgfDoctrineSetup.cs` is still incorrectly calling `SimHostNodes.ParseMoveToParams` and `SimHostNodes.BuildMoveToLocationInterpreter()`. Because of this, `Hrot.CGF` still thinks it needs to know about `Hrot.SimHost`.

*The Fix:* Update `CgfDoctrineSetup.cs` to call the newly relocated methods on `CgfNodes` instead of `SimHostNodes`, and ensure `Hrot.CGF.csproj` has zero project references pointing to `Hrot.SimHost.csproj`.

***

### Summary
You have built the correct abstractions, but you haven't closed the physical doors yet. 

To officially fulfill the `DESIGN.md`:
1. Open `Hrot.IG.csproj`, `Hrot.CGF.csproj`, and `Hrot.SimHost.csproj`.
2. Delete the `<ProjectReference>` pointing to `Hrot.Network.NED`.
3. Compile. Whatever breaks is a Dependency Inversion violation that must be moved into `NedNetworkFactory`!



-------

also the participant.EnableSenderTracking must be moved to where the participant is created (to composition root) as this must be called before any writer is created.

--------

You are absolutely right, and that is a brilliant catch regarding the DDS lifecycle and shared resources. 

By having the subsystems call `participant.EnableSenderTracking()`, the code is committing two severe architectural sins:

1. **Lifecycle Violation:** CycloneDDS mandates that sender tracking configuration must be applied to the `DdsParticipant` *before* any `DdsWriter<T>` instances are created. Because the `INetworkFactory` (which creates writers) is instantiated and passed into the subsystems, calling it inside the subsystem's `Initialize` method is too late and risks undefined behavior or crashes.
2. **Shared Resource Corruption:** In a combined deployment like `Hrot.ClusterRunner` running `--mode all`, `Program.cs` creates a single `DdsParticipant` and injects it into all subsystems. If `ExConSubsystem`, `IgApplication`, and `HrotNodeBuilder` all call `EnableSenderTracking` on that exact same shared participant, they will overwrite each other's `AppInstanceId`, corrupting the network identity tracking for the entire process.

Here is exactly how to fix this and push the configuration up to the Composition Root where it belongs:

### 1. Centralize Tracking in the Composition Root (`Program.cs`)
In `Hrot.ClusterRunner/Program.cs`, immediately after instantiating the participant, apply the tracking configuration using the base node ID for the physical process.

```csharp
var participant = config.Headless ? null : HrotEnvironment.CreateParticipant(config.DomainId);
participant?.EnableSenderTracking(new SenderIdentityConfig
{
    AppDomainId   = config.DomainId,
    AppInstanceId = config.NodeId // The base ID representing this physical process
});
```

### 2. Update the Test Harnesses
Because integration test harnesses like `HrotRunnerHarness` and `RecordReplayIntegrationTests` act as their own composition roots and create their own isolated participants, they must also be updated to call `EnableSenderTracking` immediately after participant creation.

### 3. Strip Tracking from the Subsystems and Builders
You must ruthlessly delete the `EnableSenderTracking` calls from the inner domain and infrastructure adapters:
*   Delete it from `ExConSubsystem.Initialize()`.
*   Delete it from `IgApplication.InitializeNetwork()`.
*   Delete it from `HrotNodeBuilder.Build()`.

By making this change, the `DdsParticipant` becomes a fully configured, immutable infrastructure service by the time it is handed down to the `INetworkFactory` and the subsystems. This guarantees that all DDS writers created by the adapters inherit the correct, stable sender identity and perfectly aligns with our rule that subsystems must remain entirely ignorant of DDS lifecycle management.

---



before finishing, make sure the tests are passing and the solution compiles.
