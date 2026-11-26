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
