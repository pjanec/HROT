# HANDOFF — Batch 22: BP-109, the end-to-end smoke test

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> ⚠ **Runs in parallel with a Windows visual-test session.** Different machine, different branch —
> see §6. Nothing you do blocks it, and nothing it finds lands in this batch.

---

## 0. ⚡ Model delegation — read this first

You are on **Opus**. **Delegate to Sonnet sub-agents anything not needing Opus-level reasoning.**
Tokens are the binding constraint.

| Work | Model | Why |
|---|---|---|
| Authoring the four `.bp.json` recipe assets | 🟢 **Sonnet** | Mechanical, from the shape stated in §3 |
| The gate test wiring — spawn, tick, read back | 🟢 **Sonnet** | `BlueprintRunHarness` is the established pattern; six callers to mirror |
| Registering the assets so the recipe picker lists them | 🟢 **Sonnet** | `<Content Include>` + `CopyToOutputDirectory`, already globbed |
| **Making a peer call actually execute** if it does not work first try | 🔴 **Opus** | Never run before; a failure here is a real compiler/runtime defect, not a fixture bug |
| **Two entities / two blueprints in one world** if the harness resists | 🔴 **Opus** | Unproven; may need real harness work |
| **Diff review · gate runs · revert-goes-red** | 🔴 **Opus** | Never delegate verification |

⚠ **Delegation does not transfer the verification duty.**

---

## 1. Context

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch** | `claude/blueprint-macro-feature-sdmspn` — **push here**; merge the coordinator branch first |
| **Coordinator branch** | `claude/blueprint-authoring-status-6sr5ld` — docs/tracker; do **not** push here |
| **Truth** | [Tracker](Blueprint_Issues_Tracker.md) · [Detail](Blueprint_Issues_Detail.md) |

**Shared-file protocol:** the tracker and detail files are **yours** for this batch.

**Standing rules:** verify claims against code · fix, don't disable · record findings in the detail
doc · **revert your fix and confirm the test goes red**.

---

## 2. 🔴 BP-109 — why this is not a demo

