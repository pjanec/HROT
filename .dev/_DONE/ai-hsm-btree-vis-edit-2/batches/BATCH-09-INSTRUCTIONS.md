# BATCH-09 — Emit AssetId in [BTreeDefinition] so JSON/assembly dedupe (REVIEW-BT F1)

**Task:** TASK-BT-09 (REVIEW-BT finding F1: duplicate CombatShowcase). **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0` across ALL touched test projects; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-09-REPORT.md`.
- **This is a "do for BTree exactly what HSM already does" task.** HSM already carries AssetId through codegen; BTree doesn't, which is why CombatShowcase appears twice (JSON-loaded + assembly-reflected with a *different*, name-derived AssetId).

## 🐛 Root cause
`BTreeAssetContributor` (assembly path) derives the AssetId from the tree NAME via `AssetIdHasher.FromName(defAttr.TreeName)` ([BTreeAssetContributor.cs:52](../../Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs#L52)) because **`BTreeDefinitionAttribute` has no `AssetId` property** (the generated `[BTreeDefinition("CombatShowcase")]` carries none). The JSON contributor uses the asset's real AssetId. When the real AssetId ≠ `FromName(Name)` (true for CombatShowcase `aaaaaaaa-…`, false-by-coincidence for SampleScout `54ef3847-…`), the two don't share an AssetId, so the AssetId-based dedupe (JSON-wins, design D4) fails → duplicate. HSM avoids this because `HsmDefinitionAttribute` HAS `AssetId` and `HsmEmitCore` emits it.

## ✅ Fix (mirror HSM exactly)
1. **`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs`** — add an additive optional property, mirroring `FDP/ExtDeps/FastHSM/.../Attributes/HsmDefinitionAttribute.cs:16`:
   ```csharp
   /// <summary>Stable editor asset GUID (8-4-4-4-12). Set by the editor codegen; null for hand-authored.</summary>
   public string? AssetId { get; set; }
   ```
   (Additive, like the existing `BlackboardManaged`/`HeavyDtoType` setters. Keep FastBTree tests green.)
2. **`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs:513`** — emit the AssetId, mirroring `HsmEmitCore.cs:468`:
   change `sb.AppendLine($"{Indent}[BTreeDefinition(\"{dto.Name}\")]");`
   to emit `[BTreeDefinition("{dto.Name}", AssetId = "{dto.AssetId:D}")]` using the same `QuoteStr(...)` helper HsmEmitCore uses (`QuoteStr(dto.Name)`, `QuoteStr(dto.AssetId.ToString("D"))`). Remove the now-stale "does not have an AssetId property" comment at line 511.
3. **`Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs:52`** — prefer the attribute's AssetId when present, mirroring how `HsmAssetContributor` reads it:
   ```csharp
   var assetId = !string.IsNullOrWhiteSpace(defAttr.AssetId) && Guid.TryParse(defAttr.AssetId, out var parsed)
       ? parsed
       : AssetIdHasher.FromName(defAttr.TreeName);
   ```
   (Check `HsmAssetContributor` for the exact idiom and match it.)
4. **Regenerate** (build): `CombatShowcase.g.cs` must now emit `[BTreeDefinition("CombatShowcase", AssetId = "aaaaaaaa-0000-0000-0000-000000000001")]`.

## 🧪 Tests
- **Emit-core test** (in the emit-core/persistence test project — find where existing `BTreeEmitCore` emit tests live; mirror the HSM emit test for AssetId): emitting a DTO with `AssetId = <guid>` produces output containing `[BTreeDefinition("<Name>", AssetId = "<guid:D>")]`.
- **Contributor test** (`Hrot.BTree.Editor.Tests`): define a test fixture static class in the test assembly with a `[BTreeDefinition("Bt09Fixture", AssetId = "12345678-0000-0000-0000-0000000000aa")]`-decorated method returning a `BehaviorTreeBlob`; call `contributor.LoadFrom(typeof(fixture).Assembly)`; assert the catalogued asset's `AssetId == Guid("12345678-…aa")` (NOT `FromName("Bt09Fixture")`). Add a second fixture with NO AssetId → asset AssetId == `AssetIdHasher.FromName("…")` (fallback preserved).
- **REBASELINE:** any existing test/golden that asserts the old `[BTreeDefinition("X")]` string (e.g. emit-core determinism tests, `Hrot.AiEditor.Generators.Tests` migration-equivalence) must be updated to the new `AssetId`-bearing form. Update them to the correct expected output; do NOT delete or weaken them.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.
- [ ] **Failed: 0** in ALL touched test projects: `Fbt.Tests` (FastBTree), `Hrot.AiEditor.Persistence.*Tests` (emit core, if present), `Hrot.AiEditor.Generators.Tests`, `Hrot.BTree.Editor.Tests`. List each project's pass count in the report.
- [ ] Generated `obj/.../CombatShowcase.g.cs` emits `[BTreeDefinition("CombatShowcase", AssetId = "aaaaaaaa-0000-0000-0000-000000000001")]`.
- [ ] Contributor uses the attribute AssetId when present, else FromName.
- [ ] Report written (per-project test counts; note: the visual "only one CombatShowcase in the browser" is confirmed at REVIEW-BT-2).

## Notes
- FastBTree is a kernel under `FDP/ExtDeps/FastBTree` — the change is additive (a new optional attribute property), exactly mirroring `HsmDefinitionAttribute`. Keep its tests green.
- If the emit-core test project or generators test has a golden snapshot for SampleScout's `.g.cs`, update it too (SampleScout now emits `AssetId = "54ef3847-0000-0000-0000-000000000000"`).
