<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: the table -->
# STATUS — backend, the struct editor overnight

[Handoff](HANDOFF_Coordinator_To_Backend_Struct_Editor_Overnight.md) · dispatched at `32728eb5` · started at `f11cc21ce` (merged `coordinator@f11cc21ce`).
Design: [`DESIGN_Struct_Editor.md`](../../DESIGN_Struct_Editor.md). ui and behaviors are on hold.

| item | state | sha | notes |
|---|---|---|---|
| N-0 CE-3377 the widget kit | IN PROGRESS | — | worker |
| N-1 CE-3378 the catalogue | — | — | next for the worker (touches the same drawers as N-0) |
| N-2 CE-3379 list ops + forms | model DONE · forms — | `25f0099f2` | `IContainerOps` (StructEdit.Core): insert / remove-at / in-place move / deep duplicate / clear / never-null new element; `ContainerOpsTests` 6 |
| N-3 CE-3380 parse / validate | model DONE · table — | `b0ca95bd4` | `ValueText.TryParse / Format` (message per refusal, long, flags, nullable); `ValueTextTests` 11, red-proved |
| N-4 CE-3381 default / changed / read-only | model DONE · table — | `db885cc52`, `b0ca95bd4` | S8 `[EditReadOnly]` applied (`EditReadOnlyAppliedTests`; GizmoMap.Presentation.Tests 47/47); S7 `IFieldDefaults` + `FieldValues.IsChanged` (`FieldDefaultsTests` 3, red-proved) |
| N-6 CE-3383 Define | model DONE · cells — | `6fc2d37ce` | `DeclaredEditDocumentBuilder` + `JsonMemberBinding` + `DeclaredEditSession` (`DeclaredEditDocumentTests` 7, red-proved) |
| N-7 CE-3374 · CE-3375 · CE-3388 · CE-3389 · CE-3390 | data + HTTP DONE · ui surfaces (CE-3391…3394) — | `3742e650c`, `f30b2ceb9`, `b0aeea6e9` | `VariantForms` (id 449) + translator; `header.Presets`, `ScenarioPresets` resolver, `ScenarioSaveGate.CanSave`, `OpenForEdit(name, args)`; `GET /scenario/presets`, `{preset}` on load, `/status canSave`; found: an edit save dropped `header.Features` — fixed. Rails `VariantFormsTests` 4, `ScenarioPresetsHeaderTests` 3 |
| N-1 CE-3378 contract | DONE (contract) · migration — worker next | `3a70b72b4` | `TypeCatalogue` / `ITypeEditor` / field rules; `TypeCatalogueTests` 5, red-proved; Fdp.Presentation.Tests 332/0 |
| N-5, N-8 … N-11 | — | — | |

Gate rows so far: `StructEdit.Tests` 227 / 1 failed — `DocumentBuilderTests.Build_CircularReference_CircularFieldIsUnsupported`, **pre-existing** (fails alone at `f11cc21ce` in a clean worktree). `Hrot.Editor.Tests` filtered (routes / API / status / capability / extensions): 1 red — `TheEditorComposesNodeExtensionsTests.CE4293_…`, **pre-existing** (fails alone at `f11cc21ce`); `TriggerAdvertisesItsAuthoredKeys` pre-existing (resume doc).
