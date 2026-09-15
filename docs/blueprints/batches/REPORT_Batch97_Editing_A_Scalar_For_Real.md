<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: the whole file — it is the Batch 97 return
stale-below: nothing
-->
# ⭐⭐⭐ REPORT — **Batch 97: editing a scalar, for real**

| | |
|---|---|
| **branch** | `claude/hrot-implementation-j1jvin` |
| **handoff** | [`HANDOFF_Batch97_Editing_A_Scalar_For_Real.md`](HANDOFF_Batch97_Editing_A_Scalar_For_Real.md) |
| **scope frozen at** | ⭐ **`ea353f6`** *(re-stamped twice under rule 1a)* |
| **base for every RED** | ⭐ **`d5f18e2b2`** |
| **started-marker** *(rule 1b)* | `09b56b9` — `chore: started batch 97 at ea353f6` |
| **ids allocated** *(rules 3/5)* | ⭐ **`BP-361` · `BP-362` · `BP-363` · `BP-364`** — ⛔ no others. **Closed: `BP-356`, `BP-358`** |

---

## 0. ⭐⭐⭐ THE FOUR VERDICTS — **`R-106`, one row per item, all four**

| item | verdict | |
|---|---|---|
| **`97a`** — a scalar's edit must LAND | ✅ **done** | `ScalarEditBox<T>`; `BP-356` closed |
| **`97b`** — grey the gesture the policy DENIES | ✅ **done** | `VariableEditGesture.Decide`; `BP-361` |
| **`97c`** — call the blueprint live writer | ✅ **done** | `ResolveWorkingStateField` + `BlueprintLiveValueWriter`; `BP-358` closed, `BP-364` split out |
| **`97d`** — the BINDING clock | ✅ **done** | `EntityBindingFrame`; `BP-362` |

⭐ **Nothing blocked, nothing partial, nothing unstarted** ⇒ ⛔ **no item blocked another**, and there is
no cascade to report. ⚠ **One thing was NOT built and it is not an item**: BTree/HSM live writing — see
`BP-364`, filed as the capability it is.

---

## 1. ⭐⭐ What each item did

### `97a` — **a scalar's edit goes nowhere** *(`BP-356`)* · commit `d59fb23`

📐 **The defect, restated from the measurement:** `ReflectionEditDocumentBuilder.CreateLeafBinding`
opens `if (fi == null && pi == null) return null;` — **a binding needs a MEMBER** — and a document ROOT
has none. ⇒ for an `int`/`float`/`bool`/`string` variable, `Root.Binding` was `null` and
`DrawLeafNode`'s closing `node.Binding?.SetBoxed(value)` **silently did nothing**.

⭐ **The fix is a one-field wrapper, not a root binding.**

```csharp
public struct ScalarEditBox<T> { public T Value; }
```

| ⭐ decision | why |
|---|---|
| ⛔ **NOT a root binding inside `StructEdit`** | a root genuinely has no member; a synthetic one is a lie the whole library then carries |
| ⭐⭐ **unwrapped on BOTH commit arms** | a single-field struct **shares its field's layout** ⇒ a leak is the right SIZE and the wrong bytes. ⚠ A leak on one arm only would make half the feature look correct |
| ⭐⭐ **`NeedsBoxing` railed by AGREEMENT with the corpus** | `DetermineKind` is `private` ⇒ ⛔ a hand-written type list would drift. The rail asks the BUILDER what it produced |

⭐ **The `TheEditActuallyLandsTests` case Batch 96 wrote to assert the defect on purpose
(`AScalarVariablesEditGoesNowhere`) is FLIPPED to `AScalarVariablesEditLands`.**

### `97b` — **the row menu asked the row kind** *(`BP-361`)* · commit `a2a3e2c`

📐 `DrawRowMenu` gated both items on `row.CanEverBeWritten`, a property of the ROW. The thing that
decides is `VariableEditPolicy.Resolve(action, runState, row)`. ⇒ during **replay** and on a **stale**
row the menu offered *"Edit value…"* and the click dead-ended.

⭐⭐ **Only `Denied` greys.** `ReadOnly` **deliberately opens** — that is Batch 96's read-only view, and
collapsing the two states would hide an inspectable dialog. ⛔ **No second copy of the matrix**:
`VariableEditGesture.Decide` returns `(Enabled, DisabledReason, OpensReadOnly)` from the one policy.

### `97c` — **call the blueprint live writer** *(`BP-358` → closed, `BP-364` split)* · commit `8329bb1`

