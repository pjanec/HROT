# PLAN — sequencing the cross-host parameter model into the frozen implementation queue

> **Coordinator response to [`HANDOFF_Cross_Host_Parameter_Model.md`](HANDOFF_Cross_Host_Parameter_Model.md)**
> *(design session, `claude/cross-host-variable-model-3k8cfh` @ `a01c583dd`, dispatched `b02ddb16`)*
> **Date `2026-08-15`.** ⛔ **No ids allocated here** (rule 3). `W1`–`W13` stay the handoff's placeholders.
> ⚠ **Nothing in this document amends Batch 56 or 57** (rule 1) — both are dispatched and frozen.

---

## 1. ⭐⭐ The constraint that shapes everything: **there is one queue, not two**

⛔⛔ **Implementation freeze** (user, `2026-08-15`, `.claude/CLAUDE.md`): **`claude/hrot-implementation-j1jvin`
builds for all hosts; no other session writes code until the unified variable model is done.**

⇒ ⭐ **`W1`–`W13` do not run as a parallel programme.** They enter the *same* serial queue behind
Batch 56 and Batch 57. The handoff's lane column (`blueprint` / `HSM` / `BTree`) describes **which code
each item touches**, ⛔ **not who builds it** — one session builds all of it.

---

## 2. ⭐⭐⭐ Coordinator measurements — three of the handoff's open items move

⭐ **Measured `2026-08-15` on `f849514fe`, by reading the code, not by inference.**

| | handoff said | ⭐ **measured** | effect |
|---|---|---|---|
| **`D3`** — orchestrator emitters live or dead? | *"a decision you must obtain"* | ✅ **DEAD in production, confirmed.** `WriteOrchestratorFile` has **zero callers** (only its two definitions, `HsmOrchestratorEmitter:128` / `BTreeOrchestratorEmitter:186`). `Emit` is called **only from `BTreeOrchestratorEmitterTests` and `HsmOrchestratorEmitterTests`**. `CompanionFileDiscovery:194` hunts `*.Orchestrators.g.cs` — ⛔ **nothing writes it** | ⭐ **The FACT is settled; only the DISPOSITION (delete vs. wire) is the user's.** ⇒ **not a blocker, a cleanup choice** |
| **`D2`** — which `DeclarationKind` for the reserved input variable? | *"may differ per dispatch kind"* | ⭐ **`FieldLayout:9-13` confirmed exactly**: three lists at fixed starts **0 / 8 / 16**, so `DeclarationKind` **is** the tier. ⭐⭐ **But `W8` says `Pack` SKIPS the reserved variable (heavy tier)** — and offset 0 **is** the packed region `Pack` builds ⇒ ⛔ **it cannot be `Parameter`** | ⚖️ ⭐ **Measured lean: `Variable`** *(the state tier)*, on physical grounds. ⭐⭐ **And Batch 56 DISSOLVES the per-kind half** — once the emitters walk the union there is only one state tier to name |
| **`D1`** — is `SlotKind` open or closed? | a decision to obtain | ⛔ **no code can answer this** — it is a roadmap fact about whether HSM ever exceeds six slots | ⭐ **Genuinely the user's. Forwarded unchanged — the only one of the three that is still blind** |

### ⚠ One thing I checked and **refuted before writing it down**

`FieldLayout`'s **0 / 8 / 16** starts look like three regions that must collide once `Parameters`
exceeds 8 bytes — ⛔ **they do not, and it is not a defect.** `InstanceEmitter:109` makes `State` begin
`public BlueprintLatentCursor Cursor; // first 16 bytes`, so **variables genuinely start at 16**;
`WorkingState` sits at `memory + 8` (`AiPrimitiveEmitter:291`); `Parameters` is the separate packed
region. 📌 **Recorded so nobody re-derives the same false alarm** — the arithmetic invites it.

---

## 3. 🔴🔴 `W3` is worse than the handoff states — and `W1` as written will NOT catch it

⭐ **The handoff calls it *"a live hazard — a stub can overwrite a real action"* and rates it independent.
Both halves need correcting, and the finding gets stronger.**

| step | measured |
|---|---|
| the stubs | `HsmBridgeEmitCore:119` `ushort actionId = 100` and `:138` `guardId = 200`, each `++` per entry, registering **no-op bodies** (`__hsActionStub` / `__hsGuardStub { }`) |
| ⭐ **the emitter is LIVE** | ⛔ **not dead like the orchestrators** — `HsmJsonGenerator.cs:88` and `EditorSubsystem.cs:3298` both call `EmitBridge` in production |
| ⭐⭐ **the table does not refuse** | `HsmActionDispatcher:30` — **`RegisterAction(ushort id, IntPtr a) => ActionTable[id] = a;`** ⇒ **last writer wins, silently. No guard, no diagnostic** |
| 🔴🔴 **the real ids share the range** | `HsmActionGenerator:517/528/630/…` — **`ushort id = ComputeHash(action.Name)`**, i.e. **anywhere in 0–65535**, including 100.. and 200.. |

⇒ 🔴🔴 **A real action whose name hashes into the stub window is silently replaced by a body that does
nothing.** ⭐ **The HSM does not crash — it acts correctly everywhere except one state, forever.**

