<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file — the Batch 99 return.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# REPORT — Batch 99: **the Properties form**

> 📌 **Dispatched at `e90af1936`** · ⭐ **scope frozen there** · base for every RED: **`e90af1936`**.
> ⭐ **Started-marker pushed first** *(rule 1b)*: `40a2c04` — `chore: started batch 99 at e90af1936`.
> ⭐ **Ids allocated by me** *(rule 3)*: **`BP-370`** · **`BP-371`** · **`BP-372`**, and **`BP-369` closed**.

---

## 1. ⭐⭐⭐ THE FOUR VERDICTS *(`R-106`)* — **one per item, none missing**

| item | verdict | |
|---|---|---|
| **`99a`** — the Properties form *(`BP-369`, `R-109`)* | ✅ **DONE** | ⚠ with **two of its fields shipped DISABLED WITH THEIR REASON**, both measured — §3 |
| **`99b`** — enumerate `BP-367`'s siblings | ✅ **DONE** | ⭐ **and turned into a GATE**, not left as a sentence — §4 |
| **gates · probes · tracker · report** | ✅ **DONE** | §5–§6 |

⛔ **Nothing blocked, nothing partial, nothing not-started.**

---

## 2. ⭐⭐ `99a` — **what "Properties…" is now**

📌 **`R-108`**: the two menu items are **TWO OBJECTS**, not two scopes ·
📌 **`R-109`**: ⛔ **and the declaration cannot be a StructEdit document**, because two of its fields are
**OPERATIONS**:

| field | why it is not a struct write |
|---|---|
| **`Name`** | a **RENAME** ⇒ must run the refactor service. ⚠ 📌 `M-15`: BTree/HSM store the variable's **NAME STRING** in the binding and `RenameVariable` does not fix up `ExpressionTargetField` ⇒ skipping it **dangles the binding**, caught at build as `BTREE0002` — **a whole-asset skip** |
| **`Type`** | a **RETYPE MIGRATION** — `DefaultValueJson` may not convert, offsets move, **`StructureHash` moves** *(`R-24`)* |

### ⭐ What was built

| | |
|---|---|
| `VariablePropertyFields` | ⭐⭐ **the shared form**, and **`VariableCreateModal` was FACTORED onto it** ⇒ **CREATE and EDIT-PROPERTIES draw the same code** *(ruling 9 at the right level)* |
| `VariablePropertyValues` | ⛔ **`Name` and `Type` are ABSENT BY RULING** — they are the two operations, and a value record cannot carry them |
| `VariablePropertiesModal` | opens the declaration · **properties first, rename second** *(the property write is keyed by NAME, so renaming first keys it by a name the source no longer knows)* · ⛔ **never reads `_state.TypeId`**, so enabling the control later cannot silently start writing a retype |
| `IVariablePropertiesFormHost` | one host claim, subscribed once in the registrar's existing `RegisterExtraWindow` pass |
| `VariableRenameCommit` | ⭐ **EXTRACTED, not written** — see below |

### ⭐⭐⭐ The rename was EXTRACTED, and one thing about it CHANGED

📐 `VariablesPanelControl.CommitRename` already did exactly this — `GetRefactorKey` → `PreviewRename` →
`ApplyRename` → `schema.RenameVariable` — and it was **private**. ⇒ ⛔ **a Properties-form rename would
have been a SECOND implementation** *(ruling 9)*, **and the weaker one**, because the refactor half is
the easy half to leave out and nothing would have said so.

⚠⚠ **ONE DELIBERATE CHANGE, stated rather than slipped in.** The original ran `ApplyRename` only when
the preview had no errors — ⛔ **and then renamed the declaration unconditionally anyway.** ⇒ an error
left the references behind while the declaration moved, **which is precisely the dangling state `M-15`
describes.** ⭐ **Here an error aborts BOTH halves.**

### ⚠⚠ What ships DISABLED, and why — **measured, not assumed**

