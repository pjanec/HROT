<!--STATUS
state: LIVE
build-state: AUDIT (findings for triage — not a work plan yet)
updated: 2026-08-22
current-answer: the whole file — an audit of features that may be STRANDED on branches with disjoint
  history from the re-created trunk. Ranked shortlist + per-item recommendation. The user decides what to
  port; several items need a confirm-before-port pass.
known-conflict: none.
-->
# Stranded-features audit — disjoint branches vs the re-created trunk

**Why this exists.** The trunk was re-created (fresh import/squash, ~2026-07-16), so ~20 branches share no
git ancestor with it. Two features were found stranded that way (the MCP/AI-debug server; the cross-platform
staging-root fix). This audit surveys the rest. **Key confounder:** the trunk renamed `Bagira.* → Hrot.*`
and re-homed directories, so most "absent" dirs on old branches are false alarms (renamed, not lost) —
verified by symbol/basename. "Absent" below means *the distinctive symbols exist nowhere on trunk under any
name.*

## Ranked shortlist — real un-ported work

| # | branch | date | value | what is genuinely absent from trunk |
|---|---|---|---|---|
| **1** | **`stride-integ-1`** | 2026-06-16 | ⭐⭐⭐ **whole subsystem** | `Hrot.Stride.Core` (~35 files) + `Hrot.Stride.Animation`: **DotRecast navmesh baking + crowd**, **vehicle navigation + waypoint systems**, **3D debug rendering** (`DebugPrimitiveRenderer3D`), **animation blend-trees**. ⚠ The Bullet physics backend (`BulletCharacterMotor`, `BulletReverseSyncSystem`) is **deliberately superseded by Bepu** on trunk — do NOT port those; the nav/crowd/vehicle/3D-render/animation logic is the real harvest. |
| **2** | **`claude/linux-windows-port`** | 2026-07-12 | ⭐⭐ **cheap + broad** | cross-platform portability: LangVersion pin (12.0), ref-struct-out-of-async extractions, net8 retargeting, headless-ImGui test contexts, RIDs — plus the already-applied staging-root fix. Correctness/portability edits unlikely to have re-landed. |
| **3** | **`hexag` / `cm-refac`** | 2026-04 | ⭐⭐ needs a subsumption check | `Hrot.Core/MapDefinitions/Doctrine/*` parameter DTOs (`AmbushParams`, `ConvoyEscort`, `FireAtTarget`, `FollowRoute`, `InfantryCombat`, `DoctrineIds`, `DoctrineContractAttribute`) — on neither trunk. `cm-refac` also extracted a **non-2PC cluster-master orchestrator**. ⚠ Check whether trunk's blueprint/behaviour param model already subsumes Doctrine before porting. |
| **4** | **`ios`** | 2026-02-25 | ⚠ human call | the **operator-station GUI** (`Bagira.IOS`: mission/orbat/spawner/inspector/config/diagnostics panels + `MissionEditorService`). No `Hrot.IOS` on trunk. An early *mock* on pre-rename code — the concept appears never rebuilt. Decide if the operator station is still wanted. |
| **5** | four May "editor-prototype" branches: `utility-ai`, `json-migration`, `visual-asset-comparison`, `promote-to-3d-cognitive` | 2026-05 | ⚠ weak / harvest-only | each sits on a **dead editor scaffold** (replaced on trunk); only a thin feature slice is of interest (utility-AI nodes · JSON scenario migration · asset-diff tool · 2D→3D promotion + squad coordination). Individual harvest, if wanted. |

## NOT stranded — false alarms resolved

`ig`, `pj/update-entity-attributes-json-to-binary` — old `Bagira.*` **renamed** to `Hrot.*` (present,
evolved). `blueprints` — compiler **moved** into a dedicated `Hrot.Blueprints.Compiler` project.
`blueprint-integ-1`, `btree-visual-edit`, `multi-fixes1`, `nodeedit-fixes`, `hill-attack-json-slice-3`,
`test-fixing`, `readme-dedup-guide` — the coordinator line's own lineage or docs/tests only.

## Recommendation — triage order, confirm before porting

1. **`linux-windows-port` first** — cheapest, broadest correctness win, low risk (portability edits). A focused port pass.
2. **`stride-integ-1` — a confirm-before-port investigation**, not a blind port: it's a subsystem, and the physics backend diverged (Bepu vs Bullet). Scope exactly which nav/crowd/vehicle/3D-render/animation pieces have no trunk equivalent and still fit the Bepu world. Its own history shows nav churn — pick the intended nav path. **This is the big one; treat like the MCP port (its own design + tasks).**
3. **`hexag`/`cm-refac` Doctrine model** — a measurement pass: does trunk's blueprint/behaviour param model already cover Ambush/ConvoyEscort/FireAtTarget/FollowRoute/InfantryCombat? If not, the DTO family is a clean port.
4. **`ios` operator station** — a product decision for the user before any work.
5. **The four May prototypes** — harvest only the named slice if the feature is wanted; the surrounding editor is obsolete.

⛔ **None of these should be ported blindly** — the trunk re-creation was partly deliberate (Bepu over Bullet;
editor scaffolds replaced). Each needs a confirm-what's-truly-missing pass, exactly as the MCP port reconciled
3 drifts rather than copying wholesale.

## Deeper systemic note

The re-creation losing the MCP server, the staging fix, and (at least) a 3D subsystem means **the trunk
history topology is itself a hazard** — the next person to try merging any old branch hits the disjoint wall,
and features keep hiding. Worth a one-time decision on whether to reconcile the topology (out of scope here;
flagged in `docs/UX/MCP_PORT_PLAN.md` open-question 2).