### ⛔ The correction to `W1`

`W1`'s gate refuses **duplicate hashed ids**. ⛔ **The stub ids are literal counters, never hashed — so
they do not enter the hash set and the gate as specified is blind to exactly the collision `W3`
describes.** ⇒ ⭐⭐ **`W1` must range over the FINAL id set (hashed ∪ counter-allocated), or `W3`'s defect
stays undetectable after both ship.** ⇒ **`W1` and `W3` are NOT independent: `W1` is `W3`'s detector.**

---

## 4. ⭐ Where the two programmes touch the same line

| cross-host | this programme | ⭐ ruling |
|---|---|---|
| **`W4`** — separate alignment-reliability from `SizeReliable` | ⭐ **Batch 57 extends the SAME predicate** (`CSharpEmitter:412` `layoutFromRuntime`) to the working-state path | ⛔ **`W4` runs AFTER 57.** Its handoff must be written against 57's merged text, not today's |
| **`W2`** — runtime `Marshal.OffsetOf<T>(name) == f.Offset`, every asset × every field | ⭐⭐ **the DESIGN doc already names `W2` as the rail for user structs** *(`S2`; golden Tier 1 records the COMPUTED offset and cannot see a disagreement)* | ⭐ **One rail, not two.** `S2` does not get its own — it consumes `W2`'s |
| **`W2`** vs **Batch 57's own gate** | 57's gate is **one runtime read-back on one asset**; `W2` is the corpus-wide form of the same assertion | ⚖️ **see §5 — this is the one real ordering choice** |
| **`W13`** — retire the standalone stride path | `LiveBlackboardValueProvider` reads `BrainBlackboard` at `BehaviorParameters + byteOffset` — one of the **four** variable surfaces | compiler-side; ⭐ **no conflict, but land it before the panel reads that formula** |
| **`W10`** — initializer picker | the Details-panel provider work (`U-6`) | ⭐ **different surfaces**, both in the editor. No conflict |

---

## 5. ⚖️ The sequencing choice — **one genuine either/or for the user**

⭐ **Everything else I have sequenced myself. This one changes what ships first, so it is the user's.**

| | **Option A — correctness first** ⭐ *recommended* | **Option B — panel momentum** |
|---|---|---|
| order | 56 → **`W1`** → **`W2`+`W3`** → 57 → `W4` → panel | 56 → 57 → panel → `W1`/`W2`/`W3`/`W4` |
| ⭐ **why** | `W2` is the **general form of 57's own gate** ⇒ 57's AiPrimitive offsets land under a corpus-wide rail instead of a single read-back. 🔴 **And `W3` is a live silent-overwrite defect (§3)** | the user pulled `S1` forward **specifically** to unblock the value column; this keeps that intent intact |
| ⚠ **honest cost** | delays the panel by ~3 batches | ⛔ 57 ships offsets under the narrower gate, and `W3` stays live meanwhile |
| ⛔ **what is NOT true** | ⚠ **57 is not UNSAFE without `W2`** — its read-back gate is real and headless. `W2` makes it *general*, not *valid*. **A preference, not a requirement** | — |

📌 **Tiebreaker if the user does not care:** ⭐ **the visual check is suspended**, so the panel cannot be
*visually* verified for now either way ⇒ **front-loading correctness costs nothing the user can see.**

---

## 6. ⭐ Sequenced plan (Option A), with what each batch owns

| batch | items | notes |
|---|---|---|
| **56** *(dispatched, frozen)* | emitter unification | ⭐ **dissolves half of `D2`** |
| **next** | **`W1` ALONE** | the design session's own condition. ⭐ **Extend it per §3 or it misses `W3`** |
| **then** | **`W2` red-first + `W3`** | `W2` adds a `Vector3`-after-`byte` asset ⇒ ⚠ **golden corpus 42 → 43, declared.** `W3` verified by `W1`'s extended gate |
| **57** *(dispatched, frozen)* | `S1` AiPrimitive metadata | ⭐ now under `W2`'s corpus-wide rail |
| **then** | **`W4`** | ⛔ after 57 — same line |
| **then** | `W5` · `W6` → `W7` | all headless, independent |
| **then** | the panel batches (`U-6` / 57–59c / 59b) + `S2`–`S5` | ⭐ `S2` consumes `W2`'s rail |
| **Phase B** | `W8` *(needs `D2`)* · `W9` *(needs `D1`)* · `W10`–`W13` | ⛔ **`W12` is unbudgeted — do not start it without a scope pass** |

---

## 7. 📌 The design session's two flagged items — both answered

| | |
|---|---|
| **the `.claude/CLAUDE.md` clause (§6)** | ✅ **ALREADY APPLIED.** `.claude/CLAUDE.md` carries **rule 3a** *(added `2026-08-14`, wording agreed by both sessions)*, and it also records the resolution — the blueprint side renumbered to `#31`. ⇒ **nothing to do** |
| **the held HSM reply (§5)** | ⛔ **Stays the user's call — the coordinator does not send it.** ⭐ **Recorded so it is not lost:** it is the HSM session's unblock (`Q-A`, `Q-C1`, half of `Q-D2`), and ⚠ **under the freeze the HSM session cannot implement what it unblocks anyway** ⇒ **sending it now buys design progress, not code** |

