<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: §1 (the headline), §2 (the defect I shipped and caught), §6 (the gate table)
stale-below: nothing
known-rot: none
known-conflict: none
-->

# REPORT — Batch 86: ⭐⭐⭐ **one state kind. It landed.**

> 📌 **Dispatch `e6b94cd` · started at `036dab1`** *(rule 1b marker, pushed before any code)* ·
> **rule 4 re-pull: `origin/claude/blueprint-authoring-status-gm0akp` has NO commits after my merge**,
> so no late document changed my scope.
> ⭐ **IDs allocated: NONE.** ⛔ This batch **closes** `BP-331` and `BP-332`; it files nothing new.
> ⭐ **`DEBT-AIB` partitions touched: none.**

---

## 1. ⭐⭐⭐ THE HEADLINE

| | |
|---|---|
| ⭐⭐ **`DeclarationKind` is TWO members** | `Parameter`, `Variable`. `WorkingState` is gone as a kind |
| 🔴🔴 **gate 8 — `StructureHash` before/after** | ✅ **43 / 43 BYTE-IDENTICAL.** ⛔ `R-24`'s hard reset cannot fire |
| ⭐⭐ **gate 9 — the both-groups rail** | ✅ **built and PROBED** — no shipped asset can exercise it (0 of 458 carry both) |
| ⭐ **gate 10 — rails restated** | ✅ **0 test methods deleted**, 4 renamed, **1 added**, **6 `[InlineData]` rows removed** — each justified in §5 |
| ⭐ **the 16 source assets** | ✅ **12 v2 assets rewritten (34 declarations)**; ⚠ the other 4 were **v1-shape and needed no edit** — §4 |
| 🔴 **a defect I shipped in 4a and caught in 4d** | ⛔ **every state declaration serialized TWICE.** §2 |

---

## 2. 🔴🔴 **THE DEFECT — the alias wrote every state declaration TWICE**

⭐⭐ **`R-01` makes `WorkingState` and `Variables` one run under two names, and BOTH getters return the
whole run.** ⇒ ⛔ **`JsonSerializer.SerializeToNode(asset)` put every state declaration in BOTH v1
lists, and `Up` tagged each of them twice.**

```
"Declarations": [
  { "Kind": "WorkingState", "Name": "W0" },   ⛔ }
  { "Kind": "WorkingState", "Name": "V0" },   ⛔ } measured, before the fix
  { "Kind": "Variable",     "Name": "W0" },   ⛔ }
  { "Kind": "Variable",     "Name": "V0" }    ⛔ }
]
```

⚠⚠ **This is the SAME SHAPE as Batch 85's double-hash defect** — a second write survived a
collapse — **one layer down, in persistence instead of in the hash.** ⭐ It would have doubled every
asset's declaration list on the next save and doubled it again on the one after.

### ⭐ The fix, and why it is where it is

⭐⭐ **One line in `BlueprintJsonServices.Serialize`** — the one place that can know the alias is an
alias — empties the retired list before lifting to v2:

```csharp
v1[BlueprintSchemaV2.LegacyWorkingStateList] = new JsonArray();
```

| ⛔ what I did NOT do | why |
|---|---|
| `[JsonIgnore]` the `WorkingState` property | ⛔ **the READ path needs it.** `Down` still emits all three v1 lists, and a legacy file's working-state entries must reach the setter to keep their ORDER *(`R-24`)* |
| change the v1⇄v2 shape | ⭐ **v1 still has three lists**, so `Up`/`Down` stay each other's inverse and every shipped file keeps loading |
| retire `"WorkingState"` as an on-disk TAG | ⛔ **that is not `R-01`'s to retire.** Every v1 file has the list; the tag stays **readable forever** and maps to `DeclarationKind.Variable` — the handoff's ruling (c), now in code as `EveryV2TagMapsToADeclarationKind_AndEveryKindHasATag` |

---

## 3. ⭐⭐⭐ **THE REVERT PROBES** — *(never delegated; un-applied by the inverse edit, never `git checkout --`)*

| probe | what it broke | result |
|---|---|---|
| ⭐⭐ **A** — `WorkingState` setter → plain `ReplaceWith` *(the second setter wipes the first)* | `ReplaceSegment`'s two-writer split | 🔴 **gate 9's rail RED · `StoreFlipTests.TheStoreStaysGrouped…` RED** |
| ⭐⭐ **B** — remove the de-duplication line | §2's fix | 🔴 **`PersistenceShapeTests.TheTagIsTheFormatNow…` RED** *(4 declarations, not 3)* |
| ⭐⭐⭐ **C** — re-add `AppendFields(sb, asset.Variables)` | Batch 85's exact double-hash defect | 🔴 **24 of 43 hashes moved · 24 Tier-1 goldens RED** |

