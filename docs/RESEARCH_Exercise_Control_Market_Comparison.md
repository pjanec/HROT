<!--STATUS
state: LIVE
updated: 2026-11-01 (coordinator — first pass, web research + general knowledge)
current-answer: §2 (the comparison) and §3 (the gaps, ranked)
stale-below: nothing
known-rot: none — ⚠ product facts are from public sources of uneven quality (vendor pages, community wikis); each row says how sure it is
known-conflict: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — OWNS the decisions (SR-…) this compares against; a gap adopted here becomes an SR row there
  - docs/EXPLAINER_Scenario_Evaluation_And_Direction.md — explains objectives, triggers and exercise control with examples
  - docs/DESIGN_Director_Timeline_Editor.md — OWNS the timeline lanes an MSEL view (gap G2) and AAR bookmarks (gap G5) would ride on
  - docs/USAGES.md — where a gap, once built, becomes a sellable usage
-->

# RESEARCH — **exercise control, evaluation and AAR: what others provide, what we miss**

> A comparison of the scenario-rules design (objectives, triggers, exercise control — [`DESIGN_Scenario_Rules.md`](DESIGN_Scenario_Rules.md))
> with military training systems and games. ⚠ Nothing on our side is built yet; this compares the DESIGN.

## 1. What was looked at

| product / practice | kind | what it is known for here |
|---|---|---|
| **VBS4** (BISim) | military virtual training | Battlespaces (terrain + plans + scenarios + AARs together), real-time scenario edits, 2D/3D AAR from any view, performance measures captured automatically, open APIs for AAR data; triggers for ambushes, radio/timed IEDs |
| **Arma 3** (Eden editor, Zeus) | game, used for training | triggers with activation / condition / timer / repeat / server-only; probability of presence; **Zeus** = a live game master who edits the running mission |
| **Command: Modern Operations** | naval/air wargame, pro edition | event engine = triggers + conditions + actions, Lua as condition or action, side scoring |
| **Steel Beasts Pro** | armour training | events / triggers / conditions control logic, alternate routes that make replays differ, planning → action → AAR phases |
| **MASA SWORD** | constructive simulation | doctrine AI, AAR with filtered "smart layers" (force ratios at any moment), AAR edit tool (cut the relevant sequences, export, view without a licence), KPI web dashboard feeding exercise-management tools (4C Exonaut), Monte-Carlo course-of-action runs |
| **Left 4 Dead — AI Director** | game | adaptive pacing: tracks player intensity, cycles build-up → peak → relax, removes threats when intensity is too high |
| **MSEL / EXCON practice** | military exercise doctrine | the Master Scenario Events List: a timeline of injects (time, sender, recipient, message, EXPECTED RESPONSE) delivered by exercise control; covers what the simulation does not (civilians, media, higher HQ) |
| **xAPI / competency research** (US Army STEEL-R, NATO STO) | training data standards | performance statements portable across simulators; competency states (untrained · practiced · trained) tracked across sessions on a dashboard |

## 2. The comparison