| control | state | the measurement |
|---|---|---|
| **`Type`** | ⛔ **disabled, with its reason** | 📌 the handoff: *"do not hold the dialog"*, and *"do NOT silently write the new type and leave `DefaultValueJson` unconvertible."* ⭐ Shown even in an editable form — the reason is about the **OPERATION**, not the run state |
| **`Name`** | ⛔ **disabled in `BlueprintDetailsWindow`, with its reason** | 📐 That window is constructed with a **selection store and a drawer registry** — it holds **no `IVariablesSchemaSource`**; the schema lives in the row SOURCE the outline builds. ⇒ a rename from there could only skip the refactor service. ⭐ **`VariableRenameCommit` IS built and railed** — it is the wiring that is missing, and a host that supplies a schema renames through it with **no further work** |

⭐ Both follow the visual guide's **`F3`** — *"every refusal GREYED WITH A TOOLTIP, not a click that
dead-ends"* — ⛔ not a TODO comment.

⭐ **Read-only stays DIALOG-LEVEL**, from `VariableEditPolicy`, as Batch 96 built it. ⛔ **No per-field
flag anywhere** *(`R-109`)*, ⛔ **and no second editability matrix** *(ruling 9)*.

---

## 3. ⛔⛔ FOUR RAILS ASSERTED THE OLD BEHAVIOUR — **each change argued in its own doc comment**

⭐ `R-109` overturns a premise that four rails had written down as a requirement. ⛔ **Changing a rail
quietly is how a regression becomes a "fix"**, so each carries the argument where the next reader will
see it.

| rail | what changed |
|---|---|
| `BothGesturesOpenTheWholeValueDocument` → **`TheValueGestureOpensTheWholeValueDocument`** + **`ThePropertiesGesture_OpensNoStructEditSession`** | ⭐⭐ **the "both" WAS the defect.** Batch 96 corrected *which* document Properties opened while leaving intact the premise that it opens one — ⛔ **and that premise is what `BP-359` actually was** |
| `BothGesturesDriveTheSameDialog` → **`ThePropertiesGestureNeverReachesTheValueDialog`** | ⭐ the **lifecycle** claim survives *(one dialog, reopenable, one OK/Cancel)* and is now asserted on the value gesture alone, with the Properties absence fenced in the same rail |
| `ANodeOwnedRow_StillOpens_BecauseReadOnlyIsNotAbsent` | ⭐⭐ **same property, new evidence — and STRONGER.** The old session-shaped assertion **could not tell read-only from editable at all** *(a session opened either way)*; the form event's `bool` carries exactly the distinction the rail's own name is about |
| `PropertiesOpensASession_OnEveryPerspective` → **`PropertiesLandsButOpensNoSession_OnEveryPerspective`** | ⚠⚠ **and the honest caveat is IN the rail:** `95a`'s resolver defect can no longer reach this arm *(Properties returns before the resolver runs)* ⇒ ⭐ **that coverage now lives ENTIRELY in `EditValueOpensASession_OnEveryPerspective`** — ⛔ do not delete that rail thinking this one covers it |

⭐ **New:** `OnlyBlueprintHasAPropertiesFormHost` pins the asymmetry as a **finding, not an omission** —
only `BlueprintDetailsWindow` implements the host, so BTree/HSM raise the gesture and nothing opens
*(📌 `BP-317`, **filed, not faked**)*. ⚠ The day an AI host grows a form, that rail is what says so.

---

## 4. ⛔⛔ TWO DEFECTS FOUND WHILE DOING IT — **and one of them was MINE**

### ⭐⭐ `BP-370` — **a rail went VACUOUS and would have stayed green forever**

📐 `ARowThatCanNameNoDeclarationStillOpensNothing` asserted **fail-closed** by driving the **Properties**
gesture and checking `ActiveSession is null`. ⚠⚠ `99a` made that arm return **before** `_entryResolver`
is consulted ⇒ ⛔ **`ActiveSession` is now null for an orphan row, a healthy row and every row in
between: the rail could no longer go red for any input.**

⭐⭐⭐ **Nothing would have announced it** — green before the batch, green after. **A rail that cannot
fail is indistinguishable from one that passes.**

