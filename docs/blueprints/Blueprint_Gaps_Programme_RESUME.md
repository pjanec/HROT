# RESUME / HANDOFF — Blueprint gaps & QoL programme (2026-08-04)

> **Goal:** make blueprint editing fully functional and pleasant.
> **Branch:** `claude/blueprint-gaps-qol-audit-uyjjk5` · **HEAD at handoff:** `966bc2d`
> **Live state:** [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) (checklist) ·
> [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (per-issue evidence + `DONE` notes)
>
> **The two tracker docs are the source of truth.** This file is orientation only — if it and the
> tracker disagree, the tracker wins.

---

## Status

**51 open · 16 fixed · 1 refuted (BP-46).** Counts and per-complexity breakdown live in the tracker
table; do not duplicate them here.

| Batch | Items |
|---|---|
| 1 — silent-failure | BP-59, BP-29, BP-16, BP-15, BP-12e |
| 2 — undo + docs | BP-02, BP-47, BP-48, BP-49, BP-50 |
| 3 — palette | BP-04, BP-09 |
| 4 — test health & reflection | BP-62, BP-35 (+ suite serialization) |
| 5 — coverage | BP-41 |
| 6 — undo unification | BP-11 ⭐ |

**Everything is verified headless.** No GPU/visual check has been needed so far.

---

## Next up

1. Remaining `WIRING` items (BP-03, BP-05…BP-08, BP-10, BP-12a) are **drawer/UI work — these need
   the user's eyes**, unlike everything shipped so far.
2. **BP-63** (`RW-L`) and **BP-64** (`WIRING`) — both found while fixing BP-11; headless, contained.

**Blocked / do not build as written:** `BP-31` (premise inverted — see BP-61) ·
`BP-40`, `BP-38`, `BP-52` (architect decision first) · `BP-53`, `BP-54` (UNCLEAR, re-scope first).

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

### 4. 🆕 Sinks apply; the undo stack records (**BP-11**)
`IGraphCommandSink.Apply` must **never** push an undo entry. `UndoStack.ApplyAndRecord` applies the
forward *then* pushes, so a sink that also records lands an inner entry first — and on undo the
inverse re-enters the same sink method and pushes a third. The caller snapshots the prior state and
issues the pair through `GraphView.Execute`. This was invisible for as long as the sink's stack
(`CommandHistory`) was dead; it is a live trap now.

> `GraphCommand` is a plain `public abstract record`, so a **host** assembly can extend the command
> vocabulary — `BlueprintEditCommand(Label, Mutate)` does — without editing the vendored NodeEdit
> tree. Any new variant needs a sink case in the same change (see the `default:` trap below).

### 5. Conventions the code enforces that are easy to miss
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

---

## Test baseline — what "green" means

**Full appendix:** [Blueprint_Issues_Detail.md § Test baseline](Blueprint_Issues_Detail.md#appendix--test-baseline-what-green-means-in-this-repo).

- `Hrot.Blueprints.Tests` is now **serialized** (`xunit.runner.json` + the `<Content Include>` line —
  without the latter the config never reaches the output dir and does nothing). It was the only
  suite of 10 running parallel. Before: 1 varying failure *every* run. After: **2612 / 0**.
- ⚠ **`Blueprint_Component_Access_RESUME.md`'s "~8–9 reds, DO NOT chase" is STALE** — banner-marked
  at the source. Expect 0 failures; investigate any.
- Residual: `PdbEmbeddedSourceTests` pair flaked once in ~6 runs (real Roslyn+PDB emission,
  resource-sensitive). Not yet chased.
- ⚠ **`Hrot.Editor.AiShared.Tests` is NOT in the gate list and has 2 Windows-only reds** (1202 pass) —
  see **BP-64**. Pre-existing; verified against a stashed tree.
- **To classify a failure:** `git stash` → re-run the same filter → `git stash pop`. If it fails
  identically without your changes, it predates you. This is how BP-64 was classified.

```bash
# gates used throughout (all headless)
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
```

---

## Working agreement (from the user, this programme)

- **Verify claims against code; do not trust the audit doc or the architect blindly.** **Seven** audit
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

## Key code reference points (as of `966bc2d`)

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
| AiPrimitive composition rail (BP-41/BP-30) | `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` (slot keys + manifest) · `FDP/Toolkits/.../Behavior/Systems/BehaviorIngressSystem.cs` (provisioning) |
