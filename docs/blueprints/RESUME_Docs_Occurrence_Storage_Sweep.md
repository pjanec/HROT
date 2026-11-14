<!--STATUS
state: LIVE
doc-type: RESUMPTION for the DOCS lane that rewrote the corpus onto occurrence-scoped storage.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-23
build-state: n/a — a documentation lane. The owning design is
  DESIGN_Occurrence_Scoped_Storage.md (behaviors lane); this lane never edits it.
current-answer: ⭐ START AT §6. Wave 1 (the blackboards) and wave 2 (O7c — the brain-state
  components and the tick merge) are both DONE and pushed. §1 is what shipped, §2 the measured
  facts, §3 the traps, §4 the code findings handed to `behaviors`, §5 what is left.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — owns the storage model this lane wrote the docs
    against. It wins on any disagreement. ⛔ behaviors lane owns the file; do not edit it here.
  - RESUME_Occurrence_Storage.md — the behaviors lane's own build resumption. Read it for what is
    left in CODE; this doc covers what is left in DOCS.
  - PLAN_Occurrence_Storage_Build.md — that lane's build plan. Excluded from this lane's sweep on
    purpose: it names every retired token because that is what it is FOR.
-->

# RESUME — the docs sweep onto occurrence-scoped storage

> **Branch:** `claude/blueprint-macro-feature-sdmspn` · **pushed, tree clean**
> **Merged from `behaviors` at `3642ce1c8`** *(the merge commit is `65b6c03e2` — ⚠ read §3's note
> on it before assuming this branch was ever in sync before that)*

> 🔒 **The instruction this lane serves, in the user's words:** *"update them to the new state
> where these two do not exist at all and are replaced with occurrence slot. The docs should not
> mention them anymore, the old concept should not be left as superseded, it should be replaced
> with new state."* — plus two extensions: *"there will be no fixed cap eventually"* and *"there
> will be no brainhsm64 and similar ecs components eventually either."* ⭐⭐ **Both extensions have
> now HAPPENED**, which is what wave 2 was.

---

## 1. ✅ WHAT SHIPPED — **do not redo this**

### Wave 1 — `2026-09-22`, the blackboards

| commit | |
|---|---|
| `b31a73d41` | the main rewrite — 112 `docs/` files, 733 → 60 matching lines, + 10 SVGs |
| `30089290a` | five false claims the first pass asserted without measuring |
| `c3dcafbb7` | the 100-byte cap was NOT retired then — it was live in six places |
| `b15588feb` | re-weight: the model leads, the surviving constant is a caveat |
| `5903e1b8b` | finish `Blackboard_Authoring` — my own pass had left it half-converted |
| `2f267a080` | the brain-state components are going too — lead with that |
| `d5901afc4` | close wave 2 — a DEBT tally its own flip left behind |
| `e4b7fb45b` | `CE-307`/`CE-314` landed ⇒ my caveats became the stale part |
| `69294199f` | the explainer family had gaps the token sweep never reached |
| `bcff032ef` | the wide sweep — 51 files, 192 hits triaged |
| `30a6bc82d` | this doc, and the sweep script promoted out of `/tmp` |

### Wave 2 — `2026-09-23`, `O7c`: no brain component left

| commit | |
|---|---|
| `65b6c03e2` | ⭐ **the merge** — 217 commits from `behaviors`, 11 conflicted docs *(§3)* |
| `e897b2669` | `AI_DEV_GUIDE` + `DESIGN_Role_Affinity_Ownership` — the two structural ones |
| `ed1b7888b` | fifteen docs that named two tick systems, incl. the RUNBOOK's lost field |
| `496d0f2f1` | `docs/projects/**` — the assembly censuses |
| `2e9967b52` | `DESIGN_Hsm_Storage_Model`, `Q50`, `Q52`, the translator rename |
| `01d58f745` | the roadmap's M6 code sample + the binding status doc |
| `a89eac090` | `R-39` back to green, and a section titled *"Current State"* that was not |

⚠⚠ **The `docs/designs/**` rewrite has NO commit of its own.** It was produced by a subagent while
I was committing with `git add -A docs/`, so it is spread across `ed1b7888b`, `496d0f2f1` and
`01d58f745`, whose messages do not mention it. ⛔ **Do not read those messages as the scope of
those commits.** *(Cause: running `add -A` on a tree a subagent is writing into. Stage explicit
paths when an agent is live.)*

### ⭐ The vocabulary, applied throughout — **use it for any new doc**

