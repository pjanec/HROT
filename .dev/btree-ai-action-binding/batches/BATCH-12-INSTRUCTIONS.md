# BATCH-12 — S3-1: Authoring role + scope on blackboard variables

**Task:** TASK-DETAIL.md → S3-1. **Slice 3 (§4.4 Behavior-scope shared working state MVP).**
**Design of record:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` §4.4, §4.4.3.
**Nature:** editor + persistence only. **No codegen, no runtime, no slot-key work** — those are later batches (S3-2…). Do not touch emitters, `BehaviorIngressSystem`, or `BlueprintBlackboardPartitions`.

## Goal
Every blackboard variable gains two authored attributes:
- **`Role`** — `Input` (a param, the default) or `State` (mutable working state).
- **`Scope`** (meaningful only when `Role == State`) — `Node` (default, = today's per-node local), `Behavior`, or `Entity`.

These are declared in the Variables panel, persisted in the asset blackboard block, and round-trip for **both BTree and HSM** assets. Nothing downstream consumes them yet — this batch only adds the authored data + UI + persistence.

## The precedent to mirror exactly
`IsAutoManaged` is the same kind of additive, back-compat, omit-when-default field. Follow it end-to-end:
- DTO field: `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs` → `BlackboardVariableDto` (see `IsAutoManaged` at the bottom of the class, with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`).
- Round-trip test precedent: `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence.Tests/BTree/IsAutoManagedRoundTripTests.cs` (covers BTree **and** HSM — mirror both).
- Byte-stability precedent: `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence.Tests/Json/ByteStabilityTests.cs`.
- Editor model + mapping: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs` (`VariableViewModel`, `BlackboardVariableEntry`, `IVariablesSchemaSource`), and the DTO↔model mapping used by `BlackboardAuthoringWindow.cs`. Grep `IsAutoManaged` across the repo — wherever it is plumbed (DTO, entry, view-model, add-variable path, projector), plumb `Role`/`Scope` the same way.

## Concrete changes
1. **Enums (new).** Add `enum BlackboardVariableRole { Input, State }` and `enum WorkingStateScope { Node, Behavior, Entity }` (use the name `WorkingStateScope` — the design's Mode-2 accessor `GetShared<T>(Entity, WorkingStateScope)` will reuse it). Place them next to the DTO or in the AiShared blackboard namespace, wherever `IsAutoManaged`'s neighbors live. Default values (`Input`, `Node`) must be enum value 0 so omit-when-default works.
2. **DTO.** Add to `BlackboardVariableDto`:
   ```csharp
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
   public BlackboardVariableRole Role { get; set; }          // default Input
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
   public WorkingStateScope Scope { get; set; }              // default Node; only meaningful when Role==State
   ```
   Serialize enums as strings (`JsonStringEnumConverter`) if the codebase's JSON options already do so for other enums; otherwise match whatever `IsAutoManaged`-adjacent enums use. **Byte-stability is mandatory:** an existing asset with neither field must serialize byte-identically (omit-when-default), and a legacy asset with neither deserializes to `Input`/`Node`.
3. **Editor model + view-model.** Add `Role`/`Scope` to the editor variable entry + view-model; plumb through the DTO↔model projector both directions (mirror `IsAutoManaged`).
4. **Variables panel UI.** Add a **Role** selector (Input/State) per variable; when `Role == State`, show a **Scope** selector (Node/Behavior/Entity). When `Role == Input`, hide/disable the scope control. Keep it consistent with the panel's existing control style. Node-owned/auto-managed vars stay as they are.

## Success conditions (do not invent others)
- Build: clean rebuild 0 errors; **byte-identity gate green** (`ByteStabilityTests` pass — add a case if the suite is asset-enumerated, else confirm existing assets round-trip byte-identically with the new omit-when-default fields).
- New test `BlackboardVariable_RoleScope_RoundTrips` (mirror `IsAutoManagedRoundTripTests`, BTree **and** HSM): author a `State`/`Behavior` variable → save+reload → attributes preserved; a variable with defaults (`Input`/`Node`) omits both fields from JSON; a legacy JSON with neither field deserializes to `Input`/`Node`.
- New editor test `VariablesPanel_ShowsScopeSelector_OnlyForState` — the scope control is present iff role==State. (If the panel has no existing unit-test harness, assert the view-model exposes a `ShowScopeSelector`/equivalent gating flag instead of driving ImGui.)
- No net-new failures in touched projects.

## Constraints & guardrails
- **Additive + back-compat only.** No existing test's expected JSON may change except by the intended omit-when-default (which should be a no-op for existing assets). If any golden/byte-identity asset changes, you did it wrong — fix the omit-when-default.
- Do not wire Role/Scope into any emitter/runtime/validator — that's S3-2…S3-7.
- Run tests with `dotnet test <proj>.csproj -c Debug --nologo`. If `NU1301 "local source './nugets'"`, `mkdir -p ./nugets` first. Do **not** run concurrent `dotnet test` (CS2012 DLL lock).
- Touched projects to build+test: `Hrot.AiEditor.Persistence(.Tests)`, `Hrot.Editor.AiShared(.Tests)`, and any project whose golden/byte tests cover blackboard JSON.

## Report back
Files changed; how Role/Scope are serialized (enum-as-string vs int) and why byte-stability holds; the two new tests' results; before/after pass counts for each touched test project; anything that diverged from this spec.