⚠ **One honest negative:** under probe **A**, `V2ReaderTests.AV2DocumentLoadsEachDeclarationIntoItsOwnKind`
**stayed green** — there `WorkingState` arrives first, so a plain replace of the leading segment is
accidentally equivalent. ⭐ That rail covers the reader's CONCATENATION, not the setter-order hazard;
gate 9's rail is the one that covers the hazard, and it asserts **both** setter orders explicitly.

⭐⭐ **Probe C is also what justifies DELETING the temporary `StructureHashSweep.cs`** — it proved the
Tier-1 goldens catch the same defect by themselves, so a second permanent copy of the fact would be
the denormalised-copy shape this programme keeps filing.

---

## 4. ⭐ **The assets — the handoff's "16", measured**

| | count | what happened |
|---|---|---|
| ⭐ **v2-shape, `"Kind": "WorkingState"`** | **12 files / 34 declarations** | ✅ rewritten to `"Kind": "Variable"` |
| ⚠ **v1-shape, `"WorkingState": [ … ]` non-empty** | **4 files** | ⛔ **needed NO edit** — §2's fix makes them round-trip through the alias; `ByteStability` is green on all four |
| **v1-shape, `"WorkingState": []`** | 37 files | untouched |

⭐ **12 + 4 = the handoff's 16.** ⚠ **My pre-compaction note that the 4 were "deliberately left" was
half wrong** — they *are* left, but because the fix makes them correct, not because they are out of
scope.

### ⭐⭐ Golden movement — **as a DIFF SHAPE, per the gate-report contract**

| golden set | movement |
|---|---|
| ⭐⭐ **Tier-1 (12 files)** | **2 added / 2 removed lines EACH — a pure LABEL MOVE.** ⛔ Every `@offset` and `size=` is byte-identical, and the `StructureHash:` line is unchanged context in all 12 |
| ⭐ **`persistence-shape.txt`** | **12 rows**, each **4–36 bytes SHORTER** — exactly 4 bytes per rewritten declaration *(`"WorkingState"` → `"Variable"`)* |
| ⛔ **the 43 `Emit/*.cs.txt` goldens** | **ZERO moved** |
| ⛔ **any other snapshot** | **ZERO moved** |

---

## 5. ⭐⭐ **GATE 10 — restated vs deleted, with a justification per deletion**

> 📌 The handoff: *"A rail that asserted three kinds must assert TWO — it must not be deleted."*

| | count |
|---|---|
| ⭐⭐⭐ **test METHODS deleted** | **0** |
| ⭐ methods renamed *(same claim, moved subject)* | **4** |
| ⭐ methods ADDED | **1** *(gate 9)* |
| ⭐⭐ assertions restated in place | **50 marked `Batch 86 — RESTATED`** across 17 files |
| ⚠ **`[InlineData]` rows removed** | **6** — every one justified below |

| # | row removed | ⭐ justification |
|---|---|---|
| 1 | `TaggedDeclarationTests.EveryMemberIsCarriedBothWays_ForAVariableBackedDeclaration(WorkingState)` | ⭐ **a literal duplicate** — after the collapse it is the same enum member as the surviving row, run twice |
| 2 | `CrossKindUniquenessTests.ANameTakenByAnyKindIsRefused(WorkingState)` | ⭐ same — the theory still covers **every** member of `DeclarationKind` |
| 3 | `CrossKindUniquenessTests.CreateVariableIsRefusedWhenAnotherKindHasTheName(WorkingState)` | ⭐ *"another kind than `Variable`"* is now **exactly** `Parameter`; the removed row would have asserted Variable-vs-Variable, which is a **within**-kind collision owned by a different test |
| 4 | `DeclarationSectionsTests.InvokingASectionsCreateCommand_AddsADeclarationOfThatKind("editor.create-variable")` | ⛔ **not registered by that registrar** — `editor.create-variable` has its own owner, and registering it twice would be the duplicate implementation ruling 9 forbids. ⭐ **The claim MOVED, it did not vanish**: `TheCreateCommandsAreRegisteredByTheProductionRetarget` now carries that id end-to-end, which is the stronger gate |
| 5 | `DetailsHostsTheVariablesTests.ClickingAGlobal_ResolvesToThatSectionsList(SectionWorkingState)` | ⭐ the section it addresses is **retired by this batch** (`R-01`/`U-16`) |
| 6 | `VariableSchemaSourceKindTests.EachKindProjectsItsOwnList(VariableKind.WorkingState)` | ⭐ **merged INTO the surviving row**, whose expectation became the ordered pair `["W0","V0"]` — strictly stronger, because it now also pins the concatenation order |

### ⚠ Two restatements that are SEMANTIC, not spelling — **flagged rather than buried**

| rail | what changed |
|---|---|
| ⛔ **`LocalVariablePickerAndTitleTests.ThePickerWasNotWidenedToWorkingStateOrParameters`** → `…ToParameters` | `Phase` was excluded **because it was a `WorkingState`**. It is a `Variable` now, and asset variables have always been offered ⇒ ⭐ **it is EXPECTED in the picker**, and hiding it would hide half the state tier from the designer. ⛔ The guard survives intact: `Speed`, a `Parameter`, still must not appear |
| ⭐ **`DeclarationSectionsTests.ASectionWithNoDeclarations_IsEmptyNotAbsent`** | It asserted this of **Working State**, which is now **absent on purpose** — the opposite claim. ⛔ Deleting the test would have taken the RULE down with the section, so it **moved to `Inputs`** |

