<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: **AN EDITOR-AUTHORED HSM AS AN
  ENTITY BEHAVIOUR, WITH BLUEPRINT ACTIONS AND GUARDS**. ⚠ A STATE doc, not canon: every
  "green"/"pushed"/"HEAD" line is a snapshot dated below. ⛔ VERIFY against git before acting
  ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-28
build-state: ✅ authoring programme COMPLETE (stages 1-2, all nine rails). ⏭ The LIVE work is the
  CHANNEL-LIFECYCLE programme in §8 - built: CE-402, CE-403, CE-388 (D-A2/D-B1/D-D1/D-F); left:
  rail 5, then D-E only if it measures a gap, the validator question, and the editor fixtures.
  Branch `behaviors` @ 6dcde3542.
  ✅✅✅ **STAGE 1, STAGE 2 AND ALL NINE §9 ACCEPTANCE RAILS ARE COMPLETE AND PUSHED**
  (2026-09-28: CE-397 rail ④, CE-398/CE-400 rails ⑨/⑥ + the CE-399 sizing fix, CE-401 rail ⑦ —
  see §7.2a). ⚠ The sentence below is the 2026-09-27 state and is kept for its detail:
  ✅✅ **STAGE 1 AND STAGE 2 ARE BOTH COMPLETE AND PUSHED** — `CE-381`..`CE-387`, plus
  `CE-396` found and fixed inside `CE-385`, plus the design's §9 ③ validator rule which had been
  neither built nor filed. Branch `behaviors`. ⭐ **The user's acceptance description now works AND
  is authorable from the editor.** ⏭ **NEXT IS §7** — what is genuinely left, and it is short.
current-answer: ⭐⭐⭐ **START AT §8** — the CHANNEL-LIFECYCLE programme (`Q74`), which is where the
  work now is. §1-§7 are the HSM+blueprint AUTHORING programme and it is COMPLETE (all nine §9
  acceptance rails closed, §7.2a). §8.0 is the verifiable state, §8.2 what is left, §8.3 the traps
  that must not be re-derived, §8.4 the editor end-to-end check the user originally asked for.
  ⛔ The OWNING design is Architect_Question_74_Blueprint_Channel_Lifecycle.md and its §9 is the
  AS-BUILT: where §4 and §9 disagree, §9 wins.
  (Historic: START AT §0 (three commands), then §7.2a and §7.3.) ⛔ §2 is now
  HISTORY — it describes Stage 2 before it was built; read §2 only for the measurements, never for
  what to do. §3 is the corrections that must not be re-inherited. §4 the standing constraints.
  §5 the gate baselines. §6 what is open and NOT ours.
stale-below: ⛔ §2 ("STAGE 2 — the three items") is DONE. Its measurements are still true; its
  instructions are spent. §2.4's "not built, not filed" is FIXED.
known-rot: ⚠ §2.2 said `CE-386` was "SMALLER than the design says". ⛔ HALF WRONG — the plumbing was
  indeed already there, but `ActionSchemaExporter` collapsed `hsmAction`/`hsmGuard` into one bit, so
  the item needed two new `ActionHosting` bits first. See §7 and design §13.5.
known-conflict: ⚠ `RESUME_Hsm_Subtree_Authoring.md` is the PREVIOUS programme's doc and is COMPLETE;
  its build-state block already points here. ⛔ Two `RESUME_*` files exist for this one lane — THIS
  is the live one.
related-designs:
  - DESIGN_Hsm_Blueprint_Behaviour_Authoring.md — ⭐⭐⭐ THE OWNING DESIGN. §3 the decisions, §4-§6 the
    UML, §8 the items, §8a the pre-build measurements, §8b an accepted limit, §12 the gate baseline,
    §13 the AS-BUILT. It wins over anything here.
  - HSM_Editor_NodeEditor_Host_Design.md — owns the HSM EDITOR surface; §10.1/§10.2 are superseded
    on WHERE the pickers get their items.
  - AI_Editor_Shared_Infrastructure.md — owns `ActionSchemaExporter`, which `CE-386` consumes.
  - DESIGN_Occurrence_Scoped_Storage.md — owns the occurrence storage the hosted thunks land in.