⛔⛔ **The corruption question, and the answer:**

| | |
|---|---|
| the READ path | `int start = WorkingStateLayout.ComponentOffsetOf(field.OffsetBytes);` **then slices** |
| the WRITE path | `TryWriteWorkingStateField` **applies `ComponentOffsetOf` ITSELF** |
| ⇒ ⭐⭐⭐ | **the resolver must return the RAW `field.OffsetBytes`.** ⛔ Copying the read's `start` double-applies the header and scribbles on the **neighbour** — and `Blackboard1024` is one component BTree, HSM and Blueprint share at disjoint offsets, so the neighbour may not even be a blueprint's |

⭐ **Was the read's walk reusable?** ⛔ **No, and the report says so rather than pretending.** The read
**ITERATES every field** to produce values; this **LOOKS ONE UP** by name. The two loops cannot be one
loop without restructuring a hot read path. ⭐ **The two TABLES are shared** *(`mapIndex.StateLayout.Fields`
then `def.StateFields`)*, and their **ORDER** — the thing that can actually drift — is railed.

⭐ **`AiPrimitive` only.** An `Instance` blueprint's fields are offset within a per-instance payload, a
different address space. ⚠ Answering for one would not mis-report a value; it would corrupt memory.

### `97d` — **`R-76`'s second clock** *(`BP-362`)* · commit `b896c84`

📌 `R-107`/`R-76`: **VALUE** *(every brain tick, all rows)* and **BINDING** *(only on selection change,
chameleon rows only)*. 📐 `VariableRowSampler` had only the pulse ⇒ ⛔ **a selection change re-evaluated
nothing, and while time is stopped it never would.**

⭐ `EntityBindingFrame` mirrors `BehaviorFrame` — a **polled counter**, bumped by `SharedEntitySelection`
and nothing else. ⛔ A subscription would need every panel to register and unregister *(the `R-67` shape)*.
⚠ **Over-firing is harmless, under-firing is the bug** ⇒ it bumps on every real change.
⭐ `VariableChangeMonitor` resets its baseline when the binding moves, so a new entity's first sample is
not a false highlight — and the gap rail
`TheSelectedEntityReachesEveryPerspectiveTests` is FLIPPED to
`ASelectionChangeIsVisibleImmediatelyEvenWithTimeStopped`.

---

## 2. ⭐⭐⭐ **WHOSE OBJECT · WHICH LAYER IS FAKED** — per rail

📌 `M-22` *("'is it connected?' is not 'does anything flow?'")* and `M-29` *(say which layer a rail fakes)*.

| rail | takes its input from | ⛔ FAKES |
|---|---|---|
| `TheScalarBoxTests` *(37)* | the **real** `ComponentEditServiceBuilder().Build()` document, and `NeedsBoxing` is checked against **what the builder actually produced** | nothing — no draw layer involved |
| `TheEditGestureTests` *(106)* | the **real** `VariableEditPolicy` over the full `(action, runState, rowKind, stale)` matrix; the source rail reads `VariableTableControl.cs` | ⭐ the **DRAW** layer — `R-21`/`R-62`, greying is an ImGui call no headless rail can observe |
| `TheBlueprintLiveWriteLandsTests.APausedEdit_LandsInTheBlackboard` *(and the offset/refusal rails)* | the **real** `PerspectiveWorkspaceServices` → `CreateRegistrar` → binder → launcher → `StructEdit` session → `VariableEditCommit` → `BlueprintLiveValueWriter` → **real `BlueprintDebugSession`** | ⭐ the **DRAW** layer *(the gesture is raised by calling `OnEditValue`)* **and the ECS DRAIN** — staging→world is `StagedFieldWriteEntryPointTests`' job. ⚠ Neither half alone is the chain |
| `TheBlueprintLiveWriteLandsTests.TheCompositionRootHandsBlueprintALiveWriter` | ⭐ **`EditorSubsystem.cs` AS TEXT** | ⛔ **runs nothing.** ⭐ It is the ONLY rail here that can see a composition-root defect — 📌 `R-67`: *a rail that builds its own composition root cannot see one*, and every behavioural rail above builds its own |
| `TheRowSamplesOnThePulseTests` *(binding-clock arms)* | accessor calls counted **through `VariableRowSampler`** | ⭐ the repaint — a panel that did not repaint has nothing to re-sample |

