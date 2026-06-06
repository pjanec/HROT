# BF-FIXEDSTRING: support Fdp.Core.FixedString32 / FixedString64 as blueprint string pin types

**Architect-confirmed:** the intended blueprint string type is `Fdp.Core.FixedString32` / `FixedString64`
(unmanaged, blittable), NOT `System.String` (managed; forbidden in state by BP1503). The compiler type registry
and the editor are missing these types — add them so FixedString-typed pins/params resolve, render a string
inline editor, and the types appear in the variable/pin type pickers.

**Scope note (important — do NOT over-build):** Inline pin DEFAULT VALUES are NOT consumed by the compiler today
for ANY type — `Stage3_Normalize.MaterializeDefaultPinLiterals` is a no-op stub (verified). So this batch makes
FixedString pins RECOGNIZED + EDITABLE in the editor (type resolves, mini-editor renders, value persists in
Node.PinDefaults), matching how int/float/etc. already behave. Making inline defaults actually compile is a
separate cross-cutting Stage3 feature (out of scope; note it in the report, don't implement it here).

## Lead-verified facts (file:line)
- `Fdp.Core.FixedString32` / `FixedString64` exist (FDP/Engine/Fdp.Core/FixedString32.cs, FixedString64.cs):
  `[StructLayout(Sequential, Size=32/64)]`, unmanaged, `public FixedString32(string)` ctor + implicit
  `operator FixedString32(string)`. Sizes: 32 and 64 bytes.
- `StaticTypeRegistry.cs` `TypeTable` uses `Unmanaged(fullName, sizeBytes)` (line ~120) for blittable types;
  `System.String` is the only `IsUnmanaged=false` entry. FixedString32/64 are ABSENT.
- `BlueprintTypeSystem.cs`: `_types` dict (color/name) + well-known constants (Bool/Int32/...) + `SelectableTypeIds`.
- Editor registry: `BlueprintDocumentFactory.cs:123` `PinDefaultValueEditorRegistry.CreateWithBuiltins()` (in the
  NodeEdit framework) registers bool/int/float/string/vectors/quat/color/guid — NOT FixedString. The registry
  supports post-construction `Register(TypeKey, editor)` (host may add entries). `StringPinEditor` exists.
- `BlueprintPinModel.cs` `BlueprintPinDefaultValue.ParseValue` has per-TypeId cases (e.g. "System.String" → "").

## Tasks
1. **StaticTypeRegistry.cs** — add to `TypeTable`:
   `["Fdp.Core.FixedString32"] = Unmanaged("Fdp.Core.FixedString32", 32),`
   `["Fdp.Core.FixedString64"] = Unmanaged("Fdp.Core.FixedString64", 64),`
   (These ARE unmanaged, so they're valid in Variables/WorkingState — unlike System.String which BP1503 rejects.)
2. **BlueprintTypeSystem.cs** — add `public const string FixedString32 = "Fdp.Core.FixedString32";` +
   `FixedString64`; add `_types` entries (pick a distinct string-ish color + names "FixedString32"/"FixedString64");
   add both to `SelectableTypeIds` (so the variable-create dropdown offers them — ideally near `String`, or
   INSTEAD of `String` per the architect's "String forbidden in state" note — but do NOT remove String handling,
   just add FixedString as the preferred option).
3. **Editor registry wiring** — in `BlueprintDocumentFactory.cs` right AFTER
   `PinDefaultValueEditorRegistry.CreateWithBuiltins()`, register a string editor for both FixedString TypeKeys:
   `editorRegistry.Register(new TypeKey("Fdp.Core.FixedString32"), new StringPinEditor());`
   `editorRegistry.Register(new TypeKey("Fdp.Core.FixedString64"), new StringPinEditor());`
   (Reuse `StringPinEditor` — a FixedString default is authored as plain text. Confirm StringPinEditor's
   namespace/usings. If StringPinEditor enforces no length cap, that's fine for now; optionally pass a max length
   if its ctor supports it: 31/63 usable bytes. Keep minimal — reuse, don't fork, unless trivial.)
4. **BlueprintPinModel.cs `ParseValue`** — add cases for the two FixedString TypeIds returning `""` for null/empty
   (type-zero) and passing the raw string through otherwise (mirror the `System.String` case exactly).
5. **Demo coverage** — add an unconnected In-data FixedString32 (and/or 64) pin to the editor-types demo recipe
   (`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EditorTypesDemo.bp.json`) so the user can SEE the
   string editor render. Pick a node that legally carries such a pin; if no built-in node exposes a FixedString
   data-in pin, add a Variable of type FixedString32 to the recipe (Variables drive a My-Blueprint editor) OR a
   LiteralNode/value pin — choose whatever makes the editor visibly render headlessly-verifiably. Keep the recipe
   valid (kind-first nodes, $meta, byte-stable, compiles).
6. **Tests (headless)** —
   - `StaticTypeRegistry.TryResolve("Fdp.Core.FixedString32")` succeeds with IsUnmanaged=true, SizeBytes=32 (and 64).
   - The editor registry returns a non-null editor for both FixedString TypeKeys.
   - `BlueprintPinDefaultValue.ParseValue` round-trips a string for both TypeIds (null → "").
   - The demo recipe deserializes + compiles (BlueprintCompiler.Compile, no errors) + a FixedString pin's model
     `Default` returns an editor.

## Gate
- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors / 0 new warnings. (If `Hrot.ClusterRunner` is running and
  causes MSB3021/MSB3027 copy-lock errors — NOT CS errors — note it; those are the running editor, not a code
  problem. Confirm there are zero `error CS`.)
- Blueprints suite WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS` set: subset of the known pre-existing failures
  (`ConditionSummary ScoreCrossed`, `AllocationFree AllocatesZeroBytes`; the `Library`/`LibraryMath` demo snapshots
  may also show as failing due to a known bin-copy/line-ending quirk — note if so, do NOT chase or regenerate them).
  0 NEW failures attributable to this batch. Report the failing-set by name. Do NOT use regen mode for the final
  verification run (it masks snapshot failures).
- Report → `.dev/blueprint-finalize/reports/BF-BATCH-FIXEDSTRING-REPORT.md`.

## Constraints
Branch `blueprint-integ-1`. Projection-only. Do NOT regenerate goldens. Do NOT touch EditorSubsystem/
RecipeCreateModal/AssetBrowserWindow or Count*/Loco1/InlineEd1 .bp.json. Do NOT implement Stage3 default-literal
materialization (out of scope — note it). Do NOT remove System.String handling. Do NOT commit (lead commits).
Reuse StringPinEditor; do not invent a new editor contract. Sub-agent model: sonnet.
