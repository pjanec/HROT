# BATCH-30 Review — MTB2-T1

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- Read `IconWidgets.cs` diff: `ComputeIconRect` is pure + correct (inset = size*scale, centered margins); both
  `IconHandle` overloads gained `iconScale = DefaultIconScale (0.9)`; image drawn into the inset rect while the
  `InvisibleButton` hit/spacing box stays at full `size` (layout unchanged); toggled fill (Header @0.45) and hover
  fill (Header @0.25, only when not toggled) are distinct; disabled → `Dummy` + dimmed image, no fills. Matches spec.
- Tests assert real values (exact rect coords, equal margins, strictly-inside, default-scale constant) — would fail
  on a broken impl. No skips/tautologies/weakening.
- Built `Fdp.Presentation` → **0 warnings**. Ran `Fdp.Presentation.Tests` (filtered IconWidgets|ToolbarCommandAdapter)
  → **45/45 pass** (42 existing + 3 new), no regen flag.
- Scope clean: only `IconWidgets.cs` + its test file changed; shared widget, no hitbox/layout change.

## Pending (lead runtime, non-blocking)
- Eyeball in the live editor that toolbar icons have margin + visible hover + visible toggled state. Colors/alpha are
  tunable in `ToggleIcon` if the fills read too strong/weak — no code-correctness risk.

## Commit
`feat(main-toolbar2): generic toolbar icon 90% inset + clear hover/toggle (MTB2-T1)`
