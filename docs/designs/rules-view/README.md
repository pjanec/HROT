<!--STATUS
state: LIVE
updated: 2026-11-01 (coordinator — the mockup committed as standalone HTML)
current-answer: the nine mockup files below; the decisions they illustrate are SR-105 / SR-109 / SR-110 / SR-111 / SR-112 in docs/DESIGN_Scenario_Rules.md
stale-below: nothing
known-rot: none — ⚠ a MOCKUP of an unbuilt UI: names, numbers and layout are illustrative (the village-attack example); the design rows win where they differ
known-conflict: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — OWNS the decisions: SR-105 (the rules view), SR-106 (player messages), SR-107 (results + HUD), SR-109 (this UI, how it works), SR-110 (built on the ONE shared timeline), SR-111 (the mission summary), SR-112 (the player's debrief and HUD)
  - docs/DESIGN_Director_Timeline_Editor.md — OWNS the shared timeline panel this view is a host of (TE-21 / R-4075); the extensions it needs are TL-X1…X5 (CE-4167)
  - docs/EXPLAINER_Scenario_Evaluation_And_Direction.md — §3.5 explains the view in words
  - docs/SHOWCASE_Scenarios.md — the two showcase scenarios whose screens these mockups show
-->

# Rules view — UI mockup

> ⭐ **The visual design of the rules view** (objectives and triggers on a dependency timeline, their detail tabs, editing while live) **, of the read-only mission summary** (SR-111) **and of what the PLAYER sees — the debrief and the HUD, for training and for games** (SR-112).
> How it works is written in [`DESIGN_Scenario_Rules.md`](../../DESIGN_Scenario_Rules.md) **SR-109**; this folder is the picture.
> ⛔ Mockups, not specs: where a file and an SR row disagree, the row wins.

| file | shows |
|---|---|
| [`mockup/Main.html`](mockup/Main.html) | the window in LIVE mode: the tree of objectives / triggers (grouped by macro) · the sim-time Gantt with NOW, past at actual times, the future as dashed *expected* bars after their predecessors, clock-pinned diamonds, delay / cooldown hatched, *do n/N*, dependency connectors · legend · *Coming up* · the Details panel of the selected trigger (Overview tab) |
| [`mockup/TriggerDetails.html`](mockup/TriggerDetails.html) | a trigger's tabs: **When** (the existing condition builder, live value per leaf, *true over time* strip) · **Do** (the action list: drag, inline args, ⛓ bindings, live step progress) · **Rules** (holdFor, delay, once / repeat, cooldown, maxFires, active from, fire-if-true-when-armed) · **History** (the journal) + a note on **Bindings** |
| [`mockup/ObjectiveDetails.html`](mockup/ObjectiveDetails.html) | an objective's tabs: **Status** (progress, live facts, links, manual overrides) · **Settings** (the kind's params, required / scored / on HUD) · **Results** (the composite score tree, per member and role, measures vs thresholds) · the **player HUD** preview (own objectives + a timed subtitle message) |
| [`mockup/MissionSummary.html`](mockup/MissionSummary.html) | ⭐ the READ-ONLY **mission summary** (SR-111): per-side headline score with trend and pass mark · required objectives · forces · attention items · **scores by level** (command chain ▸ platoon ▸ squad ▸ person, or by objective, or by role) with the measure columns · *why this score* for the selected row · the mission objectives · key events linked to the timeline · export |
| [`mockup/TraineeDebrief.html`](mockup/TraineeDebrief.html) | ⭐ **for the player — TRAINING debrief** (SR-112): GO / NO-GO against the standards · own score vs squad and platoon · each standard as a card (value, standard line, squad average, the facts behind it) · the player's own moments on a strip + *replay from my eyes / from above* · *went well / work on* (generated from the measures) · the instructor's note · progress across earlier runs (needs gap G7) |
| [`mockup/GameSummary.html`](mockup/GameSummary.html) | ⭐ **for the player — GAME end of round** (SR-112): winner banner · scoreboard per team (K / D / A / points / score, MVP, AI bots listed) · *your round* stats · awards · *your best moment* clip (auto-cut) · level / XP |
| [`mockup/HudTraining.html`](mockup/HudTraining.html) | ⭐ **HUD — TRAINING** (SR-112): mission objectives with progress · *my standards* (optional coaching) · a coaching hint · the timed message subtitle + message log · health, ammo, compass |
| [`mockup/HudGame.html`](mockup/HudGame.html) | ⭐ **HUD — GAME** (SR-112): tickets + capture points + round timer · capture progress · kill feed · hit marker · *+100 elimination / streak* · the message subtitle · own K / D / score, ammo |
| [`mockup/EditAtRuntime.html`](mockup/EditAtRuntime.html) | editing at any run state: the **+ Add** menu · **drag to connect** (writes a condition into the target's `when`) · what each live edit does · the live-edit dialog (a live edit lasts for THIS RUN only — the scenario file changes in edit mode) · the **Graph** view |

**How to look at them:** open a file in any browser (each is self-contained: inline styles, no scripts, no network). On GitHub the blob page
shows the source; download it or use the *Raw* view and open it locally. The same nine boards are also on a private claude.ai canvas,
[Rules View Mockup](https://claude.ai/artifact/VLfa5GxVGh3Z7nYsGbf65k) — ⚠ the committed files are the copy future work should read; the
canvas may drift.

⛔ No screenshots are committed (user rule: no PNG/JPG in git) — the HTML is the record and renders on demand.
