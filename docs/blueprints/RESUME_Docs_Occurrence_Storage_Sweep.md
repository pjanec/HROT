<!--STATUS
state: LIVE
doc-type: RESUMPTION for the DOCS lane that rewrote the corpus off the retired blackboard
  components and onto the occurrence-slot model. ⚠ A STATE doc, not canon. Every "measured"
  line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-23
build-state: n/a — a documentation lane. The owning design is
  DESIGN_Occurrence_Scoped_Storage.md (behaviors lane); this lane never edits it.
current-answer: ⭐ START AT §6 — the sweep is DONE and pushed. What is LEFT is the re-run
  after O4/O7c land (§5), and two code findings handed to the behaviors lane (§4).
  §1 is what shipped, §2 the measured facts that cost the most to establish, §3 the traps.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — owns the storage model this lane wrote the docs
    against. It wins on any disagreement. ⛔ behaviors lane owns the file; do not edit it here.
  - RESUME_P4_Retire_Blackboards.md — the behaviors lane's own build resumption. Read it for
    what is left in CODE; this doc covers what is left in DOCS.
  - Blueprint_Issues_Tracker.md — CE-303/307/308/312/314/316 all closed there, by that lane.
-->

# RESUME — the docs sweep onto occurrence-scoped storage

> **Branch:** `claude/blueprint-macro-feature-sdmspn` · **pushed, tree clean**
> **Base:** `8418335cd` · **head:** `bcff032ef` · **127 files, +8248 −2040** (all under `docs/`)
> **Last synced from `behaviors`:** `57e2c9de2`

> 🔒 **The instruction this lane serves, in the user's words:** *"update them to the new state
> where these two do not exist at all and are replaced with occurrence slot. The docs should not
> mention them anymore, the old concept should not be left as superseded, it should be replaced
> with new state."* — plus two extensions: *"there will be no fixed cap eventually"* and *"there
> will be no brainhsm64 and similar ecs components eventually either."*

---

## 1. ✅ WHAT SHIPPED — **do not redo this**

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
| `bcff032ef` | ⭐ **the wide sweep** — 51 files, 192 hits triaged |

⭐ **The vocabulary, applied throughout** *(use it for any new doc)*:

| the retired thing | what to write |
|---|---|
| `BrainBlackboard.BehaviorParameters` | **the root params slot**, located by `RootParamsAccess`, keyed `OccurrenceSlotKey.ComputeRootParamsKey(BehaviorState.ActiveBehaviorHash)` |
| `Blackboard1024` (AiPrimitive state) | **node working-state slots**, keyed `{fqn}@{offset}@{slotKey}` |
| the 100-byte cap / heavy tier / `HeavyDtoType` | **gone** — one region, bounded by the tier the allocator seats it in |
| `Interpreter<BrainBlackboard,…>` | `Interpreter<byte,…>` — `byte` is slot byte 0 |
| bytes 126/127 | `BrainInterrupts`, as named fields |

---

## 2. ⭐⭐ MEASURED FACTS — **the ones that cost the most to establish**

### 2.1 🔴 THE TIER LADDER — **the single most-repeated error in the corpus**

📐 From `BlueprintTierLadder` (total − header 32 − `MaxSlots`×16), re-picked by `B3②` `2026-09-20`:

| tier | MaxSlots | slot table | payload |
|---|---|---|---|
| `BlueprintBlackboard256` | 3 | 48 | **176** |
| `BlueprintBlackboard1024` | **12** | 192 | **800** |
| `BlueprintBlackboard4096` | **16** | 256 | **3 808** |
| `BlueprintBlackboard16384` | 16 | 256 | **16 096** |

⛔⛔ **Seven documents carried the PRE-`B3②` figures** — `MaxSlots 4/8/16`, payloads `928/3936/16368`.
⚠ **And so did I**, in five files, because I took them from a design table instead of the ladder.
⇒ ⭐ **there are FOUR tiers, not three.** Any `{1024,4096,16384}` list is stale.

### 2.2 THE CAP — **its whole history, because the docs were wrong in BOTH directions**

