# RESUME / HANDOFF — Blueprint gaps & QoL programme (2026-08-05)

> **Goal:** make blueprint editing fully functional and pleasant.
> **Branch:** `claude/blueprint-gaps-qol-audit-uyjjk5` · **HEAD at handoff:** `ae113a4`
> **Live state:** [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) (checklist) ·
> [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (per-issue evidence + `DONE` notes)
>
> **The two tracker docs are the source of truth.** This file is orientation only — if it and the
> tracker disagree, the tracker wins.

---

## Status

**34 open · 37 fixed · 1 refuted (BP-46).** Counts and per-complexity breakdown live in the tracker
table; do not duplicate them here.

| Batch | Items |
|---|---|
| 1 — silent-failure | BP-59, BP-29, BP-16, BP-15, BP-12e |
| 2 — undo + docs | BP-02, BP-47, BP-48, BP-49, BP-50 |
| 3 — palette | BP-04, BP-09 |
| 4 — test health & reflection | BP-62, BP-35 (+ suite serialization) |
| 5 — coverage | BP-41 |
| 6 — undo unification | BP-11 ⭐ |
| 7 — wiring | BP-03, BP-05…BP-08, BP-10, BP-12a, BP-63, BP-64 (+ BP-65 🔴, BP-66 🔴) |
| 8 — custom-event authoring | BP-12c, BP-68 🔴 (+ BP1407/BP1408, dispatcher section removed) |
| 9 — promote to variable | BP-60 🔴 (lifts BP-02's last bypass) |
| 10 — canvas clipboard | BP-23a |
| 11 — panel item CRUD | BP-12b |
| 12 — alignment | BP-13 |
| 13 — node header | BP-17, BP-18 |
| 14 — navigation aids | BP-19, BP-20 |

**Batch 7's visual pass (2026-08-05)** earned its keep: one bug of mine (the bookmarks ✕ was
unreachable — a full-width `Selectable` swallowed the click), one long-standing 🔴 (**BP-66**, the
peer catalog scanning a directory that does not exist), and two scope findings.

**Batches 8–14 ran overnight on 2026-08-05.** Seven batches, nine items, ~120 new tests, all suites
green. Three themes worth carrying forward:

1. **The `default:`-returns-success trap has now bitten four times** — BP-60, BP-68 and BP-18 all
   had a command the sink silently accepted and ignored. **Never assert on `Success`; assert the
   effect.** Grep for `GraphCommand` variants with no `case` in `BlueprintCommandSink` before
   assuming a command works.
2. **Asset-scoped features belong host-side, not in the vendored sink command.** BP-60 and BP-23a
   both looked like "add a sink case" and both were wrong: the single opaque command hides the ids
   the caller needs for an inverse. Composing the gesture at the host from primitives the sink
   already implements keeps BP-11's invariant *and* makes it one undo entry.
3. **A wiring fix exposes the next one.** Shipping BP-12c made the very next gesture fail (BP-68),
   because until then no asset could declare a custom event. The visual check is what finds these,
   not the suite.

---

## Next up

**The `RW-L` tier is nearly done** — 13 of 23 shipped, and everything a designer touches daily is in.

1. **BP-67** (`RW-M`) — the When node's other three forms (`ValueChanged`, `ConditionMet`,
   `EqsResult`). ⚠ *not* a repeat of BP-10: that had a catalog already injected and called; these
   need pickers built from scratch.
