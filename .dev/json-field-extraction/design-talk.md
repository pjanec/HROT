I see manual string-literal based json field extraction - i think it should be replaced with shared DTO deserialization approach; found for example in the following files/classes:
 - ClusterScenarioPanel
 - NodeOpSlaveTranslator
 - OrchestratorActionHandlers
 - ClusterOpRequestAdapter
 - GlobalContextClusterOpHandler

The current implementation exhibits a classic "Primitive Obsession" and "Magic String" anti-pattern by manually walking the JSON Document Object Model (DOM) using `JsonDocument.Parse` and `TryGetProperty` across multiple architectural boundaries. Distributing raw string literals like "TargetState" or "ScenarioId" across translation layers, UI panels, and test handlers makes the codebase brittle, duplicates parsing logic, and completely bypasses the safety of the compiler. 

Fortunately, the codebase already defines a highly cohesive, strongly-typed solution: the `OrchestrationPayloadDtos.cs` file, which contains explicit Data Transfer Objects (DTOs) like `TransitionPayloadDto`, `ManageEpisodePayloadDto`, and `NodeTransitionPayloadDto`. 

Here is the clean architecture refactoring plan to eradicate manual JSON parsing and enforce strict contract deserialization:

**1. Refactor `ClusterOpRequestAdapter`**
Currently, methods like `ToTransitionStateIntent` and `ToManageEpisodeIntent` manually extract properties using `doc.RootElement.TryGetProperty`. This must be replaced with `JsonSerializer.Deserialize<TransitionPayloadDto>` and `JsonSerializer.Deserialize<ManageEpisodePayloadDto>`. By using the existing `OrchestrationJsonOptions.Default`, we also automatically enforce the `StrictStringEnumConverter`, guaranteeing that silent integer-as-enum bugs are caught during deserialization. The internal `ExtractString` and `ExtractGuid` helpers should be deleted entirely.

**2. Refactor `NodeOpSlaveTranslator`**
The slave translator relies on local `GetString`, `GetBool`, and `GetGuid` helper methods to manually fish values out of the payload. This violates the DRY principle since the master translator already defines the contract. Replace these manual lookups with `JsonSerializer.Deserialize<T>`, targeting the existing `NodeTransitionPayloadDto`, `NodeEpisodePayloadDto`, and `NodePrefetchPayloadDto` records. 

**3. Refactor `ClusterScenarioPanel`**
The UI layer is heavily polluted with ad-hoc DOM parsing, utilizing custom `TryParseFloat`, `TryParseWallTicks`, `ParseTransitionStateIntent`, and `ParseManageEpisodeIntent` methods. A UI panel should never be responsible for parsing network wire formats. We must strip out all `JsonDocument.Parse` calls here and deserialize the payloads directly into `TransitionPayloadDto` and `ManageEpisodePayloadDto`. 

**4. Refactor `GlobalContextClusterOpHandler`**
This domain handler manually implements `ParseExerciseId`, `ParseScenarioId`, and `ParseTargetState` to determine how it should react to save and load operations. This logic must be removed. The handler should deserialize the payload into an `ArchivePayloadDto` (for serialization) or a `NodeTransitionPayloadDto` (for state commits) to maintain a strict, type-safe boundary.

**5. Refactor `OrchestratorActionHandlers` (`ClusterOpActionHandler`)**
In the headless test harnesses, the action handler manually packs a `Dictionary<string, object>` and blindly serializes it into the `ClusterOpRequest`. This skips schema validation entirely. To ensure our tests accurately reflect production contracts, the test handler must instantiate the actual `TransitionPayloadDto` or `SeekReplayPayloadDto` records and serialize them using the strict `OrchestrationJsonOptions.Default`.

**Success Conditions for this Refactoring:**
*   **Structural Purity:** There must be zero usages of `JsonDocument.Parse` or `TryGetProperty` across `ClusterScenarioPanel`, `NodeOpSlaveTranslator`, `OrchestratorActionHandlers`, `ClusterOpRequestAdapter`, and `GlobalContextClusterOpHandler`.
*   **Type Safety:** All payloads are routed exclusively through `System.Text.Json.JsonSerializer.Deserialize<T>` targeting the DTOs in `OrchestrationPayloadDtos.cs`.
*   **Strict Deserialization:** The `OrchestrationJsonOptions.Default` is applied universally, guaranteeing that malformed data or numeric enum violations are caught instantly by the serializer rather than causing silent downstream failures.


Pls make sure you are using json deserialzer options ensuring
  - PropertyNameCaseInsensitive = true,
  - IncludeFields = true
