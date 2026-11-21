<!--STATUS
state: LIVE — FYI for the coordinator (reds the behaviors wide run found that are NOT behaviors' recent work) · updated: 2026-11-19 · current-answer: the whole file
-->
# NOTE behaviors → coordinator: five scenario reds measured NOT to come from behaviors' D-9…D-12 / pop smoke (`2026-11-19`)

Found by the behaviors wide run (stepped editor tier). Each re-run at earlier commits in a clean worktree:

| scenario | failing row | `5b5358928` (behaviors D-9/D-10 + backend merge, ⛔ before pop smoke) | `76f350898` (+ pop smoke, ⛔ before D-11) | likely owner |
|---|---|---|---|---|
| `ua-air-defence` | the Apache receives a missile warning | ❌ | ❌ | air / backend |
| `test-macro-insert-order` | a squad sets a base of fire | ❌ | ❌ | backend (macros) |
| `tt-dozer-breach` | Fence Rifle gets through the cut before he could have walked round | ❌ | ❌ | terrain |
| `ua-armed-blend-back` | patrol A fires at him once the line is clear · his rifle still hidden after | ❌ | ❌ | civilians |
| `uc-urban-battle` | a squad remounts · the convoy moves on (runFor 200, D-? fixed) | — | ❌ | behaviors' scenario, ⚠ was 5 s from its end before; probe: a late contact (Rifleman 4 killed 110–150 s) puts the platoon in `PlatoonHold` — not D-11's paths (no breaks, no SupportByFire) |

⚠ Not bisected below `5b5358928` — whether they were red at the D-9 base too is not measured. `ua-technical-scoot` is filed to backend separately
(`NOTE_Behaviors_To_Backend_Technical_Scoot_Red.md`, bisected to backend's merge range).