---

## 8. ✅ USER RULINGS `2026-08-15` — Option A, and `D1` is ANSWERED

| | ruling |
|---|---|
| ⭐⭐ **sequencing** | ✅ **Option A — correctness first.** §5's either/or is closed; §6's order is the plan |
| ⭐⭐⭐ **`D1` — is `SlotKind` open or closed?** | ✅ **OPEN.** User, verbatim: *"hsm is still young not battle proven code so i would expect it might grow rather than being fixed."* ⇒ ⭐ **the tagged carrier of `#29`-A STANDS — do not overturn it**, and **`W9`'s `SlotKind` half is UNBLOCKED.** 📌 **This is the outcome the design session's own datum predicted:** *"twice the tagged carrier beat its field count, and both times the untagged cost was invisible until something broke"* |

⇒ ⭐ **Of the three blocking decisions, `D1` is now ruled and `D3` is measured. Only `D2`'s nod remains,
and it is no longer blind** — §2's lean plus Batch 56 dissolving the per-kind half.

---

## 9. ⭐⭐⭐ Audit — **does the cross-host design contradict the unified `Variable ∪ WorkingState` model?**

⭐ **User asked for this check directly.** ⛔ **Their branch never saw the unification** — measured:
**no commit on `claude/cross-host-variable-model-3k8cfh` has Batch 56 (`42d8e9894`) in its ancestry.**

### ✅ The verdict: **compatible at the model level — they reached the same place independently**

⭐⭐ **`Explainer:269` — *"Parameters, working state and asset variables are not three things"*** — and
their axes are **`Role` × `Scope`** (`Explainer:172`: *a state-slot's params are `Input`; its working
state is `State`*). ⭐⭐⭐ **That is the SAME coordinate system as our one-cell result:**
`Variable ∪ WorkingState = (State, Asset)` · `Parameter = (Input, Asset)`.
⇒ ⛔ **There is no rival model to reconcile. The unified direction is not contradicted anywhere.**

### 🔴 But ONE load-bearing sentence is now false — and it is the one `D2` rests on

> **`Design_Behavior_Asset_Parameter_Model.md:72`** — *"`Parameter`/`WorkingState` vs `Variable` are the
> storage of **DIFFERENT dispatch kinds that never coexist**."*

⭐⭐ **True of the shipped corpus** *(0 of 458 assets carry both — Batch 56's own safety argument)*;
⛔ **false of what the model now permits.** `U-12` made the mixture **legal at Stage 2**, `Stage5:4137`
**resolves across both concatenated**, and **Batch 56 unifies the emitters onto that union.**

⇒ ⭐⭐⭐ **`BP-240`'s shape a fourth time — a corpus fact written down as a model invariant.** And their
`D2` hedge (*"the answer may differ per dispatch kind"*) is **built on it** ⇒ ⭐ **retire the hedge:
after 56 there is one state tier, so it cannot differ per kind.**

### ⚠ Two things that LOOK like drift and are NOT — checked, so nobody re-flags them

| | ⭐ **verdict** |
|---|---|
| `Design:69` — **`WorkingState` ⛔ *"not an input"*** | ✅ **CORRECT AS WRITTEN.** ⚠ **This is NOT the claim the user already refuted.** They mean *not in the packed inline region* (`#29:60`: *"working state is not in the inline region at all"*) — **not** *"has no initial value."* ⭐ **Working-state defaults are emitted** (`AiPrimitiveEmitter:133`) and both statements are true at once |
| `#29:29` / `PriorArt:75` — **`DeclarationKind = Parameter · WorkingState · Variable`** | ✅ **correct** — the three-way tag survives as **the serialized shape** after the `U-12` store flip. ⚠ **But any NEW code specified against it must target the union** ⇒ **relevant to `W8` and `W10`** |

### ⭐ Carry-forward: keep the unified direction in the cross-host items

| item | ⭐ **what to hold them to** |
|---|---|
| **`W8`** reserved input variable | ⛔ **do not reintroduce a per-dispatch-kind answer** — §2's `Variable` lean over the union |
| **`W10`** initializer picker | must offer over the **union**, not `Variables` alone |
| **`W13`** stride path | one projection formula — ⭐ **the same "no two implementations" rule (ruling 9)** |
| ⭐ **`#28`/`#29`/`#30` generally** | ⚠ **written pre-56.** ⭐ **Re-read each against the merged union before building its item** — not before, since 56 has not landed |

---

## 10. ⚠ Provenance — carry this into every row

⛔ **The `W1`–`W13` rulings are Claude-authored.** The NotebookLM architect was unavailable and the
design session was designated architect of record. ⭐ **Weaker than the relayed rounds that redirected
four of the last nine batches** — every ruling names its measurement; **overturn on evidence.**
✅ **Reviewed by this programme** (`REVIEW_Behavior_Asset_Parameter_Model.md`), four corrections applied
at `b02ddb1`, ⭐ **one of which refuted a safety argument.**
