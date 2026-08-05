# RESUME / HANDOFF — Blueprint gaps & QoL programme (2026-08-04)

> **Goal:** make blueprint editing fully functional and pleasant.
> **Branch:** `claude/blueprint-gaps-qol-audit-uyjjk5` · **HEAD at handoff:** `d8cfc75`
> **Live state:** [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) (checklist) ·
> [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (per-issue evidence + `DONE` notes)
>
> **The two tracker docs are the source of truth.** This file is orientation only — if it and the
> tracker disagree, the tracker wins.

---

## Status

**43 open · 27 fixed · 1 refuted (BP-46).** Counts and per-complexity breakdown live in the tracker
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

**Batch 7's visual pass is done (2026-08-05)** and it earned its keep: one bug of mine (the
bookmarks ✕ was unreachable — a full-width `Selectable` swallowed the click), one long-standing 🔴
(**BP-66**, the peer catalog scanning a directory that does not exist), and two scope findings —
**BP-07 is blocked by BP-12c**, and the When node's other three mode forms are stubs (**BP-67**).
Everything else on the checklist behaved.

---

## Next up

**The `WIRING` tier is done** — 19 of 21 shipped; the 2 remaining (BP-01 watch-panel decoding,
BP-55 asset-browser delete) are not blueprint-authoring items.

1. **BP-12c** ⭐ (`RW-L`) — custom-event authoring. Now the highest-leverage item: it is **the
   blocker for BP-07**, whose picker is built and correct but has nothing to list. Ships one item
   and makes a second visible.
2. **BP-60** (🔴 `RW-M`) — "Promote to Variable" silently does nothing. The last 🔴 in Area A, and
   the last known instance of the sink's `default:`-returns-success trap.
3. **BP-23a / BP-13 / BP-17…BP-20** (`RW-L`) — the canvas-ergonomics tier, each with a stated
   ready-made primitive to mirror. `BP-23a` (copy/paste) is the one a designer notices first.
4. **BP-67** (`RW-M`) — the When node's other three forms. ⚠ *not* a repeat of BP-10: those had a
   catalog already injected and called; these need pickers built from scratch.

> 👀 **Re-check after the next round:** the bookmarks ✕ (now sized so the row and the button cannot
> overlap) and the `CallPeerBlueprint` picker (BP-66 — should list peers now).

**Blocked / do not build as written:** `BP-31` (premise inverted — see BP-61) ·
`BP-40`, `BP-38`, `BP-52` (architect decision first) · `BP-53`, `BP-54` (UNCLEAR, re-scope first).

---

## 🎯 Next task briefing — scouting already done, do not re-derive

Written for a fresh context. Everything here was verified against code on 2026-08-05.

### BP-12c ⭐ — custom-event authoring (`RW-L`) · **do this first**

**Why first:** it is the blocker for **BP-07**, whose picker is built, correct and currently shows
"this Blueprint declares no custom events" because nothing can create one. One item shipped, a
second made visible.

| Fact | Where |
|---|---|
| The section already declares the command | `Windows/BlueprintMyBlueprintModel.cs:54` — `"editor.create-custom-event"` |
| …and **nothing registers it** — that is the whole bug | grep: one hit repo-wide, the declaration above |
| The section already renders its items | `BlueprintMyBlueprintModel.cs:141` `BuildCustomEventItems()` reads `_asset.CustomEvents` |
| The data model | `CustomEventDecl { Guid Id; string Name; List<ParameterDecl> Parameters }` (`Assets/Declarations.cs:33`) |
| **The pattern to mirror, exactly** | `editor.create-variable`: `BlueprintDocumentFactory.RegisterCreateVariableCommand` (two overloads — quick-add and modal) + `VariableCreateModal` + `CreateVariable` at `:506` |
| Where to register | `BlueprintDocumentFactory` §9, beside `RegisterVariableGetSetCommands` (BP-12a) |
| Consumer to verify against | `NodePinSchema.CallCustomEventPins` resolves `EventId` → `asset.CustomEvents` by **GUID**; Stage5's `FindCustomEventIndex` also accepts **Name** |

**Round-out to consider:** the tracker note says *"consider removing the dispatcher section instead
(superseded)"* — dispatchers are a separate, abandoned concept. Decide that explicitly rather than
building a dispatcher-create path by symmetry.

**Done means:** "+ Custom Events" creates a declaration with a name and (at minimum) an empty
parameter list, it appears in the panel, **and BP-07's picker lists it**. Re-check checklist row 5.

### BP-60 🔴 — "Promote to Variable" silently does nothing (`RW-M`)

The last 🔴 in Area A and the last known instance of the `default:`-returns-success trap.

| Fact | Where |
|---|---|
| The command exists in the vocabulary | `GraphCommand.PromoteToVariable(PinId Pin, string VariableName, bool IsLocal, string? CategoryPath)` — `GraphCommand.cs:86` |
| The UI fully works — modal opens, name typed | `CanvasRenderer.cs:627/629` → `OpenPromoteToVariableModal` at `:977` |
| `BlueprintCommandSink` has **no case** for it | → hits `default:`, which returns **success**. Nothing happens, and it reports that it worked |
| **A complete reference implementation exists** | `NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs:357` `ApplyPromoteToVariable` — handles input pins (place `Util.GetVar` left of the owner, link to the pin) and output pins symmetrically |
| Deliberately left on `Commands.Apply` by BP-02 | `CanvasRenderer.cs:1025` — it was the 15th of 15 bypass sites, blocked on exactly this |

