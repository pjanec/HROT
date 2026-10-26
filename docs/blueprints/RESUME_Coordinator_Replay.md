<!--STATUS
state: LIVE
updated: 2026-10-26
current-answer: §0 (the 2026-10-26 update) then §1 (state at writing) and §2 (what to do next, in order)
stale-below: nothing — §5 is the record of decisions, not history
known-rot: ⚠ RESUME_Scenario_Regression.md §1 still says the post-R-3039 re-record is in flight — it landed 2026-10-23 (8e84265f…39aab237); its §2 rows 1–6 are still the open list
known-conflict: none
related-designs:
  - docs/DESIGN_Replay_Census_H0.md — H0 of the replay harness (done): which types a recording must / must not hold
  - docs/DESIGN_Scenario_Regression.md — the verifier the replay harness extends; §9 grammar gaps (approved, R-4077); §10 (H1–H4) NOT WRITTEN YET
  - docs/blueprints/RESUME_Scenario_Regression.md — the regression programme's own state (CE-4042)
  - docs/DESIGN_Director_Timeline_Editor.md — the shared Timeline (ui builds it; TE-22 / R-4076 added this session)
  - docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md — restore + DataPolicy + R1 (backend's)
-->

# RESUME — coordinator lane: replay working in general (harness H0–H4), regression, timeline

RELEARN

⭐ Start: `RULINGS.md` → this file §1–§2 → `git fetch origin backend ui behaviors` and **merge `backend` first** (it built most of the
backend handoff overnight, §1). Graph: run `scripts/cbm.sh list_projects` in the BACKGROUND first (a fresh VM indexes, ~minutes).

## 0. ⭐ Update `2026-10-26` (later) — read this before §1

| | |
|---|---|
| **merged** | `ui@37992db4c` (Timeline steer R-4078/R-4079 built — REVIEWED and accepted: one `ITimelineLaneProvider` in `Fdp.Toolkits`, no lane enum / extractors / adapter, lanes = predicate + projection registered by their toolkits, per-lane sidecar hash; red-proofs 6/6) · `backend@9c1297c37` (one wire mapping of a transition; replay span finding) |
| **§2 item 1** | ✅ done (backend's CE-4046 groups were already merged) |
| **§2 item 2** | ✅ **Scenario Regression §10 WRITTEN** — leans RH-1…RH-10 wait for the user. ⚠ It changes the plan below: the harness replays a STEPPED LIVE run through the master (R-3053: previews never record), not the sandbox |
| **next** | the user's answer on RH-1…RH-10 → §10c step 0 (four measurements) → H1. ⚠ RH-9 (one replay host shared with the director) needs the ui lane |

## 1. State at writing (`2026-10-26`, coordinator @ `07bd8cfa`)

| thread | state | where |
|---|---|---|
| ⭐ **Replay harness CE-4045** | H0 census ✅ DONE · **H1 not started** · H2–H4 designed in chat only | [census](../DESIGN_Replay_Census_H0.md); [tracker](Blueprint_Issues_Tracker.md) CE-4045 |
| **Record rail CE-4046** | rail BUILT by coordinator (`45e0500c`, 3/3): `RecordedManagedComponentsAreImmutableTests` + `FDP/TestShared/ManagedComponentImmutabilityProbe.cs`, shrink-only list of 23 | census §2 |
| **Backend handoff** ([file](batches/HANDOFF_Coordinator_To_Backend_Replay_Recording_Fixes.md), dispatched `876f32057`) | ⭐ **backend STARTED and built most of it** (on `origin/backend`, not yet merged here): serializer round-trips records (step 1, `FdpAutoSerializerImmutableRecordTests`), CE-4046 groups A/B/C converted, AirPath split, CE-4048 traffic mirror replay-gated. ⛔ **item ③ (CE-3309) DROPPED by the user** in the backend session — do not re-raise it. ⇒ verify on merge: the shrink-only list shrank, the design was folded (Air Ops §5, census §2) | `git log 876f32057..origin/backend` |
| **ui handoff** ([file](batches/HANDOFF_Coordinator_To_Ui_Replay_Ui_State.md), dispatched `876f32057`) | not merged by ui at writing | CE-4047, CE-4049 |
| **Backend's own replay work** | backend is ALSO working replay over HTTP: `POST /replay` from a live run (CE-3326), "run C" on the five-process cluster, one timeline per recording, R-3055 (*"no differences between editor and cgf"*) | [`DESIGN_Http_Control_Through_The_Cluster_Master.md`](../DESIGN_Http_Control_Through_The_Cluster_Master.md) — ⚠ **read it before writing §10/H4**: the cluster half of H4 may reuse it |
| **Timeline** | ui builds it (CE-1301…1306); TE-22 approved (Behaviour lanes Changes/Orders/Reports/Decisions; old group "Orders" → **Tasks**), reply note sent to ui | Timeline design §0 TE-22, §2b |
| **Scenario regression CE-4042** | slice 1 built; §9 grammar gaps APPROVED — they land in the predicate model (R-4077), not `AnchorBook` | Scenario Regression §9; RESUME_Scenario_Regression §2 |
| **Film director** | ui's (CE-4038 open for CE-1241's 15 takes; CE-1239 `.ogg`); coordinator rows CE-4034…4037, 4041 open (overnight film run, demo scenarios, decoy flares, traffic panic, stepped-preview differences) | tracker |

## 2. Next — in order

| # | item | basis |
|---|---|---|
| **1** | merge `backend` (restore the test projects once after it — the third stale-binary trap); check the CE-4046 shrink-only list shrank and the rail is green on the merged tree; verify backend folded the as-built into the designs it changed | handoff gates; CLAUDE.md "design must reflect the as-built" |
| **2** | ⭐ **write Scenario Regression §10 — the replay harness H1–H4**, diagram-first (classDiagram, sequenceDiagram, module diagram): `--verify-replay` in the verifier · a NEW `IVerifyWorld` over `ReplayBrowserContext.SandboxRepo` (reuse `DebugApiService`'s replay extraction, ~`:2875`) · record during the verify run (`/recording/start` works in preview) · seek to each golden snapshot, `ScenarioGolden.Snapshot`, `Compare` · DIFFs explained by the census are listed apart (like R1's `CompareNotCarried`). H2 shuffled seeks · H3 branch-from-replay vs straight run (`CompareFrom`) · H4 `--mode all` cluster, each node vs its own live snapshots — **read backend's HTTP-through-master design first** | chat decision this session (user: *"approved"*); census §1 |
| **3** | build **H1**; first targets: names survive into the sandbox (`EntityInfo`/`NetworkIdentity` are Default — unrun) · the golden's `shots`/`blasts` are NOT in a replay (`ShotLog`/`DetonationLog` live beside the world) ⇒ compare unit snapshots first · CE-4049 ② (UI command events re-fired in playback) shows here | census §2 |
| **4** | the grammar gaps (§9) in `SearchPredicateDto` / `PredicateCompiler`; `ConditionBook` lowers cues to predicates (R-4077) — ⚠ the predicate code is the Replay Browser's (FDP toolkit) that ui's Timeline R2 also touches: say it, coordinate | Scenario Regression §9, R-4074, R-4077 |
| **5** | RESUME_Scenario_Regression §2 rows 1–4 (story diff of the moved goldens, the `ua-casualty-care-heavy` +∞, `hill-attack` runFor, §7 numbers) and fix its stale §1 | that doc |
| **6** | 🔒 **user's calls, still open:** switching on the CI template (`docs/ci/scenario-regression.workflow.yml`, runner minutes) | Scenario Regression §8 ⑪ |

## 3. Ids this session

Rulings **R-4076** (Timeline Behaviour lanes, TE-22), **R-4077** (one condition model — predicates shared, never duplicated; the generic
rule is R-174) · coordinator next free **R-4078**. Tracker **CE-4045** (harness umbrella), **CE-4046** (managed in-place mutation /
records), **CE-4047** (`SquadDrillTrace` unrecorded), **CE-4048** (traffic mirror replay gate), **CE-4049** (UI state in the recording) ·
next free **CE-4050**.

## 4. Traps met this session

| trap | |
|---|---|
| the first `scripts/cbm.sh` call on a fresh VM returns *"no projects"* — it does NOT index by itself here | run `scripts/cbm.sh index_repository '{"repo_path":"/home/user/HROT"}'` in the background (~10 min, 273k nodes) |
| a merge of another lane conflicts on the id tables (`RULINGS.md` §0a, tracker "ID blocks") and the `updated:` line | take the HIGHER next-free per lane; union the `updated:` entries |
| the subagent census scan produced wrong type names | the rail's stale-entry check caught them — trust a run over a scan |
| `design-digest.py --check` exits 1 | pre-existing (same at base), not ours |

## 5. Decisions made this session (the user's words are in the cited docs)

- predicates: **share and unify, never duplicate — a generic rule everywhere** (R-4077 → R-174)
- replay harness H0→H4 approved; **H0 first** (done)
- managed components (besides singletons) are **immutable records**; never changed through the RO accessor; **no read-into-existing** (CE-3309 then dropped)
- **selection is UI state**, not simulation state ⇒ a `Transient` singleton like `UnitDebugSettings` (pins, CE-1329); `/replay/selection` withdrawn (→ ui)
- Timeline: approve ui's five Behaviour-lane leans; the old exercise group is **Tasks** (not "Missions" — clashes with the mission plan)
- grammar-gap leans approved (§9), built in the predicate model

---
_coordinator lane, 2026-10-26_
