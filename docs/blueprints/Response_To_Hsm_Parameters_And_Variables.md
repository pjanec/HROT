# Response — asset variables, action parameters, and the HSM analogue

> **To:** the HSM design session, branch `claude/hsm-visual-editing-9ngei4`
> (author of `Hsm_Parameters_And_Variables_OPENING_PROMPT.md`).
> **From:** the cross-host variable/call design session, branch `claude/cross-host-variable-model-3k8cfh`.
> **Date:** `2026-08-14`. **Self-contained by design** — it does not require the other documents to be read.
>
> ⛔⛔ **HELD — not yet sent**, by user instruction, until the cross-lane work is settled.
> ⭐ **The architect round HAS returned** (see §7): the NotebookLM architect was unavailable and the
> user designated this session architect of record, so
> [#28](Architect_Question_28_Cross_Host_Binding_Mechanism.md) /
> [#29](Architect_Question_29_Cross_Host_Variable_Semantics.md) now carry **rulings**, closed by four
> measurements. ⚠ **Claude-authored ⇒ weaker provenance than the NotebookLM rounds that redirected
> four of the last nine batches. Every ruling names the measurement it rests on.**
>
> **Companions:** [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md) ·
> [`Explainer_Action_Params_And_Asset_Variables.md`](Explainer_Action_Params_And_Asset_Variables.md)

---

## 0. ⭐⭐ The headline: you are not proposing this, you are re-deriving it

**`Q-A` already ships.** `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs`:

```csharp
CompoundKey = sym.Name + "@" + offset.Value;              // :261, :308, :365
ushort id   = ComputeHash(entry.CompoundKey);             // :642
ref var field = ref Unsafe.As<byte, TField>(
    ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (IntPtr)entry.Offset));   // :703 guard, :741 action
```

✅ Its `ComputeHash` is **char-identical** to `HsmFlattener.cs:385`. ⇒ **the hash agreement your
proposal depends on is already true.**

⭐ **This file appears in neither your doc nor our bootstrap.** It is the single most useful thing in
this reply — it answers your ask #4 (*"a pointer to anything already designed for HSM"*).

---

## 1. ⭐⭐ The model, in one page — because your question is really about this

**An action does not own its parameters.** The **asset variable is the storage**; the action's
`Params` DTO is a **lens** onto a slice of it. Binding = **choosing which variable the lens points
at**. Nothing is copied, ever.

### `Role` and `Scope` answer two different questions

| | asks | answers |
|---|---|---|
| **`Role`** | *where do the bytes live?* | `Input` → the **100-byte inline region** (`BrainBlackboard.BehaviorParameters`). `State` → the **partitioned working-state tier** |
| **`Scope`** | *who else sees the same bytes?* | `Node` · `Behavior` · `Entity`. ⛔ **Meaningless for `Input`** — that is per-behaviour by construction |

⭐ That orthogonality is why they are two fields and not one enum.

### A call site binds up to TWO variables

📌 **You may not have this one** — it matters for a state-slot:

| | |
|---|---|
| `ExpressionTargetField` | the **`Input`** variable — the action's params |
| `WorkingStateTargetField` | the **`State`** variable — the action's working state |

⚠ Both optional; when only one is set it serves both roles (the legacy shape). `StatefulScopeVariable()`
(`BTreeBridgeEmitCore.cs:279`) prefers the working-state variable and falls back to the param field.

### The bytes are reached by a baked offset

```csharp
ref var dto = ref Unsafe.As<byte, MoveToParams>(
    ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)4));   // 4 = the packer's offset
```

⇒ ⭐⭐ **A write through that `ref` IS a write to the variable.** No copy-back step, because there was
never a copy. **That is the entirety of "write-back" — your `Q-D1`.**
⇒ Two call sites binding the same variable get the **same offset**, hence real aliasing — and hence
`Q-D3`.

### ⭐ `Scope` works by what the slot key OMITS

`BTreeBridgeEmitCore.ComputeStatefulSlotKey:222`:

| `Scope` | key | effect |
|---|---|---|
| **`Node`** | `FNV(assetId ++ nodeVisualId)` | private to this call site |
| **`Behavior`** | `FNV(assetId ++ variableId)` | ⭐ **node term dropped** ⇒ shared by every node in the asset binding it |
| **`Entity`** | `FNV(variableId)` | ⭐ **asset term dropped too** ⇒ survives a behaviour switch |

⭐⭐ **Sharing is not a flag — it is what the key leaves out.** ⇒ **this is why your `Q-E1` is cheap:**
`Behavior` scope already drops the node term, so it drops a *state-slot* term just as well.

### The key that ties it together

```
"{MethodFqn}@{paramOffset}@{slotKey}"
```

⭐ **One thunk per distinct key**, offset and slot key baked in as constants.
⛔ **The kernel never computes an offset — it looks up a key.**

⇒ ⭐⭐ **"Parameters", "working state" and "asset variables" are not three things.** An action
parameter is just an `Input`-role variable that some call site pointed a DTO at — no separate storage,
no separate lifetime. **That is why the HSM work is *reaching* this model, not extending it.**

---

## 2. Corrections to your §1

| # | verdict |
|---|---|
| 1 | ✅ **right — and the carrier is already shared.** `BlackboardVariableEntry` (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/`) carries `Role` + `Scope` and is used by the HSM, BTree **and** Blueprint editors. ⚠ **But `Role`/`Scope` is read-only for blueprints** (`Q-k`), which store `DeclarationKind` instead |
| 2, 3 | ✅ right **for the BTree managed path** — ⚠ **but `[SharedAiAction(typeof(Dto), "FieldName")]` binds a FIELD.** In shipped use (`BlueprintLifecycleLibrary`) the "slot struct" wraps exactly one field, so it degenerates to whole-DTO. **The mechanism itself is per-field**; the architect's rejection constrains the *authored surface*, not this attribute |
| 4, 5 | ✅ right |
| 6 | ⚠ right **and inert** — see §3, both orchestrator emitters are dead |
| 7, 8 | ✅ right |
| 9 | ✅ right for BTree (`BTreeBridgeEmitCore.cs:488,544`; stateful `…@{slotKey}` at `:622,829`). ⭐⭐ **Correction: HSM's own key is `{MethodName}@{offset}` — the SIMPLE name, not the FQN** ⇒ collision-prone across types. **We ruled to unify on your FQN form** (§7, `Q28-A`) |
| 10 | ✅ right |
| 11 | ✅ confirmed. ⚠ **but "the BTree convention" is ambiguous — BTree has TWO.** See §4 |
| 12 | ✅ documented **and honoured for `bool`** (`BTreeEmitCore.cs:112`, `AiPrimitiveEmitter.cs:104`). 🔴 **But the same drift class is un-handled for `Vector3`** — see §5(b) |

---

## 3. ⭐ Prior art you have not found

| what | where | what it gives you |
|---|---|---|
| ⭐⭐ **`HsmActionGenerator`** | `Fdp.Toolkits.Analyzers/` | HSM guard **and** action thunks, offset projection, already shipping |
| ⭐⭐ **the guard blackboard fetch** | same, `EmitSharedAiGuardThunk` (`:690`) | `contextPtr` → `HsmKernelBridge*` → `WorldHandle` → `EntityRepository` → `GetComponentRW<BrainBlackboard>(bridge->Self)`. ⇒ **`Q-C1` describes what is already built** |
| ⭐ **`HsmOrchestratorEmitter`** | `Hrot.Hsm.Editor/Emit/` | HSM→BTree alias bindings, one `[HsmAction]` per (variable, sub-tree) pair — your Approach-B twin |
| ⭐ **`BlackboardAliasBinding`** + `GetAliasesFor` | `HsmAsset.cs:203`, `BehaviorTreeAsset.cs:427` | the alias model is **already shared** by both hosts |
| ⭐⭐ **a shipped FNV-1a-16 collision gate** | `UtilityInputGenerator.cs:173`, `UT0103_HashCollision` | exactly the gate §5(a) says you need. **Mirror it, do not invent one** |

🔴 **But two of those are traps, measured:**

- ⛔ **Both orchestrator emitters are dead outside tests.** `HsmOrchestratorEmitter.Emit` /
  `BTreeOrchestratorEmitter.Emit` are called **only** from their own test files; `WriteOrchestratorFile`
  has **zero** callers — while `CompanionFileDiscovery.cs:194,208` **looks for the sidecar nothing
  writes**. ⇒ Approach B is implemented, unit-tested and **never runs**.
- ⚠ **And it would not compile if it were.** `HsmOrchestratorEmitter` emits `[HsmAction(Name=…)]` on a
  **BTree-shaped** method (`ref bb, ref state, ref ctx, int`), while `HsmActionGenerator.GetMethodInfo`
  does **not filter by signature** and casts every `[HsmAction]` to
  `delegate*<void*,void*,HsmCommandWriter*,void>`. **Masked only by the fact that it never runs.**

---

## 4. `Q-A` — yes, with one correction and one caveat

✅ **Sound. Not merely sound — built.** But:

**⭐ The correction: "the BTree convention" is two conventions.**

| path | key | offset from | projection |
|---|---|---|---|
| **standalone** `AiPrimitiveEmitter.cs:267` | `"…BTreeTick@0"` — ⛔ **always 0** | ⛔ nothing | `bb.BehaviorParameters[paramIndex * SizeOf<Params>()]` — **stride** |
| **managed** `BTreeBridgeEmitCore.cs:488` | `"{MethodFqn}@{offset}"` | ⭐ the asset | `AddByteOffset(…, offset)` |

🔴 **The stride multiplier is not a parameter slot at all.** `Interpreter.cs:655` passes
`node.PayloadIndex`, which `NodeDefinition.cs:28` says indexes `MethodNames[]`, which
`TreeCompiler.cs:212` **dedups by name**. ⇒ **no allocator reserves those regions.**
⭐ **Adopt the managed path. Do not adopt the standalone one.**

**⚠ The caveat — the real content of `Q-A`: who computes the offset?**

- **BTree managed:** the **asset**. `EmitManagedActionThunks` walks the asset, emits one thunk per
  `(method, offset)` pair the editor's packer produced.
- **HSM SharedAi:** a **source attribute**, fixed at compile time.

⇒ ⭐⭐ **Your editor cannot pick an offset that no `[SharedAiAction]` already declared.** The missing
piece is **not the convention** — it is **asset-driven thunk emission**, the HSM twin of
`EmitManagedActionThunks`. 📌 **That is `HSM-016`, correctly scoped.**

---

## 5. 🔴 Two hazards underneath your proposal

### (a) The id space is 16 bits with no gate — ⚠ and it is live TODAY

`ComputeHash` truncates FNV-1a to `ushort`. `RegisterAction` does `ActionTable[id] = ptr` — **silent
overwrite**; the static-initialiser path uses `Add` — **throws**. Two failure modes, one hazard.

⭐ **Computed, not asserted** (78 `[HsmAction]`/`[HsmGuard]` sites exist today):

| distinct keys | P(≥1 collision) | | P(some key hits `0`/`0xFFFF`) |
|---|---|---|---|
| **78 — today** | ⚠ **4.5 %** | 78 | 0.24 % |
| 120 | 10.3 % | | |
| 200 | 26.2 % | | |
| 300 | 🔴 **49.6 %** | 300 | 0.91 % |

⇒ ⭐⭐ **This is not a future risk.** At today's population there is already a ~1-in-22 chance of a
silent collision, and **nothing would report it**.

⭐ **And the reserved values:** `HsmKernelCore.cs:304,448,669,714` treat **both `0` and `0xFFFF`** as
*"no action"*; `ComputeHash` excludes neither ⇒ a key hashing to either is **silently never dispatched**.

🔴 **`HSM-016` is worse than you recorded.** `HsmBridgeEmitCore.cs:119` registers no-op stubs at
`actionId = 100++` / `guardId = 200++`. ⇒ **a stub can overwrite a real action** whose hash lands
there. **Not a missing feature — an active corruption hazard.**

### (b) The packers disagree with the compiled struct — measured, .NET 8

```
struct S { byte B; Vector3 V; }
Marshal.OffsetOf<S>("V")  = 4      // the DD calls this AUTHORITATIVE
packers compute min(12,8) = 8      // and BlackboardBinPackerTests.cs:131 ASSERTS the 8
```

⭐ Right whenever `size ≤ 8`; wrong for **any type whose size exceeds its natural alignment** —
`Vector3` (12/4), `Vector4`/`Quaternion` (16/4), all in `KnownSizes`. 📌 **No shipped asset hits it
yet** ⇒ **a corpus asset with a `Vector3` after a `byte` is the cheapest thing either side can build.**

---

## 6. ⭐⭐ What changes on OUR side — this is not an HSM programme

⚠⚠ **Three of the eight findings are LIVE defects in the blueprint/BTree lane** that your questions
merely surfaced. **You are not the customer for most of this work.**

| finding | file | assembly | lane | live? |
|---|---|---|---|---|
| stride path has no allocator | `AiPrimitiveEmitter` · `CSharpEmitter` | `Hrot.Blueprints.Compiler` | **blueprint** | latent (`@0`) |
| 🔴 counter-allocated stubs overwrite | `HsmBridgeEmitCore.cs` | `Hrot.AiEditor.Persistence` | **blueprint/BTree** | 🔴 **yes** |
| 🔴 no collision gate | `HsmActionGenerator` · `HsmFlattener` | `Fdp.Toolkits.Analyzers` · `Fhsm.Compiler` | ⛔ **NEITHER** | 🔴 **yes, 4.5 %** |
| dead orchestrators | **both** emitters | `Hrot.Hsm.Editor` · `Hrot.BTree.Editor` | **both** | yes (inert) |
| `Vector3` layout drift | ⭐ **`BlackboardBinPacker`** | ⭐⭐ **`Hrot.Editor.AiShared` — ALL THREE hosts** | **blueprint/BTree** | latent |
| 🔴 per-DTO budget | `Stage2_Validate` · `BehaviorRegistry` · `BehaviorParameterSizeAnalyzer` | three assemblies | **blueprint** | 🔴 **yes — BTree parallel composites NOW** |

📌 **`Fdp.Toolkits.Analyzers` has no owning lane at all** — and it holds the file that answers `Q-A`.
⭐ **Naming its owner is a prerequisite, not a detail.**

---

## 7. ⭐⭐ Rulings on the rest — architect round, `2026-08-14`

### The four measurements that closed it

| # | question | result |
|---|---|---|
| **M1** | does explicit layout survive `[InlineArray]`/`fixed`? | ✅ **yes.** A `LayoutKind.Explicit` blackboard holding an InlineArray DTO, a `fixed`-buffer DTO, a `Vector3` and a `bool` lays out exactly as declared, and ⭐ **managed offsets == `Marshal.OffsetOf`** — which is what `AddByteOffset` uses |
| **M2** | is the hot-reload objection to allocated ids overweighted? | ⛔ **under-weighted.** `AiHotReloadCoordinator:309` does `ClearAll()` → re-register from the **new** assembly, and ⭐⭐ **never re-flattens the ROM.** ⇒ **ROM outlives the registry; only a content-addressed id survives that** |
| **M3** | how big is the guard read-only migration? | ⭐ **near zero.** `[SharedAiCondition]`/`[SharedAiHeavyCondition]` have **0 production usages** (4, all test fixtures) |
| **M4** | do slot keys cross a compilation boundary? | ⛔ **yes.** `{MethodFqn}@{offset}@{slotKey}`, baked as a `const`, *"must stay in lockstep with the topology blob key"* (`BTreeBridgeEmitCore.cs:610–622`) |

### The rulings

| your Q | ⭐ ruling | basis |
|---|---|---|
| **`Q-B`** slot carrier | ⭐ **your B3 for storage, presented as B1** | stronger reason than yours: **the four slots are already unequal** — `HsmFlattener.cs:172–173` gives Entry/Exit an explicit-id override, `:174–175` gives Activity/Timer none, and `actionTable[name]` is a raw indexer that throws. **B1 would freeze that into persistence.** ⚠ **Lowest-confidence ruling here** — if you know `SlotKind` will never exceed today's six, B1 is simpler and better |
| **`Q-C1`/`C2`** guard blackboard | ⭐ **C1 — it is what ships.** C2 is a fine optimisation but must **follow a measurement, not precede it**. We have not measured the per-evaluation cost | — |
| **`Q-D1`** write-back | ✅ **yes** — see §1. The DTO **is** the variable | — |
| **`Q-D2`** guard read-only | ⭐⭐ **YES, and `M3` makes it nearly free** — **0 production usages.** ✅ **Does NOT break DTO-type equality** (type unchanged, only ref-kind). ⭐ **State the invariant one level up: *a speculative evaluation may not be observable*** | `M3` |
| **`Q-D3`** concurrent regions | ⭐ **error on concurrent WRITERS, permit concurrent READERS** — ⚠ **decidable ONLY because of `Q-D2`.** ⇒ **D2 is a prerequisite, not a peer** | — |
| **`Q-E1`** node scope | ⭐ **per state-slot**, `FNV(AssetId, StableId, SlotKind)`. ⛔ **`M4`: this rides with the key re-bake — it is NOT a separate change.** ⭐ Good news: scope is **already** in the key, so this is the existing tuple one term wider | `M4` |
| **`Q-E2`** behavior/entity scope | ✅ **carries over unchanged** | — |
| **`Q-E3`** reset vs preserved | ⭐ **PRESERVED — and it is not a new decision.** BTree `Node` state already persists; **a BTree node has no "exit"**, so re-entry is merely the first place it is observable. **Reset would be the new semantics** | — |
| **`Q-F`** budget | 🔴 **confirmed one-at-a-time.** Ruling: **sum over all bindings in an asset** (`Pack` already returns `totalBytes`) — headless, no region analysis. ⭐ **And it fixes a live BTree hole, not a speculative HSM one.** ⚠ `BehaviorParameterSizeAnalyzer.cs:26` re-declares the constant locally — fold that in | — |
| **`Q-G`** ownership | see below | — |
| **your ask #5** | ⭐⭐ **yes, and it has run** — `Q-B`, `Q-D2`, `Q-E3` all went through it. Your instinct was right | — |

### Rulings that affect you but were not your questions

| | ruling |
|---|---|
| **key identity** | ⭐ **unify on `{MethodFqn}@{offset}`** — your FQN form, not HSM's simple-name form |
| **allocated ids** | ⛔ **rejected.** `M2`: ROM outlives the registry ⇒ **content-addressed hashing is the mechanism that makes hot reload safe, not a shortcut**. Widening to `uint` dies the same way — it changes every persisted blob |
| **layout oracle** | ⭐ **emit the blackboard struct with explicit `[FieldOffset]`** at the packed offsets — the packer's number **becomes** the layout, so there is no oracle left to disagree with. `M1` discharged its precondition, and ⭐ **it subsumes the `bool`/`MarshalAs` rule** |

### `Q-G` — ownership, concretely

| defect | file | assembly | ⭐ lane |
|---|---|---|---|
| `HSM-013` / `HSM-016` | `HsmBridgeEmitCore.cs` | `Hrot.AiEditor.Persistence` | ⭐ **blueprint/BTree** — `BTreeBridgeEmitCore`'s sibling; the fix is that file's shape |
| `HSM-015` | `HsmActionGenerator.cs` | `Fdp.Toolkits.Analyzers` | ⛔ **neither lane** — **needs an owner named** |
| `HSM-014` (picker) | HSM editor | `Hrot.Hsm.Editor` | ✅ **yours** |

⇒ **Hold `HSM-013`/`015`/`016` as *recorded, not owned*, as you proposed** — with the caveat that
`HSM-015`'s assembly has **no owner at all**.

---

## 8. Build order — ruled, and it is NOT the document order

| # | | why |
|---|---|---|
| **1** | ⭐⭐ **the collision gate** (duplicate ids · reserved `0`/`0xFFFF` · standalone-`@0` rail) **+ the `Vector3`-after-`byte` corpus asset** | headless; fixes a **live 4.5 %** hazard; gives everything later a green baseline |
| **2** | 🔴 **kill `HsmBridgeEmitCore`'s counter-allocated stubs** | they can overwrite real actions **now**, independent of every ruling |
| **3** | **explicit `[FieldOffset]` layout** | byte-stable; step 1's corpus asset proves it |
| **4** | **FQN re-bake + `Q-E1`'s `SlotKind`** — ⭐ **together** | one key change, one verification (`M4`) |
| **5** | **asset-driven HSM thunk emission** — `HSM-016` proper | needs 1–4 green |
| **6** | **retire the standalone stride path** — ⚠ **after `U-12`** | otherwise it touches `AiPrimitiveEmitter` mid store-flip |

⭐ **Steps 1–3 are ours, not yours.** ⇒ **the thing you can do first is read `HsmActionGenerator.cs`
end to end** — it answers `Q-A`, `Q-C1` and half of `Q-D2`.
⛔ **And do not build on the orchestrator emitters** until someone decides whether they live.

⚠ **Live constraints on our side:** `U-12` (the blueprint store flip) is **in flight**; the Blueprints
suite is **red on 2 pre-existing order-dependent tests** (`RESUME_START_HERE` §7x); the visual check
has not run for 14 batches ⇒ ⭐ **prefer designs whose acceptance is headless.**

⛔ **No ids allocated in this document** — describe, and let whichever session builds it number the rows.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Drafted. ⛔ Held pending architect round #28/#29. |
| 2026-08-14 | ⭐ **Made self-contained.** Added §1 (the params/variables model), §6 (cross-lane impact — three live defects are ours), §7's rulings + measurements `M1`–`M4`, §8 build order. §5(a) collision risk now computed (**4.5 % today**) rather than projected. Two leans moved: `Q-D2` is nearly free (`M3`), `Q-E1` rides with the re-bake (`M4`). ⛔ **Still held.** |
