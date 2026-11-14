# Morning hand-off — overnight session (2026-06-06 → next morning)

All work is committed on `blueprint-integ-1`, each batch lead-reviewed + headless-verified (0 new test
failures throughout; solution builds 0 CS errors). Below: what to visually review, then what's left.

## Committed this session (newest first)
- `98992bda` **SE2** — per-asset facet picker dropdowns (BTree action/blackboard-field; HSM action/guard/state/event) in the Inspector.
- `2bd9ba67` **SE1** — InspectorWindow now renders facets live via StructEdit (enum→combo, bool→checkbox, number/string) instead of the stub; wired at the composition root.
- `0e1c5505`/`05ac73c8` — design records (ROUND-4 inline-latent; ROUND-5 Wait-Until-Completed).
- `5e1b97be` **JSON-PRETTY** — `.bp.json` saves pretty-printed + numeric arrays inlined (scenario formatter); committed assets reformatted.
- `7c9b7189` **ENUM-NAME** — enum defaults persist as member NAME + codegen emits `global::FQN.Member`.
- `907bd0f8` **ENUM-SAMPLE** — demo enum-param action ("Locomotion / DemoEnumAction") + "Enum Demo (AN6)" recipe.
- `cee67887`/`addeeb9b`/`9d932a73`/`9f8690f7`/`81227f70`/`176b329c` **AN5/AN4/AN3/AN6/AN1/AN2** — per-action immutable palette, unified action catalog, enum data pins, Stage-3 default-literal materialization, enum-FQN acceptance.
- `3a53c235` **UX1** + `908b8a2f` NodeStatus + `2bc9ae11` FixedString (earlier).

## Please VISUALLY REVIEW (running editor; close it before rebuilding to avoid DLL locks)
1. **BTree/HSM Inspector (SE1/SE2):** select a BTree action / HSM state node → the Details/Inspector should show a 2-column **Property | Value** table of editable rows (NOT the old "[FacetName] + Apply" stub). Enum fields = combos; MethodFqn / blackboard-field / HSM refs = **dropdowns** (SE2). Edits commit back. *This is the main thing to confirm.*
2. **Per-action palette (AN4/AN5):** blueprint palette lists one entry per channel action (e.g. "Locomotion / MoveTo"); dropping one creates an immutable node with baked param pins + read-only Channel/Action labels.
3. **Enum editor (AN1/AN2/AN6 + sample):** New-from-Recipe → "Enum Demo (AN6)" (or drop "Locomotion / DemoEnumAction") → the `Stance` pin shows a combo (Standing/Crouching/Prone); saved JSON stores `"Stance":"Crouching"` (name); compile emits `global::Fdp.Toolkit.Behavior.Demo.DemoStance.Crouching`.
4. **Pretty JSON (JSON-PRETTY):** re-save any blueprint → indented, numeric arrays (e.g. a Vector3 default) on one line.

Known-deferred visual nits (not bugs): picker-dropdown polish was wired in SE2 but only morning-confirmable; the demo `DemoEnumAction` is a removable test fixture (runtime no-op).

## What I deliberately did NOT build overnight (needs you + design, not blind)
- **BB1 — per-param action authoring (the big one):** project an action's *ParamsType DTO fields* into the BTree/HSM inspector so each parameter can be set to a **static literal OR bound to a blackboard variable** (Blackboard DD §10/§11). This needs a NEW persisted binding schema in `.btree.json`/`.hsm.json` (today the facet has only `MethodFqn` + a single `ExpressionTargetField`) and does not fit the current static-facet-struct model — it's a real design decision (the persistence shape), so it should be scoped/designed with you rather than blind-built. **This is the next major task.**
- **Slice 1.5c/d/e — aggregation / Approach-A aliasing / Approach-B sync** (Blackboard DD §5/§7/§8): large, persistence-coupled; sequence after BB1.
- **Picker-dropdown per-asset polish:** SE2 wired re-registration on ActiveChanged; if a dropdown looks empty/stale on a freshly-opened asset, that's the spot to iterate.

## Action-node track (parallel; needs a "go", design fully resolved)
- **AN7** — editor: generalize the action node + palette to NON-channel actions (`[SharedAiAction]`/AiPrimitive), named by FQN, pins from their ParamsType. (Not blocked.)
- **AN8** — compiler: inline-latent lowering for non-channel action invocation (ROUND-4: `(self,ctx,DTO)->NodeStatus`, Running suspends via BlueprintLatentCursor; AiPrimitive working state over Blackboard1024; Slice-1 one-stateful-per-entity).
- **AN9** — "Wait Until Completed" static metadata (ROUND-5): default-true checkbox in Details; Stage-5 fuses ChannelCommand+WaitForChannel (channel) / inline-latent (non-channel); Stage-2 BP1405; disabled-for-non-channel in the UI. Makes channel + non-channel action nodes consistent (both block by default).

## Notes / debt
- `BTree/HSM JSON still minified` (JSON-PRETTY did blueprint only) — fast-follow for consistency.
- Vector/Quaternion inline-default literals not materialized yet (AN1 skips them); enums assume int-backed (size 4).
- Pre-existing test failures (NOT ours): ScoreCrossed, AllocatesZeroBytes, Library/LibraryMath snapshot CRLF/bin-copy flake.
- Your experiment files (`Counting.bp.json`, `EnumDemo.bp.json` instance, `format_bp_json.csx`) were left untouched/uncommitted.
