# RHS-04 — State-kind theming (color states by flags)

**Workstream:** RHS (../RHS-PLAN.md). **Layer:** Hrot.Hsm.Editor model + theme. **Depends:** none (independent of RHS-01/02).

## Problem

Every HSM state hardcodes `Category => NodeCategory.Custom` (`Model/HsmAsset.cs`, the `StateNode.Category` property ~line 753), and `HsmEditorTheme.GetCategoryHeaderColor(Custom)` returns `Vector4.Zero` (transparent — intended only for pseudo-states' glyph bypass). Result: all states render with no header color (flat gray body). The design (HSM host design §7) wants states colored & distinguishable by kind.

## Fix

Map `StateNode.Category` from the state's flags so NodeEditor's header colouring distinguishes kinds, while **keeping history/final states transparent** so `HsmHistoryGlyphsRenderer`'s body-bypass still works.

`NodeCategory` enum (NodeEditor.Primitives): `Function, Event, Pure, VariableGet, VariableSet, FlowControl, Macro, Comment, Custom`. `DefaultTheme.GetCategoryHeaderColor` returns a distinct colour per non-Custom category.

In `StateNode.Category` (replace the hardcoded `NodeCategory.Custom`):

```csharp
public NodeCategory Category
{
    get
    {
        // Pseudo-states keep Custom → HsmEditorTheme maps Custom to transparent so the
        // glyph renderer (H / H* / F) owns their visual. (RHS-04)
        if (IsHistory || IsDeepHistory || IsFinal) return NodeCategory.Custom;
        if (IsParallel)            return NodeCategory.Event;        // parallel composite
        if (Children.Count > 0)    return NodeCategory.Macro;        // composite
        return NodeCategory.Function;                               // simple state
    }
}
```

(Category choices above are the proposal — pick whichever existing categories give clear, distinct colours under `DefaultTheme`; document your final mapping. Do NOT invent new `NodeCategory` members.)

### Optional colour tuning (only if the default palette is poor)
If the DefaultTheme colours for the chosen categories read poorly for a statechart, you MAY override `GetCategoryHeaderColor` in `HsmEditorTheme.cs` to return statechart-appropriate hues for the categories HSM uses (composite, parallel, simple), while still returning `Vector4.Zero` for `Custom`. Keep it minimal and document the hues. If the defaults are acceptable, leave `HsmEditorTheme` as-is except confirm the `Custom`→transparent branch remains.

## Constraints

- Do NOT change the pseudo-state transparency behaviour (history/final must remain glyph-only). Verify `HsmEditorTheme.IsPseudostateKind` and the `Custom`→`Vector4.Zero` branch still hold.
- Do NOT touch renderers (RHS-02 done), region/divider code (RHS-05), the showcase JSON (RHS-06), or NodeEditor.
- Only files in scope: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` and (optionally) `Theme/HsmEditorTheme.cs`.

## Tests

- Add/extend a unit test asserting `StateNode.Category` returns: `Custom` for history/deep-history/final; `Event` for parallel; `Macro` for composite (has children); `Function` for simple. Put it in the HSM editor test project alongside existing model tests.
- If any existing test asserted `Category == Custom` for a normal state, update it to the new mapping (and explain).

## Verification (run + paste raw output)

1. `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj -c Debug -v q -nologo` → 0 errors.
2. `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj -c Debug --nologo -v q` → ≥458 passing, 0 failing.

## Report back

Final category mapping; whether you tuned `HsmEditorTheme` colours (and the hues if so); diff summary; raw build + test output. Do NOT commit — lead reviews & commits.