| the retired thing | what to write |
|---|---|
| `BrainBlackboard.BehaviorParameters` | **the root params slot**, located by `RootParamsAccess`, keyed `OccurrenceSlotKey.ComputeRootParamsKey(BehaviorState.ActiveBehaviorHash)` — **computed, never stored** |
| `Blackboard1024` *(AiPrimitive state)* | **node working-state slots**, keyed `{fqn}@{offset}@{slotKey}` |
| `BrainBTreeState` | the **root tree-state slot**, `RootStateAccess` — a constant 64 B, `sizeof(BehaviorTreeState)` |
| `BrainHsm64` / `BrainHsm128` | the **root HSM instance slot**, `RootHsmAccess` — width **64/128/256 at RUNTIME**, chosen by `HsmInstanceManager.SelectTier` at attach, stored in the slot's guard field |
| `BTreeTickSystem` + `HsmTickSystem<T>` | **`BrainTickSystem`** — one system, two arms, `BehaviorState.BrainTier` selects |
| `BrainBlackboardTranslator` | `BrainDiagnosticsTranslator`, DOM key `BrainDiagnostics` *(`CE-317`)* |
| the 100-byte cap / heavy tier / spill | **gone** — one region per occurrence, bounded by the tier the allocator seats it in |
| `Interpreter<BrainBlackboard,…>` | `Interpreter<byte,…>` — `byte` is slot byte 0 |
| bytes 126/127 | `BrainInterrupts`, as named fields |

---

## 2. ⭐⭐ MEASURED FACTS — **the ones that cost the most to establish**

### 2.1 ✅ EVERY BRAIN COMPONENT IS GONE — and the ids are BURNED

📐 `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`. No `struct` declaration survives for any of them.

| component | id | retired by |
|---|---|---|
| `BrainBlackboard` | **23** | `P4` |
| `BrainBTreeState` | **29** | `O7c`-② |
| `BrainHsm64` | **35** | `O7c`-① *(zero production attach sites — its query could never match)* |
| `BrainHsm128` | **36** | `O7c`-④d |
| `Blackboard1024` | **74** | `P4`-① |

⚠⚠ **`_RESERVED`, never reused** — a stale recording or replayed stream must not bind an id to a
different component. ⭐ Worth stating wherever a doc lists component ids.

### 2.2 🔴 THE TIER LADDER — **still the single most-repeated error in the corpus**

📐 `BlueprintTierLadder.cs:92-129` *(payload = total − header **32** − `MaxSlots` × 16)*:

| tier | MaxSlots | payload |
|---|---|---|
| `BlueprintBlackboard256` | 3 | **176** |
| `BlueprintBlackboard1024` | **12** | **800** |
| `BlueprintBlackboard4096` | **16** | **3 808** |
| `BlueprintBlackboard16384` | 16 | **16 096** |

⛔ `4/8/16` and `928/3936/16368` are the **pre-`B3②`** figures. ⭐ **FOUR** tiers; any
`{1024,4096,16384}` list is stale. ⚠ The header is **32**, not 16 — `docs/projects` had it wrong.

### 2.3 ⛔⛔ WHAT IS STILL LIVE AND MUST NOT BE SWEPT

📌 **I added these to the sweep pattern, got ~100 extra hits, measured them, and took them back
out.** Sweeping them would have deleted live API from the docs.

| identifier | where it lives |
|---|---|
| `SharedAiHeavyActionAttribute` / `SharedAiHeavyConditionAttribute` | `Fbt.Kernel/SharedAiAttributes.cs:72,148`, read by `HsmActionGenerator.cs:72-73` |
| `HeavyDtoType` *(a property)* | `SharedAiAttributes.cs:99,167` · `Fhsm.Kernel/Attributes/HsmDefinitionAttribute.cs:26` |
| `MaxInlineBytes` = **16 096** · `InlineMemoryExceeded` | `BTreeBlackboardPackHelper.cs:32`, `BlackboardBinPacker`. ⚠ *"inline"* now just means *the params region* |
| `BehaviorConstants.MaxBehaviorParamByteSize = 100` | ⛔ **NOT a cap.** The reservation width for a behaviour declaring a `ParseParams` and neither a manifest nor a layout type — one production reader, `RootParamsAccess.RootParamsBytes` |
| the **64 / 128 / 256 HSM tiers** | live as PAYLOAD SIZES. Only the ECS wrappers went; a 64-byte machine now occupies 64 bytes and that tier is reachable for the first time |

⇒ ⭐ a doc naming `[SharedAiHeavyAction]` is **not wrong**. ⛔ What is wrong is calling it a
**storage tier** — *"spill to heavy"*, *"overflow into `Blackboard1024`"*.

### 2.4 `BrainTickSystem` — the facts any scheduling doc needs

📐 `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BrainTickSystem.cs`

