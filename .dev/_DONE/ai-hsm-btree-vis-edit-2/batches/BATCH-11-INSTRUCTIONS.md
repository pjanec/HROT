# BATCH-11 — FlowControl composite node color (gray → distinct) **[VISUAL GATE]**

**Task:** TASK-BT-11 (REVIEW-BT F4). **One objective. Minor/cosmetic.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.
**[VISUAL GATE]:** the actual color is confirmed by the lead in the running editor; you make the mapping/value change + a headless assertion if one is feasible.

## 📋 Onboarding / context
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-11-REPORT.md`.
- User feedback (REVIEW-BT F4): BTree **composite** nodes (Root/Sequence/Selector/ObserverSelector/Parallel → `NodeCategory.FlowControl`, from BT-02) render **gray**, which reads as inert. The host design (`docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §2) shows composites in an **orange** flow-control tint. The category mapping (BT-02) is correct; only the *color* assigned to `NodeCategory.FlowControl` needs to be a clearer, distinct hue (orange).

## 🎯 Objective
Make `NodeCategory.FlowControl` render in a distinct, readable color (orange-ish) instead of gray, in the theme the BTree (and shared AI) canvas uses — WITHOUT changing the other categories' colors and WITHOUT regressing Blueprint/HSM appearance unexpectedly.

## Implementation (investigate first — pick the least-invasive correct spot)
1. Find where `NodeCategory` → color is resolved for the canvas. Likely the `IEditorTheme` implementation (e.g. `DefaultTheme` in `FDP/ExtDeps/NodeEdit/src/**` or a Hrot theme adapter `Hrot.Editor.AiShared`/`Hrot.BTree.Editor` `*Theme*`). Search for `NodeCategory.FlowControl` / a category→color map / `GetNodeColor`/`HeaderColor`.
2. Set `FlowControl` to a clear orange (e.g. an existing orange constant if the theme has one, else a sensible `Vector4`/color ≈ (0.85, 0.45, 0.12, 1) — match the theme's existing color style/format). 
3. **Scope check:** if the same theme/color map is shared by Blueprint/HSM, confirm changing `FlowControl` is acceptable for them too (FlowControl in Blueprint = exec/flow nodes — orange is reasonable and matches Unreal conventions). If a BTree-specific theme override exists, prefer changing it there. Do NOT change other categories (Function/Pure/Macro/etc.).

## 🧪 Tests
- If the color is resolved through a testable method (e.g. `theme.GetCategoryColor(NodeCategory.FlowControl)` or similar): add a headless test asserting `FlowControl` returns the new color and is **different from** `NodeCategory.Comment`/the previous gray, and that another category (e.g. `Function`) is unchanged.
- If the color is a private constant with no test seam, note that in the report; a build-only change is acceptable for this cosmetic batch (the lead confirms the hue visually at REVIEW-BT-2). Do NOT add a brittle test that just restates the literal.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.
- [ ] `Failed: 0` in any touched test project.
- [ ] `NodeCategory.FlowControl` resolves to the new distinct color; other categories unchanged.
- [ ] Report written (which file/seam you changed; whether a test was feasible; note Blueprint/HSM impact if the theme is shared).

## Notes
- Cosmetic; keep the change tiny. The final hue is the lead's visual call — pick a reasonable orange and note it.
- If you discover the BTree canvas uses a *different* theme than Blueprint/HSM, change only the one BTree uses (and say so).