⭐ **Re-pointed at "Edit value…"**, the gesture that still runs the resolver. **Probe confirms it
reddens.** ⚠ **The general lesson:** ⛔ **changing WHICH PATH a gesture takes can silently de-fang a rail
that never mentions that path.**

### ⛔ `BP-371` — **the silent-default pattern, eighth instance, and the first that was MINE**

📐 `99a`'s first draft built the form as `new VariablePropertiesModal()` and justified it **in its own
doc comment**: *"no refactor service, deliberately — this window has none to give."*
⚠⚠ **True of the WINDOW and false of its CALLER**: `EditorSubsystem` holds a `refactorService` and hands
it to `BlueprintVariablesManagedWindow` **seven lines below** the line constructing this one.

📌 **`CLAUDE.md` verbatim: *"a production caller that HAS a dependency must PASS it."***
⭐ Forwarded, with the control that ruling asks for — **a forwarding rail PER DEPENDENCY, asserted on the
CONSTRUCTED OBJECT** *(`R-67`: reached through the registrar's registered list, ⛔ not a `new` in the
test)*.

⭐⭐ **And `CanRename` could NOT serve as that probe** — it is `false` when **either** half is missing, so
a host handed no service is **indistinguishable** from one with no schema. ⚠ **That ambiguity is exactly
what a defaulted dependency hides behind** ⇒ a separate `HasRefactorService`.

---

## 5. ⭐⭐ `99b` — **`BP-367` has NO unfixed sibling**, and here is the enumeration *(`BP-372`)*

### ⭐⭐⭐ Why this is a CLASS of defect, not one mistake

**`DefaultValueJson` is a TRAILING OPTIONAL parameter defaulting to `null`** on both carriers, and
`VariableViewModel`'s own comment says it out loud — *"Trailing and optional: every existing
construction site is unchanged."* ⇒ ⛔ **a projection that forgets it compiles clean, warns nothing, and
reads correctly.** Nothing but a rail can see it.

### ⭐ The enumeration *(📌 `R-74` — the graph, ⛔ not a grep alone)*

```
query_graph: MATCH (c)-[:CALLS]->(t)
             WHERE t.name IN ['BlackboardVariableEntry','VariableViewModel']
          ⇒ total 54, of which 11 production
```

⚠⚠ **AND THE GRAPH MISSED FOUR.** `BTreeCommandSink`'s four `AddVariable(new BlackboardVariableEntry(…))`
sites carry **no `CALLS` edge** to the record constructor. ⇒ ⭐⭐ **the honest enumeration is
graph ∪ grep — 16 production sites** — and the split is worth recording: the graph found the *callers*
grep would have had to be told to look for, and grep found four the graph could not see.

| projection | verdict |
|---|---|
| `BlueprintVariableSchemaSource.Variables` / `.Entries` | ⭐ **the `BP-367` site itself** — fixed Batch 98, railed |
| `BlueprintLocalVariableSchemaSource.Variables` | ✅ carries it — ⛔ **was NOT railed** ⇒ **now gated** |
| `BlackboardAuthoringWindow.BuildViewModel` | ✅ carries it — ⛔ **was NOT railed** ⇒ **now gated.** ⭐⭐ **The widest one**: `BTreeHsmSchemaSource.Variables` is a pass-through of the view model this builds, so **every** BTree and HSM row comes through it |
| `HsmAssetMapper` / `BehaviorTreeAssetMapper` `.BlackboardFromDto` | ✅ carry it, ⭐ **already railed** — `DefaultValueJsonRoundTripTests`, **14 tests over both hosts** *(round-trip · null-omitted-from-JSON · back-compat when the key is absent)*. ⚠ **The highest-stakes pair** — dropping it there loses an authored default on **RELOAD**. ⛔ Not re-railed *(ruling 9)* |
| `BlackboardAuthoringWindow.BuildHardcodedDtoFields` | ⛔ **drops it, CORRECTLY** — sub-tree DTO field **requirements**, not declarations; no declaration exists to have authored a default, and they ship `IsReadOnly: true` |
| `VariablesPanelControl:710` | ⛔ **drops it, CORRECTLY** — a **name-only** projection whose sole consumer is `BlackboardNameValidator.Validate`, which reads `Name` and nothing else |
| the remaining **8** — `BTreeCommandSink` ×4 · both pickers' `Promote` · `BehaviorTreeAsset.GetAutoAllocatedVariables` · `VariablesPanelControl:720` | ⛔ **not projections at all** — every one **CREATES** a variable that did not exist. There is no declaration behind them |