| capability | others | us (designed) | verdict |
|---|---|---|---|
| condition → action triggers | ✅ everyone (Arma, CMO, SB Pro, VBS) | ✅ triggers, edge-based, `holdFor` / `delay` / repeat / cooldown / `maxFires` (T1–T10, SR-99) | ⭐ on par; edge semantics and `holdFor` are cleaner than most |
| a trigger the SERVER owns | ✅ Arma "server only" | ✅ triggers run on the owning Brain node, not in replay (T10) | on par |
| rich conditions | Arma: script expression · CMO: Lua | ✅ the existing JSON condition language (SR-95) + entity re-target | ⭐ no scripting needed; ⚠ Lua is more powerful for power users |
| scripting escape hatch | ✅ Arma SQF, CMO Lua | ⚠ by design NOT scripting — reusable behaviour assets, new C# primitives, an embedded HSM (ladder, explainer §3.3) | deliberate; safer, deterministic, debuggable — but the ladder must stay easy to climb |
| reusable packages | Arma compositions, VBS Battlespaces | ✅ macros with params, a library | ⭐ stronger — params + bindings at every level |
| objectives / tasks with status | ✅ Arma tasks, CMO scoring | ✅ objectives with `Score` 0..1, composites, per-member evaluation by role | ⭐ deeper than any game; on par with training tools |
| adaptive difficulty / pacing | ✅ L4D Director (intensity-driven) | ✅ triggers on `Score` bands, `SetSkillAmplifier`, a control behaviour (SR-99/100) | ⚠ we steer on PERFORMANCE; L4D steers on player INTENSITY — see G6 |
| live game master | ✅ **Zeus** (place, order, spawn, edit live), VBS real-time edits | ⚠ editor + instructor station exist; live interventions are B5, not designed | ⛔ gap G4 |
| scenario VARIATION between runs | ✅ Arma probability of presence, random timers; SB Pro alternate routes | ⛔ none — every run of a scenario is the same | ⛔ **gap G1** |
| MSEL / inject timeline | ✅ doctrine; tools (JEMM, Exonaut) | ⚠ time / state triggers can fire injects, but no inject LIST, no EXPECTED RESPONSE, no view of what is pending | ⛔ **gap G2** |
| non-kinetic injects to trainees | ✅ MSEL messages (radio, higher HQ, civilians, media) | ⚠ `Message` to the instructor only | ⛔ gap G3 |
| real-time performance display | ✅ VBS3 measurement engine (live during the exercise) | ✅ `ObjectiveStatus` replicated; HUD own-evaluation (feature); IOS view not designed | ⚠ partial — G5 |
| AAR replay | ✅ VBS 2D/3D any view; SWORD smart layers | ✅ recordings, timeline lanes, the film director, replay browser | ⭐ strong — the film tooling is beyond most |
| AAR bookmarks / notes during the run | ✅ (CM Labs IOS; common in IOSs) | ⛔ not designed | ⛔ gap G5 |
| AAR auto-focus on relevant events, export a light review package | ✅ SWORD 6 (edit, export, view without licence) | ⚠ film director cuts takes from recordings; no packaged export for trainees | ⚠ gap G5 |
| heat / kill maps, force ratios over time | ✅ SWORD smart layers | ⛔ not designed (the data is recorded) | ⚠ gap G5 |
| standards: Go / No-Go per task (T&EO-like) | ✅ military practice | ✅ `required` objectives, thresholds, composites — ⚠ not mapped to a task catalogue | ⚠ gap G7 |
| trainee record ACROSS sessions | ✅ xAPI + competency dashboards (STEEL-R) | ⛔ evaluation is per run only | ⛔ **gap G7** |
| export to LMS / exercise management | ✅ xAPI, VBS open AAR APIs, SWORD → Exonaut | ⚠ NED topics (`ObjectiveStatus`, `ExerciseEnded`), `ExternalMeasurement` IN — no xAPI OUT | ⛔ gap G8 |
| side points / game score | ✅ CMO side score, game modes | ⚠ `Score` per objective; `AwardPoints` listed early (SR-5) but not designed | ⚠ gap G9 (games) |
| batch runs to calibrate / compare | ✅ SWORD Monte-Carlo COA | ✅ deterministic headless runs exist — ⚠ no "run N times with variation" | ⚠ gap G10 (needs G1) |

## 3. The gaps, ranked by value to a customer

| # | gap | why it matters | how it fits our design (lean) | size |
|---|---|---|---|---|
| ⭐⭐⭐ **G1** | **variation between runs** — probability of presence, random choice between macros / positions, random delays (`min…max`) | trainees repeat exercises; a fixed script is learned, not trained. Every game and Arma/SB Pro have it | a seeded random per run, RECORDED (replay stays exact): `presence: 0.6` on an entity or macro instance · `InsertMacro` choosing one of N · `delay: {min, max}` · a random pick of a spawn area. ⭐ Our determinism makes it reproducible — a selling point (*"rerun last Tuesday's variant"*) | M |
| ⭐⭐⭐ **G2** | **an MSEL view — injects with EXPECTED RESPONSES** | it is how exercise control actually works; instructors think in a list of injects with times and what the trainees should do | an *inject* = a trigger (time or condition) + an OPTIONAL linked objective that measures the expected response (*"report within 5 min"* = `ReportLatency`). The MSEL view = a table / timeline lane of every trigger with its state (pending · fired · responded · missed). ⭐ No new runtime — a VIEW over triggers + objectives | M |
| ⭐⭐ **G7** | **trainee record across sessions + standards catalogue** | the customer buys proficiency over time, not single scores; commanders ask *"is this squad trained?"* | per-trainee results (already per role/station, SR-72) appended to a persistent record keyed by the PERSON; objectives tagged with a task id from a catalogue (the T&EO idea); states untrained · practiced · trained | L |
| ⭐⭐ **G8** | **xAPI export** | plugs into the customer's LMS / exercise management (Exonaut-style) — a procurement checkbox | an exporter turning `ObjectiveStatus` changes and `ExerciseEnded` into xAPI statements (actor = trainee, verb = achieved / failed, object = objective + task id, result = Score) | S–M |
| ⭐⭐ **G4** | **live game master** (Zeus-like) | the instructor reacts to what happens instead of only what was scripted; very popular in Arma communities | B5 — instructor interventions: fire / disarm triggers (T9), run any action by hand (`InsertMacro`, `Order`, `SetSkillAmplifier`) from the IOS, journaled. ⭐ the same action vocabulary (SR-93) — nothing separate | M |
| ⭐⭐ **G5** | **AAR during and after** — instructor bookmarks / notes, auto-highlights, heat / kill maps, a packaged review | the debrief is where learning happens; instructors need to find the moments fast | bookmarks = a timeline lane (Director Timeline Editor); auto-highlights = trigger fires + objective state changes already journaled; heat maps from recorded positions / fire events | M |
| ⭐ **G3** | **non-kinetic injects to trainees** — radio messages, higher-HQ orders, civilian / media events | MSELs exist because simulation misses these; important for command-post and staff training | `Message(to: trainee station / unit / side, channel, text)` through the in-sim comms rather than to the instructor only | S |
| ⭐ **G6** | **intensity-based pacing** (L4D) | games and fun-factor training: steer on how STRESSED the players are, not only how WELL they do | a measure *"intensity"* per player (hits taken, near misses, enemies close) — then the same triggers / control behaviour use it | S–M |
| ⭐ **G9** | **side points / game score** | game modes, competitions | `AwardPoints(side, n)` + a side score objective — small | S |
| **G10** | **batch calibration** — run an exercise N times headless with variation, report the spread of outcomes | difficulty tuning before the course; COA comparison | needs G1; our deterministic headless runs are the base | M |