| when | state |
|---|---|
| before `CE-307` | **100 B**, a *corruption guard* — params sat inline with neighbours after them, so an overrun overwrote unrelated state. Live in **six** places |
| ✅ after `CE-307` (`37b0f6358`) | **16 096 B**, a *capacity bound* — `BehaviorConstants.MaxRootParamsByteSize` = `Tier16384PayloadSize`; `FDP_001` retitled *"exceeds occurrence storage capacity"* |
| ✅ `CE-314` (`9e3ec9b1c`) | `BlackboardBinPacker.MaxInlineBytes` 100 → 16 096; **`MaxHeavyBytes` and `HeavyMemoryExceeded` deleted** |

⛔ **STILL 100 / 1016:** `BP1200` / `BP1201` in the **blueprint compiler** (`Stage2_Validate.cs:492-503`).
`CE-307` did not reach them. ⇒ §4 ①.

### 2.3 WHAT MOVED INTO SLOTS, AND WHAT DID NOT — **measured at the call sites**

| | |
|---|---|
| ✅ behaviour **params** | `BTreeTickSystem.cs:124`'s `P4-②` note — *"the blackboard IS the root params slot, resolved once per entity per tick"* |
| ✅ **node working state** | `HsmOccurrence.KeyFor(instance, AssetId, writer)` keys on the `(region, state)` pair the kernel stamps |
| ✅ `BrainBlackboard` | **deleted** (`4b8de3c1d`) |
| ⛔ **BTree kernel instance state** | `BrainBTreeState` — `BTreeTickSystem.cs:87` `.With<>`, `:116` `GetComponentRW<>`, **eight lines above that same `P4-②` note** |
| ⛔ **HSM kernel instance state** | `HsmTickSystem<T>` is generic **over the component**; `CognitiveRuntimeModule.cs:64-65` instantiates `<BrainHsm128>` / `<BrainHsm64>` |

⇒ ⭐ **`O4`** gives the root behaviour's state its own slot; **`O7c`** moves the HSM instance in and
**deletes `BrainHsm64`/`BrainHsm128`**. Both outstanding.

⚠⚠ **A declaration existing proves nothing** — `CE-312` was a component attached every spawn and read
by nobody. ⇒ **always measure at the call sites**, not with `grep "struct X"`.

---

## 3. ⛔⛔ TRAPS PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴 **A TOKEN SWEEP FINDS FILES THAT *NAME* THE OLD THING, NOT FILES THAT *DESCRIBE* IT** | 📌 `Blackboard_Authoring_Addendum_v3` never matched `BrainBlackboard` once and said *"its 100-byte inline memory"*, *"inline tier, spill to heavy"*. ⭐ **The wide pattern is in §5; re-run it, not a name grep** |
| 🔴🔴 **A MODEL STATEMENT LEADS WITH THE TARGET; AN INVENTORY STATES WHAT IS THERE** | 📌 I put *"the cap still fires"* and *"the components still exist"* in the LEAD of design banners — accurate, and the wrong headline. ⭐ A banner says the model and names the outstanding slice; `Hrot.CGF.md`'s *"Brain carries …"* census keeps naming what a host registers today. ⛔ **Both corrections from the user were this same error, the second one made right after the first** |
| ⛔⛔ **"RETIREMENT PLANNED" IS NOT "RETIRED"** | 📌 the cause of **all nine** of my own defects. A design says what should be true; only code says what is. ⇒ **no structural claim without a `file:line` in the CODE** |
| ⛔ **HALF-CONVERSIONS ARE THE COMMONEST DEFECT** | 📌 `PackResult` converted, its algorithm steps not · a DEBT row flipped, its tally not · §4.7's headline updated, the sentence below it not · three SVG labels fixed, two in the same file missed. ⭐ **After changing a claim, grep for what COUNTS or RESTATES it** |
| ⚠ **the subagents read carefully; the BRIEF was the weak link** | 📐 defects: **6 from agents, 9 from me**. Wave-2 agent A independently caught two of my errors by reading the authority doc instead of trusting my spec's numbers. ⛔ Do not hand down unmeasured claims |
| ⚠ **`R-149` fails `rulings-check` and is NOT ours** | its cited file is absent at the base commit too. **37/38 is green for this lane** |
| ⚠ **`R-39`'s probe was re-pointed** by this lane at `BehaviorParameterSizeAnalyzer.cs` after the sweep deleted the sentence it quoted. ⛔ Its ROW TEXT still describes the old component — **the behaviors lane owns `RULINGS.md`**, so it was left for them |

