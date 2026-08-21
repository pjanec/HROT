<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what Batch 103 built, measured and found.
stale-below: nothing.
known-rot: none.
known-conflict: R-121 renames the perspective key "Editor" → "Scenario". The layout
  shipped by 103a uses the CURRENT keys and must migrate when that rename lands. §5.
-->
# ⭐⭐⭐ REPORT — Batch 103 · **one shared layout, a staleness rail, and `BP-385`'s blast radius**

> **Scope frozen at** `d3c370ffb` · **branch** `claude/hrot-implementation-j1jvin` · **started at**
> `bd5f86f`
> ⚠ **The started-marker says `bd5f86f`, not the `d3c370ffb` the handoff asked for.** ⭐ Deliberate and
> stated here rather than silently: `bd5f86f` is the coordinator's *own* two commits later on the same
> branch, so rule 7 *("re-sync from the coordinator branch at the START of every run")* pointed at it.
> ⛔ It contains **no change to any Batch 103 item** — it is the visual-check findings note (`R-125`,
> `M-40`). ⇒ the frozen scope is unaffected; only the marker's sha differs.
> ⭐ **Re-pulled the coordinator branch before the final commit** *(rule 4)* — ⚠ **two commits landed
> DURING this run**, and they are **code**, not design: §7.

| item | verdict | one line |
|---|---|---|
| **`103a`** | ✅ **done** | the layout is **one unit** — one convention object, one user directory, reset **before** `SetupImGui` |
| **`103b`** | ✅ **done** | the shipped default cannot silently orphan a window id — ⚠ and it **found three real ones on its first run** |
| **`103c`** | ✅ **done** *(measurement)* | `BP-385`'s blast radius is **4 owners / 3 `Draw`-driven / 1 deliberately constant** — ⛔ **and the obvious fix collides with Batch 94c** |

⭐ **IDs I allocated:** `BP-386` · `BP-387` · `BP-388`. ⭐ `BP-385` **updated, still open** *(the
measurement is in, the decision is not)*.

---

## 1. ⭐⭐⭐ `103a` — **the layout is ONE UNIT, in TWO places**

📌 **The state before:** two files, two roots, and **nothing owning the pair**.

| file | held | root |
|---|---|---|
| `imgui.ini` | docking geometry | `%LocalAppData%/HROT/` — ⚠ computed **twice, independently** (`RaylibPresentationShell`, `FdpApplication`) |
| `fdp_windows.json` | open/closed · active perspective · UI scale | ⛔ **beside the exe** — the `AppContext.BaseDirectory` fallback won because **nobody passed the path** |

⇒ ⛔ **a reset of one is a HALF-reset**, and a clean rebuild wiped the second.

### ⭐ What was built

