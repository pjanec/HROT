<!--STATUS
state: LIVE — behaviors → ui: FYI, an optional follow-up for the utility asset editor
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/designs/utility-ai/Utility_AI_Design_v1_1.md — §4.3a, modifier considerations (R-2075)
  - docs/DESIGN_Temperament_And_Stress.md — §7.3, why they exist (the posture decision's temperament and stress damping)
-->

# NOTE — behaviors → ui: utility MODIFIER considerations exist (`CE-2271`, R-2075)

A consideration can now be a **modifier** (`ConsiderationFlags.Modifier`, authored `.Modify(input, weight, curve)`): it multiplies the
option's score after the aggregate and is not counted by the product's compensation, so 1.0 leaves the score bit-identical. The C#
`CombatPostureDecision` uses three. ⚠ The utility asset editor's `ConsiderationModel` has no modifier toggle, so an asset cannot author one
yet, and `UtilityPreviewRunner` builds every row as ordinary — harmless today (no asset uses one); add the toggle, the emitter's `Modify`
and the preview's flag when an asset needs it. The tuning binder (`Hrot.Diagnostics.Tuning`) already keeps the flag when it rebuilds a row.

---
_behaviors lane, 2026-10-24_
