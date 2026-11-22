<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: the newest entry at the top
related-designs:
  - PROTOCOL_Autonomous_Lanes.md §3 — decisions are skipped, never guessed; the coordinator answers here (DECIDED <date>: …)
-->
# DECISIONS — backend (newest at the top)

## CE-4679 — the LLM commander L-2: lane decisions TAKEN · `2026-11-22` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | ⭐ **the think-pause is HYBRID**: the frame waits up to 50 ms for the answer (as L-1 did); only a mind slower than that gets the clock held (`PauseTimeIntent` on the node bus, the one pause path) | the scripted mind answers in microseconds, so its orders still land on the wake's own frame and its runs still repeat bit for bit; a model gets the real clock pause the handoff asks for |
| 2 | the seat resumes the clock ONLY if it paused it AND no other `PauseTimeIntent` was seen during the hold (the bus is counted, minus the seat's own) | a user's ⏸ outlives the hold (the handoff's rule). ⚠ The other way round is NOT guarded: a user who presses ▶ during a hold runs the clock while the mind still thinks — the seat does not re-pause |
| 3 | ⚠ the pause lands on the cluster barrier, so sim time stops a few frames AFTER the wake, not on its frame | the one pause path is the master's; a frame-exact stop would need a second path (⛔ ruling 9). The scripted mind is unaffected (decision 1) |
| 4 | the hold's bound is `ExternalMind`'s own timeout (120 s) + 10 s; after it the seat says "no answer — the clock runs on with no orders" | the handoff's "bounded by wall time"; one number owns it |
| 5 | several seats holding at once each pause / resume on their own | one seat is L-2's scope; multi-seat arbitration is L-4's (named there) |
| 6 | the seats are kept WITH the world (`CommanderSeats.For(world)`, the `SquadReportLog` way), not on the pack | the HTTP surface reaches them from the brain world it already has; no host has to remember to wire it |
| 7 | the model log is `<recording>.commander-<netId>.jsonl` — one line per call (`new` radio lines, `actions`, `raw`) and per verdict (`accepted` / `refused` + reason) | "beside the recording", and the verdict line is what names each order the recording holds |
| 9 | ⭐ a STEPPED clock is held by its STEPPER, never paused / resumed by a seat (`CommanderSeats.ClockRunsFree` from the host; the verify run's `HoldsStep` reads `AnyHoldsClock`) | 📌 measured in the proof run: the verify run steps the clock, a pause does not stop explicit steps (sim time ran on while the mind waited), and the seat's `ResumeTimeIntent` would have switched the master to continuous mid-run. ⚠ A user's `/sim/step` during a hold still steps (an explicit act); only the verify stepper waits |
| 8 | ⑤ — the L-1 row *"the fight ends before the time limit: … breaks contact with Bravo (measured outcome)"* is WITHDRAWN from `llm-platoon-attack`; the scenario's `$doc` names check ④ (Red beaten OR break contact) as not expressible until the `any` combinator lands (DESIGN_Scenario_Regression §9, R-4077, unbuilt) | ⚠ the handoff says *"the checks stay as they are"* — but that row pinned the one outcome ⑤ exists to remove (Alpha shot down at once). With the fight ⑤ asks for (contact 76 s → past 420 s, a stand-off) its break-contact branch never comes, and the either/or cannot be said; §9's own rule: encode the expressible part and name the rest, never a weaker condition. Reverse: build `any` (predicate model) and restore check ④ as `any: [Red down, BreakContact]` |

## CE-4676 — the LLM commander L-1: lane decisions TAKEN (not waiting on an answer) · `2026-11-22` · 🟢 **TAKEN — reverse any by writing DECIDED here**

None of these needed the user (no design or ruling decides them, each is small and reversible). They are listed so the coordinator can overrule one.

| # | decision | why — and what would change it |
|---|---|---|
| 1 | ⭐ **think-pause in L-1 = the FRAME holds** (`CommanderActionSystem` waits up to 2 s of wall time for the mind to answer every wake), not a pause of the sim clock | the scripted mind answers in microseconds, so the run is repeatable with no clock involvement. ⚠ L-2 (a Haiku worker, tens of seconds) needs the CLOCK paused instead — a held frame would freeze the editor's UI |
| 2 | **the orders L-1 takes:** Move · Hold · Attack (by the squad's class, `CommandOrders.Order`) · SupportByFire (= Attack with `HoldBack`, R-8303) · Defend / BreakContact (the concrete `SquadDefend` / `BreakContact` through `CommandOrders.Route`, as `PlatoonDefendBehavior` sends them) | ⛔ **not** BoundingOverwatch / SuppressAndManeuver / ClearBuilding: no measured params path from a platoon to a squad for them. The handoff says "if they resolve for a squad"; they are a refusal with the list until measured |
| 3 | `target` resolves as a PLACE (like `where`) | L-1's mind has no entity ids for the enemy, only reported places. An entity target is L-2's tool |
| 4 | ⭐ **a `report` cue in the checks grammar** (`{"on":"report","by":…,"target":…,"is":"Spot"}` over the node's `SquadReportLog`, editor tier) | the scenario's done-when checks ①③ cannot be said without it (CLAUDE.md: a cue the grammar cannot say ⇒ extend the grammar) |
| 5 | the seat's code lives in **`Hrot.CGF/Commander/`**, not in behaviors' assembly | it calls the command chain and never changes it; the CGF pack registers it on every brain node (editor + CGF) |
| 6 | the seat ENFORCES its premise at open: `AssignSopEvent(InertSop, Superior)` if the SOP is not already `InertSop`, and a clear of any running task | the scenario also writes `"Sop": InertSop` — the enforcement makes a seat added at run time (L-2's `/mind`) safe too |

## CE-3473 — a ramming CAR never breaks the barrels (`cp-ram-barrier`, Checkpoint RM-5) · `2026-11-18` · ✅ **DECIDED `2026-11-18`**

**DECIDED `2026-11-18` (the user):** *"follow your lean; also realistic bullet stopping power (if barrel is full of liquid, it might be a serious/resistive obstacle for both the ramming car as well as for bullets"* ⇒ option A, and a barrel's FILL (empty / liquid / …) decides BOTH its breach strength and its protection from fire — one property, read by both.

**Question:** how should a car on a `RamTarget` (a ram order) break a weak barrier of barrels, given that (a) no `contact-breach.json` row takes a
wheeled hull and (b) a barrel is modelled as a SOLID STEEL box (strength 540, above the IFV row's 150)?

**Measured** (`2026-11-18`, while landing CE-3452 — CE-3452 is not the cause, it only stopped hiding this):
| claim | code | design |
|---|---|---|
| a wheeled hull takes no breach row | ✅ `contact-breach.json` (rows: bulldozer, tank, ifv — all tracked — and the animal row); its own comment: *"a vehicle no row takes breaks nothing (wheeled vehicles)"* | ⛔ `DESIGN_Checkpoint_And_Ramming.md` RM-5: *"a car with a `RamOrder` breaches a WEAK barrier through `ContactBreach`"* — K-2's rail uses a TANK |
| a barrel's strength is 540 | ✅ `ContactBreach.Identify`: steel-plate 1000 × short side 0.6 m × height 0.9 m (`ClutterTypes.Barrel`, "steel-plate") | ⛔ searched `docs/` — no record of a barrel's breach strength |
| the scenario's old pass was a FALSE GREEN | ✅ old golden: every barrel at Health 50 throughout; the car passed at y ≈ 292, SOUTH of barrel 1 (y 294) — round the row's end, after looping at its start (the CE-3452 bug). With CE-3452 the car drives its straight line at y 295 and is held between barrels 1 and 2 at x 363.6 | ⛔ the scenario's own Judge line: *"it goes THROUGH the barrels … never a path round"* |

**Options:**
- **A (lean)** — a `"ram": true` row in `contact-breach.json` (any hull of ≥ 1000 kg carrying an ACTIVE `RamOrder`, small barriers ≤ ~60, a ram at ≥ 4 m/s,
  no push, keeps ~0.7 of its speed), last in file order so a tracked row still wins; AND a barrel's breach strength from its real build (a drum —
  a thin shell) rather than a solid box: an obstacle type's own `breachStrength` beside its material. ⭐ Why: RM-5 is the design's stated intent; the
  barrel's BALLISTIC protection (the material, read by fire) stays as it is. Cost: a JSON row + one field per obstacle type + rails; the ram scenarios'
  goldens move.
- **B** — only the ram row, barrels re-materialled (e.g. `fence-metal-sheet` 200 → strength 108). Cost: changes a barrel's protection as COVER for every
  shot through one (the material is shared with fire) — a cross-domain side effect.
- **C** — leave both; the scenario's row is rewritten to what is built (the car is HELD at the barrels). ⛔ Against RM-5 and the scenario's own Judge.

**Meanwhile:** `cp-ram-barrier` stays honestly red (the row is not loosened); CE-3452 lands with it named as this exposed false green.
