<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this whole file — what W4 built, the one limit it did not build, and the gate table.
stale-below: nothing.
known-rot: none.
known-conflict: none.
design-basis: DESIGN_Staged_Live_Write.md §3 (classDiagram) · §4 fork A · §5 · §7 ·
  HANDOFF_UI_W3_W4_W5_The_Staged_Write.md §0/§1.
-->
# ⭐⭐⭐ REPORT — **`W4`: the shared yellow**

> **Design:** 📄 [`DESIGN_Staged_Live_Write.md`](../DESIGN_Staged_Live_Write.md) — §3's `classDiagram`,
> §4 **fork A**, §5 (the seam), §7 (both surfaces agree)
> **Handoff:** 📄 [`HANDOFF_UI_W3_W4_W5_The_Staged_Write.md`](HANDOFF_UI_W3_W4_W5_The_Staged_Write.md) §1
> **started at** `71b30c3d` *(marker `67149883`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ IDs allocated: **`BP-406`** *(built)* · **`BP-407`** *(a stated limit, open)*
> ⛔ **No diagram in this report** — 📌 the `2026-08-21` rule: diagrams live in the DESIGN. §3's
> `classDiagram` is what this batch built; **obligation ③ is answered in §1 below.**

| §1 handoff item | verdict |
|---|---|
| `DataBreakpointManager` implements `IStagedWrites` | ✅ **built** |
| `StagedWriteView` at the composition root, shared *(fork A)* | ✅ **built** |
| the row's `Pending` **and** its displayed value derive from `TryGetPending` | ✅ **built** — ⚠ one stated limit, `BP-407` |
| auto-clears on drain | ✅ **built** — and railed through a real drain |
| ⛔ do NOT wire `MarkPending`/`ClearPending` | ✅ — **DELETED**, see §2 |
| ⛔ **`W3` NOT started** *(handoff §0's order)* | ⛔ **untouched** — `MIN`'s `WriteFieldNow` still stands |

---

## 1. ⭐⭐⭐ OBLIGATION ③ — **I checked the design's UML before building, and here is the match**

📄 §3 carries **one `classDiagram` (6 boxes) and one `sequenceDiagram` (6 participants).**

| §3 box | what I built | match |
|---|---|---|
| `IStagedWrites` *(NEW, shared)* | — *(the coordinator's; already in `Fdp.Core/Abstractions`)* | ⭐ used unchanged |
| `DataBreakpointManager` *(implements it)* | 4 members added beside the existing queue | ⭐ **exact** |
| `StagedWriteView` *(AiShared/Variables, composition-root)* | `StagedWriteView` + `StagedFieldAddress` + `ResolveStagedField` | ⚠ **+2 types — see below** |
| `VariableChangeMonitor` *(reads the shared view)* | `Observe(row, runState, StagedWriteView?)` | ⭐ **exact** |
| `BlueprintLiveValueWriter ..> DataBreakpointManager` | unchanged, **+ `ResolveStagedField`** | ⚠ **a deviation, argued below** |
| `ResumeAndDrainSystem` | ⛔ **not mine** — TIME lane | — |

### ⚠ Two deviations, both stated rather than made silently

| # | deviation | why |
|---|---|---|
| **①** | §3 draws `StagedWriteView` with `IsPending(origin, entity)` / `TryGetTyped(origin, entity, …)` and **no other types**. I added **`StagedFieldAddress`** and the **`ResolveStagedField`** delegate. | ⭐ `IStagedWrites.TryGetPending` keys on **`(entity, typeId, byteOffset)`**, and a row knows only `(assetId, variablePath)`. The design's own §4 names the cost — *"per-row resolve `origin → (type, offset)`"* — but draws no box for it. ⛔ A resolver had to exist; the choice was **where**. It is a **delegate** because the only resolver in this codebase is `IBlueprintDebugSession.ResolveWorkingStateField`, in an assembly `Hrot.Editor.AiShared` must not reference *(`Q32` ruling 6 — the table is cross-host)*. |
| **②** | §3's `BlueprintLiveValueWriter` has one member, `WriteLive`. I added **`ResolveStagedField`** + **`SelectedEntity`** to it. | ⭐⭐ 📌 `R-13`. The write ALREADY resolves the address *(step 3 of `TryWrite`)* and ALREADY decides the entity *(`R-78`'s chameleon sentinel)*. ⛔ Resolving a second time, anywhere else, is how the yellow comes to paint a value the write never staged. ⚠ And that class's OWN remarks forbid the alternative: *"a narrowing delegate pair here would have put the resolve→write join in an unrailed adapter at the composition root — exactly the `R-67` shape this class exists to close."* |

⭐ **§3's `sequenceDiagram` is built as drawn** — `WriteLive` → `StageFieldMutation` → both panels query
the manager on the next Draw with no tick between → the PreFrame drain applies the bytes → both
re-sample. ⚠ **Step 6 (`K->>M: DrainInto`) is exercised by the rail calling the seam directly**; the
kernel system that will call it is the TIME lane's.

---

## 2. ⭐⭐ THE ONE DELETION — **the unwired flag was NOT wired**

📄 §4 fork A, verbatim: *"⛔ the unwired `MarkPending`/`ClearPending` flag is NOT wired — it is collapsed
into the query (`R-13`: route, don't duplicate)."*

📐 **Measured before deleting** *(§2 `I3` agreed)*: `MarkPending` · `ClearPending` · `IsPending` ·
`Entry.Pending` — **zero production callers**, three rails. Built in Batch 84, unwired for four batches.

⭐⭐ **Why deletion beats keeping it around.** A flag has to be **cleared** by whoever applies the write.
📌 `R-126` made the drain a **PULL from the tick loop** for exactly the reason that *"no path can forget
to raise what is never raised"* — ⛔ keeping the flag would have put the forgettable half straight back
in, next to a mechanism that does not need it.

⭐ **The three rails were re-expressed, not deleted** — each now stages through a `StagedWriteView` and,
in one case, gained the **auto-clear** assertion it could not previously make.

---

## 3. ⭐⭐⭐ THREE THINGS THE DESIGN DID NOT SAY, MEASURED

| # | finding |
|---|---|
| **A** | ⭐⭐ **`Observe` was suppressing yellow on tick-less rows.** 📐 Two early-outs returned `RowHighlight.None`: *no `AssetTick` source* and *unbytable value*. Both exist to stop the **RED** cache recording a change it could never clear — ⛔ neither has anything to say about a staged edit. ⇒ `pending` is now computed **first** and both arms return `new RowHighlight(false, pending)`. |
| **B** | ⭐⭐ **The display needed BOTH arms rewritten.** 📐 `VariableValueFormatter.Decode:190` — *"the OBJECT arm, preferred when present"* — and Blueprint's live rows arrive **already decoded** through that arm. ⇒ overriding only the bytes would have left a Blueprint row **yellow while showing the applied value**, the exact divergence §7 removes. `ReadValueObject` is cleared and `ReadWritten` forced true *(else `Cell`'s `if (!row.WrittenNow) return "(pending)"` hides the designer's own number)*. |
| **C** | ⭐⭐ **`_pendingMutations` is a QUEUE, so `TryGetPending` must walk it all.** ⚠ Two edits before a drain leave two entries; the **first** is superseded, and the drain applies them **in order**. ⇒ **last match wins**, and it is railed — invisible in any single-edit test. |
| **D** | ⚠ **Order matters in `Build()`.** The monitor observes the **sampled** bytes; the staged override runs **after**. ⛔ The other order makes a designer's own edit read as *"the sim changed it"* and paints the row RED — 📄 §1: *"never red and yellow for the same cause."* |
| **E** | ⚠ **`RowHighlight(changed, pending)` still allows BOTH.** ⭐ I briefly collapsed it to `changed && !pending` and a Batch-94 rail correctly went red: *"the sim moved this while my edit was still staged"* is a **different fact**, and 📌 `RowHighlight` exists so the **renderer** decides which colour wins. ⇒ reverted; §1's sentence is honoured **upstream**, by what `changed` is computed from. |

---

## 4. ⭐ THE WIRE — **one shared instance, six hosts, and the control the rule asks for**

📌 The `2026-08-16` rule: *"a production caller that HAS a dependency must PASS it"*, control =
*"a forwarding rail PER DEPENDENCY, asserted on the CONSTRUCTED OBJECT."*

```
EditorSubsystem  →  PerspectiveWorkspaceServices.StagedWrites  (ONE instance)
                 →  PerspectiveWorkspaceRegistrar (ctor arg, assigned FIRST)
                 →  AttachEditGestures(host)  →  host.TableModel.StagedWrites
```

⭐⭐ **`IVariableTableHost.TableModel`** *(no default body — `U-5`/`BP-230`)* is why this is one line and
not four assignments: 📌 Batch 87's handoff knew of **three** table hosts and the graph found **four**;
there are **six** today. ⛔ A seventh is wired with no new line anywhere.

⚠⚠ **Two ordering hazards, both real, both handled:**
- `_stagedWrites` is assigned **first** in the registrar constructor — that constructor calls
  `AttachEditGestures` several times below, and a later assignment would wire the earlier hosts with
  `null`. 📌 The same shape as `L3.3`'s first wiring, which registered nothing.
- `blueprintLiveValueWriter` **moved up** in `EditorSubsystem`, beside `facetEditService`: the services
  bag is built before the Blueprint registrar, and the view resolves through that object. ⛔ Nothing
  else about it changed.
- All three of `StagedWriteView`'s arms resolve **at call time**: `_bpManager` is assigned at `:1127`,
  **after** the bag is built.

---

## 5. 🛑 `BP-407` — **the one thing I did not build, and why**

📐 `ApplyStagedValues` skips a row whose **`ClrType` is null**: the byte path decodes through it
*(`VariableValueFormatter.Decode:200`)*, so forcing the byte arm would render **`<unreadable>`** in place
of a real number.

⭐ Such a row **still goes yellow** — the highlight is computed independently — so the designer still
sees *"staged"*; only the optimistic value is missing. ⚠ Stated rather than defaulted silently: 📌 the
silent-default rule's own qualifier is *"a default is only a defect when the caller could have done
better"*, and this row genuinely cannot say what type its bytes are. ⇒ **the fix belongs at the source**,
not here. ⛔ Not scheduled — no measured production row hits it.

---

## 6. ⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L5`'s table** *(base sha `71b30c3d`, unchanged — the coordinator branch has not moved)*.

| gate | env | `--no-build` | result | Δ |
|---|---|---|---|---|
| **solution build** | — | ⛔ builds | ⭐ **0 errors, 0 compiler warnings** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | ✅ | **1844 / 0 / 0** | ⭐ **+8 — mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | ✅ | **3886 / 0 / 10** | **0** |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | ✅ | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | ✅ | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | ✅ | **209 / 0 / 0** | **0** |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | ✅ | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | ✅ | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ✅ | ⚠ **262 / 2 / 0** | **0** — the `D003_*` pair, unchanged |
| ⚠⚠ `Fdp.ModuleHost.Tests` | **Xvfb** | ✅ | 🔴 **192 / 6 / 0** | **0** — ⛔ **CROSS-LANE, see §6a** |
| **tracker** | — | — | ⭐ **OK — open 86 / done 256 (+1 refuted)** | +1 open, +1 done |
| **rulings** | — | — | ⭐ **22/22 verified**; ⚠ 2 staleness WARNs *(pre-existing — `CLAUDE.md`, `PLAN_Time_System_Refactor.md`)* | — |
| **design digest** | — | — | ⭐ **OK** *(56 docs, STATUS + INVENTORY + UML all present)* | — |
| **working tree** | — | — | ⭐ **CLEAN after every suite run** | — |

⚠ **The 12 build warnings are all `NU1902`/`NU1903` NuGet advisories on `MessagePack 3.1.4`** — ⛔ **zero
compiler warnings**, and they are pre-existing and unrelated to this diff. `L5`'s table said *"0
warnings"* because it grepped a filtered line; ⭐ this is the same tree, counted honestly.

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **22 files: 17 modified, 5 added, 0 deleted; +1293 / −49 lines** *(of
which 2 files / +205 are this report and the tracker rows)*. ⛔ No `.approved.` / golden / snapshot /
`.verified.` file in the diff — checked by name. ⭐ The five additions are `StagedWriteView.cs`, three
rail/helper files, and this report.

### 6a. ⚠⚠ `Fdp.ModuleHost.Tests` — **6 RED, CROSS-LANE, NOT MINE, NOT FIXED, UNCHANGED**

⭐ **Identical to `L5`'s table** — same six, same subjects *(Convoy · SoD · provider assignment)*:

```
ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy
ConvoyAutoGroupingTests.AutoGrouping_SameTierAndFreq_SharesProvider
HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider
HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically
ConvoyIntegrationTests.ConvoyIntegration_5Modules_ShareSnapshot
ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced
```

📐 My diff touches **zero** files under `Fdp.ModuleHost`, `Fdp.Toolkits/Time/` or `Hrot.Orchestrator`.
⛔ **NOT FIXED, deliberately** — 📌 `R-128`: *"a cross-lane edit is a STOP-and-report, not a judgement
call."* ⭐ Reported with names so the time lane can triage; ⛔ **no `BP-` row filed** — the id prefix
belongs to their lane.

---

## 7. ⭐⭐ THE RAILS — **8 new cases, 3 re-expressed, and 5 revert probes that all went red**

| rail file | what it drives |
|---|---|
| ⭐⭐⭐ `TheStagedWriteShowsYellowTests` *(4)* | **the REAL `DataBreakpointManager` through the REAL `IStagedWrites`** — ⛔ not a double: the claim is that the production **stager** and the production **query** agree about an address, and the typeId comes from `ComponentTypeRegistry` on both sides. Covers the handoff's stated rail, sibling-field discrimination, last-write-wins, and `IsRewound`. |
| ⭐⭐ `TheSharedYellowReachesEveryTableTests` *(4)* | the forwarding, on the **CONSTRUCTED** models, with **`Assert.Same`** — ⛔ two correctly-wired-but-different views would satisfy every non-null assertion and reproduce the exact divergence §7 forbids. ⭐ Includes a **negative control** *(no source ⇒ every model's view is null)*. |
| ⭐ `VariableTableRailsTests` *(3 re-expressed)* | the collapsed-group inheritance and the two-independent-states pair, now through the shared query — ⭐ one of them gained the **auto-clear** half. |
| ⭐ `EveryTableHostIsGestureBoundTests` *(2 doubles updated)* | the `TableModel` member has no default body, so every implementer answers. |

| # | probe *(the change un-applied by its INVERSE edit, never `git checkout`)* | result |
|---|---|---|
| **1** | `TryGetPending` returns the **first** match | 🔴 `TwoEditsBeforeADrain` |
| **2** | `Observe` computes `pending = false` | 🔴 **2 of 4** |
| **3** | drop the `ApplyStagedValues` call | 🔴 **2 of 4** |
| **4** | drop the registrar's forwarding line | 🔴 **3 of 4** |
| **5** | `EntityFor` returns `origin.Entity` *(no chameleon rule)* | 🔴 the Details-shaped half |

---

## 8. ⭐ LANE CHECK — **and what is BLOCKED**

⭐ Files touched: `Hrot.Editor.AiShared` + tests · `Hrot.Diagnostics.Breakpoints` · `Hrot.Editor`
*(composition root)* · `Hrot.Blueprints.Editor` *(two `TableModel` forwards)*.
⛔ **Nothing under `Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel` or the integration
tests** *(`R-128`)*. ⭐ ids are **`BP-`**.

| | |
|---|---|
| 🛑 **`W3`** | ⛔ **NOT started, deliberately** — 📄 handoff §0: *"`W3` removes `MIN`'s `WriteFieldNow`. Do NOT do that until the drain is LIVE-WIRED."* `MIN`'s direct write is untouched. |
| 🛑 **the wire** | ⭐⭐ **This is the coordinator's cue, and the precondition is MEASURED as met.** 📐 `ResumeAndDrainSystem` is already in my tree at `FDP/Engine/Fdp.ModuleHost/Time/ResumeAndDrainSystem.cs` *(the coordinator's `71b30c3d` merge)*, and 📐 `EditorSubsystem` references it **nowhere** ⇒ the wire is genuinely absent. ⭐ `DataBreakpointManager` **is** an `IStagedWrites` now, so design §8's `_kernel.RegisterGlobalSystem(new ResumeAndDrainSystem(_bpManager))` compiles today. ⛔ **I did not add it** — the user's instruction was explicit: *"Ping the coordinator when `W4` lands and I'll add the wire (1 line)."* |
| 🛑 **`W5`** | blocked behind `W3`. |
| ⛔ **`BP-407`** | §5's stated limit. |
| ⛔ carried | **`BP-399`** *(`L3`'s four rows)* · **`BP-403`** *(`L4.4`'s View-menu half)* · **`BP-405`** *(`WatchPanelWindow`, unblocks on `Q44-B`)* · **`L6`** *(`L6.1` extracts `PerspectiveWorkspace`)*. |
