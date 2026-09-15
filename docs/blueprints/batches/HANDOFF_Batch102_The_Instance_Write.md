<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-20
current-answer: this whole file — the Batch 102 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# HANDOFF — Batch 102: **the Instance write, and the first smoke test**

> 📌 **Dispatched at `15445c4b8`.** ⭐ Branch from it *(rule 7)*. ⛔ **Scope FROZEN at this sha.**
> ⭐ **Rule 3: your own ids.** ⭐ **Rule 1b: push `chore: started batch 102 at 15445c4b8` FIRST.**
> ⭐⭐ **`R-106`: a blocked item stops THAT ITEM, never the batch. Four verdicts.**

> ## ⭐⭐⭐ RUN FEWER TESTS THIS TIME — **`M-37`, and it is now a rule**
> 📐 Measured: `dotnet build` **79 s** vs `--no-restore` **16 s**; a filtered run **3 s**; Blueprints
> **179 s**. ⇒ ⛔ **the cost was never the tests, it was RESTORE.**
> ⭐ **Use `scripts/quick-check.sh <proj> [filter] [--isolated]` while working — 8 s end to end.**
> ⭐⭐ **The FULL gate table is run ONCE, at the end.** ⛔ Not per item, not per fix.
> ⚠ **`quick-check.sh` refuses to test a failed build** — 📌 `dotnet test --no-build` runs a **stale
> binary** and prints `PASSED`; that fooled the coordinator twice in one session.

> ## ⭐⭐ BATCH 101 WAS EXACTLY RIGHT — **nothing here corrects it**
> ⭐ It proved the `N−1` DIRECTION with a control experiment instead of guessing, ⭐⭐ **refused to edit
> the eight expectations**, verified `101a` by rendering it rather than trusting it, and ⭐⭐⭐ **turned
> "gate the suite" into the finding that it CANNOT COMPLETE.** ⚠ Three reds it did not triage are named
> and stay named — ⛔ do not quietly absorb them here.

---

## 1. ⛔⛔⛔ `102a` — **A PAUSED EDIT LANDS ON AN `Instance` BLUEPRINT** *(`M-36`)*

> 🔴 **User:** *"what is correct about not being able to write into a live blackboard of instance when
> simulation is paused?"* ⭐⭐ **Nothing. The coordinator called it "correct" in three handoffs and was
> WRONG** — 📌 `M-36` carries the retraction.

### 📐 Measured — **the arithmetic is not missing, it runs every frame**

| layer | Instance? |
|---|---|
| **read** `CaptureLiveState` | ✅ `case Instance ⇒ CaptureInstanceStateFromDefinition` → picks `BlueprintBlackboard1024/4096/16384` by what the entity HAS → **`BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out payloadOffset)`** *(`:1435`–`:1466`)*. ⭐⭐ **This is what displayed the user's `312`** |
| **byte write** `TryWriteWorkingStateField` | ✅ **generic** — `(entity, componentType, offset, bytes)` → `StageFieldMutation(entity, componentType, …)`. ⛔ **no `Blackboard1024` in it** |
| ⛔ **address resolve** `ResolveWorkingStateField` | ⛔ `if (def.Kind != AiPrimitive) return null;` *(`:960`)* and hard-codes `typeof(Blackboard1024)` *(`:968`, `:974`)* |

⇒ ⭐⭐⭐ **ONE function is the whole gap.**

### ⭐ Build the `Instance` arm — **by MIRRORING the read, not by inventing**

⭐ Same component pick, same `TryGetSlotOffset`, then `payloadOffset + field.OffsetBytes`, and return
that component's `Type`. ⛔ **Do not re-derive the slot maths** — 📌 ruling 9: if the read's resolution
can be factored so both sides call it, **do that**; if it cannot, say why.

| ⚠ this is the ONE byte-write path in the feature — **so** | |
|---|---|
| ⭐⭐⭐ **the size gate stays absolute** | `bytes.Length != field.SizeBytes ⇒ refuse`. ⛔ **Never coerce** |
| ⭐⭐ **the `StructureHash` check the read does** | 📐 `CaptureAiPrimitiveState` refuses when `storedHash != def.StructureHash`. ⚠ **If the read verifies identity before trusting an offset, the WRITE must too** — ⛔ a stale layout writing at a valid-looking offset is exactly how memory gets corrupted |
| ⭐ **a REVERT PROBE that reddens** | ⛔ a green-only rail on a memory writer is not evidence |
| ⚠ **an honest limit** | ⭐ if a case cannot be made safe *(no slot, hash mismatch, an unmapped tier)* ⇒ **refuse and say WHICH** — 📌 that is `102b` |

---

## 2. 🛠 `102b` — **SAY WHICH REFUSAL IT IS** *(small, and it is why the user could not tell)*

📐 **Four distinct causes collapse into one sentence** *(`BlueprintLiveValueWriter:85`–`:99`)*: no
entity · no session · `field is null` · size mismatch. ⇒ ⛔ **a correct refusal and a broken wire look
identical**, which cost a whole measurement session.