2. **BP-24** (`RW-M`) — graph create + graph switching. Two reasons to want it now: every graph but
   the first is unreachable in the UI, **and** it is what closes the other half of BP-12c (a custom
   event's body is an `Event` graph the editor cannot create). ⚠ **fix the canvas graph-selection
   rule as part of it** — see the latent note in the detail doc's found-while-fixing section.
3. **BP-56** (`RW-L`) — wire-level execution-flow highlighting (nodes glow, wires don't).
4. **BP-23b** (`RW-M`) — cross-asset paste. BP-23a's machinery is in place; this adds variable/type
   re-resolution against the destination asset.
5. **BP-61** (🔴 `RW-M`) — the last open inert-default guard: both HSM concurrency rules never fire.

**Still unregistered on the My Blueprint menu** (deliberately out of BP-12b's scope):
`editor.move-to-category`, `editor.change-variable-type`, `editor.show-properties`,
`editor.find-references` (that one is BP-12d).

**Blocked / do not build as written:** `BP-31` (premise inverted — see BP-61) ·
`BP-40`, `BP-38`, `BP-52` (architect decision first) · `BP-53`, `BP-54` (UNCLEAR, re-scope first).

---

## 👀 Morning visual check — batches 9–14

Everything below is logic-tested headless. What a test cannot see is layout, wording and feel.
Roughly cheapest-to-reach first.

### Canvas clipboard (BP-23a) — the big one

| # | Where | What to do | What should happen |
|---|---|---|---|
| C1 | Select a node, **Ctrl+C**, then **Ctrl+V** | — | A copy appears, **offset** from the original and **selected**, so you can drag it straight away |
| C2 | Select two wired nodes, copy, right-click empty canvas → **Paste** | — | Both land with the **wire between them intact**, top-left corner at the cursor |
| C3 | Select one end of a wired pair, copy, paste | — | Just that node — a half-selected wire is **not** copied |
| C4 | **Ctrl+X** then **Ctrl+V** | — | Cut removes, paste restores. Ctrl+Z after either reverses it in **one** press |
| C5 | Copy node A, select node B, **Ctrl+D** | — | B is duplicated and **the clipboard still holds A** |
| C6 | Configure a `Compare` node (operator `>`), copy, paste | — | The copy keeps `>`. *(This is the audit's trap: a paste built on `AddNode` would have silently reset it)* |

### Promote to Variable (BP-60)

| # | Where | What to do | What should happen |
|---|---|---|---|
| P1 | Right-click a node's **data input** pin → Promote to Variable | type a name | A **Get** node to the **left**, wired in; the variable appears in My Blueprint with the pin's type |
| P2 | Right-click a **data output** pin → Promote | — | A **Set** node to the **right**, fed by that pin |
| P3 | Ctrl+Z after either | — | **One** press reverses all of it — node, wire and variable. Ctrl+Y restores |
| P4 | Promote twice with the same name | — | The second becomes `Name1` rather than failing |

### My Blueprint item CRUD (BP-12b)

| # | Where | What to do | What should happen |
|---|---|---|---|
| M1 | Right-click a variable → **Rename** / **Duplicate** / **Delete** | — | All three work. They were live-looking and inert before |
| M2 | Rename a **custom event** that has a Call node placed | — | The node's header follows the new name; Ctrl+Z restores |
| M3 | Delete a variable that a Get node uses | — | The declaration goes; **the node stays** (dangling, recoverable) rather than vanishing |

### Node header (BP-17, BP-18)

| # | Where | What to do | What should happen |
|---|---|---|---|
| N1 | Right-click a node → **Rename…** | type a title | The header shows it, and the **generated title moves to the subtitle** — you can still tell what the node is |
| N2 | Rename again, clear the field | — | The generated title comes back |
| N3 | Right-click → **Collapse Node** | then Expand | The body folds to the header and back. Ctrl+Z reverses each |

### Alignment (BP-13)

| # | Where | What to do | What should happen |
|---|---|---|---|
| A1 | Select 2+ nodes → right-click → **Align** ▸ Left / Right / Top / Bottom | — | Right and Bottom use the node's **far edge**, so different-width nodes line up properly |
| A2 | Select 3+ → **Align ▸ Distribute Horizontally** | press twice | Even gaps; the **second press changes nothing** |
| A3 | Select a wired chain → **Align ▸ Straighten Connection** | — | They snap onto the **first selected node's** row; wires run flat |
| A4 | Align an already-aligned pair, then Ctrl+Z | — | Undo reverses your *previous* edit — an alignment that moves nothing records nothing |

### Navigation (BP-19, BP-20)

| # | Where | What to do | What should happen |
|---|---|---|---|
| V1 | Right-click empty canvas → **Show Minimap** | — | A corner overlay: nodes as blocks, the current view as an outline |
| V2 | Click / drag inside the minimap | — | The canvas recentres and follows the drag. Pan far off-graph → the view outline stays **inside** the overlay |
| V3 | On a graph with a red node, press **F8** | Shift+F8 | Selects and centres each problem node, wrapping. **Errors before warnings** |

### Custom events (batch 8, if not already checked)

| # | Where | What to do | What should happen |
|---|---|---|---|
| E1 | **My Blueprint** → drag a custom event onto the canvas | look at Details | A real **Call Custom Event** node, bound, with a pin per parameter. Header shows the **name**, not a GUID *(BP-68)* |
| E2 | "Custom Events" **+** | — | Create modal: name + typed parameter rows; bad names refused *before* Confirm |
| E3 | The panel's section list | — | **No "Event Dispatchers" section** — intentional, the concept is superseded |

> ⚠ Compiling a `CallCustomEvent` still needs an `Event` graph of the same name, which the editor
> cannot create yet (**BP-24**). That fails as **BP1407** naming the graph to add, not as a Roslyn
> error. The create modal says so before you confirm.

---

## Traps that cost real time — read before touching these areas

### 1. 🔁 The inert-default guard (**three instances, one still open**)
*An optional ctor dependency defaults to an inert value; tests pass it explicitly and prove the
logic; every production site omits it, so the feature is silently dead.*

| Where | Effect | Status |
|---|---|---|
| `PredicateCompiler.blueprintRegistry` (**BP-29**) | conditional breakpoints never fired | Fixed |
| `HsmValidator.isStatefulSubtree` / `sharedScopeKeys` (**BP-61**) | both HSM concurrency rules never fire | **Open** |
| `DebugProbe.NewTick`'s `Sink as IBlueprintDebugSession` (**BP-35**) | would have silently dropped ticks behind the multiplexer | Fixed |

> **A green suite is not evidence a guard is wired. Grep the production construction sites.**

### 2. Absence claims need **both** trees
Four "nothing exists" findings were overturned because a search covered `Hrot/` but not `FDP/`.
Always search both.

### 3. Assembly load order (**BP-62** — fixed, but the shape recurs)
`AppDomain.CurrentDomain.GetAssemblies()` returns only *already-loaded* assemblies and the CLR loads
lazily; a `ProjectReference` does **not** force a load. Use
`EditorTypeResolutionScope.Assemblies()` for any type scan in the editor. Never cache the assembly
array — hot reload adds ALCs at runtime.

### 4. 🆕 Sinks apply; the undo stack records (**BP-11**) — and must adopt the caller's ids (**BP-65**)
`IGraphCommandSink.Apply` must **never** push an undo entry. `UndoStack.ApplyAndRecord` applies the
forward *then* pushes, so a sink that also records lands an inner entry first — and on undo the
inverse re-enters the same sink method and pushes a third. The caller snapshots the prior state and
issues the pair through `GraphView.Execute`. This was invisible for as long as the sink's stack
(`CommandHistory`) was dead; it is a live trap now.

> `GraphCommand` is a plain `public abstract record`, so a **host** assembly can extend the command
> vocabulary — `BlueprintEditCommand(Label, Mutate)` does — without editing the vendored NodeEdit
> tree. Any new variant needs a sink case in the same change (see the `default:` trap below).

**A sink must also adopt the ids the caller assigned.** `CommandBuilder` mints an id, puts it in the
forward command and names it in the paired inverse; a sink that mints its own instead produces
inverses that match nothing. That was BP-65 — node placement was non-undoable for the life of the
feature, while the BTree and HSM sinks had it right all along. **When adding a sink case for an
`Add*` command, check what the inverse references.**

### 5. 🆕 `default:`-returns-success — **four instances now**
`BlueprintCommandSink.Apply`'s `default:` arm returns `new GraphCommandResult(true, null)` for any
command it has no case for. A feature can therefore be fully built, fully wired, and silently do
nothing while reporting success.

| Command | Symptom | Status |
|---|---|---|
| `PromoteToVariable` (**BP-60**) | modal opened, name typed, nothing happened | Fixed |
| dynamic kinds (**BP-68**) | dragged custom event became an unbound `FunctionCallNode` | Fixed |
| `SetNodeCollapsed` (**BP-18**) | collapse silently ignored | Fixed |

> **A test asserting `Success` proves nothing here — that is the bug.** Assert the effect. Before
> assuming any `GraphCommand` works, grep `BlueprintCommandSink.Apply` for a `case`.

### 6. 🆕 Asset-scoped features belong at the host, not in a sink case
`GraphCommand.PromoteToVariable` and a paste both look like "add a sink case". Both are wrong: the
single opaque command allocates ids *inside* the sink, so no caller can write the inverse — and
`ApplyInitialProperties` only knows 8 node kinds of 50, so a paste routed through `AddNode` silently
strips the other 42. Compose the gesture at the host from primitives the sink already implements
(`BP-60`, `BP-23a`): the caller owns every id, BP-11's invariant holds, and it is one undo entry.

### 7. 🆕 A full-width `Selectable` eats every click in its row
An `ImGui.Selectable` with no explicit size spans the whole remaining row, so a button drawn after it
with `SameLine` is *drawn and correctly positioned but unreachable*. Size the Selectable to stop
short of the trailing controls — more predictable than `AllowItemOverlap`, which depends on draw
order and on remembering `SetItemAllowOverlap`. (BP-03's delete button; caught only by the visual
pass, since no headless test can see it.)

### 8. Conventions the code enforces that are easy to miss
- **Decision-asset ids are NOT parseable GUIDs.** Shipped `CombatPostureDecision` uses
  `3c6f9e42-5d10-6f3a-ac23-posture0000001`. A `Guid.TryParse` check rejects real assets.
- **Custom events resolve by *Name* as well as GUID** (`Stage5.FindCustomEventIndex`).
- **Every new `BPxxxx` diagnostic code needs a `[CoversDiagnosticCode]` test** —
  `V_AllValidatorsCoverageTests` fails the build otherwise.
- **`BlueprintCommandSink.Apply`'s `default:` silently returns success** for unknown commands, so an
  unhandled `GraphCommand` no-ops *and reports success* (this is BP-60). A test asserting `Success`
  therefore proves nothing — assert the *effect*.
- Palette baking is safe: `CreateAssetNode` builds via `CreateInstance` then only *overlays* caller
  props — it does **not** route through `ApplyInitialProperties`' 8-of-50 whitelist.
- **Blueprint assets live under `Assets/Blueprints`** (`AssetRoots.AssetsRelative`), resolved against
  the project dir when found. Never hand-build a blueprint path — BP-66 was a lone
  `{BaseDirectory}/blueprints` that silently made a whole feature inert.
- **A "graceful fallback" hides a wiring bug.** `CallPeerBlueprintPins` falls back to untyped pins
  when the peer lookup finds nothing — indistinguishable from no lookup at all, which is exactly how
  BP-66 survived. When a path degrades silently by design, test the *populated* case.

---

## Test baseline — what "green" means

**Full appendix:** [Blueprint_Issues_Detail.md § Test baseline](Blueprint_Issues_Detail.md#appendix--test-baseline-what-green-means-in-this-repo).

- `Hrot.Blueprints.Tests` is now **serialized** (`xunit.runner.json` + the `<Content Include>` line —
  without the latter the config never reaches the output dir and does nothing). It was the only
  suite of 10 running parallel. Before: 1 varying failure *every* run. After: **2657 / 0**.
- ⚠ **`Blueprint_Component_Access_RESUME.md`'s "~8–9 reds, DO NOT chase" is STALE** — banner-marked
  at the source. Expect 0 failures; investigate any.
- Residual: `PdbEmbeddedSourceTests` pair flaked once in ~6 runs (real Roslyn+PDB emission,
  resource-sensitive). Not yet chased. **`WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`
  joins it** — a wall-clock ns/tick benchmark; it reds under load and passes alone. Re-run the single
  filter before treating either as a regression.
- Batch 14 baseline: blueprints **2788 total / 2778 passed / 10 skipped** · NodeEdit core **203**
  + UI **131** · AiShared **1204** · BTree editor **612** · breakpoints **130** · generators **189**.
- ✅ **`Hrot.Editor.AiShared.Tests` is now in the gate list** (1204/0). Its 2 Windows-only reds were
  BP-64, fixed. It was missing from the list, which is why they went unnoticed.
- **To classify a failure:** `git stash` → re-run the same filter → `git stash pop`. If it fails
  identically without your changes, it predates you. This is how BP-64 was classified.

```bash
# gates used throughout (all headless)
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

---

## Working agreement (from the user, this programme)

- **Verify claims against code; do not trust the audit doc or the architect blindly.** **Nine** audit
  claims and two architect statements were wrong and were corrected in-repo. Every correction is
  recorded in the detail file rather than silently applied. Note the failure mode is not only "claim
  is false" — BP-41's claim was true but named the *wrong risk*, so building it as written would have
  re-proved something already covered.
- **Fix, don't disable.** Flaky/failing tests get root-caused; skipping is a permanent silent
  coverage hole.
- **Non-trivial designs get an architect round** (`Architect_Question_N_*.md`) — the user relays to
  NotebookLM; Claude cannot reach it. Trivial mirror-pattern work proceeds directly. ⚠ An approval is
  not a verification: Q22's approved D2 was the one step that could not work (see BP-11 gap 4). Check
  the approved plan against the code before building it.
- **Record findings in the detail doc**, not only in commit messages.
- Ask in plain prose; **never** the multiple-choice widget.

## Key code reference points (as of batch 14)

| Concern | File |
|---|---|
| Canvas undo / context menus | `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs` |
| Undoable delete (the correct path) | `.../NodeEditor.UI/Action/EditCommands.cs` |
| Command application + bakes | `Hrot/.../Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs` |
| Stage2 validators (BP-15/BP-16 live here) | `Hrot/.../Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` |
| Palette entries | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` |
| Type resolution scope (BP-62) | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/EditorTypeResolutionScope.cs` |
| Probe fan-out (BP-35) | `Hrot/.../Hrot.Blueprints.Core/MultiplexingProbeSink.cs` |
| Undo transport (BP-11) | `Hrot/.../Hrot.Blueprints.Editor/Host/BlueprintEditCommand.cs` · `NodeDrawers/EditService.cs` (`RecordUndoable`) · wired in `Host/BlueprintDocumentFactory.cs` |
| New Details-panel drawers (BP-05…BP-08) | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/{ReadRankedResult,WaitForChannel,CallCustomEvent,CallPeerBlueprint}NodeDrawer.cs` · registered in `BlueprintEditorBootstrap.cs` |
| Bookmarks panel (BP-03) | `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Bookmarks/BookmarksPanel.cs` + `Core/Bookmarks/BookmarkStore.cs` |
| AiPrimitive composition rail (BP-41/BP-30) | `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` (slot keys + manifest) · `FDP/Toolkits/.../Behavior/Systems/BehaviorIngressSystem.cs` (provisioning) |
