<!--STATUS
state: LIVE — a finding handed to backend (CE-5041) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-5 H2, the shuffled-seek phase that goes red
  - ../../designs/minefields/DESIGN_Minefields.md — §3g, mf-knowledge-survives (the scenario that found it)
-->
# NOTE terrain → backend: `--verify-replay`'s shuffled seeks never stand (CE-5041)

📄 Row: [CE-5041](../Blueprint_Issues_Tracker.md) · found running `mf-knowledge-survives` with `--verify-replay` (terrain@`4d27463d8`).

| run (`--mode editor --headless --verify <s> --verify-replay`) | REPLAY | PLAYBACK | SEEK (H2) |
|---|---|---|---|
| `mf-knowledge-survives` | ✅ same, 121 snapshots | ✅ same, 121 | ⛔ different, `diff: []`, 108 looked back at |
| ⭐ the control, `ua-civ-melee` (no minefield; "same" at `b97d9bcec`, `2026-10-27`) | ✅ same, 41 | ✅ same, 41 | ⛔ different, `diff: []`, 27 |

Every `problems` row reads the same: *"H2: the seek did not stand at 16 s within 30 s (state seek, clock 40 s)"*. The clock runs ON PAST the target, and the
state stays `seek`. ⇒ No value differs; the shuffled seek never settles. This affects every scenario, not minefields. Possibly a regression in the seek since
CE-4056/CE-4066 (`2026-10-27`); not bisected.

⭐ **Acceptance:** `ua-civ-melee --verify-replay` reads SEEK same again. ⚠ The regression script does not pass `--verify-replay` (and
`HROT_VERIFY_REPLAY=1` on the parent does not reach the child), so the nightly run never sees this.
