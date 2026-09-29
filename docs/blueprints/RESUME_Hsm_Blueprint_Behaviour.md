<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: **AN EDITOR-AUTHORED HSM AS AN
  ENTITY BEHAVIOUR, WITH BLUEPRINT ACTIONS AND GUARDS**. ⚠ A STATE doc, not canon: every
  "green"/"pushed"/"HEAD" line is a snapshot dated below. ⛔ VERIFY against git before acting
  ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-29
build-state: ✅ authoring programme COMPLETE. ✅ CHANNEL-LIFECYCLE programme COMPLETE, and the
  RUNTIME is PROVED END TO END live (§8.4): the demo scenario drives to a seeded destination and
  destroys a target. Built: CE-402/403/388(D-A2,D-B1,D-D1,D-F)/404(D-E WITHDRAWN)/405/406/407/
  409/411/412. ⏭ LEFT: three NEW authoring defects the live run exposed — CE-413 (an inert DTO
  field), CE-414 (offset-coupled param seeding), CE-415 (a dropped Vector3 pin default) — plus
  CE-408 (filed, unexercised), CE-410 (another lane), and the user's own editor pass.
  Branch `behaviors` @ 7deefb2b5. ⏭ NOW: the Q75 unification, §9 — approved, not started.
current-answer: ⭐⭐⭐ **START AT §12** — `Q76`-`B` is **APPROVED** (`2026-09-29`) and the design is
  `Q76` §12. §12 here is the handle; the OWNING document is
  Architect_Question_76_One_Blackboard_Block_Per_Primitive.md §12, whose §12.6 is the ordered slice
  list. §11 is the previous day's resumption state and its §11.3 "the open decision" is now CLOSED;
  §10 is the programme summary. ⚠ NOTHING IS BUILT YET — slice 1 is `CE-418`.
  ⛔ `Q76`-`D` (IHostVariableAccess.TryWrite) is NOT covered by the authorisation.
  ⚠ §8 is the CHANNEL-LIFECYCLE programme: COMPLETE, and its CE-413/CE-414 are BUILT (§9.1); read
  §8 only for §8.3's traps and §8.4's measured end-to-end result.
  (Historic: **START AT §8** — the CHANNEL-LIFECYCLE programme (`Q74`), which is where the
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
| branch | **`behaviors`**, pushed, at **`571f77c62`** |
| owning design | 📄 [`Architect_Question_74_Blueprint_Channel_Lifecycle.md`](Architect_Question_74_Blueprint_Channel_Lifecycle.md) — **§9 IS THE AS-BUILT; where §4 and §9 disagree, §9 WINS** |
| ⛔⛔ **the stash** | `stash@{0}` = *"EXPERIMENT: RootParamsBytes always 100 — probe only"*. 🔒 **A DIAGNOSTIC THAT MUST NEVER BE COMMITTED.** Leave it stashed |

```bash
git log --oneline -15 behaviors  # 2725b9fec back to 84808faef is this programme
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
| `CE-404` | ⭐⭐ **rail ⑤ MEASURED — there is no release-then-reclaim gap ⇒ `D-E` WITHDRAWN, not built.** `CE404_R5a`/`R5b` in `LocomotionDispatcherTests.cs` | *this batch* |
| `CE-406` | ⭐ **the channel-safety validator RE-AIMED for the auto-bind world** — skip a baked `ExitActionId`, match the short name as the dispatcher does, re-word the message. Kept as the test-suite helper its own doc specifies | *this batch* |
| `CE-405` | ⭐⭐ **a re-issued channel command no longer re-enters the executor** — the lowering bumps `ActionInstanceId` only when the action id, the params bytes or a `Failure` status say the command is genuinely new | *this batch* |
| `CE-402` | *(row only)* the tracker still read `[ ] OPEN` although the fix shipped in `2c320079c` — closed | *this batch* |
| `CE-407` | 🔴 **the derived cleanup thunk was a NO-OP** — the derivation skipped `Function`-kinded graphs while the emitter falls back to them, so every authored blueprint derived an EMPTY channel set with `IsComplete==true` and the channel leaked anyway. **Found by RUNNING it; no test could catch it** | `cb5c81f96` |
| `CE-408` | the `!= Running` re-activation reverted to `== Failure` — it stormed at **266 re-enters in 3 s** on a per-tick activity | `cb5c81f96` |
| `CE-409` | `BrainBlackboard` removed from the two scenarios still carrying it ⇒ **the shipped scenarios load again** | `088bb485b` |
| `CE-411` | **unknown-component strictness is now a per-host POLICY** — `Throw` by default, `WarnAndSkip` on the four cluster entry points only | `c02e29c43` |
| `CE-412` | the migrator P4 owed — `V2ToV3_RemoveBrainBlackboard` + reverse, `CurrentVersion`→3, and the **two drifted schema-version producers** pinned | `c02e29c43` |
| ⭐⭐ **the DEMO** | `hsm-channel-e2e` now genuinely **drives and shoots** — see §8.4 | `571f77c62` |

⭐ **The concrete result:** `HsmTwoChannelRegionsDemo` bakes `.OnExitId(26097)` / `.OnExitId(46817)`,
matching what the registrar registered for `ExitCleanup_Activity_DriveChannel` / `_FireChannel`.
**Those two channels stopped leaking on state exit.**

### 8.2 ⏭ WHAT IS LEFT — **in order**

🔒 **User `2026-09-28`, after compaction:** *"fix all to what it is meant to be."*
⭐⭐⭐ **`CE-413` and `CE-414` are DONE** *(commit `2725b9fec`)* — rows 1 and 2 below are kept as HISTORY
because the REFRAME is the useful part: `CE-414` as filed asked for the wrong fix.

| # | what | note |
|---|---|---|
| ~~**1**~~ | ✅ **`CE-413` DONE `2725b9fec`** — a guard blueprint seeds from its OWN variable. ⭐ The discriminator is the guard's ASSET GUID, so no FastHSM change was needed; the kernel stamps a guard with its SOURCE STATE and the site key separates them. 📄 `DESIGN_Occurrence_Scoped_Storage.md` §28.6c. ⛔ HISTORY of the filing: ⭐⭐ **`CE-413`** — `TransitionNodeDto.ExpressionTargetField` is **INERT**; a guard silently inherits the source state's param window | `HsmAssetDto.cs:198` declares it, `HsmBridgeEmitCore.EmitStateParamBindings:288-322` reads it **only from states**. ⇒ the guard's layout dictates what the state's window must start with. 📐 Cost a full debugging round: the guard read the first byte of `560.0f` = `0x00` = a permanent `false`, silently. ⭐ **Decide:** honour it on transitions, or DELETE the member so it stops reading as a capability |
| ~~**2**~~ | ✅ **`CE-414` DONE `2725b9fec`, and REFRAMED** — the fix was NOT a check at the seam. 🔒 User: *"those dto define the offsets … you were fighting with what should not need no fighting."* ⭐⭐ The HSM authoring path was missing the COMPOSE step the BTree path has had since `E2`: a blueprint pick now creates ONE `IsAutoManaged` variable typed from the blueprint's generated `Params`, so the seed is that variable and there is no window to check. ⚠ It also needed `HsmJsonGenerator` to compose the Option-A size fallback, without which the whole feature is silently inert. 📐 **Re-proved live:** the demo drives to its seeded destination and destroys the 500 HP target with the composed shape. ⛔ HISTORY of the filing: ⭐⭐ **`CE-414`** — a state's param seed is a **BYTE OFFSET**, so blackboard variable ORDER is load-bearing and nothing checks it | renaming/reordering/inserting a variable silently shifts every parameter of every state seeded at or after it. ⭐ **Candidate fix:** a compile-time name+type check at the seam — all the information is present at emit time |
| **1** | ⭐ **`CE-415`** — a `Vector3` **pin default** is silently discarded; only a WIRED value survives | ⛔ not an authoring typo: shipped `Loco1.bp.json` has the same shape and emits no `Destination` either. ⚠ **`Loco1` is presumably affected in production today** |
| **2** | ⭐ **`CE-408`** — a deliberate re-issue of an identical, COMPLETED channel command is swallowed | 📄 `Q74` §9.9. ⛔ Filed not fixed; **nothing exercises the hole today**. The principled fix is in the EMITTER (gated path vs per-tick body) |
| **3** | ⚠ **`CE-410`** — `POST /scenario/load/live` answers `ok:true` for a load whose 2PC prepare faulted | ⛔ **NOT this lane** — orchestrator / debug API |
| **4** | ⭐ **the EDITOR AUTHORING pass — the user's own** | ✅ The RUNTIME is proved (§8.4), and `CE-413`/`CE-414` mean the pick now composes the params variable for you. What remains is judging the authoring UX by opening these assets in the editor on Windows. ⭐ **`CE-413`/`414`/`415` are exactly what to watch for** |

⭐⭐ **The channel-lifecycle programme itself is COMPLETE.** Everything above is either a NEW defect
the live run exposed, another lane's, or the user's own editor pass.

### 8.3 ⛔⛔ TRAPS THIS PROGRAMME PAID FOR — **do not re-derive these**

| trap | what it cost |
|---|---|
| ⭐⭐⭐ **ENUMERATE THE SNAPSHOT ROOTS before regenerating**, not the files your diff touched | `Hrot.Blueprints.Tests/Snapshots/` has **FIVE** roots — `DebugMap`, `Demos`, `Emit`, `Golden`, `Schedule`. Regenerating only the two the diff pointed at cost **a wasted 3 m 14 s gate run**. `ls <proj>/Snapshots/` first |
| ⛔⛔ **THE GOLDEN CORPUS CANNOT CATCH A `D-D1` REGRESSION** | `AiAssetCorpus` has **no Roslyn compilation**, so it cannot answer *"does this action declare `[WritesChannel]`"* and emits **no `OnExitId` at all**. ⇒ a break in `D-D1` moves **zero baselines**. `CE388_R8` is the ONLY check — and it reads ARTEFACTS *(baked id vs the dispatcher's `ActionTable`)*, never emitted text |
| ⚠ **`HsmFlattener:173` — the baked id WINS over the name** | `ExitActionId != 0 ? ExitActionId : hash(OnExitAction)`. ⇒ the builder **cannot** express "only if unset"; the fill-an-empty-slot rule lives at the EMIT site (`s.OnExitAction == null`). ⛔ Move it into the builder and authored cleanups get silently overridden |
| ⛔⛔ **the cleanup id is keyed on the SHORT method name** | `HsmActionGenerator.cs:565` emits `m.Name`; assets hold the FQN. `CE403_R2` pins it. ⛔ Hashing the FQN binds an id nothing registered — **no error anywhere**, the channel simply never releases |
| ⚠ **`[WritesChannel]` is opt-in and always will be** | an UNDECLARED C# writer is undetectable. ⭐ What the compiler CAN do is refuse to guess when it cannot classify a call at all — that is `IsComplete == false`, and it must never be treated as "writes nothing" |

### 8.4 ✅ THE END-TO-END CHECK — **RUNTIME PROVED, AND THE DEMO NOW DRIVES AND SHOOTS**

🔒 **User:** *"a scenario with entity using hsm that calls blueprint guards and blueprint actions, to
test it end to end"* · *"i need to be sure the runtime part is OK."*

⭐⭐⭐ **It is. And the editor was never required** — a `.hsm.json` self-registers as a named behaviour
at build time, so a scenario's `behaviorName` references it directly.

| the subject | |
|---|---|
| `Assets/Blueprints/HsmDriveActivity.bp.json` | `HsmAction`; `GetAllParameters` → `VectorOps.Vec3` → **linked** into `MoveTo`'s `Destination` |
| `Assets/Blueprints/HsmFireActivity.bp.json` | `HsmAction`; `TargetNetworkId` → `NetworkEntityMapOps.ResolveTarget` → **linked** into `AimAndFire`'s `Target` |
| `Assets/HSMs/HsmChannelE2E.hsm.json` | `Driving` → `Firing`, POLLED, guarded by the `HsmGuardDemo` blueprint. Blackboard: `Open`, `DestX`, `DestY`, `TargetNetworkId` |
| `scenarios/hsm-channel-e2e/` | two Bradleys (`Open=false` / `Open=true`) + a static 500 HP target |

📐 **Measured** *(`--mode all` headless, **Scenario perspective** — ⚠ the AI runs on the CGF node;
SimHost shows no `BehaviorState`)*:

| t | `Open=false` | `Open=true` | target |
|---|---|---|---|
| 6 s | (401.6, 296) `loco 1/Running` | (403.1, 360) `loco 0` · `wep 1/Running` · ammo **293** | hp **300** |
| 18 s | (500.0, 296) `loco 1/Running` | (500.8, 360) `loco 0` · `wep 1/Running` · ammo **278** | hp **0** |

✅ Every clause: HSM as behaviour · a blueprint-guarded POLLED transition reading a blackboard
variable · a blueprint action ticking per state · one action per channel · the channel released on
exit. **The `Open=true` entity destroys the target.**

⚠ **Two caveats worth knowing before reading anything into it:**
⭐ the `Firing` entity **keeps rolling** after releasing locomotion — clearing the channel does not
retract the nav intent `MoveTo` already latched *(correct per the channel design)*; and
⭐ `NetworkIdentity` is **REASSIGNED at load** *(authored 2050, runtime 1002, by entity order)*, so
`TargetNetworkId` names the assigned id.

⭐⭐ **THE PARAM-SEEDING CHAIN, which is what `CE-413`/`CE-414` are about:**
`behaviorParams` → HSM blackboard variable → blueprint `Parameter` *(seeded as a **byte window** from
the state's `ExpressionTargetField` offset)* → `GetAllParameters` → `Vec3`/`ResolveTarget` →
**LINKED** into the command pin. ⛔ The link is load-bearing — a `PinDefault` is dropped for a
`Vector3` (`CE-415`).

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

---

## 9. ⭐⭐⭐ THE PARAMS-PIPELINE / ACTION-BINDING UNIFICATION — **`Q75`, APPROVED, NOT STARTED** *(`2026-09-28`)*

> ⭐⭐⭐ **RESUMING? START HERE, NOT AT §8.** §8's channel-lifecycle programme is COMPLETE, and the three
> defects it left — `CE-413`, `CE-414` — are BUILT (§9.1). What remains from §8 is only `CE-415`,
> `CE-408`, `CE-410` and the user's editor pass. **This section is the live programme.**

### 9.0 📐 STATE, verifiable in one command

| | |
|---|---|
| branch | **`behaviors`**, pushed, at **`7deefb2b5`** |
| owning design | 📄 [`Architect_Question_75_One_Params_Pipeline_And_One_Action_Binding.md`](Architect_Question_75_One_Params_Pipeline_And_One_Action_Binding.md) — `build-state: DESIGN`, **approved, nothing built** |
| tracker | [`CE-416`](Blueprint_Issues_Tracker.md) *(the pipeline — slices `S1`–`S4`)* · [`CE-417`](Blueprint_Issues_Tracker.md) *(the carrier — slice `S5`)* |
| ⛔⛔ **the stash** | `stash@{0}` = *"EXPERIMENT: RootParamsBytes always 100 — probe only"*. 🔒 **A DIAGNOSTIC THAT MUST NEVER BE COMMITTED.** Leave it stashed |

```bash
git log --oneline -6 behaviors   # 7deefb2b5 back to 2725b9fec is the CE-413/414 + Q75 work
git stash list                   # must still show the RootParamsBytes probe
```

### 9.1 ✅ BUILT AND PUSHED SINCE §8 WAS WRITTEN

| id | what | commit |
|---|---|---|
| `CE-414` | ⭐⭐⭐ **the HSM authoring path was missing the COMPOSE step the BTree has.** A blueprint pick now creates ONE `IsAutoManaged` variable typed from the blueprint's generated `Params`; the seed is that variable, so there is no byte window and nothing to check. Re-proved live | `2725b9fec` |
| `CE-413` | a transition guard seeds from its OWN variable — the site key is the guard's asset Guid, so **no FastHSM change** | `2725b9fec` |
| — | **shared:** `AutoManagedVariables` *(one lifecycle, both hosts — 63 insertions / 138 deletions across five sites)* + `AiPrimitiveNaming` | `185cf45f1` |
| — | the managed-blackboard flip is **not** BTree-specific *(it moved into the shared compose)*; `Open` → `EngageTarget` | `54570bde0` |
| `Q75` | the design, the two rows, reciprocal links, and the `G1` correction in `DESIGN_Parameter_Model.md` §6 | `bd56ad2ba` · `7deefb2b5` |

### 9.2 🔒 THE APPROVAL, AND WHAT IT DOES *NOT* MEAN

> 🔒 **User, `2026-09-28`:** *"approved, but just by trusting your judgement, not because i understand
> all the internals."*

⛔⛔ **Read this before citing "approved" as cover for anything.** All four decisions (`A`–`D`) are
approved, so the programme may start — ⚠ **but the approval carries no independent verification of the
internals.** ⇒ ⭐⭐⭐ **the design's own rails (`Q75` §6) and the user's editor pass are the ONLY checks
this programme has.** ⛔ *"The user approved it"* is not an argument in a later disagreement about a
mechanism; re-measure instead.

### 9.3 ⛔ THE PLAN — **five slices, `Q75` §5** ⚠⚠ **SUPERSEDED BY §9.6 — do NOT start at `S1`**

| # | slice | depends | ⚠ |
|---|---|---|---|
| **S1** | make `EmitBlackboardStructSource` **host-neutral** *(it reads only 3 things from the BTree DTO)*, then the HSM emits `{Asset}_Blackboard` from its existing `BlackboardTypeName`; set `JsonParamsDtoType` + `BlackboardLayoutType` | — | additive; HSM goldens move |
| **S2** | ⭐⭐ **widen the factory to `FromJson<TJson, TLayout>(convert)` FIRST** — §9.4 ① — then route the three emitted producers through it with the identity conversion | S1 | rail: byte-for-byte equality before/after |
| **S3** | split the five `[BehaviorResolver]` methods into their conversion halves | S2 | 🔴 the risky one — the geo shapes |
| **S4** | D's three states: identity parse auto-generated for ②, ① left alone, warn on the `JoinFormation` shape | S3 | ⭐ mostly REMOVES author obligations |
| **S5** | `BehaviorActionBinding` + the file-format migrator + per-slot `ExpressionTargetField` + the `(childAssetId, slotKind)` site key | — | 🔴 ~83 refs, whole-corpus golden move. ⛔ **independent of S1–S4 and must NOT be interleaved** |

### 9.4 ⛔⛔ THE TRAPS — **measured, do NOT re-derive**

| # | |
|---|---|
| **①** | 🔴🔴 **`BehaviorParams.FromJson<TDto>` CANNOT express the two-shape case** — it writes the type it deserialized (`Unsafe.Write(memory, dto)`), while `MoveToLocation` authors `[lat, lon]` and stores cartesian. ⇒ **S2 must widen it before routing anyone through it**, or S3 cannot land for three of the five |
| **②** | **`G1` is NOT "partly done"** — `FromJson<TDto>` has **`callers_total: 0`** in production *(graph-measured, exact)*. Five producers exist and none uses it. `DESIGN_Parameter_Model.md` §6's row is corrected |
| **③** | ⭐ **no curated resolver reads the world during DESERIALIZE** — `Resolve*` is the dependency-fetch half, `Parse*` the conversion half. S3 is a deletion, not a restructure |
| **④** | 🔴 **`DelegateShape` is BTree-only** *(interpreter arities; the HSM has one dispatch signature)*, and **value 2 has no named member**. ⇒ the shared carrier does NOT carry it, and S5 must name value 2 first |
| **⑤** | ⭐ **`BlackboardTypeName` IS populated on all 7 HSM assets** — S1 needs no naming decision |
| **⑥** | ⚠ **`JoinFormation` is not a bug.** Its layout declares two fields its `[BehaviorContract]` deliberately does not — a reserved-for-later shape, carved out in `AGeneratedBehaviourAdvertisesItsManifestTests`. It gets a WARNING. ⛔ An earlier chat claim that its params are "silently dropped" **overstated it** |
| **⑦** | 🔒 **`R-132` BINDS S4** — an auto-generated identity parse must never outrank a declared `[BehaviorResolver]`, and the precedence must be decided at GENERATION time. ⛔ **Never `if (ParseParams == null)`** — that is the exact shape `R-132` was filed against, and its case was `PlatoonHillAttack` driving to `(0,0)` with no exception and no log line. 📄 `Q75` §D.1 |
| **⑧** | ⛔ **an HSM asset with an unmanaged blackboard emits nothing SILENTLY** where the BTree raises `BTREE0002`. The missing diagnostic rides with `CE-416` |

### 9.5 ⏭ STILL OPEN FROM §8 — **not part of this programme**

`CE-415` *(a `Vector3` pin default is discarded; `Loco1` presumably affected in production)* ·
`CE-408` *(the deliberate-repeat hole, filed and unexercised)* · `CE-410` *(another lane)* ·
⭐ **the user's editor authoring pass on Windows** — now with the compose step in place.

---

## 9.6 🔴🔴 THE SECOND MEASUREMENT PASS — **`Q75` REOPENED, `S1`/`S2` BLOCKED** *(`2026-09-28`)*

> 🔒 **User, verbatim:** *"revise the architect question, measure rather than rushing to
> implementation. use codebase memory, not just grep."*

⭐⭐⭐ **RESUMING? THIS SECTION REPLACES §9.3.** Nothing was built; the plan changed before it started.
📄 The revision lives in [`Q75` §0](Architect_Question_75_One_Params_Pipeline_And_One_Action_Binding.md)
and that is the one section to read there first.

### 9.6.1 📐 What the re-measurement found — **two things, both blocking**

| # | finding | where |
|---|---|---|
| ⭐⭐⭐ **①** | **The corpus has TWO offset authorities and they already disagree.** `ManagedBlackboardVariables[i].ByteOffset` *(from `BTreeBlackboardPackHelper.Pack`)* vs the CLR's layout of `BlackboardLayoutType`. 📐 **15 generated behaviours carry both; 3 disagree; 9 of 31 fields.** Cause: `Pack` derives alignment from **size** — `Math.Min(size,8)` — so `Vector3` *(12 bytes, aligned 4)* lands on 8; and an empty `Params` is 0 to `Pack`, 1 to the CLR. ⛔⛔ **LIVE:** StructEdit's *"Active Parameters"* **reads and writes** at the struct's offsets and the replay predicate compiler binds against them, while the ImGui render uses the manifest — **the two panels disagree about one entity.** ⇒ `CE-418` | `Q75` §2.5 |
| ⭐⭐⭐ **②** | **A live design on this lane changes the SAME emit site at the OPPOSITE grain.** [`DESIGN_Per_Variable_Param_Resolver.md`](DESIGN_Per_Variable_Param_Resolver.md) *(`E8c`)* adds a per-VARIABLE Step 3 to `EmitParseParamsLocal` ×2; `Q75`'s `S2` **deletes those two lambdas**. Both `build-state`-marked the same day, neither naming the other. ⇒ `CE-419` | `Q75` §2.6 |

⚠ **And a third, smaller:** `BehaviorParams.FromJson` has **no bake stage** — it is
`Deserialize → resolve → Unsafe.Write`. ⇒ routing an emitted producer through it as-is **discards
every authored default silently** *(`Q75` §5.1c)*. ⭐ The good news measured alongside it: the two
**wire formats already agree**, because the generated struct declares one field per variable named
by the variable. ⇒ the JSON contract survives the move; the defaults and the offsets do not.

### 9.6.2 ⏭ THE PLAN NOW

| # | | ⚠ |
|---|---|---|
| ⭐⭐⭐ **`S0`** | **one layout authority** — emit the struct with explicit `[FieldOffset]` taken from `Pack`, so the struct **becomes** the manifest. Write the parity rail **first** and watch it go red on today's tree | ⭐ **zero runtime bytes move**; 17 BTree goldens do. Fixes `CE-418` |
| ⛔ **`PRE`** | ⛔ **not a slice — the joint grain decision with `E8c`**, taken once, with the user | ⛔ blocks `S2`/`S3` *(and `E8c`'s `D3`)*; does **not** block `S0`/`S1` |
| **`S1`**…**`S4`** | as `Q75` §5, but `S1` now depends on `S0` and `S2`/`S3` on `PRE` | `S1` emits for **4** HSM assets, not 7 — the other three are unmanaged with zero variables |
| **`S5`** | unchanged and still independent | — |

### 9.6.3 ⭐ What did NOT move — **so the re-measurement is legible**

✅ Decisions **A**, **B**, **D** stand. ✅ `§2.1`'s five producers · `§2.2`'s schema table ·
**`FromJson<TDto>`'s zero production callers** · `§5.1a`'s two-shape hole · `§5.1b`'s `DelegateShape`
decision — **all re-checked, all still true.** 🔴 **C is reopened**, and a new decision **E**
precedes it.

### 9.6.4 ⛔⛔ THE HONEST NOTE ABOUT THE APPROVAL

⚠ §9.2 said the approval was on trust and carried no verification of the internals. 📐 **The second
pass is what that warning was for, and it found the plan wrong in two places within one session.**
⇒ ⛔ **Do not treat any remaining "approved" decision as measured** — A, B and D survived a
re-measurement, which is a different and stronger claim than "the user approved them".

---

## 10. ⭐⭐⭐ THE STORAGE SIMPLIFICATION — **`Q76`, PARKED ON PURPOSE, ONE DECISION OPEN** *(`2026-09-28`)*

> 🔒 **User:** *"not approved, i want it recorded so i can return to it any time."*
> ⛔⛔ **Nothing is in flight. Nothing is to be started — including `Q75`'s `S0`.** This section
> exists so the question can be picked up cold, without this session's conversation.

📄 **THE DOCUMENT:** [`Architect_Question_76_One_Blackboard_Block_Per_Primitive.md`](Architect_Question_76_One_Blackboard_Block_Per_Primitive.md)
⭐ **Its §10 is the way back in** — the one open question, everything already measured, and what
happens to each waiting finding under either outcome. ⛔ Do not re-derive any of it.

### 10.1 ⭐ The single open question

> **Adopt `B` — one contiguous blackboard block per running AI primitive, root and children alike?**

⭐ `A` *(retire cross-entity shared memory)* and `C` *(retire `WorkingStateScope`)* are **RESOLVED —
remove** — ⛔ **but both are sequenced INSIDE `B` and are INERT without it.** `A` standalone is
~200 lines and parks under the user's own rule; `C` is impossible until occurrence identity supplies
the keying. ⇒ **one decision, not three.** `D` and `E` are subordinate leans.

### 10.2 📐 Why it got here — the short version

⭐ The user asked why the storage model felt over-complex. Measured: the **root** behaviour is split
across ≥2 slots while a **hosted child** is one; **two key schemes** exist for one concept; `Node`
scope *(the default)* has **0** users and silently provisions nothing; and the shared-memory feature
built on `Entity` scope has **one** cross-entity consumer — its own proof asset. 📌 **`Q76` §9**
records that the author's *"this is incomprehensible"* arrived on `2026-07-16`, **the day after the
feature shipped**, and was triaged as a documentation gap. ⇒ 🔒 **`R-150`**.

### 10.3 ⛔ The one finding that does NOT wait for the decision

🔴 **[`CE-418`](Blueprint_Issues_Tracker.md) — two offset authorities, and they already disagree**
*(3 of 15 generated behaviours, 9 of 31 fields, runtime-probed)*. ⭐ It is a **live** defect in two
user-facing surfaces — StructEdit's typed params editor and the replay predicate compiler both read
the struct's offsets while the runtime writes the manifest's — ⭐ it **blocks `Q75`**, and ⭐ **its
fix is identical whether or not `B` is adopted.** ⇒ **the one thing worth doing first, when work
resumes.**

⭐ The rest sit: `CE-420` *(a `Role=State` default is silently dropped)* · `CE-421` *(the shadow
carries the previous behaviour's bytes across a switch)* · `CE-422`/`CE-423` *(both **dissolve** if
`B` is taken — the scopes they describe stop existing)* · `CE-424` *(the seam header claims it is
unimplemented; it shipped)*.

### 10.4 ⚠ And `Q75` is not where it was

📄 [`Architect_Question_75`](Architect_Question_75_One_Params_Pipeline_And_One_Action_Binding.md)
went back from `READY-TO-BUILD` to `DESIGN` on `2026-09-28` *(§9.6)*, and `Q76` §4-`E` rules that
**`Q75` DEPENDS on `Q76`**: `Q75`'s `S0` is `Q76`'s first step, and its `S1`/`S2` assume one answer
to *"where is this variable"*. ⛔ **Do not resume `Q75` at `S1`.**

---

## 11. ⭐⭐⭐ RESUMPTION — **`2026-09-29`, written before a compaction**

> 🔒 **User:** *"pls write resumption document and let me compact before we continue analyzing and
> verifying before implementation."*
> ⇒ ⭐⭐ **The next phase is ANALYSIS AND VERIFICATION, not building.** ⛔ Nothing is authorised.

### 11.0 📐 STATE, verifiable in three commands

| | |
|---|---|
| branch | **`behaviors`**, pushed |
| working tree | clean |
| ⛔⛔ **the stash** | `stash@{0}` = *"EXPERIMENT: `RootParamsBytes` always 100 — probe only"*. 🔒 **A DIAGNOSTIC THAT MUST NEVER BE COMMITTED.** Leave it stashed |
| built this session | ⛔ **NOTHING.** Documents and tracker rows only |

```bash
git log --oneline -14 behaviors   # the whole session is docs
git stash list                    # must still show the RootParamsBytes probe
git status --short                # must be empty
```

### 11.1 ⭐ WHAT THIS SESSION DID — **measured, then recorded; no code**

⭐ It began as *"revise `Q75`, measure rather than rushing"* and became a storage-model
investigation, because the measurements kept contradicting the design record.

| | |
|---|---|
| 📄 **`Q75`** | went **back** from `READY-TO-BUILD` to `DESIGN` *(§9.6)*. Its `C` is reopened; `S1`/`S2` are blocked |
| 📄 **`Q76`** *(NEW)* | [`Architect_Question_76_One_Blackboard_Block_Per_Primitive.md`](Architect_Question_76_One_Blackboard_Block_Per_Primitive.md) — one contiguous block per running AI primitive. **`A` and `C` RESOLVED (remove); `B`, `D`, `E` are unapproved leans.** §10 is the way back in, **§11 is the fully designed `S-SUB` slice** |
| 📄 **the baseline** | [`EXPLAINER_Where_Parameters_And_State_Live.md` §7](EXPLAINER_Where_Parameters_And_State_Live.md) — **the as-measured storage picture**, written at the user's request as the basis for further decisions |
| 🔒 **`R-150`** | *complexity an author must understand is a cost; an unadopted capability is not a neutral one* |
| 📋 **filed** | `CE-418` `CE-420` `CE-421` `CE-422` `CE-423` `CE-424` |

### 11.2 ⛔⛔ THE ONE THING THAT DOES NOT WAIT

🔴 **`CE-418` — two offset authorities, and they disagree.** `Pack` derives alignment from **size**
*(`Math.Min(size,8)`)*; the CLR aligns by **type**. 📐 Runtime-probed: **3 of 15 generated
behaviours, 9 of 31 manifest fields.** ⛔ **Live** in StructEdit's typed params editor *(reads AND
writes)* and the ReplayBrowser predicate compiler, while the ImGui render uses the manifest ⇒ **two
panels disagree about one entity.** ⭐ It blocks `Q75`, and **its fix is identical whether or not
`Q76`-`B` is adopted.** ⚠ Still not authorised — but it is what to propose first.

### 11.3 ⏭ THE OPEN DECISION, AND ITS SHAPE

> **Adopt `Q76`-`B` — one contiguous block per running AI primitive, root and children alike?**

⭐ `A` *(retire cross-entity shared memory)* and `C` *(retire `WorkingStateScope`)* are decided
**REMOVE** — ⛔ **but both are sequenced INSIDE `B` and are INERT without it.** ⇒ **one decision.**
⭐ **`S-SUB` (`Q76` §11) is the natural pilot** — its BTree arm needs neither `S1` nor the scope work.

### 11.4 ⛔⛔ THE TRAPS — **measured; do NOT re-derive**

| # | |
|---|---|
| **①** | ⛔ **I corrected myself FIVE times this session**, each time after the user pushed: the lazy/eager claim, the per-variable framing, and `Entity` scope **three times**. ⇒ ⭐⭐ **do not trust a summary of this area — including §7's — without the file:line it cites.** §7 carries them |
| **②** | ⭐ **`Entity` scope is cross-ENTITY coordination by name**, not cross-behaviour persistence and not host/subtree sharing. Its key folds **the variable name and nothing else** |
| **③** | ⭐ **The declared blackboard is EAGER** — `ProvisionStatefulSlots` at ingress. Only a **hosted occurrence** is lazy, and **identity forces it, not allocation** |
| **④** | ⭐ **Parse runs BEFORE provisioning** *(parse → commit → provision)*, which is why a resolver cannot write working state today |
| **⑤** | ⭐ **The two hosting paths disagree on freshness** — a subtree **aliases** the host's params every tick; an AiPrimitive holds a **snapshot**. 📄 `S-SUB` fixes it and §11.4 there names it as a behaviour change |
| **⑥** | ⛔ **`tracker-counts.py --check` counts `BP-` rows ONLY** — it verified none of `CE-418`…`CE-424`. Known gap, `CE-073`. ⚠ Do not report it as if it gated them |
| **⑦** | ⭐ **`PlatoonHillAttack2` is KEPT** *(`Q76` §6)* — the six `HillAssault2I_*` graphs are deletable duplicates, but the tree is the only 7-primitive composition demonstrator |
| **⑧** | ⛔ **`IHostVariableAccess`'s header forbids a write path** *("a second supply mechanism")*. `Q76` §D.1 argues the narrowing; ⚠ **it is an argument, not a settled point** |

### 11.5 ⏭ WHAT "ANALYSING AND VERIFYING" SHOULD COVER NEXT

⭐ The user's stated next phase. The open measurements, in the order they matter:

| | |
|---|---|
| **1** | 🔴 **the six `HillAssault2I_*` graphs' WRITE patterns** — can the host own the mutation *(children return values, host writes)*? ⇒ **decides `Q76`-`D`**, and if yes `TryWrite` is unnecessary and `IHostVariableAccess`'s rule survives intact |
| **2** | ⚠ **whether anything writes a root param at RUNTIME** other than StructEdit ⇒ sizes trap ⑤'s real-world impact |
| **3** | ⚠ **`CE-418`'s blast radius on the goldens** — 17 BTree `*.Blackboard.g.cs` move under `S0`; confirm nothing else keys on those offsets |
| **4** | ⭐ **the `E5`/`E6` suites** — the regression net for `S-SUB`; confirm they cover HSM-hosts-BTree and BTree-hosts-BTree before touching either |

### 11.6 🔒 STANDING CONSTRAINTS

⭐ Push only to **`behaviors`** *(`git push -u origin behaviors`, retry network errors 2/4/8/16 s)* ·
⛔ **never commit `stash@{0}`** · ⭐ questions in plain chat text, **never** the question widget ·
⭐ docs and task ids as **GitHub blob links on `behaviors`**, and **gloss every id on first mention**
*(the user reads on mobile)* · ⭐ run builds/tests/searches **in the background** · ⛔ no model
identifier in commits or repo artefacts · ⛔ **do not create a PR** unless asked ·
⚠ **cross-lane STOP-and-report:** `Hrot/Runner/Hrot.SystemTests/`, `Hrot.IG.Tests`,
`FDP/Engine/Fdp.Core`.

---

## 12. ✅✅ **APPROVED — ONE BLACKBOARD BLOCK PER RUNNING BEHAVIOUR** *(`2026-09-29`)*

> 🔒 **User, verbatim:** *"whatever leads to this single-blackboard-slot-per-running-behavior is
> authorized"* · *"brain state was never part of the blackboard so it is ok to be in another slot."*

📄 **THE DESIGN IS [`Q76` §12](Architect_Question_76_One_Blackboard_Block_Per_Primitive.md)** — who
defines the block's DTO, and how parameters reach it. ⛔ Do not re-derive it; this section is the
handle, not the content.

### 12.1 ⭐ What changed from §11

| | |
|---|---|
| §11.3's "the single open decision" | ✅ **CLOSED — `B` approved.** `A` *(retire cross-entity shared memory)* and `C` *(retire `WorkingStateScope`)* stop being inert |
| the brain state | ⭐ **scoped OUT of the blackboard by the user** — it keeps its own slot. `Q76` §12.0 ② records why that is the *simplifying* answer: an HSM instance's width is a RUNTIME value from the blob |
| ⛔ `Q76`-`D` | **still NOT approved.** The grant is a resolver writing its OWN block; `D` is writing its HOST's. `Q76` §12.0's warning and §D.1 both stand |
| build-state | ⭐ `Q76` is now **READY-TO-BUILD**. ⛔ **nothing is built** |

### 12.2 ⏭ The slices, in order — 📄 `Q76` §12.6 carries the detail

`CE-418` *(one layout authority — still slice 1, still a live defect)* → `CE-425` *(emit the two-part
struct, Input first and byte-identical)* → `CE-429` *(size from the DTO, so no `Role=Input` variable
is legal)* → `CE-426` *(one bake→supply→resolve helper; closes `CE-420` for free)* → `CE-427`
*(widen the seam to the whole block, one resolver registry)* → `CE-431` *(`S-SUB`, the pilot)* →
`CE-428` *(bind a blueprint `Construction` graph as a resolver)* → `CE-430` *(the one hand-written
behaviour)* → `A` + `C`.

### 12.3 ⭐⭐ The three findings §12 added, which §11 did not know

| | |
|---|---|
| **①** | ⭐⭐⭐ **A blueprint-authored resolver already ships and is bound to NOTHING.** `GraphKind.Construction` is authorable, purity-checked, compiled and published in `BlueprintDefinition.Resolvers` — and that index is read by **no production code**, only 3 test files *(measured `2026-09-29`)*. ⇒ the user's *"not exactly sure how if the resolver is for non-blueprint behavior"* has a clean answer: the graph lives in a **Library** asset, so it was never tied to the behaviour's kind. `CE-428` is one binding |
| **②** | ⭐⭐ **The editor must NOT emit the struct.** `BlackboardDtoEmitter` has **0** production callers; the build-time generator is the sole producer and should stay so *(`R-132`)* |
| **③** | ⚠ **`Q76` §12.1 supersedes §11.3 ①** on the subtree payload: `[cursor][one blackboard struct]` — **two** regions, served unchanged by the shipping `OccurrenceWorkingState.ResolveOrAttach<,>` with the cursor at the base |
