While FastHSM inherently solves the resource lifecycle problem via native `OnExit` actions, the engine sources explicitly identify several other deferred enhancements, missing implementations, and technical debt items within the FastHSM subsystem and its hosting environment that are still needed. 

Here are the specific HSM-related code changes and improvements mapped out in the architecture:

**1. Full Orthogonal Region Arbitration (Priority-Based)**
Currently, when multiple orthogonal regions within a parallel state attempt to write to the same actuator lane (e.g., both trying to output a locomotion command), the `ArbitrateOutputLanes` method in `HsmKernelCore` only detects the conflict and suppresses the output by logging a conflict to the trace context. The codebase explicitly notes that "Full arbitration with priority would be P4 (future)". Implementing this priority-based arbitration is a required future enhancement.

**2. Brain-Side Persistence and Replay (CGF Node)**
Because HSMs run on the CGF (Brain) node, they are currently affected by a brain-side persistence gap. The CGF node uses a `FailLoudRecordReplayStub` for cluster operations because it does not yet host a recordable `ModuleHostKernel` (Phase 3+ brain kernel). Until this is resolved, operations like `FinalizeLive`, `PrepareReplay`, and `FinalizeReplay` are explicitly unsupported for Brain-tier HSMs. The sources dictate that once the CGF acquires a fully recordable kernel, this stub must be removed and replaced with a real implementation using shared orchestration handlers.

**3. Binary Serialization of HSM Compiler Blobs**
In the `Fhsm.Compiler` toolchain, the `HsmEmitter` currently generates the in-memory `HsmDefinitionBlob` and the debug metadata JSON, but explicitly skips binary serialization. The source notes: "Note: Binary serialization of blob is not implemented here... I'll skip binary serialization of blob". A robust binary serializer for the compiled blobs will be necessary for offline asset baking or network distribution of state machine definitions.

**4. Hardening Deep History Restoration into Parallel States**
There is tentative logic inside `HsmKernelCore.DrillDownToInitial` regarding how History pseudo-states interact with Parallel (orthogonal) states during a history restore. The engine currently breaks the drill-down if it hits a parallel state, noting: "If we hit a Parallel state, we stop? Usually History restores into Parallel means entering Parallel". This interaction between `IsDeepHistory` and `IsParallel` flags requires hardening to ensure deterministic restoration of complex concurrent sub-states.