⭐⭐ **The unrailed draw, stated plainly:** ⛔ **no rail in this batch asserts that anything RENDERS.**
`R-21`/`R-62` stand — the surface layer is unrailed **by construction**, and every claim above is about
the object the draw path reads, never about pixels.

---

## 3. ⭐⭐ REVERT PROBES — **one per production edit, never delegated**

⛔ **Every probe was un-applied with the INVERSE EDIT**, never `git checkout --`.

| # | the probe | ⭐ went RED |
|---|---|---|
| **P1** | drop `writeLive:` from `EditorSubsystem`'s Blueprint `CreateRegistrar` | `TheCompositionRootHandsBlueprintALiveWriter` — 1 failed / 10 passed |
| **P2** | `ResolveWorkingStateField` returns the **CONVERTED** offset, **debug-map arm** | `TheDebugMapWins…` — ⚠ **see the finding below** |
| **P2b** | the same, **`def.StateFields` fallback arm** | `TheOffsetIsRaw_AndTheHeaderIsAppliedExactlyOnce(null)` |
| **P3** | drop the width guard in `BlueprintLiveValueWriter.Write` | `APayloadOfTheWrongWidth_IsRefused` — both widths |
| **P4** | drop the `def.Kind != AiPrimitive` refusal | `ADispatchKindLaidOutAnotherWay_ResolvesToNothing` |

### 🔴 **What P2 found, and it is the useful part of this batch's probe pass**

⛔⛔ **The first version of the offset rail covered ONE of the two resolution arms and stayed GREEN under
P2.** The corruption I was pinning could have shipped through the debug-map arm untouched.
⇒ ⭐⭐ **two tables means two places the convention can be got wrong, and a rail over one of them proves
nothing about the other.** The rail is now a `[Theory]` over both arms, and **each arm's probe reddens
its own case**.

⚠ **`97a`, `97b` and `97d` probes** were run and reported in their own commits *(`d59fb23`, `a2a3e2c`,
`b896c84`)*; the four above are `97c`'s.

---

## 4. ⭐⭐⭐ THE GATE TABLE — **the seven-row contract**

⭐ **Every gate run UNFILTERED with `EXIT=` shown**, except where a documented filter applies *(named)*.
⭐ **Base for every RED: `d5f18e2b2`.**

| gate | `--no-build`? | command | result | Δ baseline |
|---|---|---|---|---|
| solution build | — | `dotnet build IOS-IG-SimHost.sln` | **0 errors**, 69 warnings · `EXIT=0` | — |
| **AiShared** | ✅ yes | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --no-build` | **1705 / 0 / 0** · `EXIT=0` | **+145** *(1560)* |
| **BTree.Editor** | ✅ yes | `… Hrot.BTree.Editor.Tests --no-build` | **622 / 0 / 0** · `EXIT=0` | **0** |
| **Hsm.Editor** | ✅ yes | `… Hrot.Hsm.Editor.Tests --no-build` | **554 / 0 / 0** · `EXIT=0` | **0** |
| **Blueprints** | ✅ yes | `… Hrot.Blueprints.Tests --no-build` | **3814 / 0 / 10 skip** · `EXIT=0` | **+13** *(3801)*, skips **0** |
| **Hrot.Editor** | ✅ yes | `… Hrot.Editor.Tests --no-build` | **201 / 0 / 0** · `EXIT=0` | **0** |
| **Breakpoints** | ✅ yes | `… Hrot.Diagnostics.Breakpoints.Tests --no-build` | **143 / 0 / 0** · `EXIT=0` | **0** |
| **Generators** | ✅ yes | `… Hrot.AiEditor.Generators.Tests --no-build` | **277 / 0 / 0** · `EXIT=0` | **0** |
| **Persistence** | ✅ yes | `… Hrot.AiEditor.Persistence.Tests --no-build` | **143 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.Core** | ⛔ **NO** *(out of solution)* | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests` | **211 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **NodeEditor.UI** | ⛔ **NO** *(out of solution)* | `… NodeEditor.UI.Tests` | **135 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **Fhsm** | ⛔ **NO** *(out of solution)* | `… FDP/ExtDeps/FastHSM/tests/Fhsm.Tests` | **300 / 0 / 0** · `EXIT=0` | **0** |
| ⛔ **StructEdit** | ⛔ **NO** *(out of solution)* | `… FDP/ExtDeps/StructEdit/tests/StructEdit.Tests` | ⚠ **191 / 1 / 0** · `EXIT=0` | **0** — ⭐ **RED CONFIRMED PRE-EXISTING**, below |
| **Fdp.Presentation** | ✅ yes | `--filter "FullyQualifiedName~Fdp.Presentation.Tests.WindowManager"` *(`BP-337`)* | **146 / 0 / 0** · `EXIT=0` | **0** |
| **Fdp.Toolkits** | ✅ yes | full run *(📌 `DEBT-AIB-030` — identity ROTATES)* | **1964 / 0 / 0** · `EXIT=0` | ⭐ green **this run** — ⛔ **not evidence**, see below |
| `tracker-counts.py --check` | — | `python3 scripts/tracker-counts.py --check` | **OK — open 78 / done 221 (+1 refuted)** · `EXIT=0` | done **+4** |
| `rulings-check.py` | — | `python3 scripts/rulings-check.py` | **73 / 73 verified** · `EXIT=0` | — |
| `design-digest.py --check` | — | `python3 scripts/design-digest.py --check` | **51 docs OK**, every `INVENTORY` present · `EXIT=0` | — |