---

## 4. ⭐ HANDED TO THE `behaviors` LANE — **code findings, not docs**

| # | |
|---|---|
| **①** | ⛔ **`BP1200` / `BP1201` still hard-code 100 / 1016** *(`Stage2_Validate.cs:492-503`)* while the FDP behaviour path is now a 16 096 capacity bound. ⭐ The Instance arm (`BP1210`) already reads `BlueprintTierLadder`. ⇒ **a real inconsistency in the code**, and if `CE-307`'s intent was one bound everywhere, these are the leftover |
| **②** | ⚠ **`R-39`'s ledger ROW** still describes `BrainBlackboard` and `BrainBlackboardByteSize`. The probe now verifies, but the prose is stale |

---

## 5. ⚠ STILL OPEN — **for the next docs pass**

| | |
|---|---|
| ⭐⭐ **RE-RUN THE WIDE SWEEP AFTER `O4`/`O7c`** | they will invalidate a fresh batch of statements exactly as `CE-307`/`CE-314` did. ⛔ A name grep will not find them |
| ⛔ **`.dev/` (230 files, ~1300 lines) and `docs/blueprints/batches/` are UNTOUCHED, deliberately** | dated as-built records of finished programmes. Rewriting them to claim they built occurrence slots would be false. ⚠ **If the user wants them swept, that is a decision to overwrite history, not to update intent** |
| ⚠ **45 files / 159 sweep hits survive, all triaged** | dated records behind their own markers · verbatim user quotes · live identifiers (`BrainBlackboardTranslator`, `BlackboardTier.Blackboard1024`, the `SharedAiHeavy*` attributes) · unrelated `128-byte`/`60-byte` figures · SVG path coordinates containing `928` |

### ⭐ THE SWEEP PATTERN — **re-run this, not a name grep**

```python
re.compile(r"""
  BrainBlackboard | (?<!Blueprint)Blackboard1024 | BehaviorParameters
| MaxBehaviorParamByteSize | BrainBlackboardByteSize
| inline\s+tier | heavy\s+tier | heavy\s+DTO | heavy\s+component | heavy\s+blackboard
| spill\s+to\s+heavy | promote\s+to\s+heavy | MaxInlineBytes | MaxHeavyBytes
| InlineMemoryExceeded | HeavyMemoryExceeded | RequiresHeavyComponent
| 100-byte | 100\s+bytes | 128-byte | 128\s+bytes | 60-byte | 60\s+bytes
| cognitive\s+bus
| \b928\b | \b3936\b | \b16368\b | MaxSlots\s*4 | 4\s*/\s*8\s*/\s*16
""", re.I | re.X)
```
Over every `.md` **and `.svg`** under `docs/`, excluding `batches/`, `RULINGS.md`,
`Blueprint_Issues_Tracker.md`, `DESIGN_Occurrence_Scoped_Storage.md`, `RESUME_P4_Retire_Blackboards.md`
**and this file** — all six name the retired tokens on purpose, so they are noise in the result.
⚠ **Add the new stale figures each time the ladder or a bound moves** — that is what made it work.

---

## 6. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git log --oneline 57e2c9de2..origin/behaviors` — anything new?
2. **If `O4` or `O7c` landed:** re-measure §2.3 at the call sites, then re-run §5's sweep and fix
   what it finds. ⛔ The brain-state banners in `Fdp.Toolkits.md`, `Predicate-Infrastructure-
   Capabilities.md` and `Architect_Question_34`/`_35`/`_37` name those slices — they change first.
3. **If nothing landed:** this lane has no work. ⭐ The two items in §4 belong to `behaviors`.
4. ⛔ **Before any edit**, re-read §3. The two corrections the user made were the same error twice.

### ⭐ GATES for this lane

```bash
python3 scripts/rulings-check.py                 # 37/38 — R-149 is pre-existing, not ours
python3 scripts/design-digest.py --check
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <changed .md files>
python3 -c "import xml.etree.ElementTree as ET,glob; [ET.parse(f) for f in glob.glob('docs/**/*.svg',recursive=True)]"
```
⛔ **No build or test gate applies** — this lane touches no `.cs`.
