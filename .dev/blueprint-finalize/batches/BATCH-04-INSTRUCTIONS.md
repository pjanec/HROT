# BATCH-04 — DEBT-MVE-002: emit StateFields in codegen (durable observe)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, run implement→build→test→fix to green. **Codebase Memory MCP first**
> (`search_graph`/`get_code_snippet`). Project `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.

## Mission

Today the C# emitter does NOT write `BlueprintDefinition.StateFields`, so a **compiled** Instance
blueprint's working state can't be read by field name via `BlueprintStateView.TryGetField` (the
observe/hot-reload tests use hand-built defs or the DebugMap path as a workaround). Emit `StateFields`
from the compiler's already-computed field layout so compiled blueprints are observable by name.

## Verified facts (lead-confirmed — re-verify, don't re-derive)

- `BlueprintDefinition.StateFields` (`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs:23-25`):
  `IReadOnlyDictionary<string, BlueprintFieldDescriptor>` (key = field name, Ordinal comparer; default empty).
- `BlueprintFieldDescriptor` (`.../BlueprintFieldDescriptor.cs:6-11`): positional record
  `(string Name, System.Type ClrType, int OffsetBytes, int SizeBytes, string CategoryOrEmpty)`.
- `BlueprintStateView.TryGetField<T>` (`.../BlueprintStateView.cs:26-36`) reads at
  `_slotMemory + fd.OffsetBytes`, where `_slotMemory` is **byte 0** of the slot payload. The 16-byte
  `BlueprintLatentCursor` occupies bytes 0-15; user variables start at byte 16.
- `FieldLayout.ComputeFieldLayouts` (`.../Compiler/Lowering/FieldLayout.cs:7-15`) lays out
  `asset.Variables` with **`startOffset: 16`** — so each `IrField.Offset` for an Instance variable is
  **already absolute from byte 0** (first variable = 16). **Emit `f.Offset` DIRECTLY — no +16/−16
  adjustment.** This matches what `TryGetField` expects and what the DebugMap path already emits.
- `IrField` (`.../Compiler/Ir/IrAsset.cs:5-14`): `Name`, `Type` (IrTypeRef; `Type.FullName` = CLR name),
  `Offset` (set by FieldLayout), `Size` (= Type.SizeBytes).
- For **Instance** dispatch, user state lives in `asset.Variables` (NOT `asset.WorkingState`, which is
  empty for Instance). Enumerate `asset.Variables`.
- Layout runs in Stage6 before Stage7 emit (`Stage6_Lower.cs:24`), so offsets/sizes are available at
  emit time via the lowered `IrAsset`.

## Change — emit StateFields

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`,
method `EmitInstanceRegistration` (~line 222-250).

Insert a `StateFields = ...` initializer **after** `WriteLine($"Tick = {className}.TickThunk,");`
(~line 237) and **before** the `if (eventHandlers.Count > 0)` block (~line 238). Only Instance
registration gets it — do NOT touch `EmitAiPrimitiveRegistration` / `EmitLibraryRegistration`.

Emit a dictionary initializer mapping each variable's name → a `BlueprintFieldDescriptor`. Requirements:
- Iterate `asset.Variables` in order.
- Key: the field name string (escaped).
- Descriptor: `new global::Fdp.Toolkit.Blueprints.BlueprintFieldDescriptor("{f.Name}",
  typeof({csharpType}), {f.Offset}, {f.Size}, "")` where `csharpType` comes from the SAME helper the
  emitter already uses for CLR types — `StatementEmitter.TypeRefToCSharp(f.Type)` (used at
  `CSharpEmitter.cs:66-70` for the DebugMap pins). Verify that helper produces a `global::`-qualified,
  `typeof()`-valid type expression; if not, use the form already proven in the DebugMap emission.
- Use an Ordinal-comparer dictionary to match `BlueprintDefinition`'s default
  (`new global::System.Collections.Generic.Dictionary<string, global::Fdp.Toolkit.Blueprints.BlueprintFieldDescriptor>(global::System.StringComparer.Ordinal) { ... }`).