**Two things to get right, both learned the hard way this programme:**
1. **The sink applies; the stack records** (BP-11). Do *not* record undo inside the new sink case —
   build the forward/inverse pair at the `CanvasRenderer` call site and route through
   `view.Execute`, which also lifts BP-02's last bypass.
2. **A test asserting `Success` proves nothing** here — that is the bug. Assert the *effect*: the
   variable exists on the asset and a Get/Set node is linked to the pin.

**Done means:** promote from an input pin and from an output pin; the variable appears in My
Blueprint; Ctrl+Z reverses the whole gesture as one entry.

### Then, in order

3. **BP-23a** (`RW-L`) — copy/cut/paste/duplicate. The one a designer notices first. Paste is
   hard-disabled; `AddNodeCommand` already accepts a prebuilt `Node`, so paste can skip the
   8-of-50 property whitelist. ⚠ now also needs to honour `AssignedId` — see BP-65.
4. **BP-13 / BP-17…BP-20** (`RW-L`) — align-distribute, node titles, collapse, minimap, error list.
   Each has a named ready-made primitive in its detail entry.
5. **BP-67** (`RW-M`) — the When node's other three forms. Largest of these; hold it until the
   above land.

---

## 👀 Visual check — what batch 7 added, and where to look

Everything below is logic-tested headless. What a test cannot see is layout, wording and feel.
Listed roughly cheapest-to-reach first.

| # | Where | What to do | What should happen |
|---|---|---|---|
| 1 | **My Blueprint** → right-click a variable | "Get", then "Set" | A node appears **in the middle of the visible canvas**, bound to that variable. **Ctrl+Z removes it.** *(BP-12a + BP-65)* |
| 2 | Canvas → drop any node from the palette | Ctrl+Z | The node goes away. This never worked before — *BP-65*, the one to check first |
| 3 | **Details** on a `ReadRankedResult` node | Change Rank; hold the stepper | Editable at last; a hold produces **one** undo entry, not one per frame *(BP-05)* |
| 4 | Details on a `WaitForChannel` node | Open the Channel combo | Each channel appears **once**, sorted, with a filter box *(BP-06)* |
| 5 | Details on a `CallCustomEvent` node | Open the Event combo | Lists **this Blueprint's own** custom events with their parameter names; picking one re-projects the node's argument pins *(BP-07)* |
| 6 | Details on a `CallPeerBlueprint` node | Pick a peer, then a function | Function list is scoped to the peer. Pick a function, then switch to a peer without it → **the function clears**, and one Ctrl+Z restores both *(BP-08)* |
| 7 | Details on a `When` node, mode = **Event Fired** | Open the Event combo | A real filtered list instead of "(n events available)". The "Only when it targets me" checkbox appears **only** for events that carry a target field *(BP-10)* |
| 8 | **Bookmarks** panel (set one with Ctrl+Shift+1) | Click the row · double-click it · the ✕ | Click jumps the canvas · double-click renames inline (Esc cancels) · ✕ deletes; right-click has all three *(BP-03)* |
| 9 | Any Details-panel edit at all | Ctrl+Z | Reverses it. Before BP-11 **no** inspector or drawer edit was undoable |

### Outcome of the 2026-08-05 pass

| Row | Result |
|---|---|
| 1, 2, 3, 4, 7, 9 | ✅ as described |
| 5 — `CallCustomEvent` | ⛔ **untestable — no asset can declare a custom event.** The drawer is right; **BP-12c** is the blocker |
| 6 — `CallPeerBlueprint` | 🔴 **empty — BP-66**, the catalog scanned a directory that does not exist. Fixed; **re-check** |
| 8 — bookmarks ✕ | 🔴 **unreachable** — a full-width `Selectable` swallowed the click. Fixed; **re-check** |
| — When node | 🔎 the other three modes (`ValueChanged`, `ConditionMet`, `EqsResult`) are stubs — **BP-67** |

I predicted the ✕ would be the thing to look wrong. It was, but for the wrong reason: the
*alignment* was fine, the *hit test* was not — which is the half a headless test can never reach.

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

### 5. 🆕 A full-width `Selectable` eats every click in its row
An `ImGui.Selectable` with no explicit size spans the whole remaining row, so a button drawn after it
with `SameLine` is *drawn and correctly positioned but unreachable*. Size the Selectable to stop
short of the trailing controls — more predictable than `AllowItemOverlap`, which depends on draw
order and on remembering `SetItemAllowOverlap`. (BP-03's delete button; caught only by the visual
pass, since no headless test can see it.)

### 6. Conventions the code enforces that are easy to miss
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
  resource-sensitive). Not yet chased.
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

## Key code reference points (as of `d8cfc75`)

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
