# DEC-05 — Implementation spec: bind a real condition to an editor-managed blackboard variable

> **Status:** spec / awaiting steer. Written 2026-06-14 (overnight, autonomous).
> **Author:** lead (opus). All "VERIFIED" claims below were read in source this session; "OPEN" items need confirmation before coding.
> **Goal:** close VE-DEBT-002 — let a node bind a real `[BTreeCondition]`/`[BTreeAction]` (whose first ref param is a typed value) to an authored blackboard variable, and have it compile **and run**.
> **Precursor done:** DEC-05a (`b2e09b29`) — load-flag round-trip fix + `T09_BlackboardManaged` demo asset; the Variables panel is now visible/editable.

---

## 1. The kernel mechanism (VERIFIED — this reframes everything)

The earlier investigation assumed binding happens via a call-site `Unsafe.As<BrainBlackboard, Dto>` with a `paramIndex` byte offset. **That is wrong.** The FastBTree kernel already has first-class expression-bound leaves:

`FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`:
- `Action<TValue>(Expression<Func<TBlackboard, TValue>> fieldSelector, ReusableActionDelegate<TValue, TContext> logic, …)` — line 290
- `Condition<TValue>(Expression<Func<TBlackboard, TValue>> fieldSelector, ReusableConditionDelegate<TValue, TContext> logic, …)` — line 249
- `where TValue : unmanaged`