### ⭐ Row 3 — **golden movement, as a DIFF SHAPE**

⛔ **ZERO golden files moved. Zero asset `.json` files moved.**
`git diff --name-only d5f18e2b2..HEAD | grep -iE "golden|\.json$|Assets/"` returns **nothing**.
⇒ ⭐ the diff is **code + tests + docs only**: 27 files, +1909 / −84.

### ⭐ Row 4 — **every RED confirmed PRE-EXISTING against the base**

| RED | evidence |
|---|---|
| `StructEdit.Tests.Reflection.DocumentBuilderTests.Build_CircularReference_CircularFieldIsUnsupported` | ⭐ **Clean worktree at `d5f18e2b2`**, full build, same run: **1 failed / 191 passed** — **identical on both sides.** ⇒ ⛔ not this batch's. 📌 Filed as **`BP-363`**: it is `R-104`'s static cycle fence missing from StructEdit's document builder |

### ⭐ Row 5 — **the working tree is CLEAN after every suite run**

`git status --short` after the last gate: **empty**. ⇒ ⛔ nothing regenerated a golden behind the tests.

### ⭐ Row 6 — **quarantine counts**

| | baseline | now |
|---|---|---|
| **Blueprints skips** | 10 | ⭐ **10** |
| every other suite | 0 | ⭐ **0** |

⛔ **No new skip.** 📌 *A new skip is a finding, not a fix* — there is none to report.

### ⚠ `Fdp.Toolkits.Tests` — **stated, not claimed**

📌 `DEBT-AIB-030`: **seven distinct tests, and the identity ROTATES between runs** ⇒ ⭐ **neither a red
nor a green is evidence.** This run was green at **1964 / 0 / 0**; ⛔ **that is reported as an
observation, not as a pass.**

---

## 5. ⭐⭐ Rule 4 — **what landed on the coordinator branch DURING this run**

⭐ Re-pulled before the final commit. Two commits arrived after my frozen sha `ea353f6`:

| commit | what it does | ⭐ effect on Batch 97 |
|---|---|---|
| `edcff18` | corrects `R-108` — **`S5` shipped in Batch 65**, the `Type` picker is **not** blocked | ⛔ **none** — `R-108` is the *Properties* dialog, which is **Batch 98** |
| `d522f9d` | queues the Properties dialog for Batch 98 with its answer settled | ⛔ **none** — it explicitly says *"write it WHEN 97 RETURNS"* |

⇒ ⭐⭐ **Nothing INVALIDATES an item.** 📌 Scope stayed frozen at `ea353f6`; ⛔ nothing was adapted and
nothing was reverted.

---

## 6. ⭐ What Batch 98 inherits

| | |
|---|---|
| ⭐⭐ **`BP-364`** *(new, `RW-H`)* | BTree/HSM live writing — **a capability, not a wire.** Their `writeLive` is `null` **deliberately** and the asymmetry is railed, so a future guess cannot land quietly |
| ⭐ **`BP-363`** *(new, `RW-L`)* | StructEdit's document builder has no static cycle fence — `R-104`'s ruling, applied to the other serializer |
| ⭐ **`BP-359`** | the *Properties* dialog — ⭐ **already queued by the coordinator**, answer settled in `R-108` |
| ⭐ **`BP-360`** | the outline's dead Watch entry — ⛔ still not started; `96e`'s carry-over |

⛔ **Nothing else.** ⭐ `BP-356` and `BP-358` are closed.