⭐⭐ 📌 The handoff allows *"a count of 'one, and it is fixed' is a fine answer"* — ⛔ **but an enumeration
that ends in a sentence rots.** ⇒ `EveryDeclarationProjectionCarriesTheDefaultTests` gates the two
correct-but-ungated carriers, **and carries the whole enumeration in its doc comment.**

⚠ **What it does NOT cover, stated:** a projection written **after today**. That is the standing limit of
a per-site rail, and it is why the enumeration is recorded and not only the two assertions.

---

## 6. ⭐⭐ GATES — **the seven-row contract** *(rule 8)*

⭐ Base for every RED: **`e90af1936`**. ⭐ Every command run **UNFILTERED** unless a row says otherwise.

| gate | `--no-build`? | result | Δ baseline |
|---|---|---|---|
| solution build | — | **0 errors** · `EXIT=0` | — |
| **AiShared** | ✅ | **1706 / 0 / 0** · `EXIT=0` | **+1** *(1705)* |
| **BTree.Editor** | ✅ | **622 / 0 / 0** · `EXIT=0` | **0** |
| **Hsm.Editor** | ✅ | **554 / 0 / 0** · `EXIT=0` | **0** |
| **Blueprints** | ✅ | **3852 / 0 / 10 skip** · `EXIT=0` | **+25** *(3827)*, skips **0** |
| **Hrot.Editor** | ✅ | **201 / 0 / 0** · `EXIT=0` | **0** |
| **Breakpoints** | ✅ | **143 / 0 / 0** · `EXIT=0` | **0** |
| **Generators** | ✅ | **277 / 0 / 0** · `EXIT=0` | **0** |
| **Persistence** | ✅ | **143 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.Core** | ⛔ **NO** *(out of solution — a `--no-build` here reports a STALE BIN)* | **211 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.UI** | ⛔ **NO** | **135 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **Fhsm** | ⛔ **NO** | **300 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **StructEdit** | ⛔ **NO** | ⚠ **191 / 1 / 0** | **0** — ⭐ `BP-363`, **pre-existing and unchanged** |
| **Fdp.Presentation** | ✅ | **146 / 0 / 0** *(`BP-337` filter)* · `EXIT=0` | **0** |
| **Fdp.Toolkits** | ✅ | ⚠ **1964 / 0 / 0** this run — see row 4 | — |
| `tracker-counts.py --check` | — | **OK — open 77 / done 230 (+1 refuted)** · `EXIT=0` | open **−1**, done **+4** |
| `rulings-check.py` | — | **74 / 74 verified** · `EXIT=0` | **0** |
| `design-digest.py --check` | — | **50 docs OK** · `EXIT=0` | — |

### ⭐ Row 3 — **golden movement as a DIFF SHAPE**

⛔ **ZERO golden files, ZERO asset `.json` files, ZERO removed lines from any asset.**
`git diff --name-only e90af1936..HEAD | grep -iE "golden|\.json$|Assets/"` → **nothing.**
⇒ **code + tests + docs only.** ⭐ No emitter, no `StructureHash`, no persisted shape was touched — and
that is expected: `99a` writes through existing seams and `99b` adds only rails.

### ⭐ Row 4 — **every RED confirmed pre-existing**

| RED | evidence |
|---|---|
| `StructEdit.Tests…Build_CircularReference_CircularFieldIsUnsupported` | ⭐ **`BP-363`** — **identical here: 1 failed / 191 passed**, same single test. ⛔ Nothing in this diff touches StructEdit |
| ⚠ `Fdp.Toolkits.Tests` | 📌 **`DEBT-AIB-030`** — the identity ROTATES between runs, so ⛔ **neither a red nor a green is evidence.** ⭐ **Green this run (1964 / 0)**, reported as a fact and **not** as a clearance |

