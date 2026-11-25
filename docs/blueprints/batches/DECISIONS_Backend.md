<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: the newest entry at the top
related-designs:
  - PROTOCOL_Autonomous_Lanes.md §3 — decisions are skipped, never guessed; the coordinator answers here (DECIDED <date>: …)
-->
# DECISIONS — backend (newest at the top)

## CE-3494 — the unit intent and the held plan · `2026-11-22` · ✅ **DECIDED by the user** (R-3114…R-3117)

🔒 User, verbatim: *"switch ruling 50 from discarding to pausing the plan and resume only by the user"* · *"agreed with restarting the paused task"* ·
*"making the mission plan and the intent mutually exclusive in the editor is a great idea"* · *"If this turns out not to be practical, then let's edit the memory directly."*

| | |
|---|---|
| the model | NOW (what runs) vs NEXT (the authored intent, one value per slot); the request systems run only when time advances — intent first, then every request since the last step, in order (the independent review moved it off 'fold every request', design §9) — [DESIGN_Unit_Intent.md](../../DESIGN_Unit_Intent.md) §1 |
| the plan | an operator order HOLDS it (`Halted = 2`); only the user resumes; Resume restarts the held task (R-3115, supersedes Entity Commanding ruling 50) |
| edits | no general queue: the AI request and a breakpoint edit are queued; everything else writes directly, as today (R-3116) |
| priority | intent first, then the plan's tick; edit mode: intent task XOR plan, warned both ways (R-3117) |
| supersedes | the CE-3445 entry below: its "the step-reader window" mechanism — the fold into a component replaces it (design §7, first rejected line) |
| §8 O1–O3 | ✅ DECIDED `2026-11-23` as leaned (user: *"i agree witho your leans"*, R-3118): Brain tab first expander · Superior in edit mode, Operator live · R-4137 rows case by case (CE-3496) |

## CE-3445 — nothing takes effect on a paused frame (B) · `2026-11-22` · ✅ **DECIDED by the user**

🔒 User, verbatim: *"i cant imagine how an order could take effect on paused world where no simulation should be running. at most it could be placed to some queue
to be executed the first frame after the pause."* · *"isnt this the same flaw as fith order? does it need to? why not on the first unpaused frame, wouldnt that be one
single way for anything?"* · *"'edit mode would then keep the AI as authored data' - i hoped this is what should have been happening. intents are the way to store AI
settings at editing time … My desire is simplicity and consistency."*

