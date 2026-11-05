<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-05
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Film_Director.md — OWNS the director, takes, the runner and the report (item ①)
  - docs/DESIGN_Player_Join.md — §11 S3-d, the spawn area chosen on the map (item ③); backend built S1–S3
-->

# HANDOFF — coordinator → ui: the director that plays no takes, picker thumbnails, the spawn area on the map

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `271d9331c`**. ⭐ Scope FROZEN there;
later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒ a NOTE
to coordinator.

⭐ **First: merge `coordinator`** — it carries backend's Deploy S1–S3, behaviors' performance work and the November merges; ⚠ restore the
test projects once after it (new projects and references). Your earlier queue is unchanged: the November batch's leftovers, the replay
UI-state handoff (CE-4047, CE-4049), the Timeline (CE-1301…1306). Order this beside them; **① first** — it is a 🔴 the user hit.

## The items

| # | row | what | design basis | done when |
|---|---|---|---|---|
| **①** | 🔴 **CE-4267** | 🔒 User: *"the director does not play almost no movies in editor, it shows just the take title on black screen, then next takes title, with no scenario run in between … Only few takes work."* ⛔ Not measured — the cause is unknown. **Measure first**: play the seeded reel over HTTP (`POST /director/play`, `scripts/director-check.py`) and log per take whether its scenario LOADED, whether its anchors resolved (CE-4125 says why one is unresolved), how many frames were stepped; set that beside CE-1241 (the 15 seeded takes the scout could not complete) and the takes that DO play. Then fix the cause(s) | Film Director (runner, report) | the seeded reel plays every take whose scenario exists; a take that cannot play SAYS WHY on screen and in the report — ⛔ never a silent black skip to the next title; a rail per cause found |
| **②** | **CE-4268** | 🔒 User: *"the scenarios and entities need their thumbnails to be shown in their pickers (some defaults only shown there)"*. Scope: the scenario picker (CE-4140 / CE-4141 preview, CE-1460) and the entity / TKB type picker. **Measure first** which items lack one and why (none authored · none generated · a path not found). ⚖️ Lean in the row, confirm or change it in a short design note: generate a thumbnail per scenario and per entity type from the 3-D view (film capture already renders offscreen), cached beside the asset; a hand-authored image wins (R-132) | the picker designs (find the owner by topic, R-129) | every scenario and entity type in both pickers shows its own image; ⛔ no generated image committed to git (user: no screenshots in the repo) — a cache outside the tree, or regenerated |
| **③** | **CE-3370** | Player Join S3-d — click a spawn area on the Player's 2-D map. ⭐ Backend's [note](NOTE_Backend_To_Ui_Deploy_Map_Area.md) has every seam: `DeployFlowSystem.Offers` (`IsArea`), `Request(side, unit, kit, areaNetId)`, the `DeployScreen` area list, the `SpawnArea` outline. No backend change needed | Player Join §11 S3-d | while Choosing, the side's areas are outlined on the map; a click selects one (the screen's list follows) and it rides the next request; the spawned man lands inside; a rail in the map's suite proves click → `areaNetId` |
| **FYI** | **R-3076** | backend moved the keys (cross-lane, declared): **hold Tab = scoreboard only**, **J / Shift+J = next / previous man** (and the fall screen's "a squad mate"), K stays the kill cam — [note](NOTE_Backend_To_Ui_Scoreboard_Key_R3076.md) | — | nothing to build unless a help screen or doc of yours still names Tab as the next man |

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · a rail per cause in ① and for ③ · display rails under
`xvfb-run -a` for any drawing change (`…FrameRail`; a skip is not a pass) · ①'s per-take log (before / after) in the report · every red
confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-05_