⭐ **Give the outcome a REASON** and surface it in the dialog. ⚠ `VariableEditModal:41` already says
`LiveWriteUnavailable` *"cannot be known in advance"*, so ⛔ OK cannot be greyed up front — ⭐ **but the
message after the click must name the cause.**
⚠⚠ **And fix the false premise in that file's own comment** — *"a row whose value the designer can SEE
is a row this can resolve"* — ⛔ **untrue for Instance until `102a`**; ⭐ after `102a`, make it true or
correct the comment.

---

## 3. 🛠 `102c` — **`EditorHarness`'s FIRST PUMP MUST DELIVER A REAL `dt`** *(`BP-379`)*

📐 Batch 101 proved it: `pump #1 ⇒ dt=0 ⇒ frozen`, and a **late entity in a warm world loses nothing**
⇒ ⛔ **not "attach costs a frame"** — the harness's first `Kernel.Update()` arrives cold.

⭐ **Fix the HARNESS.** ⛔⛔ **Do NOT edit the eight expectations** — 📌 Batch 101's own instruction, and
it would bake a startup artefact into the contract in eight places.
⚠ **Batch 101 did not trace the last hop** — *why* `MasterSyncController.Update()` yields `dt = 0`
although `PumpFrames` calls `Step(0.005f)` first. ⭐ **The controller IS the kernel's instance**
*(`EditorHarness:157`)*, so the answer is inside it. ⭐ **Start there.**
⭐ **Acceptance:** the 8 assertions across `BlueprintKernelRunTests` + `BlueprintObserveTests` go green
**per-class, in isolation** *(⛔ the full suite still cannot finish — `BP-378`)*.

---

## 4. ⭐⭐ `102d` — **THE FIRST SMOKE TEST, IN ITS OWN SMALL PROJECT** *(`S1′`)*

📄 **[`DESIGN_Smoke_Suite.md`](DESIGN_Smoke_Suite.md)** — ⭐ **it carries the class and sequence diagrams;
📌 `R-123`: CHECK THEM before building and report the match or the deviation.**

⛔⛔ **It does NOT go into `Hrot.ClusterRunner.Integration.Tests`** — 📌 `BP-378`: that project **aborts
every run** on accumulated `EntityRepository` allocations. ⭐ **A new, small project stays gateable
because it is small.**

| ⭐ | |
|---|---|
| **scope** | ⭐ **ONE scenario: `Count4`, one entity.** ⛔ No DSL, ⛔ no second scenario yet |
| **T1 — behaviour** | pump N frames, the blackboard counts. ⚠ **depends on `102c`** — if it slips, say so and assert the warm-world shape |
| ⭐⭐⭐ **T2 — panel model** | **the row TEXT the Details table and the Watch would render**. ⛔ **No pixels.** ⭐⭐ **This is the tier that would have caught the Watch reading `0`**, and it is the reason this item exists |
| ⛔ **T3** | ⭐ **not this batch** — the frame rail exists, but T2 first |
| ⭐ **on failure** | print **both row texts** — *"Watch showed 0, Details showed 11"*, ⛔ not `Assert.Equal failed` |
| ⚠ **`G-c` is the work** | `EditorHarness` builds **no** window graph. ⭐ Build it **through the production composition path** *(`R-67`)* — ⛔ not a hand-assembled copy. ⚠ **If that turns out to need the `PerspectiveWorkspace` extraction (`R-121`), STOP and report** — ⛔ do not start that refactor here |
| ⭐ **gate it** | from day one, in the table |

---

## 5. ⛔ NOT IN THIS BATCH

reviving the 174 *(`S1″`)* · the `EntityRepository` accumulation · `T3` smoke · the
`PerspectiveWorkspace` extraction · anything from `DESIGN_Details_Panel_View_Switching.md` *(`R-27`)* ·
Batch 101's three untriaged reds · reverting anything from 94–101.

---

## 6. ⭐ GATES — **ONCE, at the end**

⭐ Baseline = Batch 101's table, base **`15445c4b8`**: AiShared **1714** · Blueprints **3870 / 0 / 10**
*(Xvfb)* — ⚠ **3862 / 0 / 18 with no display; STATE WHICH** · BTree.Editor **622** · Hsm.Editor **554** ·
Hrot.Editor **201** · Breakpoints **143** · Generators **277** · Persistence **143** ·
NodeEditor.Core **211** · NodeEditor.UI **135** · Fhsm **300** · StructEdit **191 / 1** *(`BP-363`)* ·
Fdp.Presentation **146 filtered** · Fdp.Toolkits ⚠ `DEBT-AIB-030` · tracker **80 / 235** · rulings **92/92**.

⭐ Keep the table shape. ⭐ **Extra rows:** the new smoke project's counts · frame-rail ran/skipped ·
`102a`'s revert probe.
⛔ **`Hrot.ClusterRunner.Integration.Tests` stays OUT of the table** *(`BP-378`)* — ⭐ report only the
per-class result for `102c`'s eight.
