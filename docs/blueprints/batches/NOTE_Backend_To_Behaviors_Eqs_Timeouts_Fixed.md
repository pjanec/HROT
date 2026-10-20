<!--STATUS
state: LIVE — backend → behaviors: CE-3279 (the EQS timeouts on demo-city) fixed; what changed in the answers you read; one red that is yours.
updated: 2026-10-20
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Sensors_And_Doctrine.md — §5.3 the budget (counted work), O5 the measurements
  - docs/DESIGN_World_Query_Seam.md — §5a the terrain index, now attached
-->
# Note — backend → behaviors: the EQS timeouts are fixed (CE-3279)

The handoff's S2. Measured live on `uc-request-chain`: every solve took 1.8–2.4 s once your squads' query sensors ran, so the module
was cut off at 400 ms and perception went dark 10 s at a time. Now ~320 ms, and in 60 s only 2 timeouts (warm-up and the stop).

| what changed | ⇒ for you |
|---|---|
| the terrain index (WQ-D) is attached and point queries use it | identical answers, only faster |
| ⭐ `PathCostScoreTest` makes ONE navmesh search per sensor instead of an A* per candidate | ⚠ path costs are the search's corridor, straightened — on test-town within 1 % on average of the old per-point path (worst +16 %); a candidate reached only by a detour longer than 1.5 × its straight distance + 25 m now reads as unreachable. Your flank / retreat / firing-position answers can shift slightly |
| path and exposure work is counted honestly in the EQS budget | a far flank (a target 1 km+ away) costs ~10 000 units — on a busier scene the budget will spread such sensors over ticks instead of timing out |

🔴 **Yours, not new:** `EqsFlatTerrainGoldenTests.RegisteredTemplates_AllHaveAGoldenScenario` is red — six templates from `CE-2275`
(2026-10-19) have no flat-terrain golden scenario: `FindDefensiveWindowPosts`, `FindDefensiveWindowSetBackPosts`, `FindRoofPosts`,
`FindWindowPostsAllRound`, `FindWindowSetBackPosition`, `FindWindowSetBackPostsAllRound`.

---
_backend lane, 2026-10-20_
