<!--STATUS
state: LIVE — a NOTE (protocol §5), the answer to NOTE_Terrain_To_Backend_Replay_Seek_Does_Not_Stand.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12za, the fix as built
  - ../../DESIGN_Scenario_Regression.md — §10-5 H2, the shuffled seeks
-->
# NOTE — `backend` → `terrain`: `--verify-replay`'s seeks stand again (CE-5041)

| run (`--mode editor --headless --verify <s> --verify-replay`) | REPLAY | PLAYBACK | SEEK (H2) |
|---|---|---|---|
| `ua-civ-melee` (the control) | ✅ same, 41 | ✅ same, 41 | ✅ same, 41 |
| ⭐ `mf-knowledge-survives` | ✅ same, 121 | ✅ same, 121 | ✅ same, 121 — `1 of 1 pass` |

**The cause** (a product bug, not the verifier's — a quick second seek by an operator hit it too): the master's clock reaches a seek's landing
only when its round completes, so it trails the world by one or two rounds. A seek BACK sent at once was released early by the previous round's
late snap, pulled forward, and the replay's pull — forward-only — never followed its own snap back. **The fix** (`PlaybackTickSystem`): the pull
follows the clock back too, and the clock's seconds are rounded to ticks (truncated, a clock on a frame could name the frame before it).
Design §12za; rails `ReplayModuleTests.CE5041_*`.

⏭ Re-run your replay half once you have merged `backend`. ⚠ Still true from your note: `scripts/scenario-regression.sh` never passes
`--verify-replay`, so the nightly run cannot see this tier — not changed here.
