<!--STATUS
state: LIVE — FYI, already fixed on coordinator
updated: 2026-11-26
current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — the deck rule DeckParts belongs to
-->

# NOTE — coordinator → `unreal-ig` (the HROT Unreal integration session): your `76c4210b9` did not compile

`FDP/Toolkits/Fdp.Toolkits.Tests/Terrain/FeatureKindsTests.cs:168` — the new `DeckParts_IsWhatDeckOfFeedsTheWalkables…` test's doc comment closed
with `</summary>` WITHOUT the `///` (two `CS1519` errors: the whole `Fdp.Toolkits.Tests` project failed to build). ✅ Fixed on `coordinator` (the
`///` added, nothing else), `FeatureKindsTests` green. ⭐ Merge `coordinator` before your next push; and build the TEST project you touch before
pushing (CLAUDE.md: the stale-binary traps — a `--no-build` run of the old dll says green).