What they do (lines 257-266 / 298-307):
```csharp
var (memberName, offset) = ExtractFieldInfo(fieldSelector, …);   // Marshal.OffsetOf<TBlackboard>(memberName)
NodeLogicDelegate<TBlackboard,TContext> curried =
    (ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int _) =>
    {
        ref TValue projected = ref Unsafe.As<TBlackboard, TValue>(
            ref Unsafe.AddByteOffset(ref bb, offset));
        return logic(ref projected, ref st, ref ctx);
    };
```
`ExtractFieldInfo` (line 506-522) does `Marshal.OffsetOf<TBlackboard>(memberName)` — **so the selected field must physically exist on `TBlackboard`** (the tree's blackboard type), and `TBlackboard` should be `[StructLayout(Sequential)]`.

**Consequence (the crux):** `dto => dto.{ExpressionTargetField}` only compiles if the tree's blackboard type *has* a field named `{ExpressionTargetField}` of the method's param type. Today every generated tree is `BTreeBuilder<BrainBlackboard, BTreeContext>`, and `BrainBlackboard` is a fixed engine struct with no such fields. **Therefore the tree must be built over a generated, per-asset blackboard struct that contains the authored variables as fields.** This is forced by the kernel — there is no viable "keep BrainBlackboard + inject a DTO" path (you cannot add fields to the engine struct, and `Marshal.OffsetOf` needs a real field).

---

## 2. What already exists (VERIFIED)

| Piece | Location | State |
|---|---|---|
| Expression-bound `Action`/`Condition` overloads | `BTreeBuilder.cs:249,290` | ✅ real, projects by offset |
| Emit of `Action(dto => dto.{ExpressionTargetField}, m, …)` | `BTreeEmitCore.cs:489-500, 515-526` | ✅ emits when `DelegateShape==ThreeParamReusable && ExpressionTargetField set` — **but unreachable** |
| Validator gate | `BTreeMethodCompatibilityValidator.cs:149-150` | ❌ hard-rejects `ThreeParamReusable` ("not supported by the generator (VE-DEBT-002)") — blocks the emit above |
| Typed-struct emitter | `BlackboardDtoEmitter.cs` (Editor.AiShared) | ⚠ exists, emits `[StructLayout(Sequential)] partial struct` from a var list — **never called by `BTreeJsonGenerator`** (orphaned, test-only) |
| Bin-packer (offsets) | `BlackboardBinPacker.cs` | ⚠ computes byte offsets, `MaxInlineBytes=100` — **consumed only by the panel display**, not codegen |
| `ExpressionTargetField` on payload | `BehaviorTreeAsset.cs:27`, DTO `BehaviorTreeAssetDto.cs:124` | ✅ persists & round-trips |
| `BrainBlackboard` layout | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` | 128 bytes; `BehaviorParameters` = fixed byte[100] at offset 0; interrupt registers at tail (120-127). `MaxBehaviorParamByteSize=100`, `BrainBlackboardByteSize=128` (`BehaviorConstants.cs`) |

So the feature is **~50% built but disconnected**: the kernel supports it, the emit branch is written, the struct emitter + bin-packer exist — they're just not wired, and the validator slams the door.

---

## 3. The target model (recommended: "Model A", essentially forced)

For an asset with `Managed=true`:

1. **Generate a per-asset blackboard struct**, e.g. `T09_BlackboardManagedBlackboard`, `[StructLayout(LayoutKind.Sequential)]` (or Explicit), containing the authored variables as public fields laid out by `BlackboardBinPacker` within the first 100 bytes, **plus** the `BrainBlackboard` tail registers (`ExpectedThreatLevel`@120, `Interrupt_MobilityLost`@126, `Interrupt_Reserved`@127) at their fixed offsets, total size **128** — i.e. **ABI-identical to `BrainBlackboard`**. (`BlackboardDtoEmitter` is the starting point; it likely needs the size/tail-register additions.)
2. **Emit topology over the generated struct**: `BTreeBuilder<T09_BlackboardManagedBlackboard, BTreeContext>` instead of `<BrainBlackboard, …>`.
3. **Binding**: a node with `DelegateShape=ThreeParamReusable` + `ExpressionTargetField="Fire"` emits `Condition(bb => bb.Fire, MethodRef, …)`. The bound `[BTreeCondition]` must have first param `ref {typeof Fire}`. This works for **both** primitives (`ref float`) and DTO structs (`ref FireAtTargetParams`) — `TValue : unmanaged` covers both. (The addendum's "whole-DTO binding" = the variable's type *is* the DTO.)
4. **Runtime**: the ECS component stays `BrainBlackboard` (128 B). Because the generated struct is ABI-identical, reinterpret `ref BrainBlackboard` → `ref T09…Blackboard` at the tick seam (`Unsafe.As`).

Model B (keep `BrainBlackboard`, no generated struct) is **not viable** — see §1.

---

## 4. The two gating questions — now ANSWERED (VERIFIED 2026-06-14)

Both were traced to source this session. They reframe DEC-05 from "wire 3 pieces" into "fix two prerequisites first."

### 4.1 Runtime method-resolution path — ANSWERED: bound methods do NOT execute today
- `Interpreter` binds delegates **once at construction** from the `ActionRegistry` passed to its ctor: `BindActions` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs:691-715`) looks up each `blob.MethodNames[i]` via `registry.TryGetAction(name, …)`; **on a miss it logs a warning and installs a `(…) => NodeStatus.Failure` fallback**. The blob carries **only string `MethodNames`**, never delegates (`BehaviorTreeBlob.cs`; `TreeCompiler.FlattenToBlob`).
- The generated bridge passes a **`new ActionRegistry<BrainBlackboard,BTreeContext>()` that is never populated**, then registers the real method names as **stub thunks** (`=> Success` / `=> true`) into the global `BehaviorRegistry` via `beh.RegisterAction/RegisterCondition` — a registry the interpreter **never consults**. Confirmed in the REAL asset, not just the demo: `CombatShowcase.Registrar.g.cs` emits `new ActionRegistry<…>()` + `beh.RegisterAction(…, "…Action_Wander", (…) => NodeStatus.Success)`.
- `AiHotReloadCoordinator` does **not** rebind the interpreter's registry (no `GetRegistry`/`new Interpreter`/`actionRegistry` references).
- **Net:** a JSON-defined BTree's actions/conditions all fall back to **`Failure`** at runtime — the user's method body never runs. This is true even for `FourParamFull` (the only shape the validator allows today). So the JSON→runtime execution path is **unfinished**, independent of typed binding.
- **The likely fix is small:** the builder already exposes `GetRegistry()` (`BTreeBuilder.cs:380`) holding the real curried delegates; the bridge just needs to use `CreateBuilder().GetRegistry()` (or otherwise transfer those delegates) instead of `new ActionRegistry()`. But this is the FourParamFull binding path and the keys are `{DeclaringType.FullName}.{Method}[@offset]` — confirm key parity before wiring. **This is a prerequisite to DEC-05 and is arguably its own debt item ("BTree JSON runtime execution gap").**

### 4.2 The ECS→typed-struct reinterpret seam — ANSWERED: runtime is hard-typed to BrainBlackboard
- Tick site: `Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs:136` — `def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context)`, with `blackboard` a `ref BrainBlackboard` from the ECS component. Hard-typed, not generic.
- Storage: `BehaviorDefinition.BTreeInterpreter` is typed `Interpreter<BrainBlackboard, BTreeContext>?` (`Fdp.Toolkits/Behavior/BehaviorRegistry.cs:60`) — a concrete generic reference, no non-generic base/interface.
- **Consequence:** you cannot store an `Interpreter<{Asset}Blackboard,…>` in `BehaviorDefinition`, nor safely reinterpret the typed reference. Model A (build the tree over a generated struct) therefore requires EITHER (a) making the behavior runtime generic over the blackboard type — `BehaviorDefinition<TBB,TCtx>` / `BTreeTickSystem<TBB,…>` — a breaking change cascading across the AI system, OR (b) introducing a **non-generic interpreter seam** (e.g. an `IBTreeInterpreter` with a `Tick(ref BrainBlackboard, …)` that internally reinterprets to the generated struct via `Unsafe.As`, valid iff ABI-identical 128 B). Option (b) is the smaller, recommended path but is a real kernel/runtime change.

