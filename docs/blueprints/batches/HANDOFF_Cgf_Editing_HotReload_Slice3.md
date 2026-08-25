<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for cgf==editor SLICE 3 (CE-011) — editing + hot reload on CGF. Carries NO
  design: cites DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md (classDiagram + sequenceDiagram + §8
  collision plan).
known-conflict: ⚠ shares the DebugApi surface with the PARALLEL MCP-authoring track (AQ56). This batch stays
  strictly save/reload on the MCP side (§8); the authoring track branches from a base that includes this.
  CONSUMES Hrot.Editor.AiShared — additive wiring only.
-->
# HANDOFF — **cgf==editor slice 3 (CE-011): editing + hot reload on CGF** *(CGF / backend lane)*

> 📌 **Dispatched at `a44d81043`.** ⛔ **Scope FROZEN at that sha.** ⭐ **Branch fresh from
> `claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker naming `a44d81043`
> BEFORE any code.** ⛔ **No PR.** ⭐ **You allocate the ids** *(rule 3)* — continue the `CE-` series,
> tracker **Area L**; state every id *(rule 5)*.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md`](../../DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md)**
*(READY-TO-BUILD)* — §2 inventory, §3 the two write paths, §4 classDiagram, §5 sequenceDiagram, §6 the
items, §7 gates, **§8 the collision plan with the parallel authoring track**. ⭐ Build what §4/§5 draw;
report the match *(obligation ③)*; fold deviations into the design *(obligation ⑤)*.
📄 Context: slice-2 as-built *(CE-009 gave CGF OPEN documents — the reload trigger's precondition)* ·
`STEER_Cgf_Shell_Adoption_Slice1.md` *(editing wholesale; live value-write OFF)* ·
`AI_Editor_Shared_Infrastructure.md` §17 *(Cosmetic/Soft/Hard)* · ruling 53 *(Hard-reload confirm at the
interactive node)* · ruling 67 / `AssetRoots`.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. ⛔⛔ build the AFFECTED PROJECT
*(`Hrot.CGF` · `Hrot.Editor` DebugApi · `Hrot.SystemTests`)*, never the whole solution in the fix loop.
⛔ E2E/system suite is T3 — async. ⭐ prove each fix through the rail that reddens; pre-existing reds proven
by `git diff`, not rebuild.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Take the windows' native editing WHOLESALE** on CGF *(no gating — the steer)* + **wire the reload pipeline**: `QuickReloadService` + the three per-host quick-reload triggers + `ApplyQuickReload`, mirroring the editor `:344-3266` | ⭐ Soft keeps state, Hard resets *(intended, §17)*; ⛔ don't gate the editing |
| ⭐ **②** | **Wire the `SaveDelegate`** + asset→path via `AssetRoots` | ⚠ deployed-node roots = ruling 67 — report if it bites, ⛔ no silent save-to-nowhere |
| ⭐ **③** | **Hard-reload confirm at the INTERACTIVE node** *(ruling 53)* | ⛔ never pop a modal on a headless CGF; the confirm resolves where the operator sits |
| ⭐ **④** | **Main-toolbar hot-reload/save button on CGF**, published to the toolbar `PanelKind` | ⭐ assert the affordance is present + SAME on CGF *(CE-009 §7 rule)* |
| ⭐ **⑤** | **Minimal MCP trigger** — `POST /assets/{id}/save` · `POST /assets/{id}/reload`, each a `RouteDoc` | ⛔⛔ **keep to save/reload — do NOT add node-authoring routes** *(that is AQ56's parallel track; this is the collision boundary, §8)* |

## 3. ⭐⭐ HOW TO TEST
✅ conformance suite is the acceptance vehicle. | **T0** baseline green at `a44d81043` | **T1** capture editor goldens for an edit→reload cycle | **T2** build | **T3** *(acceptance)* edit a param *(Soft, state kept)* → save → reload → the running brain reflects it; a topology edit *(Hard)* → reset + confirmed; assert editor==cluster where applicable | **T4** `gen:catalog:check`/`gen:skill:check` green for the two new routes.
⭐⭐⭐ **YOU MAY EXTEND THE HARNESS/MCP** for testing *(route + RouteDoc, conformance case, golden, PanelSnapshot)* — ⛔ do NOT fake a pass; ⚠ AiShared changes beyond additive wiring ⇒ STOP and coordinate with the variable-model lane.

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours (CGF/backend lane):** `Hrot.CGF/CgfSubsystem.cs` *(the reload/save wiring)* · `Hrot.Editor/DebugApi/DebugApiService.Assets.cs` *(the save/reload routes — extend the EXISTING file)* + `DebugApiRouteDocs`/`DebugApiHost` registration · the toolbar button · `Hrot.SystemTests/**`.
⛔⛔ **Do NOT add node-authoring routes or a `DebugApiService.Authoring.cs`** — that is AQ56's track, dispatched separately from a base that includes this. ⛔ **Do NOT enable the live variable-VALUE write** *(R-52; variable-model lane)*. ⛔ Map/Axis B.
⭐ **Rule 4:** re-pull coordinator before the final commit.

## 5. GATES *(rule 8 contract)*
one row per gate · verbatim command · pass/fail/skip · delta vs `a44d81043` · `--no-build` column · every RED pre-existing by name *(by diff)* · goldens as a diff shape · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `CE-` ids. **Row 8 rails:** the Soft-then-Hard edit→save→reload headline *(RED by reverting the trigger wiring)* · save persists + reload hot-applies · the toolbar button present+SAME · **the live value-write path still OFF** · conformance suite as the integration gate.

## 6. ⭐ WHEN DONE
Fold the as-built into the design; flip the gap-map Axis-A "editing" rows; close `CE-011`; report whether the deployed-node asset-root *(ruling 67)* bit. State the `CE-` ids; the report points at the design. Report per obligation ③.