-->

# RESUME — HSM + blueprint behaviour authoring (`behaviors` lane)

> 🔒 **The user's ask, verbatim (`2026-09-27`)** — the acceptance test for the whole programme:
> *"To use the editor defined HSM as entity behavior, To be able to define few states and transitions
> between them, thw transition controlled by a condition programned using a function in blueprint
> (taking params from hsm's blackboars variables), when in the state i would like to tick an action
> defined in blueprint taking params from hsm blackoard variables (or multiple actions in parallel,
> one per channel like one action for movement, one for weapon control), with possibility to
> transition to exit state to finish the behavior."*

---

## 0. ⭐ FIRST MOVES — three commands, then read §2

```bash
git log --oneline -6                 # expect d760b25db at the top; VERIFY, do not trust this file
python3 scripts/rulings-check.py && python3 scripts/design-digest.py --check
git fetch origin coordinator && git merge-base --is-ancestor origin/coordinator HEAD && echo "rule 7 OK"
```

⛔ **Do NOT write a DESIGN BRIEF** — this is an implementation lane; the brief is a coordinator
obligation and `session-design-brief.sh` says so on this branch.

---

## 1. ✅ STAGE 1 — **done, pushed, and what each one actually decided**

| id | what shipped | the thing worth remembering |
|---|---|---|
| `CE-376` | the four stale `SlotCount` rails | assert **WHICH** slots are attached (a SET, root keys DERIVED), never **HOW MANY**. Suite went 317→**321/321** |
| `CE-381` | `TransitionFlags.IsPolled` (bit 6) · `StateFlags.HasPolledTransition` (bit 9, DERIVED) · `ReservedEventIds` | ⭐⭐⭐ **`ReservedEventIds.Polled = 0xFFFD` + a flattener NORMALISATION were an unplanned addition** — without them `.On(0).Polled()` would be selected by the RTC **completion** pass too |
| `CE-382` | `TryTakePolledTransition` in the `Idle` arm, before the activities | ⭐⭐ **no selection code was written** — the normalisation means `SelectTransition` already matches exactly the polled set; the arm delegates to `ProcessRTCPhase` for full run-to-completion |
| `CE-383` | explicit-id override extended to **activity** + **guard**; `ActivityId()` / `GuardId()` builders | also FIXED a silent drop: `TransitionNode.ActionId` was parsed and ignored |
| `CE-384` | `Func<Guid, ushort?>` resolver on `EmitTopologyCore`; `GeneratedBlueprintSchema.AssetId`; the 5 DTO fields | ⛔ an unresolved reference emits **NOTHING, never `0`** |

⭐ **Net effect: the user's whole description WORKS AT RUNTIME**, drivable from a hand-written
`.hsm.json` / `HsmBuilder`. ⛔ **It is not reachable from the editor** — that is Stage 2.

---

## 2. ⏭ STAGE 2 — **the three items, with the measurements ALREADY MADE**

> ⛔⛔ **Everything in this section was MEASURED on `2026-09-27`. Do not re-derive it — verify a line
> only if you are about to act against it.**

### 2.1 `CE-385` — model + mapper *(the DTO half is ALREADY DONE)*

⭐ **`CE-384` already shipped the DTO fields**: `StateNodeDto.ActivityBlueprintAssetId` /
`ActivityBlueprintName`, `TransitionNodeDto.GuardBlueprintAssetId` / `GuardBlueprintName` /
`IsPolled` — all `JsonIgnore(WhenWritingDefault)`, all round-tripped by a rail.

⏭ **What is LEFT:** the EDITOR MODEL and the mapper.
- `Hrot.Hsm.Editor/Model/HsmAsset.cs` — `StateNode` and `TransitionNode` need the matching fields.
- `Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs` — **both directions**. 📐 The existing
  `ExpressionTargetField` arms are at `:121` / `:142` (model→dto) and `:339` / `:365` (dto→model);
  the new fields go beside them.
- The facets (`HsmFacets.cs`) + `HsmFacetMapper` / `HsmFacetDispatcher` for the inspector.

⛔⛔ **`HsmGoldenCorpusTests.TheCanonicalJsonOfEveryCorpusAssetIsUnchanged` compares CANONICAL JSON**
⇒ any new DTO field MUST be `WhenWritingDefault`/`WhenWritingNull` or all four shipped assets churn.

### 2.2 `CE-386` — the pickers *(SMALLER than the design says)*

📐 **Measured — the plumbing already exists:** `AiFacetPickerBinder.Rebuild:95` *(the ONE production
site)* **already passes `services.ActionSchema`** into `HsmPickerDrawerFactory.BuildDrawers`, and that
factory **already takes `IActionSchemaExporter? exporter`**. ⛔ It hands it only to
`HsmBlackboardFieldPickerDrawer`.

⇒ ⭐ **The work is: pass it into `HsmActionPickerDrawer` and `HsmGuardPickerDrawer` and filter.**
- Today both `GetItems()` return **only names already in the asset** ⇒ the FIRST binding is unmakeable.
- `ActionSchemaExporter.cs:106` reads `[GeneratedAiPrimitiveAction]` and already populates
  `IsAiPrimitive` + the `hsmAction` / `hsmGuard` flags. ⛔ **Do not build a second catalog** (`R-137`).
- ⭐ **Pattern to mirror:** `BehaviorHashPickerDrawer.GetItems()` → `_registry.GetRegisteredNames()`,
  and `BTreeCommandSink.cs:170` for turning a catalog entry into an asset binding.
- ⚠ **Rail ⑧ is RE-AIMED** (design §9): assert the drawers **USE** what they are given — the caller
  already forwards it, so "the caller forwards" is not the defect.

### 2.3 `CE-387` — the per-state param binding *(a complete worked precedent exists)*

📐 `StateNodeDto.ExpressionTargetField` exists and `HsmBridgeEmitCore.cs:316` already consumes it to
emit `HsmParamBindings.Register`. ⛔ **`StateNode` (editor model) has no such field** and the mapper
maps it for transitions ONLY ⇒ always null ⇒ **every state seeds from offset 0**.

⭐⭐ **`E7b` did exactly this for the TRANSITION-level field** — model, mapper, command sink
(`HsmCommandSink:249`), validator rule, emitter and a golden (`HsmExpressionTargetTests`). ⇒ `CE-387`
is *"do for states what `E7b` did for transitions"*. ⚠ The two consumers are DIFFERENT mechanisms:
transitions feed a compound `{Fqn}@{offset}` key; states feed `HsmParamBindings` seed offsets.

### 2.4 🔴 **NOT BUILT AND NOT FILED — the validator rule (design §9 ③)**

⛔⛔ **A state naming BOTH `ActivityAction` and `ActivityBlueprintAssetId` is currently ACCEPTED.**
The design calls it a validator error; `HsmValidator.cs` mentions neither new field *(measured: 0
hits)*. ⭐ Same for a transition with both `GuardFunction` and `GuardBlueprintAssetId`.
⇒ **Build it in Stage 2** (it belongs with `CE-385`'s model work) or file it — ⛔ do not let it ship
silent.

---

## 3. ⚠ CORRECTIONS — **do not re-inherit these**

| ⛔ what was believed | ✅ what is true |
|---|---|
| *"nothing dispatches event 0, completion transitions are inert"* | 🔴 **FALSE.** `ProcessRTCPhase` sets `currentEventId = 0` after every executed transition ⇒ the **completion pass runs on every RTC iteration**. Eventless transitions are live, just unreachable from `Idle`. 📄 §2.3 |
| *"the BTree side already solved referencing a blueprint"* | 🔴 **FALSE.** `T31` binds `DemoAiPrimitiveNodes`, a **hand-written stand-in** whose own header calls the editor cross-compile path *"not-yet-built"*. 📄 §2.4 |
| *"`HsmBridgeEmitCore` emits the blueprint ids"* | ⛔ **wrong emitter** — `HsmEmitCore` emits the builder chain; the bridge writes the registrar. 📄 §13.3a |
| *"`CE-381`..`CE-384` are a spine that lets an ASSET address a blueprint"* | ⛔ not without DTO fields; they were pulled forward into `CE-384`. 📄 §13.3a |
| *"the slot linear scan is the blueprint-call cost"* | ⛔ tables are 3/12/16/16 × 16 B = 1–4 cache lines. The cost is the delegate-dispatched component probes. 📄 §11.5 |

### 3a. ⚠ FIXTURE TRAPS THAT COST ME FOUR ROUND-TRIPS — **the product code was right every time**

| trap | symptom |
|---|---|
| `HsmBuilder.State("A")` **creates**; calling it twice throws | *"State 'A' already exists"* — hold the returned `StateBuilder` |
| `HsmDefinitionBlob.States` is a **readonly span** | `CS8332` — build the blob you want, do not mutate it |
| the emitter finds the compiler root as *"the state with no resolvable parent"* and emits ITS CHILDREN | a flat state list with no `__Root` emits an **EMPTY builder**, silently |
| with a `__Root`, **`States[0]` is the root** | address states by NAME in assertions |
| `HsmInstance64` has TWO leaf slots, both default 0 | every activity dispatch DOUBLES; park unused regions at `0xFFFF` |

---

## 4. 🔒 STANDING CONSTRAINTS *(user, this session)*

| | |
|---|---|
| **branch** | push only to `behaviors`; `git push -u origin behaviors`, retry network errors 2s/4s/8s/16s |
| ⛔⛔ **the stash** | **`EXPERIMENT: RootParamsBytes always 100 — probe only`** is a DIAGNOSTIC that must **NEVER** be committed. Leave it stashed |
| **questions** | plain chat text — ⛔ **never** the `AskUserQuestion` widget |
| **links** | render docs AND task ids as GitHub blob links on `behaviors`; ⭐ gloss every id on first mention *(the user is on mobile)* |
| **slow work** | builds/tests/searches **in the background**; ⚠ do NOT nest `nohup` inside a backgrounded tool call — the process group gets reaped *(cost: 30 min this session)* |
| **cross-lane** | `Hrot/Runner/Hrot.SystemTests/`, `Hrot.IG.Tests`, `Fdp.Core` are other lanes' — STOP-and-report |
| **commits** | `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_01Y7DZG7BBQ8rNTesibu9NXo`; ⛔ no model identifier in any repo artefact |
| **PRs** | do not create one unless explicitly asked |
| **method** | measure before leaning; a lean carries a CLAIM TABLE; red-prove every rail |

---

## 5. 📐 GATE BASELINE — **at `d760b25db`, `2026-09-27`**

| suite | count |
|---|---|
| `Fhsm.Tests` | **335 / 335** |
| `Fdp.Toolkits.Tests` | **2342 / 2342** ⚠ weak evidence — `DEBT-AIB-030` is 7 tests whose identity ROTATES |
| `Hrot.AiEditor.Generators.Tests` | **329 / 329** |
| `Hrot.Hsm.Editor.Tests` | **587 / 587** |
| `Hrot.Blueprints.Tests` | **4036 passed / 18 skipped** |
| `T3` *(system, ~20 min)* | **6 failed / 111 passed** — ⭐ ALL SIX PRE-EXISTING, proved by name against the prior run's 13 (§12.2) |

⭐⭐ **The property worth protecting: every suite this programme touches is GREEN**, so a red is ours.
⚠ **It expires on any other lane's merge** — re-capture after a rule-7 re-sync.
⭐ Run with `scripts/quick-check.sh <proj>`; ⛔ **build the TEST project, never the production one**.

---

## 6. 📋 OPEN, AND WHOSE

| id | what | owner |
|---|---|---|
| `CE-385` `CE-386` `CE-387` | **Stage 2** — §2 | ⭐ **ours, next** |
| the §9 ③ validator rule | method XOR blueprint — **not built, not filed** | ⭐ **ours** — §2.4 |
| `CE-388` | channels for blueprint-hosted HSM actions | ours, separable — only if ④'s actions become blueprints |
| `CE-389`..`CE-392` | the call-cost thread (§11) | ours, independent of Stage 2 |
| `CE-395` | transition priority written to bits 8-11, read from 12-15 ⇒ always 0 | ours; **filed not fixed** — it changes which transition wins |
| `CE-393` | the engine-wide accessor campaign | ⛔ BACKEND (`Fdp.Core`) |
| `CE-394` | `HN-015` is FIXED and its tripwire is firing | ⛔ BACKEND (system-test harness) |
| `CE-332` · `CE-321`② | `Hrot.IG.Tests` rails · rotted example scenarios | ⛔ other lanes / unmeasured |

---

## 7. ⏭ WHAT IS LEFT — **Stage 1 and Stage 2 are done; this is the remainder**

> ⭐⭐⭐ **The user's acceptance description WORKS and is AUTHORABLE.** A few states, transitions
> between them, a transition guarded by a blueprint function reading HSM blackboard variables, a
> per-tick action in a state defined in a blueprint, parallel regions one action per channel, and an
> exit state that finishes the behaviour — all of it is now reachable from the editor.

### 7.1 ✅ BUILT IN STAGE 2 *(design §13.4-§13.6)*

| id | one line |
|---|---|
| `CE-385` | the five editor-model fields + mapper + inspector; `ResolvePickedAssetId` shared by all three picks |
| the §9 ③ rule | `MethodAndBlueprintBothBound` — a slot naming BOTH a method and a blueprint is now an ERROR, was silently accepted |
| `CE-396` | `[AiAssetPicker]` dispatches on the attribute's KIND — it was hard-wired to BTree and would have offered BTrees in the blueprint fields |
| `CE-386` | `ActionHosting.HsmGuard` + `HsmActivity`; both pickers read the catalog ⇒ the FIRST binding is makeable |
| `CE-387` | `StateNode.ExpressionTargetField` — the emitter had consumed it since `E3b-0` and the editor could never produce it |

### 7.2 ✅ ACCEPTANCE RAIL ④ IS CLOSED — `CE-397` *(`2026-09-28`)*

⛔ **It used to be blocked by CONTENT, not by code:** no blueprint in the repo declared `HsmGuard`
*(measured: 34 `BTreeAction` · 9 `BTreeCondition` · 2 `HsmAction` · **ZERO** `HsmGuard`)*, so the
chain existed and nothing exercised it.

⭐ **`CE-397` authored the two assets that do** — `HsmGuardDemo.bp.json` (the first `HsmGuard`
AiPrimitive in the corpus) and `HsmPolledGuardDemo.hsm.json` (a POLLED transition guarded by it by
GUID, seeded through `CE-387`'s state binding) — and the rail reads **both sides from artefacts**:
the compiled blob's `TransitionDef.GuardId` against the keys the blueprint's own generated registrar
puts in `HsmActionDispatcher.GuardTable`. 📄 design **§13.7**.

🔴 **It also found a golden that UNDER-RECORDED:** `AiAssetCorpus` emitted with no blueprint id
resolver, so the baseline omitted the baked `.GuardId(...)`. Fixed; only the new asset's baseline
moved. ⚠ Same silent-default shape as everything else this programme has caught.

### 7.2a ✅✅ **ALL NINE §9 ACCEPTANCE RAILS ARE CLOSED** — `2026-09-28`

⭐ Closed after ④: **⑨** and **⑥** by `CE-398` / `CE-400` *(design §13.8, which also carries `CE-399`,
a hosted-occurrence params **sizing defect** the end-to-end run surfaced)*, and **⑦** by `CE-401`
*(design §13.9)*. ⭐⭐ **The rail-to-test map lives in design §9's own table** — ⛔ do not re-derive it
here; this line only says the work is done.

| ⚠ two things worth not re-litigating | |
|---|---|
| **① and ⑨ OVERLAP ON PURPOSE** | ⭐ `CE382_R1` runs the **kernel phase machine** *(names which component broke)*; `CE398_R1` runs the **product** *(catches a broken registrar scan the kernel rail cannot see)*. ⛔ Deleting either loses a distinct signal |
| **`CE-399` is NOT what unblocked ⑨** | 🔒 reverting it left `CE398_R1` green — it is a correctness fix with **its own rail** `CE399_R1`. ⛔ Do not credit it with rail ⑨ |

### 7.3 📋 THE REST, AND NONE OF IT BLOCKS THE ACCEPTANCE TEST

| id | what | note |
|---|---|---|
| `CE-388` | `WritesChannel` for blueprint-hosted HSM actions | separable; only matters once the per-channel actions are BLUEPRINTS rather than methods |
| `CE-389`..`CE-392` | the per-call cost thread (design §11) | independent of §8 entirely; ⚠ ORDER is load-bearing: `CE-389` → `CE-390` → any `CE-392` decision |
| `CE-395` | transition priority written to bits 8-11, read from 12-15 ⇒ always 0 | **filed not fixed** — it changes which transition wins. ⚠ Polled transitions inherit it |
| `CE-393` · `CE-394` | ⛔ BACKEND lane | not ours |

### 7.4 ⚠ A TRAP THIS SESSION PAID FOR TWICE — **add it to §3a's list**

⛔⛔ **A red-proof script that restores with `mv file.bak file` restores the BACKUP's mtime**, which is
OLDER than the patched build's output ⇒ **MSBuild skips the rebuild and the next run tests a STALE
binary.** 📌 It surfaced as a phantom red in a rail whose production code was correct.
⭐ **`touch` the file after every restore.** ⚠ This is the same disease as the two stale-binary traps
in `CLAUDE.md`, in a third disguise: there the build failed or the wrong project was built; **here the
right project was built and MSBuild decided it had nothing to do.**

---

## 8. ⭐⭐⭐ THE CHANNEL-LIFECYCLE PROGRAMME — **`Q74`, and it is where the work now is** *(`2026-09-28`)*

> ⭐⭐⭐ **RESUMING? START HERE, NOT AT §7.** §1–§7 are the HSM+blueprint AUTHORING programme, which is
> COMPLETE *(all nine §9 acceptance rails closed)*. Everything since is a SECOND programme that came
> out of one user question — *"how do I check this in the editor?"* — and it is the live one.

### 8.0 📐 STATE, verifiable in one command

| | |
|---|---|
| branch | **`behaviors`**, pushed, at **`6dcde3542`** |
| owning design | 📄 [`Architect_Question_74_Blueprint_Channel_Lifecycle.md`](Architect_Question_74_Blueprint_Channel_Lifecycle.md) — **§9 IS THE AS-BUILT; where §4 and §9 disagree, §9 WINS** |
| ⛔⛔ **the stash** | `stash@{0}` = *"EXPERIMENT: RootParamsBytes always 100 — probe only"*. 🔒 **A DIAGNOSTIC THAT MUST NEVER BE COMMITTED.** Leave it stashed |

```bash
git log --oneline -8 behaviors   # 6dcde3542 back to 84808faef is this programme
git stash list                   # must still show the RootParamsBytes probe
```

### 8.1 ✅ BUILT AND PUSHED

| id | what | commit |
|---|---|---|
| `CE-402` | a blueprint-issued channel command **CLAIMS** the channel. ⛔ Before it, `ChannelArbitrationSystem` wiped **every** blueprint channel command on the next tick — nothing ever moved, on the BTree route too | `2c320079c` |
| `CE-403` | `[WritesChannel]` on the two `[HsmAction]` channel writers ⇒ `RequiredExitCleanups` non-empty **for the first time in this repo's history** | `2c320079c` |
| `CE-388` slice 1 | `BlueprintChannelDerivation` — exact for channel-command ops + `GraphCall` reachability, **incomplete-with-names** for hardcoded C# and cross-asset calls | `c69405a0d` |
| `D-F` | **ONE `HsmActionKey`**, moved to `Fdp.Toolkits.Analyzers/Shared/`, `<Compile Link>`-ed into the compiler and Persistence | `6dcde3542` |
| `D-B1` | blueprint cleanup thunk emitted + registered; `HsmEmitCore` bakes `.OnExitId(n)` **only when the state has no `OnExit`** | `6dcde3542` |
| `D-D1` | the SAME auto-bind for the C# route, predicate read off the Roslyn `Compilation` | `6dcde3542` |

⭐ **The concrete result:** `HsmTwoChannelRegionsDemo` bakes `.OnExitId(26097)` / `.OnExitId(46817)`,
matching what the registrar registered for `ExitCleanup_Activity_DriveChannel` / `_FireChannel`.
**Those two channels stopped leaking on state exit.**

### 8.2 ⏭ WHAT IS LEFT — **in order**

| # | what | note |
|---|---|---|
| **1** | ⭐⭐ **rail ⑤ — MEASURE the release-then-reclaim gap** | 📄 `Q74` §6 ⑤. `CE382_R5` already proved the newly entered state's activity runs in the SAME tick ⇒ ⭐ **`D-E` may be DELETED rather than built.** ⛔ Do not build `D-E` before this measurement |
| **2** | `D-E` — a per-state `KeepChannelsOnExit` opt-out | **only if ⑤ shows a visible gap** |
| **3** | ⚠ **the validator question — USER'S CALL, not decided** | `HsmGraphValidator.ValidateChannelSafety` has **no production caller** and the **wrong key shape**. Wire it up, or delete it? ⛔ Do not quietly do either |
| **4** | ⭐⭐⭐ **the EDITOR END-TO-END CHECK — the thing that started all this** | see §8.4 |

### 8.3 ⛔⛔ TRAPS THIS PROGRAMME PAID FOR — **do not re-derive these**

| trap | what it cost |
|---|---|
| ⭐⭐⭐ **ENUMERATE THE SNAPSHOT ROOTS before regenerating**, not the files your diff touched | `Hrot.Blueprints.Tests/Snapshots/` has **FIVE** roots — `DebugMap`, `Demos`, `Emit`, `Golden`, `Schedule`. Regenerating only the two the diff pointed at cost **a wasted 3 m 14 s gate run**. `ls <proj>/Snapshots/` first |
| ⛔⛔ **THE GOLDEN CORPUS CANNOT CATCH A `D-D1` REGRESSION** | `AiAssetCorpus` has **no Roslyn compilation**, so it cannot answer *"does this action declare `[WritesChannel]`"* and emits **no `OnExitId` at all**. ⇒ a break in `D-D1` moves **zero baselines**. `CE388_R8` is the ONLY check — and it reads ARTEFACTS *(baked id vs the dispatcher's `ActionTable`)*, never emitted text |
| ⚠ **`HsmFlattener:173` — the baked id WINS over the name** | `ExitActionId != 0 ? ExitActionId : hash(OnExitAction)`. ⇒ the builder **cannot** express "only if unset"; the fill-an-empty-slot rule lives at the EMIT site (`s.OnExitAction == null`). ⛔ Move it into the builder and authored cleanups get silently overridden |
| ⛔⛔ **the cleanup id is keyed on the SHORT method name** | `HsmActionGenerator.cs:565` emits `m.Name`; assets hold the FQN. `CE403_R2` pins it. ⛔ Hashing the FQN binds an id nothing registered — **no error anywhere**, the channel simply never releases |
| ⚠ **`[WritesChannel]` is opt-in and always will be** | an UNDECLARED C# writer is undetectable. ⭐ What the compiler CAN do is refuse to guess when it cannot classify a call at all — that is `IsComplete == false`, and it must never be treated as "writes nothing" |

### 8.4 ⭐⭐ THE EDITOR END-TO-END CHECK — **the original question, still unanswered**

🔒 **User:** *"a scenario with entity using hsm that calls blueprint guards and blueprint actions, to test it end to end."*

📐 **Measured gaps, and `CE-402` removed the one that made it impossible:**

| # | gap | state |
|---|---|---|
| ① | **no HSM asset names a blueprint activity** — `ActivityBlueprintName` is **zero** across all six `.hsm.json` | ⛔ OPEN. ⚠ **This is also why `D-B1` has no subject yet** |
| ② | **no scenario uses an HSM** — the four name only `MoveToLocation` / `FireAtTarget` / `PlatoonHillAttack` | ⛔ OPEN |
| ③ | nothing shipped would be VISIBLE — `HsmPolledGuardDemo` is 2 states and one bool | ⛔ OPEN |
| ④ | a blueprint channel command never survived a tick | ✅ **FIXED — `CE-402`** |

⭐⭐ **The build order, and step 1 needs no new C#:** `BuiltInChannelCommandCatalog.cs:73-87` already
ships `MoveTo`→Locomotion and `AimAndFire`→Weapon as palette nodes, so the activity blueprints are
authorable **in the editor** — which exercises `CE-385`/`386`/`387` at the same time.

1. an activity blueprint issuing `MoveTo` · 2. one issuing `AimAndFire` · 3. an HSM asset: state A
activity = the MoveTo blueprint, A→B polled + guarded by `HsmGuardDemo`, state B activity = the
AimAndFire blueprint · 4. a scenario copying `test-move` with `behaviorName` → the new HSM.

⚠ **`HsmPolledGuardDemo` + `HsmGuardDemo` already exist** and the guard half is proved end-to-end
(`CE398_R1`–`R3`). ⛔ **Running the editor needs the WINDOWS session** — not possible from cloud.

### 8.5 📐 GATE BASELINE at `6dcde3542`

| suite | |
|---|---|
| `Hrot.Blueprints.Tests` | **4048** passed / 18 skipped |
| `Fdp.Toolkits.Tests` | **2352** passed |
| `Hrot.Hsm.Editor.Tests` | **615** passed |
| `Hrot.AiEditor.Generators.Tests` | **342** passed |
| `Fhsm.Tests` | **335** passed |

⭐ Doc gates: `tracker-counts.py --check` *(open 112 / done 379 +1 refuted)* · `rulings-check.py`
*(38/38)* · `design-digest.py --check` · `mermaid-check.mjs`.

### 8.6 🔒 STANDING CONSTRAINTS — **still binding** *(and §4 above still applies)*

⭐ Push only to `behaviors`, `git push -u origin behaviors`, retry network errors 2/4/8/16 s ·
⛔ **never commit the stash** · ⭐ plain-chat questions, **never** the `AskUserQuestion` widget ·
⭐ GitHub blob links for every doc and task id, and gloss every id on first mention *(user is on
mobile)* · ⭐ run builds/tests/searches in the BACKGROUND · ⛔ no model identifier in any commit,
PR or repo artefact · ⛔ no PR unless explicitly asked ·
⚠ **cross-lane, STOP-and-report:** `Hrot/Runner/Hrot.SystemTests/`, `Hrot.IG.Tests`, `FDP/Engine/Fdp.Core`.

### 8.7 ⚠ OPEN ELSEWHERE, NOT OURS

⭐ `CE-389`..`CE-392` *(the §11 call-cost thread)* · `CE-395` *(priority bits — filed not fixed)* ·
`CE-393`/`CE-394` *(BACKEND lane)* · ⛔ `DESIGN_Resolver_World_Reach.md:346` carries a dead
`CgfCuratedBehaviorRegistrar` citation — flagged in `E8c`'s `known-conflict`, deliberately not edited.
