<!--STATUS
state: LIVE — FYI, backend → ui
updated: 2026-11-05
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Player_Join.md — D-16 (the ruling's home)
  - ../../DESIGN_Player_HUD_And_Debrief.md — §7 scoreboard row (updated)
  - ../../DESIGN_Entity_Control.md — §8j T-1, K-1 (updated)
-->
# NOTE — backend → ui: the scoreboard is on hold Tab; the next man moved to J (R-3076)

🔒 User, `2026-11-05`: *"i do not like a single key used for 2 completely different purposes; rather put the 'next man' to something else."*

| | before | now |
|---|---|---|
| scoreboard (CE-4231) | hold **K** | hold **Tab** (`FpsCameraDriver.ScoreboardKey`) |
| next / previous man (CE-1214) | **Tab** / Shift+Tab | **J** / Shift+J (`FpsCameraDriver.TeamSwitchKey`) |
| fall screen "a squad mate" (CE-1220) | **Tab** | **J** (reads `TeamSwitchKey` — one next-man key) |
| fall screen kill cam | K | K (unchanged — K is free of the scoreboard now) |

⭐ Cross-lane edit by backend, declared: `PossessionStation.cs`, `FallScreen.cs`, `PlayerHudLayer.cs` (comment), `PossessionStationTests(.HudFeatures).cs`, and the two
designs above. Rail: `PossessionStationTests.CE1214_J_JumpsToTheNextManOfHisSquad_ShiftJBack_AndTheManHeLeavesIsReleased_TabOnlyHoldsTheScoreboard` (a Tab press takes
no man and holds the board; J takes the next man). R-1009's Tab row is marked superseded in `RULINGS.md`. Nothing for you to do unless a help screen or doc of yours
still names Tab as the next man.

_backend lane, 2026-11-05_