### ⭐ Row 5 — **the working tree is CLEAN after every suite run**

`git status --short` after the full sweep → **only the new test file**. ⛔ **No golden was regenerated by
a test.**

### ⭐ Row 6 — **quarantine counts**

Blueprints skips **10 → 10**; **every other suite 0**. ⛔ **No new skip** — and a new skip would be a
finding, not a fix.

### ⭐ Row 7 — **ids allocated**

**`BP-370`** *(the vacuous rail)* · **`BP-371`** *(the silent default)* · **`BP-372`** *(the enumeration)*,
and **`BP-369` CLOSED**. ⛔ No architect-question number taken.

### ⭐ The **+25** on Blueprints and **+1** on AiShared, itemised

**19** `ThePropertiesFormIsCustomTests` · **3** `OnlyBlueprintHasAPropertiesFormHost` *(theory)* ·
**1** `TheProductionDetailsWindowIsHandedTheRefactorService` · **2**
`EveryDeclarationProjectionCarriesTheDefaultTests` = **25**.
AiShared **+1**: `ThePropertiesGesture_OpensNoStructEditSession` *(the four renamed rails are 1-for-1)*.

---

## 7. ⭐⭐ REVERT PROBES — **one per item, never delegated**

⛔ **Never `git checkout --`** — every probe was un-applied with the **inverse edit**.

| # | probe | red |
|---|---|---|
| **P1** | Properties falls through to the launcher *(the `BP-359` shape)* | **1** — `ThePropertiesGesture_OpensNoStructEditSession` |
| **P2** | the rename skips `PreviewRename`/`ApplyRename` | **2** |
| **P3** | `refactorService` not forwarded at the composition root | **1** — the forwarding rail, ⭐ **and only it** |
| **P4** | `Open` fabricates an entry instead of failing closed | **1** — the re-pointed `BP-370` rail, ⭐ **proving it is no longer vacuous** |
| **P5** | `BlueprintLocalVariableSchemaSource` passes `DefaultValueJson: null` | **1** |
| **P6** | `BuildViewModel` passes `DefaultValueJson: null` | **1** |

---

## 8. ⭐ WHOSE OBJECT · WHICH LAYER IS FAKED *(📌 `M-29`)*

| rail | input comes from | ⛔ what is faked |
|---|---|---|
| `ThePropertiesFormIsCustomTests` | a real `BlueprintAsset` + the real `BlueprintVariableSchemaSource` | ⛔ **the DRAW, entirely** *(`R-21`/`R-62`)* — the commit is driven directly. **Nothing asserts a control appears** |
| `TheDialogOpensOnEveryHostTests` | ⭐ **the registrar PRODUCTION built** — `EditorSubsystem` + its real `RegisterWindows` pass *(`R-67`)* | the WindowManager's icon atlas is a zero handle; the ROWS are built in the fixture rather than by an outline click |
| `EveryDeclarationProjectionCarriesTheDefaultTests` | ⭐ **the real production projections** over real declarations | ⛔ **nothing** — both rails read the real result |
| `VariableEditGestureBinderTests` | the binder's own fixture | the table gesture is raised directly, not clicked |

---

## 9. ⭐ WHAT WAS **NOT** BUILT — **and stayed not built**

⛔ Properties as a StructEdit document · a per-field read-only flag in StructEdit · a second editability
matrix · `Role`/`Scope` in the dialog · a silent retype · an `Instance`-blueprint live write · a
BTree/HSM live writer · **any revert of Batch 98**.

⭐ **Rule 4 discharged:** the coordinator branch was re-pulled before the final commit
*(`e90af1936..04e3df9`)*. Four documents changed — `RULINGS.md` *(`M-31` marked CLOSED by my own Batch 98
result)*, `Architect_Question_38` *(two supersession banners, `R-98`/`R-100`)*, the handoff *(created,
never amended)* and `RESUME_START_HERE.md`. ⛔ **None invalidates a Batch 99 item** ⇒ **FYI only**, no
adaptation, per the scope-frozen rule.
