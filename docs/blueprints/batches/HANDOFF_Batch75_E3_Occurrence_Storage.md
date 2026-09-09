# HANDOFF — Batch 75: **`E3` at last** — the occurrence storage move · wire the launcher · persist `SubtreeAssetId`

> 📌 **Dispatched at `509f80ac3`.** Frozen per rule 1.
> ⛔⛔ **NEW — rule 1b, and it is YOUR finding:** ⭐ **push an empty `chore: started batch 75 at <sha>`
> commit immediately after your rule-7 merge, before writing any code.** ⚠ **I will not amend this
> handoff once that marker exists** — and if it does not exist I will **ask**, not infer.
> ✅ **Batch 74 MERGED at `5b0c9563e`** — gates re-run by me, blueprint golden set untouched.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐⭐ Batch 74 — **you found a hole in the protocol and two in my premises**

| | |
|---|---|
| ⭐⭐⭐ **rule 1a's blind window** | 📐 *"my branch had no PUSHED commits yet, so the dispatch sha was genuinely not its ancestor — while three items were already built locally."* ⛔ **My check was correct about the remote and wrong about reality, twice.** ⭐ **Both your fixes are now `rule 1b`** — ⚠ **and I recorded that nothing broke only by LUCK**, not because the control worked |
| ⭐⭐⭐ **`E6`(A) never reached the COMPOUND key** | `HsmActionGenerator` spelled it `sym.Name` at **three** sites where BTree spells the FQN ⇒ **every `[SharedAiAction]` binding was addressable by nobody.** ⭐ **Batch 72 fixed the plain key and I did not ask you to sweep the compound one** — you found it by building `E7b` |
| ⛔⛔ **item 4 inverted MY premise, and `.dev/` answered first** | ⭐ **Track C's `VariableEditLauncher` is constructed by NOTHING** ⇒ **retiring the panel would have deleted the only LIVE surface and left the replacement unreachable.** 📌 **That is why it is item 2 below** |
| ⭐⭐ **the guards answer was better than the question** | *"they were TWO conditions that disagreed… copying the FIXED guards would have reproduced the SPLIT, because the split is structural, not textual."* ⭐ **One packed field list, three consumers** |
| ⚠ **a fifth vacuous rail, self-caught** | `Contains("__paramJsonOpts")` was satisfied by the emitted **body** with the **declaration** gone. ⭐ **Probe-first is why you keep finding these** |

---

## 1. ⭐⭐⭐ `E3` — **the occurrence storage move.** ✅ *Both halves are now ruled*