- ⭐⭐⭐ **DISCOVERY IS THE TIER WALK, NOT A COMPONENT QUERY.** One cached `EntityQuery` per tier,
  index-aligned with `BlueprintTierTable.Ascending`. `BrainTier` discriminates *inside* the loop.
- **The gate is `qb.WithOwnedWhen<BehaviorState>(_gateOnAuthority)`** on those queries, `:125`.
  ⚠ `_gateOnAuthority` **defaults to false** and is turned on per host by step 4.
- Chain: arbitration → interrupt → **tick** → cleanup → pulse. ⭐ The merge removed a **node**, never
  reordered a step. ⚠ The first **four** take `gateOnAuthority`; `BehaviorFrameSystem()` takes none.
- ⛔ **`BlueprintTickSystem` is deliberately NOT merged in** — four measured grounds, §31.14.2.
- ⚠⚠ **Trace-buffer resolution is PER-ARM, not shared body** — BTree at `:253-264`, HSM at
  `:350-369`, different buffer types. 📌 **I wrote the opposite into my own agent brief**, having
  taken it from the class docstring's list of shared elements instead of the method bodies. A
  subagent caught it. ⇒ §3's rule, broken by me, in the very document that states it.

---

## 3. ⛔⛔ TRAPS PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴 **THIS BRANCH HAD NEVER MERGED `behaviors`** | 📌 earlier sessions *read* files from `origin/behaviors` and recorded *"last synced"*, which is not a merge. ⇒ the first real merge brought **217 commits and 11 conflicted docs**. ⭐ **`git merge-base --is-ancestor <their-sha> HEAD` before believing any sync claim** |
| 🔴🔴 **CONFLICT RESOLUTION IS A JUDGEMENT, NOT A SIDE-PICK** | ⭐ lane-owned docs *(tracker, the storage design, other lanes' resumptions)* → **theirs**. ⭐ docs I had rewritten where they had added a SUPERSESSION BANNER → **mine**, because the banner names concepts the body no longer contains — ⚠ but check the banner for a FACT worth keeping first *(`E3b`'s per-site values survived that way)* |
| 🔴🔴 **A TOKEN SWEEP FINDS FILES THAT *NAME* THE OLD THING, NOT FILES THAT *DESCRIBE* IT** | 📌 `Blackboard_Authoring_Addendum_v3` never matched `BrainBlackboard` once and said *"its 100-byte inline memory"*. ⭐ **The wide pattern is `scripts/docs-storage-sweep.py`; re-run it, not a name grep** |
| 🔴🔴 **A MODEL STATEMENT LEADS WITH THE TARGET; AN INVENTORY STATES WHAT IS THERE** | 📌 I put *"the cap still fires"* and *"the components still exist"* in the LEAD of design banners. ⛔ **Both user corrections were this same error, the second right after the first** |
| ⛔⛔ **"RETIREMENT PLANNED" IS NOT "RETIRED"** | 📌 the cause of all nine of wave 1's own defects. ⇒ **no structural claim without a `file:line` in the CODE** — ⚠ **and a doc COMMENT is not that citation either** *(§2.4: I sourced a false claim from a class docstring)* |
| ⛔ **HALF-CONVERSIONS ARE THE COMMONEST DEFECT** | 📌 wave 2's: a safe-to-drop set changed from six to three while three separate counts still said *"ten droppable"*; `PackResult` converted but not its algorithm steps. ⭐ **After changing a claim, grep the file for what COUNTS or RESTATES it** |
| ⛔ **A HEADING IS A CLAIM** | 📌 `btree-hsm-unif` had a STATUS block, a `stale-below` AND a warning paragraph — under a section still headed **"Current State"** documenting two deleted systems. ⭐ A reader navigates by headings; rename the heading |
| ⚠ **DO NOT `git add -A` WHILE A SUBAGENT IS WRITING** | 📌 §1's missing commit. ⭐ Stage explicit paths |
| ⚠ **the subagents were GOOD, and each got one thing wrong** | 📐 agent 1 over-claimed *"all five systems take `gateOnAuthority`"* *(four do)*; agent 2 said id 31 is `VehicleState` *(it is `VehicleParams`)* — ⭐ **but agent 2 corrected MY brief on a load-bearing point.** ⇒ spot-verify every surprising claim in both directions |

---

## 4. ⭐ HANDED TO THE `behaviors` LANE — **code findings, all verified, none fixed here**

