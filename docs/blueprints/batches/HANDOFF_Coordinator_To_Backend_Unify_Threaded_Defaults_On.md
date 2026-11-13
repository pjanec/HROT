<!--STATUS
state: LIVE — re-dispatched unstarted 2026-11-13 (rule 1a): the user tests editor + player / server; `-m all` without the threaded kernel
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — ⭐ §0a the architecture as it stands; §14.8 the threaded node (incl. "an unstaged subsystem draws nothing in a threaded --mode all")
  - ../../DESIGN_Parallel_Simulation.md — §18.5–§18.8 the per-entity rule, the conversions, the gate
  - ../../DESIGN_Threaded_Review_Source.md — the review source on the sim side (exp/threaded-review)
  - ../../DESIGN_Threaded_Work_Inventory.md — §8 the decision list
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — unify the programme, every optimisation ON by default, switchable in View ▸ Settings

**For:** a backend VM lane. **From:** coordinator. **Dispatched at `<stamp>`** — scope frozen.
🔒 User, `2026-11-13`: *"unify - OK. bring to lanes - not yet, it needs my manual tests first, with all optimization ON hardcoded by default …
and best switchable ON/OFF easily in the clusterrunner settings somehow (in main menu maybe somewhere in the View -> Settings …, savable to user
folder)"* · *"windows run and automated test of course first so i can run something what has been tested already at least a bit automatically"*
(R-4224).
🔒 User, `2026-11-13`, re-dispatch: *"i am testing in editor mode and also player/server mode. "-m all" is for ai debug testing of network interactions and feel free to turn the parallelism off for that mode."* · *"...turn off  parallelism if it helps anything"* · *"also separate CGF and separate simhost needs running with optimizations ON — only the \"-m all\" is likely the one where parallelization hurts"*

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip a test. Commit +
push per item, by path; tick its tracker row. Deviations folded into the designs.

## Where to work

On **`exp/threaded-build`** (the unified branch from here on). Push `chore: started unify at <sha>`. Status file
`docs/blueprints/batches/STATUS_Unify_Threaded_Defaults_On.md`. ⛔ Do NOT merge into `ui` / `behaviors` / `backend` / `coordinator` — the user
tests it by hand first (R-4224).

## The work

| # | item | done when |
|---|---|---|
| **U-1** · `CE-4530` | ⭐ **unify**: merge `exp/next-conversions` and `exp/threaded-review` into `exp/threaded-build` (merge, never rebase); reconcile what both touched (the per-entity code, `PartIndex`, the debug API, the egress gate) — ONE implementation of each, never two | builds; the three branches' own rails green on the result |
| **U-2** · `CE-4531` | ⭐⭐ **every optimisation ON by default in the product, switchable** — `View ▸ Settings ▸ Performance` (`GlobalMenuRegistry.SettingsMenuPath`): **Threaded editor** (the sim on its worker, the UI on the staging; applies on restart — say so in the menu), **Parallel simulation** (the per-entity phases), **Worker threads** (Auto = cores − 2, or a number). ⭐ Persisted in the USER folder beside the layout files (`LayoutPaths`' user directory), ⭐ read at start by EVERY HROT process on the machine (editor, player, server, a separate node) so they agree by construction (R-4215). Precedence: an explicit `HROT_*` environment variable (scripts, tests) > the user file > the default ON. The default flips at the PRODUCT entry point (`ClusterRunner` start-up); library defaults stay as they are, so test harnesses keep choosing explicitly. The status bar or the menu shows when a saved choice differs from what is running ("restart to apply") | rails: no file ⇒ ON; the file ⇒ obeyed by the editor and a headless node; an env var overrides the file; the menu writes the file; the switch OFF ⇒ today's serial behaviour (the existing switch-off rails) |
| **U-3** · `CE-4532` | ⭐⭐ **the user's modes run with the defaults ON: the editor, the PLAYER (`-m player`) and the SERVER (`-m server`, the fused headless server)** — the threaded node is in the SHARED node frame (R-4215), so the player host gets it as the editor does: measure that it really does (the staging composed, the UI on A, the guard silent), and fix what the editor-only build left out; the server runs the per-entity phases. ⭐ **`-m all` is the ONE exception: SERIAL by default** — neither the threaded kernel nor the parallel phases there, whatever the user file says (an explicit `HROT_*` variable still turns them on); it is the AI-debug network-interaction mode, and threading would hide its unstaged subsystems' windows (§14.8). ⭐⭐ **Separate CGF and SimHost node processes (and IG, traffic, the orchestrator) run with the defaults ON** — the per-entity parallel phases, the user file obeyed | a player joined to a server, both defaults ON: play, move, fire, a scenario load, 5 min, 0 faults, the guard 0 on the player; `-m all` starts serial, every window drawn (a rail on the mode's resolved settings); a separate CGF and SimHost node started with NO environment variables run the parallel phases (a rail on their resolved settings) |
| **U-4** · `CE-4533` | ⭐⭐ **the automated proof, with the defaults ON** — ① the stepped gate (`scripts/stepped-hash.py --threads 1,2,4,8`) equal; ② the threaded 1 000-man scripted session ×2 (the M-7 chain) 0 faults, guard 0; ③ a player + server session 5 min, 0 faults; ⭐ separate orchestrator + CGF + SimHost processes (`scripts/scale-rig.py` / `cluster-node.sh`, NO env vars) 5 min at 1 000 men, the parallel phases on, 0 faults; `-m all` serial, every window drawn; ④ the review source's mode-3 end-to-end rail; ⑤ the touched systems' own rails serial and parallel; ⑥ with the settings OFF: today's behaviour. ⭐ And ONCE, in the background, because the product's default changes: the unit suites of every touched project and the system tests — every red proved pre-existing against the base or fixed | the GATES table, every row with its command and counts |
| **U-5** · `CE-4534` | ⭐ **a manual test sheet for the user**: what to try in the editor and in player / server (play, edits while playing, undo, scenario load, replay review, film; a player joined to a server), where the switches are, what "restart to apply" means, what to report (the log lines, `GET /world/hash` when something looks different) — a section in Threaded Presentation §0a, not a separate document | the section, and every step on it run once by the lane itself |
| **U-6** · `CE-4535` | CE-3445 (the rare threaded divergence, 1 in 36) — the long saved-world series in the background while the rest runs; fix if found | the series' result; fixed or the next lever named |
| **U-7** · `CE-4536` | fold: §0a (the unified state, the settings, the sheet), §14.8, Parallel Simulation §18, Review §4 | the design commits named |

## How to work

The touched features' OWN rails; build the TEST project before `--no-build`; restore after merging; long runs in the background, never polled;
one build at a time; `scripts/cbm.sh` AND grep before claiming something exists. No LINQ / allocation per frame. Language-neutral output
(R-4221). ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-13_
