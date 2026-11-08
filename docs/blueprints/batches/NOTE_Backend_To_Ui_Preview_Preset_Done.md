# NOTE — backend → ui: a Preview WITH a run setup is complete (CE-4326, your L-10)

`DONE — a preset's params and its macro args now apply in place too (parts ② and ③); every preset previews as it would load`

Follows [NOTE_Backend_To_Ui_Preview_Preset_Part1](NOTE_Backend_To_Ui_Preview_Preset_Part1.md). The calls are unchanged:
`DryRunIntents.Enter(startPaused, presetArgs)` and `POST /preview/enter {args | variant | preset}`.

| what now applies in place | how | rail |
|---|---|---|
| the variant (presence, places, one-of-N macros) | part ① | `VariantFormsTests.CE4326_APresetAppliedInPlace_…` |
| ⭐ a pen preset's PARAM values on bound fields (`ParamBindings` paths) | part ②: each bound entity goes through the host's scenario serializer and the ONE binder, and only the changed components are written back | `VariantFormsTests.CE4326_APresetsParams_AppliedInPlace_EqualTheLoadWithThatPreset` |
| ⭐ a param that reaches a macro's children (through the instance's args) | part ③ (P2): the macro reconcile also writes to kept children every component the new args changed | `MacroExpanderTests.CE4326_P2_…` |

⚠ One gap, measured as unused: a `"^name"` param form on a TOP-LEVEL scenario entity (no `scenarios/*/scenario.json` has one; inside macros these
forms re-bind on re-expansion). Design: [DESIGN_Scenario_Presets §9a.2–§9a.3](../../UX/DESIGN_Scenario_Presets.md).

⭐ On your side, nothing changes beyond the part ① note: pass the run setup through `IRunTransport.EnterDryRun` → `DryRunIntents.Enter`, and
*Run setup…* on the Preview row can offer every preset.

---
_backend lane, 2026-11-08_
