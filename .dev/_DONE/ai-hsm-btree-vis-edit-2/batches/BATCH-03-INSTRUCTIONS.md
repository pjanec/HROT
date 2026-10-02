# BATCH-03 — Pill glyph + param label (BTree) **[VISUAL GATE]**

**Task:** TASK-BT-03 (`.dev/_DONE/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-03--pill-glyph--param-label`)
**Phase:** A · **One objective only.**

## 🔒 Working agreement (MANDATORY)
Same as prior batches (`.dev/_DONE/ai-hsm-btree-vis-edit-2/TASK-TRACKER.md`): one task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.
**[VISUAL GATE]:** implement + write the **headless** test asserting the Label/Glyph contract below. Exact glyph aesthetics are confirmed by the lead in the running editor later — you only guarantee the contract.

## 📋 Onboarding
- Design: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §5 (EB-B); host `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §6.
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-03-REPORT.md`.

## 🎯 Objective
Decorator pills currently show the bare enum name (`BTreePillAttachmentModel.Label => _pill.DecoratorType.ToString()`, `Glyph => null`). Make each pill show a per-type **glyph** and a **label that includes its parameter** (Repeater count from `IntParam`, Cooldown duration from `FloatParam`), so a pill stack reads like "Repeater 3 / Cooldown 2s" rather than "Repeater / Cooldown".

## File (exact)
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` — in `BTreePillAttachmentModel`, replace `Glyph`/`Label`:

- **`Glyph`** — a short non-null per-`DecoratorType` symbol. Prefer ASCII-safe single chars to guarantee font rendering (e.g. Inverter `"!"`, Repeater `"R"`, Cooldown `"C"`, ForceSuccess `"S"`, ForceFailure `"F"`, UntilSuccess `"U+"`, UntilFailure `"U-"`). (You MAY use a nicer unicode glyph, but it must be non-null for every decorator type.)
- **`Label`** — MUST include the parameter value when the type has one:
  - `Repeater` → label includes the `IntParam` value (e.g. `"x3"` for `IntParam == 3`; if `IntParam` is null use `1`).
  - `Cooldown` → label includes the `FloatParam` value with an `s` suffix (e.g. `"2s"` for `FloatParam == 2`).
  - `Inverter`, `ForceSuccess`, `ForceFailure`, `UntilSuccess`, `UntilFailure` → a short human label (the type's short name is fine).
  - Use `System.Globalization.CultureInfo.InvariantCulture` for the float→string so the label is locale-independent (no comma decimal).

`_pill` is `BTreeEditorPill` with `DecoratorType` (`Fbt.NodeType`), `IntParam` (`int?`), `FloatParam` (`float?`), `Comment`, `StackIndex`. Do NOT change `Id`, `HostNodeId`, `Category`, `Tooltip`, `State`, `StackIndex`.

## 🧪 Tests (write EXACTLY these; new file `Model/BTreePillLabelTests.cs`)
Build a `BTreeEditorPill` directly (or via the asset's pill API) and wrap a `BTreePillAttachmentModel` over it (it is `internal` — the test assembly already has access, as in `Host/BTreeDynamicCatalogTests.cs`). Assert the `IAttachmentModel` projection:

- `Repeater_LabelIncludesCount`: pill `DecoratorType=Repeater, IntParam=3` → `Label.Contains("3")`, `Glyph` non-null/non-empty.
- `Cooldown_LabelIncludesDuration`: pill `Cooldown, FloatParam=2f` → `Label.Contains("2")` and `Label.Contains("s")`, `Glyph` non-null.
- `Cooldown_LabelIsInvariant`: pill `Cooldown, FloatParam=2.5f` → `Label.Contains("2.5")` (dot, not comma) — run under any culture (set `CultureInfo.CurrentCulture` to `de-DE` in the test to prove invariance).
- `Inverter_HasGlyphAndLabel`: pill `Inverter` → `Glyph` non-null/non-empty, `Label` non-null/non-empty.
- `AllDecoratorTypes_HaveNonNullGlyph`: `[Theory]` over Inverter/Repeater/Cooldown/ForceSuccess/ForceFailure/UntilSuccess/UntilFailure → `Glyph` non-null/non-empty for each.

(If wrapping `BTreePillAttachmentModel` directly is awkward, build a `BehaviorTreeAsset`, add the pill via its pill API, construct `BTreeGraphModel`, and read the attachment via `GetAttachmentsForNode`/`FindAttachment` — assert the same.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0**.
- [ ] Every decorator type has a non-null glyph; Repeater/Cooldown labels include their param; Cooldown label is locale-invariant.
- [ ] Only `BTreePillAttachmentModel.Glyph`/`Label` changed.
- [ ] Report written.

## Notes
- Locale bug class: a `float.ToString()` without `InvariantCulture` produces `"2,5"` on `de-DE` — that's the exact mistake FIX-B fixed elsewhere. Use InvariantCulture.
