# HANDOFF — the cross-host behaviour-asset parameter & variable model

> **To:** the cross-host coordinator session (owns tracker + implementation plans for **all** parts).
> **From:** the cross-host variable/call design session.
> **Branch:** ⭐ **`claude/cross-host-variable-model-3k8cfh`** · **Dispatched at `b02ddb16`**
> **Date:** `2026-08-14`
>
> ⛔⛔ **Design is COMPLETE and REVIEWED. Nothing is built.** This handoff exists to be turned into
> tracker rows and batches.
>
> ⛔ **I ALLOCATE NO IDS.** Work items below are `W1…W13` — ⭐ **placeholders local to this document.**
> The implementing session numbers its own rows and diagnostic codes.

---

## 0. ⭐ Read in this order

| # | doc | why |
|---|---|---|
| **1** | [`Design_Behavior_Asset_Parameter_Model.md`](Design_Behavior_Asset_Parameter_Model.md) | ⭐⭐ **the design.** Everything else is support |
| **2** | [`Explainer_Action_Params_And_Asset_Variables.md`](Explainer_Action_Params_And_Asset_Variables.md) | how it works **today** — ⭐ fastest way to load the model. **Dated as-built snapshot; it will age** |
| **3** | [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md) | the **14 measured findings** + the cross-lane impact table. ⭐ **Where a skeptic should push** |
| **4** | [`#28`](Architect_Question_28_Cross_Host_Binding_Mechanism.md) · [`#29`](Architect_Question_29_Cross_Host_Variable_Semantics.md) · [`#30`](Architect_Question_30_Editor_Authored_Param_Preprocessing.md) | rulings **+ rejected options with reasons** — read before re-litigating anything |
| **5** | [`Response_To_Hsm_Parameters_And_Variables.md`](Response_To_Hsm_Parameters_And_Variables.md) | the HSM reply — ⛔ **HELD, not sent.** See §5 |

⚠⚠ **Provenance — state this in any tracker row you create.** The NotebookLM architect was
**unavailable**; the user designated the design session **architect of record**. ⇒ the rulings are
**Claude-authored**, weaker than the NotebookLM rounds that redirected four of the last nine batches.
⭐ **Every ruling names the measurement it rests on. Overturn on evidence, not authority.**
✅ **Reviewed by the blueprint session** (`REVIEW_Behavior_Asset_Parameter_Model.md`, `800a0fe1b`) —
verdict *"build it"*, with four corrections **already applied**, one of which **refuted a safety
argument** (see `W2`).

---

## 1. ⛔ Three decisions you must OBTAIN before parts of this can be built

⭐ **These are not mine to make. Do not let an implementation session guess them.**

| | decision | blocks | why it is open |
|---|---|---|---|
| **D1** | ⭐ **Is `SlotKind` open or closed?** | the `SlotKind` half of `W9` | the carrier ruling turns on whether HSM ever exceeds today's six slots. **No roadmap evidence either way.** If six is final, plain fields are simpler and `#29`-A should be overturned. ⭐ *Datum from review:* twice the tagged carrier beat its field count, and both times the untagged cost was invisible until something broke |
| **D2** | ⭐ **Which `DeclarationKind` is the reserved input variable, on the BLUEPRINT side?** | the blueprint half of `W5` | `FieldLayout` has **no `Role` concept** — it lays out by list at fixed offsets 0/8/16, so **`DeclarationKind` IS the tier.** `Parameter` is semantically exact but inline-region; `Variable` matches the tier but reads oddly; and the two belong to **dispatch kinds that never coexist** ⇒ the answer may differ per kind |
| **D3** | ⭐ **Are the orchestrator emitters LIVE or DEAD?** | anything touching Approach B | both `Emit` methods are called **only from their own tests**; `WriteOrchestratorFile` has **zero** callers; `CompanionFileDiscovery:194` looks for a sidecar **nothing writes**; and the emitted file **would not compile** if it were written |

---

## 2. ⭐⭐ Work items — in dependency order

⚠ **This is NOT the document order.** `W1`–`W4` are correctness and gate everything else.
✅ **Every item is headless** — the visual check is 16 batches out.