📄 **[`LayoutPaths.cs`](https://github.com/pjanec/HROT/blob/claude/hrot-implementation-j1jvin/FDP/Engine/Fdp.Presentation/ImGui/WindowManager/LayoutPaths.cs)** —
the whole convention in one place, ⭐ **with the app name as a PARAMETER**: the shared assembly carries
the convention, the host carries the name *(the design's constraint 1)*.

| ⭐ | |
|---|---|
| **①** | `layout/default/{imgui.ini,fdp_windows.json}` → **`CopyToOutputDirectory=PreserveNewest`**. ⭐ The files were already committed; ⛔ this batch authored no layout |
| **②** | ⭐⭐ **one user directory holds both** — the two `LoadSettings`/`SaveSettings` call sites now **pass the path**. ⛔ **No `WindowManager` change**: the seam existed and was unused, exactly as the design said |
| **③** | **reset-on-run**, dev default **ON** (`--reset-layout=false` to opt out) — a force-copy of the shipped pair over the user pair |
| **④** | exit saves to the **user** location — ⛔ only the location changed, not the moment |
| **⑤** | **`File ▸ Layout`** — the mode **indicator** (checkable) + **Save current as default**, ⛔ **disabled WITH ITS REASON**, not hidden |
| **⑥** | **one-time migration** from the exe-adjacent json. ⭐ The new file's *presence* is the marker ⇒ ⛔ no second piece of state to fall out of step |
| **⑦** | ⛔ the tracked root `imgui.ini` **deleted** — 283 lines, also `.gitignore`d, **and the app never read it** |

### ⛔⛔ THE ORDER IS THE DEFECT THAT WOULD HAVE SHIPPED

⭐⭐⭐ **ImGui reads the ini when the path is installed at `SetupImGui`.** ⇒ a reset performed *after* it
lands on disk and is **ignored until the next run** — ⚠ the classic *"it works on the second launch"*
bug, which looks exactly like the reset not working at all.

⇒ migrate → reset → **then** `SetupImGui`. 📄 `LocalWindowController:50-71`.

### ⚠ Two of the three "measure, do not assume" items

| ⚠ the handoff's question | 📐 measured |
|---|---|
| **the ini path is computed TWICE** | ⭐ **Byte-identical and independent.** ⇒ both route through `LayoutPaths` now; `FdpApplication` keeps its own app name |
| **`%LocalAppData%` is Windows-only** and ⭐ **the frame rails run on LINUX** *(`R-124`)* | ⭐ `Environment.SpecialFolder.LocalApplicationData` resolves on Linux to `~/.local/share` (or `$XDG_DATA_HOME`) ⇒ **no platform branch needed**. 📐 Confirmed by the 9 `LayoutPathsTests`, which do real I/O on this Linux box |

### ⚠⚠ The third — **`ActivePerspective: "Blueprint"` on a cold start**

📄 `UX_Feature_Perspective_Restore.md` rules `BTree`/`HSM`/`Blueprint` **document-driven — never
restored**, because no document survives a restart and restoring one lands the user in an empty graph
workspace.

📐 **MEASURED: it falls back, and the OUTCOME is right.** ⛔ **But the MECHANISM is not the ruling** —
`LocalWindowController:121` validates the persisted name against **`ISubsystem.Name`**, ⛔ not against
`WindowManager.GetPerspectives()` and ⛔ not against any notion of *durable*.

⇒ ⭐ `"Blueprint"` is rejected because **no SUBSYSTEM is called Blueprint** — ⚠ **which would reject a
durable perspective just as readily**, and would accept a document-driven one the moment a subsystem
took that name. **Filed as `BP-388`.**

⭐ The rail `AColdStartDoesNotRestoreADocumentDrivenPerspective` pins the **outcome** the ruling wants,
⛔ and its own doc comment says it does **not** bless the mechanism *(`M-29`)*.

### ⚠ A document claim that is now FALSE

📄 `UX_Feature_Perspective_Restore.md:49` — *"**no `fdp_windows.json` is committed** — verified,
`git ls-files` returns nothing"*. ⭐ **True when written; false since the dispatch's parent**, which
committed `layout/default/fdp_windows.json`. ⛔ **Not edited here** — it is the design session's
document and the `known-conflict` belongs in it, not in a drive-by fix from this lane.

---

## 2. ⭐⭐ `103b` — **a rail that the shipped default is not STALE**

⭐⭐⭐ **The failure it prevents is certain, not hypothetical:** the json names **55 window ids**, and
`WindowManager` **skips unknown ids BY DESIGN**. ⇒ rename a window and its entry silently orphans — the
layout still loads, that window just never appears, **and nothing says so.**

### ⛔ Why it is BEHAVIOURAL and not a grep

📐 **28 of the 55 ids appear NOWHERE as a string literal.** They are composed:
`$"ai_details_{suffix}"`, `$"ai_canvas_{assetKind.ToLowerInvariant()}"`. ⇒ ⛔ **a grep-based rail would
have reported 28 false orphans** and been switched off within a batch.

⇒ the rail builds **the three production registrars into a real `WindowManager`** and asks it
`RegisteredWindowIds`. 📌 `M-29`: what is faked is the icon atlas and the shell; ⛔ the registration is
the production path.

### ⚠⚠ IT WENT RED ON ITS FIRST RUN — and that was information

📐 **Three orphans reported: `ai_canvas_btree` · `ai_canvas_hsm` · `ai_canvas_blueprint`.**
⭐ **Measured: they are REAL windows** — composed by `AiGraphCanvasWindow`, registered **outside** the
three registrars the rail builds.

⇒ ⭐⭐ **The fix was to teach the fallback to read an interpolated prefix, ⛔ NOT to relax the
assertion.**

### ⭐ Three judgements, weakest last — so a green cannot read as more coverage than it is

| # | judgement | claims |
|---|---|---|
| **①** | ⭐⭐⭐ **registrar-behavioural** — the id is in `RegisteredWindowIds` | the strongest: a real window really registered it |
| **②** | ⭐ **source literal** — the id appears verbatim in a `.cs` file | weaker: it exists in code, not proven registered |
| **③** | ⚠ **interpolated prefix** — the id's prefix matches a `$"…{`-composed registration | ⛔ **the weakest.** It is what rescues the 28, and the report **names which ids rest on it** |

⭐ **The final run reports ZERO orphans and ZERO unclaimed registrations.** ⚠ **Stated fairly:** the
weakest judgement carries the `ai_canvas_*`/`ai_details_*` family — ⛔ a rename *within* that prefix
would still pass. **Filed as `BP-387`** with that limit written down.

---

## 3. ⭐ `103c` — **`BP-385`'s blast radius, measured**

⭐ The handoff: *"measure the blast radius across table hosts, then decide — ⛔ do not rush a change that
touches every host."* ⇒ **measured; not changed.**

| 📐 | count | detail |
|---|---|---|
| `VariableTableModel` owners *(production)* | **4** | `WatchPanelWindow:77` · `AiVariablesWindow:58` · `AiWatchWindow:78` · `VariableDetailsSection:50` |
| **driven from `Draw`** | **3** | `AiVariablesWindow:141` · `AiWatchWindow:149` · `VariableDetailsSection:157` — each calls its own `SyncRunState()` |
| ⭐ **deliberately CONSTANT** | **1** | `WatchPanelWindow:81` — `RunState = VariableRunState.Running`, because *"a Watch only ever shows RUNTIME values"* |
| `RunState` assignments | **5** | the four above + `AiVariablesWindow:129`'s setter |
| `Build()` call sites *(production)* | **5** | ⚠ `WatchPanelWindow` calls it **twice** (`:83` construction, `:158` refresh) |
| `Build()` call sites *(tests)* | **62** | ⭐ the number that makes "just make `Build()` sync" expensive to be wrong about |

### ⛔⛔ The obvious fix COLLIDES with Batch 94c

⭐ The tempting change is *"`Build()` syncs from the source itself, so the frame is not the only thing
that can make the model current."*

⛔ **Batch 94c ruled the opposite for the VALUE half:** *sample on the pulse, render from cache* — **one
sample per row per behaviour frame.** ⇒ making `Build()` self-sync puts a **source read back on every
`Build()`**, which is the shape 94c removed. ⚠ **Run state is not the same thing as a value sample** —
⭐ **but it is the same seam**, and the two must be decided together, not one by accident.

### ⭐ My recommendation *(for the coordinator, not applied here)*

⭐⭐ **Give `VariableTableModel` an explicit `RunStateSource` and have `Build()` read it — not the panel.**
⇒ the three `Draw`-driven call sites collapse to one place, `WatchPanelWindow` passes a constant source
*(its ruling preserved, and now VISIBLE as a choice rather than a literal)*, and ⭐ **a headless reader
gets the right arm without knowing to drive a panel method.**
⚠ It is one source read per `Build()` — ⛔ **a delegate returning an enum, not a blackboard sample**, so
94c's cost argument does not transfer. ⭐ **But 94c is the reason this needs a nod before it is built.**

---

## 4. ⭐ REVERT PROBES — **each reddened, each un-applied by the inverse edit**

⛔ **Never `git checkout --`** to undo a probe.

| # | the probe | result |
|---|---|---|
| **①** | moved the reset to **after** `_shell.SetupImGui()` | ⭐⭐ **2 rails red** — the behavioural one (`TheResetIsVisibleToImGuiOnTheSameRun`) and the source-order one |
| **②** | added an **invented id** to `layout/default/fdp_windows.json` | ⭐ the orphan rail red, **naming the id** |

### ⚠⚠ One rail was FIXED BY its own probe — worth stating plainly

📐 Probe ① **did not redden at first.** ⭐ **Measured why:** the `RecordingShell` recorded the **LAST**
`SetupImGui` call, so a second call overwrote the observation and the rail passed while the reset ran
late. ⇒ it records the **first** now (`??=`), and the probe then reddened.

⚠ **And the repair of that probe broke something else:** my restore dropped `SetupImGui()` entirely.
⭐ **Caught by an existing rail** — `GZH012_2_OpenLocalWindow_IsIdempotent` asserts
`SetupImGuiCallCount == 1`. ⛔ Repaired with a direct edit, not a checkout.

---

## 5. ⛔⛔ THE `R-121` CONFLICT — **stated, not resolved**

📌 **`R-121`** renames the perspective key **`"Editor"` → `"Scenario"`**, with a layout migration,
because `OwningPerspective` and `CurrentPerspective` are **persisted**.

⇒ ⚠⚠ **the default layout shipped by `103a` uses the CURRENT keys.** ⭐ When the rename lands,
`layout/default/fdp_windows.json` **must migrate with it** — ⛔ or the shipped default silently stops
matching, and every developer's first run lands on a perspective that no longer exists.

⭐ **The rename is not this batch** *(handoff §5)*. ⭐⭐ **But `103b`'s rail will catch the window half**
— ⛔ **not the perspective half**: `ActivePerspective` is not a window id and no rail reads it.
⇒ ⭐ **that is a gap worth an item in the rename's own batch**, and it is why this section exists.

---

## 6. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **Batch 102's table**, base **`d3c370ffb`**. ⭐ Every suite run **UNFILTERED** unless the
row says otherwise, and every row states its **environment**.

| gate | command | result | Δ |
|---|---|---|---|
| **solution build** | `dotnet build --no-restore` | ⭐ **0 errors** | — |
| `Hrot.Editor.AiShared.Tests` *(Xvfb)* | `dotnet test --no-build` | **1723 / 0 / 0** | ⭐ **+3** — the `103b` rails |
| `Hrot.Blueprints.Tests` *(Xvfb)* | `dotnet test --no-build` | **3877 / 0 / 10** | **0** |
| `Hrot.Blueprints.Tests` *(**no display**)* | `env -u DISPLAY dotnet test --no-build` | **3869 / 0 / 18** | **0** |
| `Fdp.Presentation.Tests` *(filtered — see note)* | `--filter "WindowManager|LayoutPathsTests"` | **155 / 0 / 0** | ⭐ **+9** — `LayoutPathsTests` |
| `Hrot.ClusterRunner.Tests` | `dotnet test --no-build` | ⚠ **254 / 2 / 0** | ⭐ **+2 passed**, and **the 2 reds are PRE-EXISTING** — §6.1 |
| `Hrot.Smoke.Tests` | `dotnet test --no-build` | **4 / 0 / 0** | **0** |
| `Hrot.BTree.Editor.Tests` | `dotnet test --no-build` | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | `dotnet test --no-build` | **554 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | `dotnet test --no-build` | **201 / 0 / 0** | **0** |
| `Hrot.Breakpoints.Tests` | `dotnet test --no-build` | **143 / 0 / 0** | **0** |
| `Hrot.Persistence.Tests` | `dotnet test --no-build` | **143 / 0 / 0** | **0** |
| `Hrot.Blueprints.Generators.Tests` | `dotnet test --no-build` | **277 / 0 / 0** | **0** |
| `Fdp.Toolkits.Tests` | `dotnet test --no-build` | **1964 / 0 / 0** | **0** |
| ⚠ `NodeEditor.Core.Tests` *(**out of solution — BUILT**)* | `dotnet test` | **211 / 0 / 0** | **0** |
| ⚠ `NodeEditor.UI.Tests` *(**out of solution — BUILT**)* | `dotnet test` | **135 / 0 / 0** | **0** |
| ⚠ `Fhsm.Tests` *(**out of solution — BUILT**)* | `dotnet test` | **300 / 0 / 0** | **0** |
| ⚠ `StructEdit.Tests` *(**out of solution — BUILT**)* | `dotnet test` | ⚠ **191 / 1 / 0** | **0** — `BP-363`, pre-existing |
| **tracker** | `python3 scripts/tracker-counts.py --check` | ⭐ **OK — open 81 / done 242 (+1 refuted)** | ⭐ **+1 open / +2 done** — `BP-386` · `BP-387` closed, `BP-388` opened |
| **rulings** | `python3 scripts/rulings-check.py` | ⭐ **92/92 verified** · ⚠ 1 staleness WARN — §6.2 | — |
| **design digest** | `python3 scripts/design-digest.py --check` | ⭐ **59 docs OK** — STATUS, INVENTORY and UML all present | — |
| **working tree** | `git status --porcelain` | ⭐ **CLEAN after every suite run** — ⛔ no golden was regenerated | — |

⛔ **`Hrot.ClusterRunner.Integration.Tests` stays out** *(`BP-378` — it does not finish; OOM at
`EntityRepository..ctor`, `MAX_ENTITIES = 1_000_000` per harness).*

### ⭐ The `--no-build` column, explicitly *(gate-report contract row 2)*

| | |
|---|---|
| ⭐ **`--no-build`** | every project **in** the solution — the solution build above is what produced their binaries |
| ⛔⛔ **MUST BUILD** | `NodeEditor.Core` · `NodeEditor.UI` · `Fhsm` · `StructEdit` — ⚠ **out of solution**, so `--no-build` would report a **STALE BIN**. ⭐ They were built |

### ⚠ Why `Fdp.Presentation.Tests` is filtered

📌 `BP-337`, pre-existing and unchanged: the full suite does not run clean in this environment. ⭐ The
filter is the **standing** one from Batch 102's table, widened by exactly the new class
(`LayoutPathsTests`). ⛔ **Not narrowed to hide anything** — `WindowManager` is the area `103a` touched.

### ⭐⭐ 6.1 — the two `Hrot.ClusterRunner.Tests` reds are **PRE-EXISTING**, and I proved it at the source

⚠ `DataDrivenGizmoPredicateTests.D003_*` ×2.

📐 **Built a worktree at the dispatch base `d3c370ffb`** and reproduced them **identically — 2 failed /
0 passed** on the same two test names. ⇒ ⛔ **not mine**; the worktree was then removed.
⭐ **Done once, at the source** *(gate-report contract row 4)* — ⛔ so the coordinator need not build one.

### ⚠ 6.2 — the rulings staleness WARN, explained

```
WARN 1 cited source(s) changed after the ledger was last updated.
  Hrot/Runner/Hrot.ClusterRunner/Program.cs
```

⭐ **It is MY OWN edit** — `103a` added `ResetLayoutOnRun = config.ResetLayout,` to `Program.cs`.
📐 **Checked the ruling it cites:** `R-79` quotes `config.RequestedSubsystems` at `Program.cs:212`; it
now sits at **`:213`**, one line down, **unmoved in substance**. ⇒ ⭐ **the warning is correct and the
ruling is intact.** ⛔ Not silenced.

### ⭐ Quarantine counts — **both, and a new skip would be a finding**

| | |
|---|---|
| `Hrot.Blueprints.Tests` skipped | **10** *(Xvfb)* / **18** *(no display)* — ⭐ **unchanged both ways** |
| every other suite | **0 skipped** *(Xvfb)* |
| ⛔ **new skips this batch** | ⭐ **none** |

### ⭐⭐ The frame rails — **ran / skipped, in BOTH environments** *(`R-124`)*

| family | Xvfb | no display |
|---|---|---|
| `Hrot.Blueprints.Tests` · `Editor.Frame` | ⭐ **8 ran / 0 skipped** | ⛔ **0 ran / 8 skipped** |
| `Hrot.Editor.AiShared.Tests` · `Variables.Frame` | ⭐ **8 ran / 0 skipped** | ⚠ **7 ran / 1 skipped** |

⭐ **The 8-test Blueprints family is exactly the `10 → 18` skip delta** in the table above — ⛔ nothing
else moves between the two environments.

⚠⚠ **Worth stating rather than glossing:** the AiShared `Variables.Frame` namespace holds **8 tests and
only ONE is display-gated** — `AScalarEditsAsOneNamedRowTests.TheScalarDialogRendersWithRoomForTheNumber`.
⇒ ⭐ **that namespace is not 8 frame rails**; it is one frame rail among seven ordinary tests.
⛔ **A reader counting rails by namespace would overcount this family 8×.** ⭐ Nothing to fix — ⚠ but the
number a future *"how much UI is railed?"* question wants is **9**, not 16.

### ⭐⭐ Golden movement, as a DIFF SHAPE *(contract row 3)*

⭐⭐⭐ **ZERO goldens moved.** 📐 The diff is **13 files**: 8 changed, **4 added**, **1 deleted**
(`imgui.ini`, 283 lines, the tracked-but-ignored file item ⑦ retired). ⛔ **No `.approved.` / golden /
snapshot file appears in it at all**, and the working tree was clean after every suite run.

---

## 7. ⚠⚠ RULE 4 — **two commits landed on the coordinator branch DURING this run, and they are CODE**

| sha | what |
|---|---|
| `f0b1e14` | *"ask the CLOCK whether the simulation is paused (`M-40`)"* — `EditorSubsystem`'s `isFrozen` |
| `eb310fd` | *"the run-state refusal reports its INPUTS"* — `RunStateSource.Describe` → `VariableEditModal` |

⛔ **NOT merged.** ⭐ **Scope is frozen at `d3c370ffb`** *(the handoff, verbatim: "documents that change
after it are FYI ONLY")* — ⚠ and neither commit **invalidates** a Batch 103 item, so `R-106`'s
STOP-and-report arm does not apply. ⭐ **They are the coordinator's own work on the run-state seam**, and
they land cleanly on top of this batch: **no file overlaps** the 13 in my diff.

### ⭐⭐ Do they move `103c`'s numbers? — **MEASURED: no**

📐 `git diff bd5f86f..eb310fd`, filtered to the four things §3 counts:

```
+        Func<string>? describeRunState = null)
+        _describeRunState = describeRunState ?? (() => runState().ToString());
+        _describeRunState = RunStateSource.Describe(isSimUp, isFrozen);
```

⇒ ⛔ **zero new `VariableTableModel` owners · zero new `RunState =` assignments · zero new
`SyncRunState` sites · zero new `Build()` sites.** ⭐ **`103c`'s blast radius holds against the newer
tree**, and the two commits change *what the predicates read*, ⛔ **not the shape that makes a headless
reader see `Planning`** — ⚠ which is `BP-385` and is still open.

---

## 8. ⭐ IDS ALLOCATED *(rule 3 — the coordinator allocates none)*

| id | state | what |
|---|---|---|
| **`BP-386`** | ✅ **closed by `103a`** | the layout lived in **two files under two roots** with nothing owning the pair ⇒ **a half-reset**, and a clean rebuild wiped the json |
| **`BP-387`** | ✅ **closed by `103b`** | the shipped default names **55 window ids** and `WindowManager` skips unknown ones **by design** ⇒ a rename **silently orphans**. ⚠ Rail limits stated in §2 |
| **`BP-388`** | ⛔ **open** | the cold-start perspective check validates against **`ISubsystem.Name`**, ⛔ not against durability or `GetPerspectives()`. ⭐ The outcome is right today **for the wrong reason** |
| **`BP-385`** | ⚠ **updated, still open** | blast radius measured *(§3)*; ⛔ the decision needs a nod because it touches Batch 94c's seam |
