I see orchestratorsubsystem is not decoupled from concrete network at all, i see "_clusterOpTranslator = new Hrot.Orchestrator.Translators.ClusterOpMasterTranslator", is that proper hexagonal architecture? Is that following the DESIGN?

---

You have an incredibly sharp eye. You caught the one subsystem that was given a "hall pass" in the `DESIGN.md`, but even then, it is currently violating its own rules.

Here is the exact architectural reality of `OrchestratorSubsystem`, why it looks like that, and the strict violation you need to fix.

### The "Pragmatic Exception" in DESIGN.md
In a *pure* Hexagonal Architecture, you are completely right: `Hrot.Orchestrator` should have zero knowledge of DDS topics, and `ClusterOpMasterTranslator` should live in an infrastructure adapter behind an interface. 

However, if you look at **Rule 2 and Rule 3** in the `DESIGN.md` blueprint, a deliberate pragmatic compromise was made for Orchestration:
1. **Separation of Schemas (Rule 2):** Simulation data (NED/BDC) is strictly air-gapped behind `INetworkFactory`. But cluster management schemas (like `ClusterOpRequest`) were extracted into a third, shared assembly: `Hrot.Network.Orchestration`.
2. **Pragmatic Coupling (Rule 3):** *"CycloneDDS.Runtime is accepted as a universally available base technology... References to DdsParticipant and DdsWriter<T> in domain code are acceptable."*

Because Orchestration is considered an application-level concern rather than a swappable simulation protocol, the design technically *permits* `Hrot.Orchestrator` to reference `Hrot.Network.Orchestration` and instantiate `DdsWriter` directly.

### The Severe Violation: Rogue Participant Creation
While the translator instantiations are technically permitted by the pragmatic exception, **`OrchestratorSubsystem.cs` is committing a massive lifecycle violation.**

Look at `OrchestratorSubsystem.Initialize()`:
```csharp
_participant = HrotEnvironment.CreateParticipant(config.DomainId);
```
This is a direct violation of **Rule 3**, which explicitly states: 
*"No subsystem (Hrot.SimHost, Hrot.CGF, Hrot.IG, Hrot.ExCon, Hrot.Orchestrator) calls new DdsParticipant() or HrotEnvironment.CreateParticipant() internally."*

By spinning up its own network socket, the Orchestrator bypasses the Composition Root (`Program.cs`) and corrupts the offline-testing constraints.

### How to Fix It
To bring `OrchestratorSubsystem` back into compliance with the design, you must perform the following:

**1. Inject the Network Factory**
Update the constructor of `OrchestratorSubsystem` to accept the `INetworkFactory` provided by the Composition Root, just like the other subsystems:
```csharp
private readonly INetworkFactory? _networkFactory;

public OrchestratorSubsystem(INetworkFactory networkFactory)
{
    _networkFactory = networkFactory;
}
```

**2. Delete the Rogue Participant Creation**
In `OrchestratorSubsystem.Initialize()`, remove `HrotEnvironment.CreateParticipant(...)` and replace it with the injected instance:
```csharp
_participant = _networkFactory?.Participant;
```

**3. Relocate the Translators**
Our goals the  Hexagonal Architecture across the board, the pragmatic exception is unsatisfying. 
You should completely clean `Hrot.Orchestrator` by moving the three master translators (`ClusterOpMasterTranslator`, `NodeOpMasterTranslator`, and `OrchestrationObserverTranslator`) physically into the `Hrot.Network.Orchestration` assembly, exactly as was done for `NodeOpSlaveTranslator`. You should then inject them into `OrchestratorSubsystem` via interfaces (e.g., `IMasterOrchestrationTranslators`), rendering the Orchestrator domain 100% blind to DDS!

----