---

## 5. Proposed phased plan (after §4 answered)

- **P1 — struct generation (additive, low risk).** Wire `BlackboardDtoEmitter` + `BlackboardBinPacker` into `BTreeJsonGenerator`: for `Managed==true` assets, emit `{Asset}.Blackboard.g.cs` (ABI-identical, 128 B, tail registers). Nothing references it yet. Existing assets are all `Managed=false` → untouched; byte-identity gate safe. Verify `T09` emits a correct struct.
- **P2 — topology over the generated struct.** For `Managed==true`, emit `BTreeBuilder<{Asset}Blackboard,…>`. **This changes the topology `.g.cs`** → must NOT regress `ByteIdenticalGateTests` (CombatShowcase/SampleScout are `Managed=false`, so guard the change strictly behind `Managed==true`).
- **P3 — unblock the validator** (`BTreeMethodCompatibilityValidator.cs:149-150`): accept `ThreeParamReusable` *iff* `ExpressionTargetField` is set, resolves to an authored variable, and the method's first ref-param type matches that variable's type; reject otherwise with a clear diagnostic.
- **P4 — runtime binding** (depends entirely on §4.1): make the user's method actually execute (real registry, not stub) under the reinterpreted struct.
- **P5 — editor authoring UX**: a `[BlackboardFieldPicker]` on the node inspector to set `DelegateShape=ThreeParamReusable` + `ExpressionTargetField`, type-filtered to variables matching the method's param type (the "+ Promote to new variable" flow from the addendum). Much of this may already exist in the inspector — audit before building.
- **P6 — end-to-end demo**: extend `T09` (or a new asset) with a real bound condition; prove it compiles AND runs (a runtime test through the coordinator).

## 6. Risks / gotchas
- **Byte-identity gate** (`ByteIdenticalGateTests`, CombatShowcase/SampleScout): all are `Managed=false`; keep every emit change guarded behind `Managed==true`.
- **Incremental-generator caching** (VE-DEBT-003 / the build-server gotcha): always `dotnet build-server shutdown` + `-t:Rebuild` when verifying codegen; a new `.Blackboard.g.cs` per asset is fine but validate its syntax pre-emit so a bad var can't poison the whole asset.
- **ABI drift**: if `BrainBlackboard`'s layout/size ever changes, every generated struct must track it — centralize the tail-register layout (don't hand-copy offsets per asset; derive from `BehaviorConstants`).
- **Unmanaged constraint**: only `unmanaged` variable types are bindable; the panel's known types (bool…Quaternion) all qualify; DTO structs must be blittable.

## 7. Recommendation (revised after §4 verification)
The investigation changed the picture: DEC-05 (typed binding) sits on top of **two unfinished prerequisites**, not one.

- **PREREQ-A — JSON BTree runtime execution gap (§4.1).** Bound methods don't run *at all* today (everything → `Failure`). This is the highest-leverage fix and is independent of typed binding: likely "bridge uses `CreateBuilder().GetRegistry()` instead of `new ActionRegistry()`." **Recommend doing this first, as its own batch** — it makes existing `FourParamFull` actions (e.g. `Action_Wander`) actually execute, which is independently valuable and gives a runnable demo. Confirm key parity + why the empty-registry/stub pattern was chosen (there may be a hot-reload reason).
- **PREREQ-B — blackboard-type genericity (§4.2).** Building a tree over a generated struct needs either a generic runtime or a non-generic `IBTreeInterpreter` reinterpret seam. This is a kernel/runtime architecture decision and **must be steered by you** — it is the single biggest design call in DEC-05.
- **Then DEC-05 proper** (P1 struct-gen → P3 validator unblock → P5 editor UX → P6 demo), per §5.

**Sequencing ask for the user:** (1) Confirm whether the JSON runtime path *should* execute methods now (PREREQ-A) or is intentionally stubbed pending something — if it should, I can scope that small batch immediately. (2) Decide PREREQ-B's direction (generic runtime vs non-generic reinterpret seam) — I recommend the non-generic seam. Until (2) is decided, P1 (struct generation) is the only DEC-05 step safe to build speculatively, and even that is wasted if PREREQ-B goes a different way. Net: **hold DEC-05 coding; start with PREREQ-A if you approve.**