## 4. Where we are AHEAD — worth saying to customers

| strength | why it is rare |
|---|---|
| ⭐ one action vocabulary for triggers, behaviours and exercise control (SR-93) | most tools have a trigger language separate from AI scripting |
| ⭐ macros with typed params and bindings at every level | games have compositions without params; training tools have templates without composition |
| ⭐ evaluation by ROLE with a member library, composites with custom aggregation | games stop at win / lose; most training tools at a fixed measure set |
| ⭐ deterministic, recorded runs — exact replay, restore mid-run, reproducible variants (with G1) | rare in game engines |
| ⭐ the film director — cut takes from recordings | beyond the usual AAR replay |
| ⭐ no scripting needed for exercise control, with a clear ladder when it is | avoids the "only the scripter can change the mission" trap of SQF / Lua |

## 5. Sources

[VBS4 (BISim)](https://bisimulations.com/?p=735) · [VBS4 admin syllabus](https://bisimulations.com/wp-content/uploads/2019/03/syllabus_adm101_2019-4ac.pdf) ·
[Eden Editor: Trigger (Bohemia wiki)](https://community.bistudio.com/wiki/Eden_Editor:_Trigger) · [Zeus Enhanced](https://github.com/zen-mod/ZEN) ·
[Command Lua API](https://commandlua.github.io/oldsite/index.html) · [Command event handling](https://commandlua.github.io/oldsite/topics/98-event_handling.md.html) · [CMANO Lua scoring](https://commandops.github.io/posts/lua-scoring-example) ·
[Steel Beasts scenario design](https://www.steelbeasts.com/sbwiki/index.php/Scenario_Design) · [Steel Beasts Pro review](https://tanksim.com/reviews/steel_beasts_pro5.htm) ·
[MASA SWORD (Janes)](https://www.janes.com/defence-intelligence-insights/defence-news/c4isr/masa-evolves-sword-simulation-system) · [SWORD 6.0](https://www.shephardmedia.com/news/landwarfareintl/masa-releases-sword-version-60) ·
[PaceMaker paper (L4D Director)](https://arxiv.org/pdf/2408.15001) · [modl.ai on the Director](https://modl.ai/?p=1984) · [Evolve on L4D pacing](https://shacknews.com/article/82787/how-evolve-assures-action-peaks-and-valleys) ·
[A more realistic CPX (MSEL)](https://www.mca-marines.org/gazette/a-more-realistic-command-post-exercise/) · [Writing an MSEL](https://wmpllc.org/ojs/index.php/jem/article/view/1307) · [Joint exercise scripting](https://reference-global.com/download/article/10.2478/kbo-2020-0129.pdf) ·
[xAPI interoperable training data](https://www.academia.edu/115643251/Leveraging_Interoperable_Data_to_Improve_Training_Effectiveness_Using_the_Experience_API_XAPI_) · [NATO STO MSG-184](https://www.sto.nato.int/publications/STO%20Meeting%20Proceedings/STO-MP-MSG-184/@MP-MSG-184-O3PP.pdf) · [CM Labs Intellia Instructor](https://www.cm-labs.com/wp-content/uploads/2025/06/Intellia-Instructor-datasheet-06-25.pdf) · [Aptima VBS3 measures](https://www.unmannedsystemstechnology.com/2014/12/aptima-measures-human-performance-in-manned-unmanned-scenarios-for-us-army-training/)

⚠ Source quality: VBS4 and SWORD details come from vendor pages and trade press; Arma trigger attributes from the official Bohemia wiki; L4D
from an academic paper plus secondary articles; MSEL from practitioner articles. No vendor documentation behind a login was read.