📄 [detail](Blueprint_Issues_Detail.md#bp-109)

**No test anywhere has ever executed a `CallPeerBlueprint`.** Every occurrence in the suite is
compile-time:

| Test | Level |
|---|---|
| `V_PeerReferencesTests` | Stage 2 validation |
| `NodePinSchemaEnrichmentTests` | editor pin projection |
| `DynamicKindNodeCreateTests` | node creation |
| `AssetJsonRoundTripTests` | serialization |

Both halves individually locked; **the seam never crossed** — trap #9, on the feature the user most
wants to rely on. It is how [BP-66](Blueprint_Issues_Detail.md#bp-66) survived for months with a green
suite while the peer catalog scanned a directory that did not exist.

⚠ **Treat a failure here as a finding, not an obstacle.** If the peer call does not execute, you have
found a live defect in a shipped feature — register it and report it. **Do not reshape the scenario
until it passes.** That would convert the one test that could have caught it into another test that
cannot.

---

## 3. What to build

### The scenario

Two entities, two different Instance blueprints, one shared Function Library:

| Asset | Dispatch | Contains |
|---|---|---|
| `SmokePatrol` | `Instance` | a **local** function returning something derived from its own state · a call to the shared library function |
| `SmokeGuard` | `Instance` | a **different** local function, also state-derived · a call to the **same** shared library function |
| `SmokeMathLib` | **`Library`** | one shared function taking args and returning a value |

**Assertions that make it worth having:**

1. Each entity's **local** function returns a value derived from *that entity's* state ⇒ the two
   entities must produce **different** results. A test where both return the same number proves the
   plumbing but not the isolation.
2. The **shared library** function returns a **consistent** result for equal inputs, called from both.
3. ⭐ **The peer call actually ran** — assert on its returned value, not on "the graph ticked".

### Ship as recipe assets, not test-only fixtures

Put them in `Recipes/Blueprints/` so the same artifact is **both** the gate test's input **and** sample
data a designer can open. A fixture only the test can load proves less than one a human can also
inspect — and the user explicitly asked for openable sample data.

⚠ `Recipes\Blueprints\*.bp.json` is already globbed as `Content` with `CopyToOutputDirectory`, so
registration is one entry, not new plumbing.

### The substrate

`BlueprintRunHarness` (`Tests/Runtime/BlueprintRunHarness.cs`): spawns an entity, attaches a registered
blueprint, pumps the **real** `BlueprintTickSystem` + `BlueprintMaintenanceSystem`, reads a field via
`BlueprintStateView.TryGetField<T>`. Six callers to mirror.

⚠ **Every current caller does one entity, one asset.** Two entities running different blueprints in one
world is **plausible but unproven** — verify it, do not assume it. If the harness cannot do it, that is
🔴 Opus work and possibly its own tracker row.

---

## 4. ⚠ What is NOT in this batch

**[BP-108](Blueprint_Issues_Detail.md#bp-108), the Print/Log node — deliberately excluded.**

The coordinator initially proposed pairing them, then checked and reversed: there is **no `ToString` /
`Format` / `Concat` node and no string coercion** anywhere, so a Print node accepting only a string
could print **literals only** — useless here. It must take a **format literal + typed value args**,
which is a pin shape no node has, and the repo rule is that a non-trivial node gets a design note
first. 💡 The likely cheap answer is **fixed arity** (`Print(format, arg0..arg2)`, per-pin declared
types) rather than true variadic — but that is a design call, not a build call.

⇒ **Assert via `BlueprintStateView.TryGetField<T>`.** Less legible than log lines; entirely sufficient.
The test can be made to read better once BP-108 lands.

---

## 5. Gates

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

**Baseline, measured by the coordinator on the merged Batch-21 tree:**
build **0 errors** · Blueprints **2905 / 0 / 10 skipped** · AiShared **1213 / 0** · BTree **612 / 0**.
Known flakes: `PdbEmbeddedSourceTests`, `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`.

⚠ **New recipe assets are compiled as an MSBuild step of `Hrot.AI.Behaviors`** — a malformed one
breaks the **solution build**, not just a test (that is [BP-103](Blueprint_Issues_Detail.md#bp-103)'s
lesson). Build after adding them, before writing the test.

---

## 6. Parallel session — what it means for you

A Windows session is running the **visual check** of Batches 20–21 at the same time, on a different
machine and a different checkout.

- **Nothing it does affects your tree**, and nothing you push blocks it — it is testing the merged
  Batch-21 state, which is already pushed.
- **Anything it finds goes to the coordinator**, gets registered, and lands in a **later** batch.
  ⚠ **Do not widen this batch to chase incoming reports.**
- ⚠ **Do not commit scratch `.bp.json` assets.** [BP-87](Blueprint_Issues_Detail.md#bp-87) is still
  open — an asset with an unresolvable type (`Vector3`, `uint`, …) breaks the solution build for
  everyone who pulls.

---

## 7. Reporting back

1. **The gate numbers you actually ran** — not "gates green".
2. **Which tests you reverted the fix against, and that they went red.** ⚠ For a *new* test the
   equivalent is: break the thing it covers and confirm it fails. A smoke test that cannot fail is
   worse than none.
3. **What you delegated to Sonnet and what you kept.**
4. **Did the peer call work first try?** State it plainly either way — if it did not, that is the most
   valuable output of this batch.
5. Anything contradicting this handoff; anything deliberately not done.

⚠ **Register what you leave behind as a tracker row, not a note inside a `DONE` block.**

**Definition of done:** gates green vs the baseline · the four assets load in the editor's recipe
picker · tracker rows `[x]` with `DONE` notes · counts reconciled **three ways** · pushed to
`claude/blueprint-macro-feature-sdmspn`.

⚠ **Do not create a pull request.**