| # | |
|---|---|
| **①** | ⛔ **`BP1200` / `BP1201` still hard-code 100 / 1016** *(`Stage2_Validate.cs:492,500`)* while `FDP_001` is a **16 096** capacity bound. ⭐ The Instance arm beside them already reads `BlueprintTierLadder`. ⇒ a real inconsistency in the compiler. *(Now stated in `Blueprint_Subsystem_Architecture_v1.2.md` §"Params total size".)* |
| **②** | 🔴 **A TOMBSTONE NAMES A LIVE COMPONENT'S ID AS BURNED.** `BrainComponents.cs:16` says `BrainBTreeState`'s id **31** stays reserved. It is **29**; **31 is `VehicleParams`**, live. The error repeats at `BrainComponents.cs:45` and `GlobalComponentIds.cs:128` *("like 23, 31, 35 and 74")*. ⚠ A reader following it would conclude a live id is burned |
| **③** | `HealthApplicationSystem.cs:26,112` still name `HsmDamageBridgeSystem` as the downstream consumer. That class does not exist; the consumer is `CognitiveInterruptSystem` |
| **④** | `BehaviorIngressSystem.cs:109` comment: *"before writing `BehaviorState`/`BrainBTreeState`"* |
| **⑤** | `StrideNodeBootstrapper.cs:305` — the *"STILL EXCLUDED, AND DELIBERATELY"* comment names `BrainBTreeState`, `BrainBlackboard` and `BTreeTickSystem`. ⚠ It is **quoted verbatim** by `DESIGN_Role_Affinity_Ownership.md`, so the doc will go stale again when it is fixed |
| **⑥** | `HeadlessDemoApp.cs:348-349` — *"`CognitiveRuntimeModule` groups … BTreeTick, HsmTick …"* |
| **⑦** | `HsmDefinitionAttribute.cs:24` and `BTreeDefinitionAttribute.cs:27` still say `HeavyDtoType` *"provisions a `Blackboard1024` component"*. ⚠ **These are XML docs — they show in IDE tooltips**, so the stale version is what a developer actually reads |

✅ **`R-39` is NO LONGER on this list** — it was, but its probe went red after the merge *(this lane
had re-pointed it; `CE-307` then renamed the constant)*. ⛔ Fixing the probe alone would have left
canon asserting a `fixed byte[100]` inside a deleted component, so the row was rewritten too.
**38/38 green.**

---

## 5. ⚠ STILL OPEN

| | |
|---|---|
| ⭐⭐ **RE-RUN `scripts/docs-storage-sweep.py` after the next storage slice** | it is what caught both waves. ⛔ A name grep will not |
| ⚠ **36 files / 177 hits survive, all triaged** | ⭐ **35 of them are ONE file** — `btree-hsm-unif/DESIGN.md`'s baseline section, which by construction describes the system its own programme replaced; it is behind a renamed heading, a `stale-below` and a warning. The rest are live identifiers *(§2.3)*, verbatim user quotes, dated records, unrelated byte figures, and SVG path coordinates containing `928` |
| ⛔ **`.dev/` (230 files) and `docs/blueprints/batches/` are UNTOUCHED, deliberately** | dated as-built records of finished programmes. ⚠ **Rewriting them would be a decision to overwrite history, not to update intent** |
| ⚠ **`RESUME_START_HERE.md` (12 hits) and `RESUME_UI_Lane.md` (5)** | other lanes' state docs; almost all hits are inside `>` blockquoted historical records. ⭐ Left alone on lane discipline — ⚠ but `RESUME_START_HERE` is the shared entry point, so it is worth **asking** whether this lane should sweep it |

---

## 6. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git merge-base --is-ancestor $(git rev-parse origin/behaviors) HEAD`
   — ⛔ **ancestry, not a "last synced" line.** If it is not an ancestor, merge and expect conflicts;
   §3 row 2 is how to resolve them.
2. **If a new storage slice landed:** re-measure §2.1/§2.2/§2.4 at the call sites, add the newly
   retired tokens to `scripts/docs-storage-sweep.py`, re-run it, and fix what it finds.
   ⛔ **Measure the new tokens' code status FIRST** — §2.3 is the list of things that look retired
   and are not, and adding one to the pattern costs ~100 false hits.
3. **If nothing landed:** this lane has no work. ⭐ §4's seven items belong to `behaviors`.
4. ⛔ **Before any edit, re-read §3.**

### ⭐ GATES for this lane

```bash
python3 scripts/rulings-check.py                 # 38/38
python3 scripts/design-digest.py --check
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <changed .md files>
python3 -c "import xml.etree.ElementTree as ET,glob; [ET.parse(f) for f in glob.glob('docs/**/*.svg',recursive=True)]"
python3 scripts/docs-storage-sweep.py            # the lane's own measure
```
⛔ **No build or test gate applies** — this lane touches no `.cs`.