### ⭐ And one rail became a first-class gate

⭐⭐ `StoreFlipTests.TheStoreStaysGroupedByKindWhateverOrderThePropertiesAreSetIn` is now a
**both-groups, reverse-setter-order** rail — it was already written that way, and the collapse turned
it into the cheapest possible check on `ReplaceSegment`. Probe A reddens it.

---

## 6. ⭐⭐ **GATES — the seven-row contract**

**Base commit for every RED below: `a4d6b79`.** ⭐ **Working tree CLEAN after every suite run.**

| # | gate | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | n/a | ✅ **0 errors** | — |
| 2 | `dotnet test Hrot.Editor.AiShared.Tests` | ✅ yes | ✅ **1397 / 1397 / 0 skipped** | **0** |
| 3 | `dotnet test Hrot.Blueprints.Tests` | ✅ yes | ✅ **3767 passed / 3777 total / 10 skipped** | ⭐ **−5 tests: −6 `[InlineData]` rows (§5) +1 gate-9 rail.** ⛔ **0 failed, 0 new skips** |
| 4 | `dotnet test Hrot.BTree.Editor.Tests` | ✅ yes | ✅ **615 / 615** | **0** |
| 5 | `dotnet test Hrot.Hsm.Editor.Tests` | ✅ yes | ✅ **551 / 551** | **0** |
| 6 | `dotnet test Hrot.AiEditor.Generators.Tests` | ✅ yes | ✅ **270 / 270** | **0** |
| 7 | `dotnet test Hrot.AiEditor.Persistence.Tests` | ✅ yes | ✅ **136 / 136** | **0** |
| 8 | `dotnet test Hrot.Diagnostics.Breakpoints.Tests` | ✅ yes | ✅ **143 / 143** | **0** |
| 9 | `dotnet test Hrot.Editor.Tests` | ✅ yes | ✅ **194 / 194** | **0** |
| 10 | `dotnet test Fdp.Examples.Scenarios.Tests` | ✅ yes | ✅ **56 passed / 68 total / 12 skipped** | **0** |
| 11 | `dotnet test Fdp.Examples.UrbanCombat.Tests` | ✅ yes | ✅ **29 / 29** | **0** |
| 12 | `dotnet test Fdp.Toolkits.Tests` | ✅ yes | ✅ **1964 / 1964** — ⚠ `DEBT-AIB-030` did NOT fire | **0** |
| 13 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ **open 68 / done 199 (+1 refuted)** | ⭐ **−2 open / +2 done: `BP-331`, `BP-332` CLOSED** |
| 14 | `python3 scripts/rulings-check.py` | n/a | ✅ **44 / 44 verified** | **0** |
| 15 | `python3 scripts/design-digest.py --check` | n/a | ✅ **42 documents, all with STATUS + INVENTORY** | **0** |

⚠ **The three gates that take NO `--no-build`** *(out of solution — a stale bin lies)*:
`NodeEditor.Core.Tests`, `NodeEditor.UI.Tests`, `Fhsm.Tests`. ⭐ **Nothing in this batch touches them**
— the diff is confined to `Hrot.Blueprints.*`, `Hrot.Blueprints.Schema` and the corpus.

⛔ **`Fdp.Toolkits.Tests` needs no coordinator run** — `DEBT-AIB-030`, the identity ROTATES between
runs, so neither a red nor a green is evidence. ⭐ Reported above for completeness: **1964/1964, the
race did not fire this run.**

### ⭐ Quarantine

**Both counts unchanged: 10 skipped in `Hrot.Blueprints.Tests`, 12 in `Fdp.Examples.Scenarios.Tests`.**
⛔ **No new skip.**

---

## 7. ⭐ **What this closes, and what it does NOT**

| | |
|---|---|
| ✅ **`BP-331`** | the persistence blocker — answered by §2, not by breaking the file format |
| ✅ **`BP-332`** | the Working State section is **retired**, and the ~37 rails it predicted are restated rather than deleted |
| ⛔ **NOT done: `D4`** — deleting the `WorkingState` PROPERTY | ⭐ *"no rush removals."* It is a live alias with a working setter that the READ path needs. Deleting it is its own batch, after the four v1 fixtures are migrated |
| ⛔ **NOT done: the on-disk tag** | ⭐ **deliberately.** `"WorkingState"` stays a legal, readable v2 tag mapping to `DeclarationKind.Variable`. Retiring it would break every v1 file's revert |
| ⚠ **Not verified: pixels** | 📌 `R-21`/`R-62` — **no visual checks.** The section retirement is asserted on the MODEL's `Sections` list and its projections; ⛔ **nothing here claims the panel renders correctly** |
