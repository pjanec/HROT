<!--STATUS
state: SUPERSEDED — items 1–3 done 2026-10-17 (shelter live 15/15, both CIV-2 twins, the mutants); item 4 continues in the handoff below.
superseded-by: docs/blueprints/batches/HANDOFF_Behaviors_Armed_People_Build.md
updated: 2026-10-17
current-answer: the whole file; start at "Next, in order".
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — the programme. §5c demos (one per feature, each a test), §5d CIV-1 as-built, §5e CIV-2 as-built.
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_StoppedMovePath.md — the CE-2245 cross-lane fix, sent to backend.
-->

# HANDOFF — behaviors civilians programme, takeover

**Dispatched at:** the commit that adds this file, on `behaviors`. Work ON `behaviors` itself: it is in a clean state, builds, and its
rails are green. Merge `origin/backend` / `origin/ui` first if they moved (lane protocol rule 7), and name them in your first commit.

## The user's standing asks *(verbatim gist)*

- "Go autonomously, write handoffs to other lanes as needed."
- ⭐ **"A demo scenario for each civilian feature, with a clear success condition so it is usable as a test."** Each feature gets three
  things: `scenarios/<name>/scenario.json`, a live check (`python3 scripts/utility-demo-check.py --launch <name>`), and an in-process
  twin in `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/PostureScenarioTests.cs`. The demo table is design §5c.

## Where it stands

| item | state |
|---|---|
| [`CE-2235`](../Blueprint_Issues_Tracker.md) CIV-1 Routine / Alarmed / Casualty | ✅ built; live 11/11; twin green. ⏳ The perf gate waits on backend B-4 (`ua-civ-crowd-300`) |
| [`CE-2236`](../Blueprint_Issues_Tracker.md) CIV-2 Panic / Shelter / Inside / Cower | ✅ built (`5cd7e7e55`), design §5e. Rails green (`EnterableShelterTests` 3, `CivilianHsmTests` 7). Live: `ua-civ-cower` 11/11; `ua-civ-shelter` 14/15 — the fail was the check's poll, now fixed, **not re-run** |
| [`CE-2245`](../Blueprint_Issues_Tracker.md) a stopped move came back when its late path arrived | ✅ fixed cross-lane in backend's `EngineBackedPathResponseSystem`, red-proved; note sent to backend |

## Next, in order

1. **Re-run `ua-civ-shelter` live**: `dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore`, then
   `python3 scripts/utility-demo-check.py --launch ua-civ-shelter`, in the background. Expect 15/15; update the §5c row.
2. **Twins for both CIV-2 demos** in `PostureScenarioTests`. Mirror `CE2235_CivRoutineAlarm_…`: the same harness, `ByName`, and
   `Mode(e) = CivilianMind.ModeOf(cgf, e)`. Sample every frame, so the shelter twin can assert `Panic` itself. The shelter twin
   asserts: Panic → Inside inside House A's footprint (100–110 × 100–108), never House B (160–168 × 100–110), and gone on CGF and
   SimHost about 20 s later. The cower twin asserts: `Effects.IsCowering` on CGF + SimHost, then cleared after the hostile is down.
   ⚠ Rule `CE-2244`: two test-town twins in one process can interfere with each other. If one fails only in a full-class run, run it alone.
3. **Mutants** of the new states (each must turn a rail red, then restore it with `git checkout`, ⛔ never commit a mutant). Candidates: drop the
   `Locked` skip in `EnterableDoorGenerator`; `Cower` not setting cowering; `Inside` publishing every frame; the `ShelterWaitSeconds`
   timeout removed.
4. Then **`CE-2237` crowds** → `CE-2238` hostile acts and riot → `CE-2239` the escalation ladder → `CE-2240` protest and gestures →
   `CE-2241` hands up holds fire, plus provocation. Each needs a design section with UML BEFORE its build, and its §5c demo(s) after it.

## Facts you will need *(measured)*

- `CivilianHsm`: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CivilianBehavior.cs`. Fear: `FDP/Toolkits/Fdp.Toolkits/Civilians/`
  (`CivilianState` component **399**, brain-local; `CivilianFearSystem` on CGF). Shelter EQS: `Spatial/Eqs/EnterableShelter.cs`.
- Fear impulse is `0.45 × (1 − 0.5·d/100)` per small-arms shot, and it fades at 0.05/s. AlarmAt 0.3, PanicAt 0.6, CalmBelow 0.1. So
  civilians 40 m from a firefight pass through Alarmed into Panic within about a second. A live check polling at 0.2–0.25 s
  must accept "Alarmed or beyond" (or "Panic or beyond"); the twin asserts the exact state.
- test-town covers 0–400 m. Its Pond is x 298–365 / y 300–365, and every building is solid. ⇒ every panic there ends in Cower. Only bt-range
  has enterable houses. Door keys look like `bt-range/House A/front`; the scenario's `TerrainObjects` section sets door states.
- A scenario's `Behavior` component `{Name, Params, Origin}` overrides the type's default behaviour. The shelter demo uses it to set
  `HideMinSeconds = HideMaxSeconds = 20`, measured live at 19.0–21.1 s.
- Ids: behaviors' next free ids are **CE-2246** and **R-2025**.
- Rules that bit this lane: run builds and tests in the background and never poll them; build the TEST project, then use `--no-build`;
  a failed build followed by `--no-build` runs a STALE binary that reports green.
