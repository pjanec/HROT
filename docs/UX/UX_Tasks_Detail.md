# Scenario-Authoring UX — Task Detail (`UXT`)

> **Status: BASE — empty register, 2026-08-06.** Tasks are cut from the
> **[golden-path walk](UX_Golden_Path.md#deviation-log)**, which has **not yet been performed** (it needs
> a Windows session — see [UX_RESUME.md](UX_RESUME.md#next-up)).
>
> Checklist view: [UX_Task_Tracker.md](UX_Task_Tracker.md) · Scope: [UX_Requirements.md](UX_Requirements.md) ·
> Journey spec: [UX_Golden_Path.md](UX_Golden_Path.md) ·
> Design: [UX_Design.md](UX_Design.md) · Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## How this doc works

One `<a id="uxt-nn">` anchored entry per task, so the tracker can deep-link every row (`#uxt-nn`).
**This doc holds the evidence and the outcome; the tracker holds only status.**

### Rules

1. **Every task traces upward.** A task with no `UXR` reference does not belong in this programme.
   A task whose design decision is still `OPEN` in [UX_Design.md](UX_Design.md) is **not ready to
   start** — say so in its Status rather than starting it.
2. **Evidence is code, not doc claims.** Cite `file.cs:line`. The register that opened this programme
   was assembled from a code scan, but *the blueprint audit was wrong ten times* — re-derive before
   building, and correct the entry in the same commit if it was wrong.
3. **`DONE` notes record what was actually observed**, including the visual check
   ([Briefing §5.11](UX_Programme_Briefing.md#511-visual-verification-is-mandatory)) and the
   revert-to-red confirmation ([§5.5](UX_Programme_Briefing.md#55-revert-to-watch-it-go-red)).
   A `DONE` note without a visual observation is incomplete.
4. **Wrong estimates get recorded**, not quietly corrected — see [Corrections](#corrections). The
   blueprint programme's estimate-failure table was one of its most useful artefacts.

### Complexity scale

Same scale as the blueprint programme, so estimates stay comparable:

| Code | Meaning |
|---|---|
| `WIRING` | Call existing code. No new logic. |
| `RW-L` | Real work, low — ≲150 lines, no new concepts. |
| `RW-M` | Real work, medium — new panel/component, some design. |
| `RW-H` | Real work, high — new subsystem, or an architect decision first. |

🔴 marks a correctness / data-loss / trust defect rather than an enhancement.

### Entry template

Copy this block verbatim for a new task.

```markdown
### <a id="uxt-nn"></a>UXT-nn — <short imperative title>

| | |
|---|---|
| **Requirement** | [UXR-nn](UX_Requirements.md#uxr-nn) — <one-line restatement> |
| **Design** | [UXD-nn](UX_Design.md#uxd-nn) (status must be `DECIDED` or `LEAN` to start) |
| **Question improved** | 1 Where am I / 2 What's in my world / 3 What is this / 4 What can I do / 5 Did it work |
| **Complexity** | `WIRING` \| `RW-L` \| `RW-M` \| `RW-H` |
| **Status** | `NOT READY` \| `READY` \| `IN PROGRESS` \| `DONE` \| `REFUTED` |
| **Delegation** | hands-on \| Sonnet subagent (per [Briefing §5.1](UX_Programme_Briefing.md#51-model-delegation-token-thrift)) |

**Evidence** — what is broken, with `file.cs:line` citations. Verified or ⚠ unverified.

**Scope** — what changes. Name every file expected to change, and every other host of a shared panel.

**Acceptance** — the observable test a person performs in the running editor. Must satisfy the
requirement's acceptance plus A1 (≤2 clicks, no window detour) and A2 (outcome stated).

**Out of scope** — what a reader might reasonably assume is included and is not.

**Gates** — which suites must be green.

**DONE (date, commit)** — what shipped, what the visual check showed, revert-to-red confirmation,
anything the task exposed. Added on completion.
```

---

## Open tasks

*None yet — the register opens after the golden-path walk.*

## Done tasks

*None yet.*

---

## Corrections

<a id="corrections"></a>

Where this programme's own claims turned out to be wrong. **Add a row rather than silently editing** —
the pattern of failure is more useful than a clean record.

| # | Claim | Reality | Found by |
|---|---|---|---|
| — | *(none yet)* | | |

## Baseline evidence index

Findings from the opening audit (2026-08-06), hand-verified against code unless marked ⚠. These seed
the task register; each is restated in the `Now` column of the relevant
[requirement](UX_Requirements.md).

| Finding | Evidence |
|---|---|
| Outliner is a stub printing `• [entityId]` | `Hrot/Subsystems/Hrot.Editor/UI/EditorOrbatPanel.cs` — whole file, 27 lines |
| No scenario-side undo at all | Zero `Undo` matches in `Hrot/Subsystems/Hrot.Editor/`, `Hrot/Engine/Hrot.Presentation/`, `Hrot/Engine/Hrot.UI.Common/`. `Hrot/Subsystems/Hrot.Editor/Commands/` contains one file (`CenterOnEntityCommand.cs`) |
| Toolbar: 6 text buttons, no state/shortcuts/tooltips | `Hrot/Subsystems/Hrot.Editor/UI/EditorToolbarPanel.cs:35-47` |
| `New Scenario` produces a void | `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs:138-143` |
| Scenario name only in a submenu | `Hrot/Subsystems/Hrot.Editor/WorkspaceMenuBuilder.cs:112-122` |
| No command palette | No `CommandPalette`/`QuickOpen` matches anywhere in the repo |
| Menu bar is thin | Registered paths: `File/{New Asset, Open Asset, Save, Save As, Save All}`, `File/Scenario/*`, `Blueprint/*`, `Assets/*`, plus auto-generated per-perspective window lists and `Perspective` |
| Two assignment models | `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` (OCC commit, conflict modal, Force Commit) vs `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/` |
| Allocator internals exposed to the author | `EntityBlueprintsEditModel.cs` — `Projection(Slots, Bytes, Tier, Status)`, `UsageStatus.OverCeiling`, `CommitPlan.UpgradeToTier`, Reality/Staging |
| Behavior list ungated for BTree assets | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMissionService.cs:66-106`, incl. the `TODO (option c)` comment |
| Params fall back to raw JSON | `MissionPanel.cs:481-492` → `DrawRawJsonEditor`; typed forms via `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs` + `BehaviorSchemaDiscovery.AutoRegister` |
| Params stored as escaped JSON-in-JSON | `scenarios/hill-attack/scenario.json` — `behaviorParams` is a `string` |
| Play/preview snapshot+rewind is correct | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorPreviewAdapter.cs:54-67` |
| Transport controls exist in the status bar | `Hrot/Subsystems/Hrot.Editor/UI/TimeControlStatusBarSection.cs` → `ClusterTimeControlStatusBarSection` |
| Silent-failure house style | Blueprint audit: `BlueprintCommandSink.Apply` `default:` returns success; `MyBlueprintPanel.InvokeCreate` discards `EditorCommandResult`; `EditorCommandsImpl.Invoke` returns an unread `"Unknown command"` |
| README overstates the editor's ORBAT | README §11.4 claims "ORBAT drag-and-drop unit hierarchy"; that is ExCon's `OrbatPanel` (434 lines), not the editor's 27-line stub |
