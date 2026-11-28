<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI: the reply to NOTE_Behaviors_To_Terrain_Stechovice_ZU23_In_Forest · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Terrain_Stechovice_ZU23_In_Forest.md — the ask this answers (CE-5062)
-->
# NOTE — `terrain` → `behaviors`: CE-5062 — the ZU-23 and the SAM moved to open ground (thank you for the root cause)

| | |
|---|---|
| measured | the rule perception uses (`TerrainWorldQuery.SightBlocked`) from 2.5 m above the ground to 8 sky points 400 m out, 120 m up: the old ZU-23 spot **2 / 8** clear, the SAM's **2 / 8** — both in the wood, as you found |
| done | `scripts/stechovice_battle_scenario.py`: ZU-23 → scenario (−340, 240), **8 / 8**; SAM → (−320, 260), **7 / 8** — a clearing on the same hill, 28 m apart, slope ≤ ~15°; the A-10's strike follows the gun (it aims at the gun's place) |
| next | a `stechovice-battle` regression run confirms the row *"the ZU-23 fires at an aircraft"*; nothing in the behaviour changes |
| aside | driving the headless editor's `/terrain/query` in a loop crashed it twice (`QueryCandidateIndex.Build`) — **CE-5050** (backend), evidence added; the search was then done offline on the same rule |

## ⭐ Update `2026-11-27` — the site works, the gun still does not fire: over to you

| | measured |
|---|---|
| the regression | `stechovice-battle` (`artifacts/scenario-regression/20261126-224343`, the real Vltava bridges): 20 / 23 — ⭐ **the SAM now fires** (115.5 s); the ZU-23 row still red |
| a live sample at the new site (ZU-23 at terrain-local (1687, 2361), after its cover search) | `/perception/los` ZU-23 → **A-10 visible** (3 of 3 body points clear) at 1 344 m (t 20 s) and 1 179 m (t 100 s); → **Apache visible** at 144 m (t 120 s) |
| the gun | `EngageAircraft` started 7.5 s / 64.5 s / 88.8 s, ended Success / Success / Failure — **0 rounds** (`/combat/shots?shooter=`); its `TargetMemory` held only GROUND units (z 315–318, the hill's level), **never an aircraft, even while the A-10 was visible** |

⇒ ⭐ the line is clear now; the aircraft never reach the gun's memory. That is between a clear line and the memory (the air sense / how a vehicle's perception takes an aircraft), not the site — **CE-5062 is yours again**. Repro: load `stechovice-battle`, preview, run to 20 s, `GET /perception/los?observer=<ZU-23>&target=<A-10>` then `GET /entities/<ZU-23>` (`TargetMemory`).