### Phase A — correctness (blueprint lane; ship before any extension)

| | work | lane | acceptance | deps |
|---|---|---|---|---|
| **W1** | 🔴 **Collision gate.** Refuse duplicate hashed ids; refuse any key hashing to **`0` or `0xFFFF`** (the kernel treats both as *"no action"*); rail refusing any standalone key but `@0`. ⭐ **Mirror `UT0103` — do not invent** | blueprint (`Fdp.Toolkits.Analyzers`) | generate-time diagnostic. **Fixtures: two colliding keys ⇒ build fails; a key hashing to `0` ⇒ build fails.** ⭐ **Ship first and ALONE** | none |
| **W2** | 🔴🔴 **Runtime layout gate + the corpus asset.** `Marshal.OffsetOf<T>(name) == f.Offset` for **every corpus asset, every emitted field**; add a `Vector3`-after-`byte` asset | blueprint | ⭐⭐ **MUST GO RED on the new asset BEFORE any fix** — red-first. ⛔ **The golden corpus cannot see this class**: Tier 1 records the *computed* offset, so it stays byte-identical while the real field moves 4→8 | none |
| **W3** | 🔴 **Kill `HsmBridgeEmitCore`'s counter-allocated stubs** (`:119` `actionId = 100++` / `guardId = 200++`) | blueprint/BTree | no counter-allocated registration remains. ⭐ **Live hazard — a stub can overwrite a real action.** Independent of every ruling | none |
| **W4** | **Explicit `[FieldOffset]` on the emitted struct** + ⭐ **separate alignment-reliability from `SizeReliable`** (`CSharpEmitter:412`'s hatch cannot fire for `Vector3`: reliable size, unreliable alignment) | blueprint | ⭐ **`W2` goes GREEN**; golden Tier 1 unchanged. ⭐ Subsumes the `bool`/`MarshalAs` rule. ⚠ Add a rail refusing a **managed-typed** blackboard variable (Explicit forbids overlapping managed refs) | **W2 red first** |
| **W5** | **Summed parameter budget** — sum over all bindings in an asset (`Pack` already returns `totalBytes`); fold in `BehaviorParameterSizeAnalyzer:26`'s duplicated constant | blueprint | an asset exceeding 100 B across simultaneously-live bindings is refused. ⭐ **Live for BTree parallel composites today — not an HSM-only gap** | none |
| **W6** | **Guard read-only projection** — `GetComponent` not `GetComponentRW`; `in`/`ref readonly` at the thunk boundary | blueprint | ⭐ **Measured near-free: 0 production `[SharedAiCondition]` usages** (4 exist, all test fixtures). State the invariant as *"a speculative evaluation may not be observable"* | none |
| **W7** | **Concurrent-region rule** — error on concurrent **writers**, permit concurrent **readers** | HSM | ⚠ **undecidable without `W6`** — a guard must be *statically* a reader first | **W6** |

### Phase B — the extensions

| | work | lane | acceptance | deps |
|---|---|---|---|---|
| **W8** | ⭐⭐ **`E-A` — reserved input variable + generated deserialize**, in **both** emitters (`BTreeBridgeEmitCore` **and** the HSM twin, which does not exist at all). Closes `DEBT-AIB-021` | blueprint/BTree | ⭐ **a managed asset is parametrized from scenario JSON end-to-end** — today impossible. Reserved name is **REFUSED**, not renamed. `Pack` skips it (heavy tier). ⛔ **BTree/HSM only until `D2`** | `W1`,`W2` green |
| **W9** | **FQN key unification + `SlotKind`** — ⭐ **one re-bake, one verification.** HSM's key is `{MethodName}@{offset}`; unify on `{MethodFqn}@…` | blueprint + HSM | every hashed id moves once; `W1`'s gate green throughout. ⛔ **`SlotKind` half blocked on `D1`** | `W1`, **`D1`** |
| **W10** | **Initializer picker (C# provider).** ⭐ **Reuse `IBehaviorActionCatalog`** — add a source enum member + contributing catalog, **not a new picker**. Persist **one string: the catalog `Id`**; `Source` is display-only | blueprint editor | a picker offers attribute-discovered initializers, filtered by host. ⚠ **Requires adding `TryGetById`** — a missing `Id` **must diagnose, never silently mean "no initializer"** | `W8` |
| **W11** | **`E-D` — asset-driven HSM thunk emission** (`HSM-016` proper): the HSM twin of `EmitManagedActionThunks` | blueprint/BTree | an HSM asset binds an editor-chosen variable and dispatches correctly. ⭐ Rail: **a method carrying `[SharedAiAction]` may not also be asset-bound** | `W1`–`W4`, `W9` |
| **W12** | **`E-B` — authored `Construction` initializer** + ⭐ **world-singleton vocabulary** (+ geo→cartesian, entity-from-network-id, a defined failure contract) | blueprint | an editor-authored asset preprocesses params without a programmer. ⛔ **Largest new surface; unbudgeted** | `W10` |
| **W13** | **Retire the standalone stride path** — route `BTreeTick` through the offset form | blueprint | one projection formula repo-wide. ✅ **`U-12` is CLOSED, so this is UNBLOCKED** | `W1` |

---

## 3. ⛔ Do not re-litigate — rejected, with reasons

| rejected | why |
|---|---|
| dense / allocated ids · `uint` ids | ⭐⭐ **ROM outlives the registry.** Reload does `ClearAll()` → re-register from a **new assembly** and **never re-flattens the ROM** ⇒ **content addressing is the mechanism, not a shortcut** |
| initializer name derived from asset name | silent no-op; breaks on rename; `BP-228` already ruled against this class |
| authored JSON parsing | no JSON vocabulary — it means drawing a parser |
| wrapper JSON keyed by variable name | superseded by `E-A` |
| per-field binding | architect-rejected: one `ref` over a contiguous slice; scattering forces a per-tick copy |
| a discriminated union for initializer selection | unnecessary — the catalog `Id` already varies by source |

---

## 4. ⚠ Live constraints — verified `2026-08-14`

| | |
|---|---|
| ✅ **`U-12` CLOSED** | store flipped B53, `persistence-shape.txt` unchanged ⇒ **`W13` unblocked** |
| ✅ **suite GREEN** | `3551 / 3541 passed / 0 failed / 10 skipped` |
| ⚠ **visual check SIXTEEN batches out** | ⇒ **headless acceptance is mandatory.** Every item above is |
| 🔴 **`U-10`'s WRITER is BLOCKED** | ⛔ **`BP-235` is a project-reference CYCLE.** Anything needing a new `Hrot.Common` → blueprint edge hits the same wall — **check before designing one in** |

---

## 5. ⛔ The held HSM reply

[`Response_To_Hsm_Parameters_And_Variables.md`](Response_To_Hsm_Parameters_And_Variables.md) is
**drafted and HELD** by user instruction. It is now a **pointer** into the design.
⭐ **It is the HSM session's unblock** — `Q-A`, `Q-C1` and half of `Q-D2` are answered by a file they
never found (`HsmActionGenerator.cs`). ⚠ **Ask the user before sending.**

⭐ **`HSM-013`/`015`/`016` are OURS, not theirs** — they hold them *recorded, not owned*.
✅ **`Fdp.Toolkits.Analyzers` is blueprint-lane** (user ruling `2026-08-14`) — the last open ownership
question, now closed.

---

## 6. 📌 One process note for `.claude/CLAUDE.md`

⛔ **Two independent `Architect_Question_28`s were created on the same day on two branches.**
⭐ **Architect-question numbers are ids too**, and rule 3 names only coordinator/implementation — it
does not cover **two design sessions**. Suggested clause:

> **Architect-question numbers are ids.** Any session creating `Architect_Question_N_*.md` must first
> `git fetch` every active branch and take the next free `N` **across all of them**.

📌 **Resolution taken:** the blueprint session renumbers theirs to `#31`; `#28`/`#29`/`#30` on this
branch are cross-linked from five documents and stay.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Created. ⛔ Dispatched at `b02ddb16`. |