- **Only emit the block when `asset.Variables.Count > 0`.** When there are no variables, omit it entirely
  so variable-less Instance goldens do not change (the record default already supplies an empty dict).
- Match the emitter's existing indentation / `WriteLine` style exactly.

**Verify-first before coding:** read `CSharpEmitter.cs:60-90` (the DebugMap emission) to copy the exact
type-expression pattern, and read `EmitInstanceRegistration` in full to match brace/indent style. Confirm
the emitted `State` struct lays its variables at `f.Offset` (so the descriptor offsets are correct) —
the DebugMap already relies on this, so it should hold; cite the line.

## Golden regeneration (do this CAREFULLY)

1. Implement the emission, build.
2. Run the golden/snapshot tests and observe **which** goldens fail:
   `dotnet test Hrot.Blueprints.Tests --filter "FullyQualifiedName~EmitGolden|FullyQualifiedName~Snapshot"`.
   Only **Instance-dispatch** assets **with variables** should change. (Note: `MoveToAndFire` is
   **AiPrimitive** dispatch — its golden must NOT change. If it changes, you have a bug. Library/AiPrimitive
   goldens must be untouched.)
3. Regenerate ONLY the genuinely-affected goldens:
   `$env:BLUEPRINT_REGENERATE_SNAPSHOTS=1; dotnet test Hrot.Blueprints.Tests --filter "<the failing tests>"; Remove-Item Env:\BLUEPRINT_REGENERATE_SNAPSHOTS`
4. **Inspect every regenerated golden's git diff** and confirm it is **purely additive** — a single new
   `StateFields = { ... }` block inside the Instance registration, nothing else changed. If any golden
   shows a non-StateFields change, STOP and report — do not commit a non-additive golden change.
5. Check `HasVisibleTarget_EndToEndTests` (substring-based, not snapshot) — if its substring assertions
   reference the registration block, update them; otherwise leave untouched. Confirm by reading the test.

Candidate Instance goldens (verify which actually change — let the failing tests tell you, don't assume):
`Hrot.Blueprints.Tests/Snapshots/Emit/InstanceCounter.cs.txt`, `HealthRegen.cs.txt`, `DoorActor.cs.txt`.

## New test — prove the payoff

Add a test that compiles an **Instance** blueprint with a named variable end-to-end (Roslyn-compile the
emitted source, register, attach, tick) and asserts the **compiled** def's `StateFields` contains the
variable by name with the right Offset/Size, and that `BlueprintStateView.TryGetField<T>(name)` returns
the live value — WITHOUT any hand-built def or DebugMap workaround. Use the existing compile+run harness
(`BlueprintTestFixture` / `BlueprintRunHarness` — `CompileAndLoad`/`SpawnAndAttach`/`ReadIntField`). Place
it near the other Instance compile tests. This is the proof DEBT-MVE-002 is closed.

## Verification (paste real output)

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. The regenerated goldens pass; the diff is purely additive `StateFields` (state this explicitly).
3. Full `Hrot.Blueprints.Tests` — the previously-failing **DEBT-006 golden tests that you regenerated now
   PASS** (so the pre-existing failure count DROPS by the number you regenerated); confirm no NEW failures
   appear and no non-regenerated test newly fails. Report the before/after failure list precisely.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10
   (project: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/...`).

> Note on DEBT-006: those golden failures are "golden-source drift" — by regenerating the affected Instance
> goldens you are intentionally re-baselining them as part of this task. After this batch, the regenerated
> ones should be GREEN. Do not regenerate goldens you did not change (Library/AiPrimitive/Demos that don't
> involve StateFields). Report the exact remaining pre-existing failures.

## Report

Write `.dev/blueprint-finalize/reports/BATCH-04-REPORT.md`: the emission code (file:line), the exact
type-expression form used, the list of goldens regenerated with confirmation each diff was additive-only,
the new proof test, before/after full-suite failure breakdown, and whether a DEBT-MVE-002 tracker row
exists (update it to RESOLVED if so; otherwise note it). **Do not commit** — the lead reviews and commits.
