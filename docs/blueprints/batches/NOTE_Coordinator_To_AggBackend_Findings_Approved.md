<!--STATUS
state: LIVE — for exp/agg-backend
updated: 2026-11-17
current-answer: the whole note
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the split / merge (§16a), sensing (ST-2), the slot table
  - ../../designs/aggregate-units/DESIGN_DP3_Planned_Posts.md — DP3-B, the Planning wait you build (on exp/agg-behaviors)
-->

# NOTE — coordinator → agg-backend: agg-behaviors' findings, the user's decisions (R-4286)

🔒 User, `2026-11-17`: *"approved, write the notes"*. The findings are agg-behaviors' (`STATUS_AggBehaviors.md` §findings, on its branch).

| # | the finding | ✅ decided — build it |
|---|---|---|
| **F-23** ⛔ | merge (band + margin, 500 m) sits inside the split trigger (band + lead, 600 m): a unit flip-flops every ~63 s with an enemy at 500–600 m (measured) | **hysteresis**: merge only beyond the split trigger + a margin (≈ 800 m with today's numbers) AND after 30 s without contact; a rail with an enemy parked at 550 m — no flip in 5 min |
| **F-1** | a stochastic unit's SITREP ammo colour is UNKNOWN — `UnitSlot` has no capacity | the full load per weapon from the weapon TKB (the basic load), not a new per-slot field |
| **F-20** | the naked eye (150 m) is inside the split band (600 m): an eye-only observer is split before it could see a body | **no change** — splitting early is the safe direction (the physical men are the truth); record it in the design as intended |
| **F-22** | a split's children carry no name (`UnitResolution.cs:348`) | name each child `<unit name>/<slot>` (e.g. `Alpha/3`) so checks, goldens and the UI can name them |
| **F-19** | a stochastic unit keeps its authored z (0); its line of sight starts underground | put the unit on the terrain when it is placed / moves |
| **DP3-B** | the Planning wait for `PlannedPosts` (agg-behaviors' design) | yours: `Planning` waits for `PlannedPosts.Ready` or `DeadlineSeconds`; forced splits unchanged; a withdrawn trigger clears the posts |

Each with a rail in the feature's own suite; the agg scenarios re-run after.
