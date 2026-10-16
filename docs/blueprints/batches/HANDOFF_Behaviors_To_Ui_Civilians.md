<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — the design this hands off (§4 CV-1, CV-4, CV-5, CV-6; §6 known limits)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — the backend half; U-1 needs B-2, U-4 needs B-1
  - docs/DESIGN_Map_3D_Mode.md — §6q civilian outfits/children (CE-1099), the body layer U-3 extends
  - docs/DESIGN_Orders_Params_Entity_Classes.md — §2 EntityClassTable (R-280), the one table U-1 reads
-->

# HANDOFF — behaviors → ui: the civilian programme's display half *(U-1…U-5)*

**Dispatched at `24fa3462e`** (behaviors). ⭐ **Your scope is FROZEN at `24fa3462e`. Documents that change after it are FYI
only**; a later document that invalidates an item ⇒ stop that item and report it.

**What it is.** The civilian programme ([DESIGN_Civilians_And_Crowd_Control](../../DESIGN_Civilians_And_Crowd_Control.md),
🔒 R-4033…R-4036) adds:
- a new civilian family, plus security forces;
- a side that flips Neutral → Hostile at runtime when a civilian commits a hostile act;
- poses for cowering and casualties;
- tear-gas and smoke clouds;
- crowds.

**These five items are what the user SEES of it.**

**This is a FRAME.** The CV decisions are leans the user has not ruled yet (design §4). ⭐ **Step 1 of each item is your
detailed design**: UML in the owning design, folded as-built. ⛔ **Build an item only after the user rules the CV it
shows**, and after the backend item it reads exists. Ids come from your block (`CE-1xxx`); state them in your report.

## Items

| # | what | design basis | key decisions, with my lean | needs | acceptance |
|---|---|---|---|---|---|
| **U-1** | **the new classes in every entity display** | design §4 CV-1; R-280 (one class table, hard-coded) | the Add-Entity picker folders (`DisNameTable`) and the 3-D model (`VisualFamilies`) read the new `Civilian` and `SecurityForce` rows of `EntityClassTable`. ⛔ No JSON, no second table. Outfits and children keep CE-1099 | backend B-2 | a user can place an adult, a child, an elderly civilian and a police officer from the picker, and each shows the right model |
| **U-2** | **live restyle when a side flips** | design §4 CV-4, §6 known limit 3 ("the SIDC symbol keeps its TKB affiliation letter and an `IgSymbolOverride` may keep its old style") | ⭐ Lean: the symbol's affiliation letter and any `IgSymbolOverride` style follow live `EntityInfo.ForceId` on all map hosts, the same way the tint already does. The TKB letter is only the birth default | backend B-3 | a civilian flipped to Hostile redraws as hostile on every map host within one update, with no reload |
| **U-3** | **civilian poses** | design §2.2 (Cower, Casualty, Dispersing), §4 CV-6 ⓑ | the body layer shows cowering (crouched, hands over head) and a lying casualty, and reads `EffectState` for KnockedDown (lying) and Irritated (an affected cue). ⭐ Lean: one pose table keyed by stance + `EffectState`, extending Map 3D §6q, not per-state models | backend B-1 (for `EffectState`) | each state is distinguishable at map zoom in 3-D; a text/indicator fallback in 2-D, as stance already has |
| **U-4** | **area-effect clouds** | design §4 CV-6 ⓒ, §2.4 | an `AreaEffectCloud` (Kind: TearGas, Smoke, later Fire; Radius; Lifespan) is drawn as a fading disc in 2-D and a volume in 3-D, through the shared map gizmo seam (all map hosts) | backend B-1 | a gas canister's cloud appears where it lands, grows to its radius and fades at the end of its lifespan |
| **U-5** | **crowd overlay** *(optional)* | design §4 CV-5 (a bodiless `Crowd` entity: members, mood, focus) | ⭐ Lean: LATER. Only once the behaviors lane's `CE-2237` exists. A hull around the members, tinted by mood, with an arrow to the focus | behaviors `CE-2237` | — |

**Order:** U-1 and U-2 first, since they are small and their backend items come first. Then U-3 and U-4. U-5 waits.

## Gates — report each row (the reviewing lane will not re-run them)

| # | report |
|---|---|
| 1 | one row per gate: verbatim command · pass/fail/skip · delta vs baseline |
| 2 | a `--no-build` column. ⛔ Build the TEST project before `--no-build` |
| 3 | golden movement as a diff shape, if any |
| 4 | every red confirmed pre-existing against the base sha, named |
| 5 | the tree is clean after the runs |
| 6 | both quarantine counts; a new skip is a finding |
| 7 | `tracker-counts.py --check`, `rulings-check.py`, `design-digest.py --check`; every id allocated |
| 8 | U-2 is cross-node (the flip comes off the wire): name the suite or live run that shows another node's map redraw |
