<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: **AN EDITOR-AUTHORED HSM AS AN
  ENTITY BEHAVIOUR, WITH BLUEPRINT ACTIONS AND GUARDS**. ⚠ A STATE doc, not canon: every
  "green"/"pushed"/"HEAD" line is a snapshot dated below. ⛔ VERIFY against git before acting
  ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-27
build-state: ✅ **STAGE 1 COMPLETE AND PUSHED** — `CE-381`, `CE-382`, `CE-383`, `CE-384` (+ `CE-376`
  cleared first). Branch `behaviors`, HEAD `d760b25db`. ⏭ **NEXT IS STAGE 2 — `CE-385`, `CE-386`,
  `CE-387`** (§2), which makes the runtime reachable from the EDITOR rather than only from
  hand-written JSON.
current-answer: ⭐⭐⭐ **START AT §0** (three commands), then **§2** (what to build, with the
  measurements ALREADY MADE — ⛔ do not re-derive them). §3 is the corrections that must not be
  re-inherited. §4 the standing constraints. §5 the gate baselines. §6 what is open and NOT ours.
known-rot: nothing yet.
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
