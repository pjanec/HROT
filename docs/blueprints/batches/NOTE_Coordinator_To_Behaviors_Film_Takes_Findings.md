<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../DESIGN_Film_Director.md — the director films the EDITOR's stepped preview (D-17, R-3038), so a beat that happens only on the cluster cannot be filmed
  - ../../DESIGN_Civilians_And_Crowd_Control.md — §5k (escalation ladder), §5m (provocation, baton charge)
-->

# NOTE — coordinator → behaviors: three demos behave differently in the editor's stepped preview (CE-4041)

**Why it matters:** the film director (CE-4038, built by ui) plays and films every take in the editor's **stepped preview** — the run
that now repeats (R-3038). A beat that happens on the cluster but not there cannot be filmed. Measured with the director's own scout
(probe takes, one stepped run each, 2026-10-21) and compared with each scenario's live check on `--mode all`.

| scenario | editor preview, stepped (the director) | cluster live check | the take it blocks |
|---|---|---|---|
| ⛔ **`ua-civ-provocation`** | the police START `MoveToLocation` (0.07 s) but **never reach their posts** — `Police shield` never within 3 m of (194, 259) in 180 s; **no `BatonCharge`** in 180 s; a riot anyway at 57 s | ✅ PASS 7/7 — police within 30 m at 21.8 s, the baton charge knocks a protester down at 56.5 s, 3 of 12 riot on every node | F-07 |
| ⛔ **`ua-civ-escalation`** | **no policeman shows the `Warning` gesture in 150 s**; the gunman is below half health at **2.3 s** — before he draws (his `FireAtPoint` into the air at 10.1 s, the police rifle at 13.0 s) | ✅ PASS — "warned True, rounds 0", hit at 30.7 s (the check reads its own warned flag, not the gesture) | F-13 |
| ⚠ **`ua-hill-attack`** | the hull-down runs start at 6.3 s / 41.8 s, but the **first round is at 210.7 s** (the take now ends at 270 s) | — (in a real-time editor run: first round at 66 s) | F-46 runs 3½ minutes |

| ⭐ asks | |
|---|---|
| provocation | why the police's move does not finish in the editor preview — ⚠ the same family as **CE-4040** (backend `f1f4c89ba`, "a move getting nowhere ends") is a candidate: three police walking abreast at 1.5 m/s to 1 m arrival radii |
| escalation | whether the `Warning` cue reaches the gesture in the editor (one world) — and why the gunman is hurt before he draws (non-lethal force on a man who is Hostile from the start?) |
| hill-attack | whether ~170 s from the attack run to the first round is the intended creep, or a stepped-run difference (66 s real time) |

**Also blocked, already filed:** civilians do not hear gunfire (**CE-4039**, backend) and do not shelter (**CE-4040**, backend) —
`ua-civ-shelter`, `-cower`, `-contagion`, `-routine-alarm`, `-gathering`.