| | |
|---|---|
| the rule | ONE way for anything: a behaviour change (an order from HTTP / UI / the commander seat, a brain's order, a unit's type defaults) takes effect only on a frame where time ADVANCES; one that arrives while paused waits (the paused/stepped design's step-reader window) and lands on the next step, in order |
| editing | the AI is the INTENT (`InitialBrainIntent`: task, SOP, ROE) — kept on the unit for the whole edit session (no longer Transient / consumed), shown and edited by the AI panels, and what the SAVE writes (same JSON keys, so no scenario file changes). A running behaviour exists only once time runs |
| a run | on its first advancing frame each intent starts through the ingress, as a load does today |
| supersedes | the paused/stepped design's "BehaviorIngressSystem — EVERY edit" row (Appendix A) and R-192's save-from-the-running-state (`BrainSnapshotTranslator`, Sensors_And_Doctrine §7.5) — kept as HISTORY |
| why not A | A (the rule only during a run; editing keeps applying at once) was the coordinator's lean only because it changed less — the user: B is the consistent one |
| next | the design (UML, `DESIGN_Paused_And_Stepped_Execution.md`), then the build (behaviors' infrastructure — a cross-lane edit, said), then the 1- / 4-thread pairs again |

## CE-4686 — the four findings of the LLM commander over aggregated units: lane decisions TAKEN · `2026-11-24` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | ① ⭐ **HQ-3 AMENDED (HQ-3b)**: a commander of UNITS (its roster holds commanders or stochastic units) relays its picture's fresh GROUND elements up one level — grouped (50 m), the freshest speaking with its own age, ≤ 2 groups per sender, refreshed on a 25 m move or every 15 s | the company heard no infantry at all; HQ-3's reason (an HQ's 8 slots filling with infantry) is kept by the bound and by #2. ⚠ `CE2310_…` now expects the rifles relayed — the amendment, not a regression |
| 2 | ① a FULL picture evicts an unflagged element before any IndirectFire / AirDefence one; a ground report never pushes a support element out | protects every superior alike — no class says "this is a support HQ" |
| 3 | ① the criterion "commands units" (a live roster member is a commander or a stochastic unit), not "has no SquadCognitiveState" | a platoon's leader entity may carry a cognitive state with an empty pool; what has no eyes is a commander of commanders |
| 4 | ② roles through a READ-ONLY interface on the plays' blackboards (`IPlayRoles`), read via `OccurrenceSlotCatalog` | the one way into a running behaviour already exists; each play names its own roles, the seat never learns a layout. A cross-lane, additive edit of four plays |
| 5 | ③ a subordinate's SupportByFire: `support_at` = where it stands, the objective = what it fires at; a physical platoon gets the KIND (`PlatoonSupportByFire`), a squad or a stochastic unit keeps Attack + HoldBack | measured: the kind resolves to nothing for a squad and to the HoldBack-reading generic attack for a stochastic unit |
| 6 | ③ ⭐ R-8392's hold-back half AMENDED (⛔ the first version of this row said "not changed" — the run overturned it): at Physical a platoon's hold-back attack re-resolves to `PlatoonSupportByFire` (Position carried, HoldBack dropped); a company's still waits | 📐 measured: the old rule CLEARED the order at a forced split (TaskFailed 150 m short) — the AGG lane's code, changed under the user's "all 4"; the ruling's premise ("no platoon play holds back") was false since `PlatoonSupportByFire` fulfils SupportByFire |
| 9 | ③ an order its unit REPORTED failed (picture progress TaskFailed) leaves the seat's in-force table | measured: the model's re-issue of the cleared order was answered "already in force — nothing sent" |
| 7 | ④ `BoundOrder { Position, SupportPoint, Speed }` — the place is `Position` (as Attack / Defend), not `Objective` | the stochastic generic attack (the table's Bound row for a stochastic unit) already reads `Position`; the physical Bound plays accept it and read it when `Objective` is unset |
| 8 | ④ `PlatoonBound` with no support point sets its cover AT the objective | the commanders always filled it; an unset field sent the cover to (0, 0) |

## CE-4685 — the LLM commander over AGGREGATED units: lane decisions TAKEN · `2026-11-24` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | the seat's echelon comes from the entity header's DIS type first (then the TKB type, then `AggregateUnit.Echelon`) | the routing table resolves the plays from the SAME header — the seat's words and its plays can never disagree |
| 2 | ⭐ **USER `2026-11-24`: the seat gives ORDER KINDS, never a behaviour name** (*"We need to introduce the order kinds to higher level echelons. We cannot call directly a behavior name"*): his whole unit gets `Order.<kind>`; the kinds offered are those `OrderReissue.PlayFor` resolves for his class (a company: Attack, Defend); "still running" reads `CurrentOrders` by kind | ONE table decides what a class runs (shared, never duplicated) — the seat now takes the same path as a superior's `CommandOrders.Order`. ⛔ Superseded the same day: the seat named the plays (`CompanyAttack`) |
| 3 | an aggregated row gives its men only once its slot table holds slots | 📌 at seat open the table is not filled yet — "0 men" told the model the platoon was gone |
| 4 | the split is news ONCE, a waking event worded "near the enemy" (not "in contact") | the hand-over is the 600 m band (X-5), not contact; the seat keeps the same entity, so its orders stand |
| 5 | ⭐ cross-lane (the AGG lane's `UnitRelayBehaviors`, CE-8307): a child GIVEN the relayed order but not yet running it is busy for 3 s sim | 📌 measured on `llm-company-aggregated`: the relay ended 2 frames after ordering and ending it cleared the squads' moves — every split platoon stopped where it split. A child that never starts still lets it end (never a hang) |
| 6 | the scenario sits inside BOTH of berlin's grids (collider ±750 m, perception [0..1000]) | 📌 the first two placements put units outside one or the other: Red unseen, no fight |

## CE-4684 — the LLM commander L-3c (the decision contract): lane decisions TAKEN · `2026-11-23` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | the chat-model mind answers with decisions by DEFAULT; `HROT_COMMANDER_FORMAT=tools` keeps L-3a's tool calls | the contract is what L-3b measures; the tools format stays for the side-by-side baseline |
| 2 | ⚠ the design's `Via` / `Orient` are NOT in the schema | no order on this path takes them — a field the executor ignores teaches the model nothing true |
| 3 | `support_at` is a PLACE (a play's support position), not a unit | `PlatoonBound` / `PlatoonAttack` take a support point; the order path has no "supported unit" |
| 4 | validation is all or nothing: any problem ⇒ the whole decision goes back once, then CONTINUE | a partly applied decision is a plan nobody made; the model repairs one coherent answer |
| 5 | a play AND a squad order in the same decision is a validation error | a squad order ENDS the play (§5b) — the model is told, not silently overridden |
| 6 | ⭐ a stale decision's refusal is NOT a wake | 📌 measured: as a wake it staled the next answer too — 15 refusals in a row, the clock held at 00:00:36. The newer news it was stale against re-asks the model anyway |
| 7 | the model's next decision is held to ≥ 10 s and ≤ 300 s after the last wake | a floor keeps a fallback (CONTINUE, no new deadline) from asking every frame; the ceiling = the schema's maximum |
| 8 | terrain facts: a soldier's eye 1.7 m sees a man's chest 1.0 m; a standable place is judged at its own spot, a building at the standable points around its walls; cover ≥ 4 points covered, 1–3 partial; a vehicle reaches within 15 m; foot pace 1.4 m/s | 📌 a 6 m ring (the first build) read the objective behind the Low Wall as seen from the house — the place is where a man stands |
| 9 | terrain facts go in EVERY seat's static block, not only a decision seat's | static, measured once, useful to any mind; the tools baseline's prefix grows by them too |
| 10 | enemy confidence: reports within 30 m are one enemy, a report ≤ 60 s old is fresh | the picture keeps one reporter per element — grouping is the only way to count squads |
| 11 | STRENGTH_BELOW reads the reported colour as Green 100 · Amber 75 · Red 50 · Black 25 | the commander knows colours, never percentages |
| 12 | the watcher's orders are given on the NEXT frame's action pass (injected into the seat's queue) | one order path — the action system's validation, no-op and refusal apply to them like to the model's |

## CE-4683 — the LLM commander L-3a: lane decisions TAKEN · `2026-11-23` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | ⭐ the handoff's lean as built: ONE running estimate, an answer's notes JOINED then capped at 800 chars (`…[cut]`), replacing the last | several notes in one answer are one estimate; a cap with a visible marker tells the model it was cut |
| 2 | ⭐ a note is NO LONGER echoed into the feed as `NOTE:` | 📐 it was in the briefing twice (the radio log AND the notes); the node log (INFO) and the model log keep it |
| 3 | ⭐ the runner's own failure and an outside mind's timeout are NO LONGER notes | they would REPLACE his estimate with a system message; the model log (`failure` / `raw`) and the node log say them — the same rule as CE-4681 #4 for the chat mind |
| 4 | ⭐ the in-force table is ONE dictionary on the seat (`InForce`, `GivenOrder`), replacing B-4's text-only `Given` | "shared, never duplicated": the snapshot shows it, the action system checks a repeat against it |
| 5 | "identical" = the same kind and a point within 2 m — not the same text | *"house"* / *"House"* / *"the house"* resolve to one point; text would miss them |
| 6 | a play's squad orders enter the table from `SubordinateOrders` (the play's run), *since* = when the seat first saw them | the entries carry no time; snapshot granularity is honest and said |
| 7 | a repeat of a PLAY's order is a no-op too — it does not end the play | a direct order ends the play (B-1): re-sending what the play already did would end it for nothing |
| 8 | ⚠ "in force" is the seat's record: a squad that FINISHED a Move is still "in force" for a repeat of the same Move | harmless (it is there); a different order or a play replaces it. A finished-order detector would need a per-order completion signal the squads do not report uniformly |
| 9 | the snapshot's squad line no longer repeats the task — the ORDERS IN FORCE block says it once | the budget |
| 10 | the token estimate is chars / 4 | a rule of thumb for English + JSON, said as such; no tokenizer in the process |

## CE-4681 — the LLM commander L-3 (cloud VM): lane decisions TAKEN · `2026-11-23` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | ⭐ the mind is `CommanderMind.ChatModel` — the reserved `Ollama` value RENAMED (Roslyn), not a new value | the handoff names the scenario value `"ChatModel"`; nothing referenced `Ollama`; any server speaking Ollama's chat API answers it (Ollama, the stand-ins) |
| 2 | ⭐ one conversation per WAKE: `system` = the seat's prefix + a fixed instruction (byte-stable), `user` = `Briefing.Changing()` + the time — NO history of earlier calls | §3: the briefing IS the memory (snapshot, radio log, notes); a growing chat history would blow an 8 B model's context (§6) |
| 3 | the retry keeps the first answer's GOOD calls and adds the second answer's good ones; the tool message names what was taken | ⛔ otherwise a model that resends everything doubles the orders; ⛔ never a second retry (the handoff) |
| 4 | no answer (timeout, refused connection, HTTP error, not JSON) ⇒ NO actions — not ExternalMind's *"no answer"* note | a note lands in the briefing's notes for the rest of the run: a flaky server would fill the context with noise; the model log says why |
| 5 | settings and the run's mind by ENVIRONMENT (`HROT_COMMANDER_*`), not a scenario field | an endpoint is a machine fact, not scenario content; the laptop script runs ONE scenario per model. `HROT_COMMANDER_MIND` writes the seat so the checks see the mind that ran |
| 6 | ⭐ TWO stand-in servers (a C# rail fixture, a Python process) | a NECESSARY difference (CLAUDE.md "shared, never duplicated"): the rails need one in process they control (a hang, a dead port); the run needs a process the editor reaches that can relay to an agent |
| 7 | checks per mind with NO grammar change: an anchor on `CommanderSeat.Mind` + `"if"` | the grammar already had both; a `"mind"` key would be a second way to say the same thing |
| 8 | the model log falls back to `HROT_COMMANDER_LOG_DIR` when nothing records | a verify run records nothing, so L-2's log was absent for the unrecorded Haiku run (status §L-2 ★ run 3); the proof needs every exchange |

## CE-4680 — the LLM commander L-2b: lane decisions TAKEN · `2026-11-22` · 🟢 **TAKEN — reverse any by writing DECIDED here**

| # | decision | why |
|---|---|---|
| 1 | ⭐ `llm-platoon-attack`: the L-2 row *"Red still fires 60 s after its first shot"* is REPLACED by *"Blue still fires 60 s after Red's first shot"*, and L-1's check ④ is BACK as *"every Red man down by the time limit"* (reverses CE-4679 #8's withdrawal for this branch) | 📐 measured, two probe runs: with `run_play(PlatoonAttack)` the play beats Red — every Red down, one Bravo man lost — after about a minute of Red fire, so Red's last shot falls just before +60 s. The row was there to prove "a fight, not a rout"; the fight still lasts a minute (Blue's side), and the outcome is now the check-④ branch that IS expressible |
| 2 | `ScriptedMind.UsePlays` — on in production, off for L-1's rail | the handoff's proof 1 wants the scripted mind on `run_play`; L-1's rail pins the direct-order rule, which stays a valid mind |
| 3 | B-4's task falls back to the SEAT's own last direct order when the commander holds no `SubordinateOrders` | 📌 measured: `SubordinateOrders.Record` records nothing for a commander with no running task (run 0) — the seat's own record is the only source then |
| 4 | B-5's "heard" is NOT shown; asked of behaviors | ⛔ `CommandReport` has no seen / heard bit; `Radius` means two different things (a seen group's spread, a sound's uncertainty) — guessing from it would be a ground-truth-free lie |
| 6 | ⭐ `CommanderBrief` / `PlaceNote` are UNMANAGED (`FixedString128` fields), not the handoff's managed record | 📌 measured: a managed component needs a scenario translator, and in `Hrot.EcsNode`'s serializer list (which cannot see `Hrot.CGF`) — the unmanaged form is auto-serialized like `CommanderSeat`, and `NoReplay` keeps it out of recordings. Cost: 127 UTF-8 bytes per field |
| 5 | component ids 320 / 321 (not the lane block) | backend's block 434–449 is full; 320 / 321 are on the catalogue rail's FREE list, no branch declares them (318 / 319 are the minefields') |

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