> 📄 **[`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md) — RESOLVED `2026-08-17` with the user.**
> 📄 **[`DESIGN_Hsm_Storage_Model.md`](DESIGN_Hsm_Storage_Model.md) §3** — the storage half.
> ⛔⛔ **Do not re-derive either.** ⭐ **This is the only occurrence case that SILENTLY CORRUPTS.**

### ⭐ The three rulings, verbatim

| | ruled |
|---|---|
| **delivery** | ⭐⭐ **carry the occurrence on `HsmCommandWriter`** — a **kernel-owned** `ref struct` already passed to every action. The kernel stamps it before each dispatch. ⛔⛔ **NO delegate signature changes anywhere** *(that is what makes this two fields instead of a 25-directory ABI break)* |
| **identity** | ⭐⭐ **the PAIR `(regionSlotIndex, stateId)`** — ⛔ **not a pre-hashed key.** ⚠ **A hash in the kernel would put `ComputeStatefulSlotKey`'s algorithm in `ExtDeps` — two homes for one algorithm, which is exactly the shape that produced `E6`** |
| **storage** | ⭐⭐ **ONE path.** Per-occurrence bytes from `BlueprintBlackboardPartitions` under `ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)`. ⛔ **Do NOT keep the baked-offset route "for the simple case"** — ⭐ **that divergence is what made this invisible** |

### ⭐⭐ The division of labour — **this is the load-bearing sentence**

> ⭐ **The kernel supplies IDENTITY. The thunk does the LOOKUP.**

⛔ **The kernel knows nothing about the partition allocator and must not learn.** ⭐ **The thunk already
contains the code shape it needs** — the tier probe + `TryGetSlotOffset` that stateful **BTree** actions
have used since `S2`. ⇒ **what it lacked was four bytes of *"who am I"*.**

### 📐 What you measured that this builds on

| | |
|---|---|
| ⛔ **today** | the thunk resolves its DTO at `bb.BehaviorParameters[0] + <baked offset>` — a fixed offset into the entity's **single 100-byte `BrainBlackboard`** ⇒ ⭐ **one home by construction** |
| ⭐ **HSM already uses the allocator** | `EmitStatefulWorkingSlotsArray` provisions `Role=State` slots through it — ⚠ **passing `Guid.Empty` for `nodeVisualId`.** ⭐⭐ **That `Guid.Empty` is precisely what this item replaces** *(and it stays `Guid.Empty` for `Scope.Behavior`/`Entity`, which are deliberately shared — ⛔ **adding an occurrence THERE would be a bug**)* |
| ⚠ **five emitters produce the fixed shape** | ⭐ **they change what they EMIT, not the shape they emit to** |

### 🔴 STOP conditions

| | |
|---|---|
| ⭐⭐ **the accepted limit — ASSERT it** | 📐 `EvaluateGuard`'s third argument is `eventId`, **so guards have no writer.** ⭐ **Free today — `VE-DEBT-004`, zero production `[HsmGuard]`** — ⛔ **but assert it**, so a stateful guard surfaces as a decision rather than a silent wrong answer |
| ⚠ **occurrence stability across ticks** | ⭐ **a re-entered region must reach the SAME bytes.** ⛔ **A key that changes per tick is worse than none** |
| 🔴 **if `(regionSlotIndex, stateId)` is not stable** across a transition that re-enters the same state, ⭐ **STOP and report** — that would mean the pair is the wrong identity, which is a `Q35-B` reopen, not an implementation detail |
| ⛔ **`ExtDeps` is additive only** | two fields + the kernel's assignment at the **13** call sites. ⭐ **If it turns out to need a breaking change, STOP** — the whole reason `B` beat `A` was that it does not |

**rails — 📄 plan §4B `E3`, pre-written:** ⭐⭐⭐ *"two concurrently-active orthogonal regions running the
SAME action write DIFFERENT bytes"*, ⛔ **failing before the change** — ⭐ **`HsmOrthogonalRegions` is in
the corpus for exactly this.** ⭐ **Plus:** occurrence stability across ticks · ⛔ **the 100-byte tail is
untouched** *(no write to `ExpectedThreatLevel` or either interrupt — 📄 design §4.3)* · ⭐ **the three
Batch-72 gap tests INVERTED, not deleted.**

⭐ **The Batch-73 generated-code tier is what watches this** — its acceptance test proves it reaches
thunk ids. ⚠ **Expect it to move; show the diff is thunk bodies and keys.**

---

## 2. ⭐⭐ Wire Track C's `VariableEditLauncher` — **the third surface with no caller**

> 🔴 **Your Batch-74 measurement:** the launcher *"is constructed by nothing: the table's context menu is
> not wired yet"* ⇒ ⛔ **the `InspectorWindow` panel is the ONLY live way to edit a variable's default.**

📄 **`DESIGN_Variable_Details_And_Editing.md` §3** — ⭐ **already designed, and the ruling is old:**
*"two menu items = the two `EditScope`s"* — **"Edit value…"** (`ForField`, double-click the **value**
cell) · **"Properties…"** (`WholeComponent`, double-click the **name** cell). ⭐ **Run state decides
WRITABILITY, not which dialog.**

| | |
|---|---|
| ⭐⭐ **this is WIRING, not building** | the launcher exists · `DefaultValueAuthoring.OpenSession` is the one call site · the `ExactlyOneCallSite` rail already pins it ⇒ ⛔ **do not add a second opener** |
| ⭐ **invert the Batch-74 gap rail** | it asserts *"the panel routes; the launcher is constructed by nobody."* ⭐ **The second half flips** |
| ⭐⭐ **and the panel STAYS** | ⛔ **no rush removals** *(user ruling)*. ⭐ **Two entry points, one implementation** — that is what ruling 9 asks for, and Batch 68 already secured it |
| ⚠ **the visual half is unverifiable** | ⭐ **assert the MEANING** — which gesture opens which scope, what is writable in which run state |

---

## 3. ⭐ `DEBT-AIB-028`(a) — **persist `StateNode.SubtreeAssetId`**

⭐ **Small, independent, and it is `E5`'s ONLY remaining prerequisite.**
📐 **Filed measurement:** *"a NEW field, not persisted to JSON, and no real HSM asset sets it"* ⇒
⛔ **rules 8/8b still cannot fire on assets loaded from disk**, which is why `E4` reads as done but is
not observable end-to-end.

| | |
|---|---|
| ⭐ **round-trip it** | mapper + projector, the way every other `StateNode` field goes |
| ⭐⭐ **and then rules 8/8b become live** | ⚠ **check whether they now fire on a disk-loaded asset** — ⭐ **that is `E4`'s missing half arriving, and it is worth one line in the report** |
| ⚠ **`DEBT-AIB-029` is adjacent** | the check walks **DIRECT children only** ⇒ deeper nesting undetected. ⛔ **Not this item** — ⭐ **but if persisting the field makes the shallow walk observably wrong, say so** |

**rail:** ⭐ an HSM asset with a subtree-hosting state **round-trips `SubtreeAssetId` through disk**, and
⭐⭐ **a rule-8 violation authored in that asset produces its error after a load** *(not only in-memory)*.

---

## 4. ⛔ NOT in this batch

**`E5`** *(next, once item 3 lands)* · **`E7a`** · **the compound-key thunk's bytes** *(⭐ your named
boundary: no shipped assembly generates one — the thunk must be generated where the method lives)* ·
⛔⛔ **blueprint multi-occurrence** *(deferred by the user)* · ⛔ **wiring the producer picker** *(parked;
its runtime does not exist)* · the 12 quarantined scenario tests · the Track C **visual check**.

---

## 5. Gates

**Baseline — coordinator-verified at `5b0c9563e`:** build **0 / 69** · Blueprints **3691 / 3681 / 0 / 10** ·
AiShared **1281** · BTree.Editor **615** · Breakpoints **134** · Generators **266** · Hsm.Editor **543** ·
AiEditor.Persistence **136** · Examples.Scenarios **56 / 68 (12 skipped)** · Examples.UrbanCombat **29** ·
Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 170**.

| | |
|---|---|
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⭐ **expected movement** | **item 1 moves the generated-code tier** *(thunk bodies, keys)* and may move the HSM emit baseline · item 3 moves `hsm-persistence-shape` · **item 2 should move nothing** |
| ⚠ **the quarantine count stays 12** | ⛔ **a new skip is a finding, not a fix** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ **neither red nor green is evidence** — `DEBT-AIB-030`, **six** distinct tests, and Batch 74 saw its first red-in-both-samples. ⚠ **Item 1 lands squarely in this assembly** ⇒ **confirm any red by class AND namespace, as you did** |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **did the two-regions rail FAIL before the change?** *(it must)* · **occurrence
stability across ticks** · **the `ExtDeps` surface, and that it stayed additive** · ⭐⭐ **the guards
limit, asserted** · what moved in the generated-code tier.
⭐⭐ **item 2** — **which gesture opens which scope**, asserted · the inverted gap rail · ⛔ **confirm the
panel still routes** *(the `ExactlyOneCallSite` rail must still hold with two entry points)*.
⭐ **item 3** — ⭐⭐ **do rules 8/8b fire on a DISK-LOADED asset now?** · anything `-029` made observable.
**Always:** ⭐ **the started-marker sha** *(rule 1b)* · **every id you allocated** · **which `DEBT-AIB`
rows this batch touched** · **the quarantine count**.

⭐⭐⭐ **Eight batches, a finding every time — and Batch 74's was about the PROTOCOL, not the code.**
⭐ **That is the most useful kind, and it is the second time your report has changed how I work** *(the
first was proportionate gating).* ⛔ **Keep reporting them.**
