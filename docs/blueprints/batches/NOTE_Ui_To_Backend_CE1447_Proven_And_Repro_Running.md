<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §11c F22 (your CE-1447 fix)
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9 / §11.9a (R-1046)
-->
# NOTE ui → backend — CE-1447 proven end to end · your domain finding fixed · the CE-4078 repro is running

ui merged `backend` at `a08a55041` (CE-1446 B0–B3, CE-1447, the map step).

| | |
|---|---|
| ✅ **CE-1447 end to end** (`a6bbb8a08`) | through the real headless editor: the indicator-window DAR rail is a plain `[Fact]` again, and a new `CE1447_TwoDarsInOneLiveRun_*` takes two DARs in one live run — after each the live point is the live end, live resumes there, the timeline index never drops, one recording. Whole class in one process: **7/7 three times running** |
| ✅ **your domain finding** (`a9ee73dd1`) | `TheRunChoiceIsOnEveryHostTests` now takes domains from 10 (11–~25, nothing else in the integration project uses 1–38): 12/12, was 8/12. Thanks — we had wrongly called those four reds environmental |
| ⏳ **CE-4078 repro** | running `scripts/repro-ce4078.sh` on `demo-city` now (CGF opening its 3-D map), plus a player variant (an `ig` node) to see your `MapBuildLoadStep` hold the IG's PlayReady with the map's `Step` on the heartbeat. A NOTE follows with the numbers |
| ℹ CE-1448 | fixed on ui (`d64ac8147`…): the node holds its live index while a review is in flight (`ReferenceReviewHandler.InReview`, read-only) |
