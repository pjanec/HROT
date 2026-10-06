<!--STATUS
state: LIVE
updated: 2026-10-06
current-answer: §0 — the UTILITY AI DEMO programme (CE-3069; design docs/DESIGN_Utility_AI_Demo_Scenarios.md, approved R-209). §1 below is the 2026-10-03 state (terrain world, asset management) and is HISTORY for this lane's current work.
stale-below: §1 and below — the 2026-10-03 terrain / asset-management state (history for this lane)
related-designs:
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the programme's owning design (push-only ownership, S1–S8, §5.7.1 live matrix, §5.9/§5.10 deferred designs).
  - docs/DESIGN_Subsystem_Composition_Unification.md — B5 (not started) gates CE-3006 / CE-524 / CE-513.
-->
# RESUME — backend lane

⚠ A STATE doc: verify every line against git before acting on it.

## 0. NOW (`2026-10-06`) — the utility AI demo programme

- **Owning design:** [`DESIGN_Utility_AI_Demo_Scenarios.md`](../DESIGN_Utility_AI_Demo_Scenarios.md) — §4 the seven scenarios U1–U7, §6
  the G-items, §8 the approved leans (R-209), §9 armour (CE-3071 ✅), §10 the danger sensor (CE-3072/3078/3079/3080 ✅), §11 G8+G3
  (fire distribution + `/squad`), §12 G7 (weapon mounts). Runbook: [`RUNBOOK_Utility_AI_Demos.md`](../RUNBOOK_Utility_AI_Demos.md).
- **Built:** U1 `ua-posture`, U2 `ua-threat-ranking` (live + in-process, CE-3085) · `ua-danger-crossing(-bp)` · G8 `CE-3088`, G3
  `CE-3087`, G7 `CE-3089` (code + rails; U6 `ua-fire-distribution` / U5 `ua-weapon-choice` scenarios + checks — see the tracker rows
  for the live result).
- **Behaviors lane (P2):** G4 `CE-3082`, G5 `CE-3083`, G6 `CE-3084` dispatched in
  [`HANDOFF_Utility_Demo_P2_Behaviors.md`](batches/HANDOFF_Utility_Demo_P2_Behaviors.md) — ⭐ its §6 SYNC is the channel; read the
  newest entries first. The backend then builds U3 `ua-three-hosts` / U4 `ua-attack-approach` against their assets.
- **Next on backend:** G10 `CE-3086` (desert ridge + wadi — only U7 needs it) · U7 waits on CE-507 D2/D3 (user decisions) ·
  CE-3081 (more danger kinds, not approved) · open reds CE-3076 / CE-3077.
- **Id block:** `CE-3000`–`CE-3999`, next free per the tracker header (`CE-3090` at this writing).

## 1. Where it stands (`2026-10-03`)

- ⭐ **Terrain-world slice 1 BUILT** ([`DESIGN_Terrain_World.md`](../DESIGN_Terrain_World.md) §8, report [`REPORT_Terrain_World_Slice1.md`](batches/REPORT_Terrain_World_Slice1.md)): GeoJSON world on every ECS node, map layer, movement Z (W8), DotRecast navmesh baked per terrain + solver on SimHost (W6, `CE-3006` closed), Z-up navigation (`CE-3011` closed), 3-D LOS with posture eye heights (W5). Open: `CE-3010` (Stride animation, Windows), `CE-3029` (vehicle follower cuts corners) (`CE-3017`, `CE-3018`, `CE-3024`, `CE-3025`, `CE-3026` — MoveTo is a `PathToPoint` intent planned on the vehicle side on every host, `CE-3027` — vehicle bake 1.8 m, `CE-3028` — `/world/info` live — closed `2026-10-03`).
- ⭐ **Terrain EQS slice BUILT** (`CE-3030`, [`EQS_Design_v1.3_final.md`](../designs/eqs-2/EQS_Design_v1.3_final.md) §19): terrain sight + cover, child-sensor self, new generators/tests, starter templates `FindOpenFiringPosition` / `FindFlankingPosition` / `FindSafeRetreatPoint` / `FindThreatsInView`. ⚠ No behaviour consumes the four new templates yet — the natural next step is a behaviours-lane consumer (BTree / blueprint) and a live run. Live-run scenario: `scenarios/tt-nav-los` (copy into `/tmp/FDP_Temp/shared/scenarios/` — the runtime NAS root).
- ⭐ **Asset management BUILT** (user put it in scope `2026-10-03`): [`DESIGN_Asset_Management.md`](../DESIGN_Asset_Management.md) §8 increments A→B→C (`CE-3019`/`CE-3020`/`CE-3021`), §10 the terrain deltas; plan [`PLAN_Asset_Management_Build.md`](PLAN_Asset_Management_Build.md).
- Backend id block **next free `CE-3022`**.

### ⛔ HISTORY — `2026-10-02`

- Branch **`backend`**. Last batch: [`HANDOFF_Ownership_Remaining_Work.md`](batches/HANDOFF_Ownership_Remaining_Work.md) → report [`REPORT_Ownership_Remaining_Work.md`](batches/REPORT_Ownership_Remaining_Work.md).
- Ownership programme: S1–S8 built; live matrix §5.7.1 **E1–E8 all ✅** (E8 = the multi-process crash reclaim, +10.2 s, no message).
- Done this batch: `CE-3003` (debug writes ask the owner), `CE-3004` (CGF polls mission acks), `CE-516` (editor uses the injected offline factory), `CE-518`'s 11 unit reds (all stale tests).
- Backend id block `CE-3000`–`CE-3999`, **next free `CE-3029`**. Every `behaviors` merge conflicts on the id-block table: keep their behaviors row and our backend row.

## 2. Waiting on the user

| id | question | lean |
|---|---|---|
| `CE-3017` | the editor (≡ CGF solo) declares `NavigationSolver` but composes no solver | add the SimHost `NavigationSolver` capability to `EditorCapabilities.BuildDefault` + `AttachNavmesh`; keep the order rails green |
| `CE-3018` | spatial grids fixed at [0,1000)² / [-750,750)² | size from `TerrainWorld.Bounds` via a holder-style generation swap; today a loud warning at commit |
| `CE-3007` | after a crash an IG creator reclaims Muscle/Perception descriptors it has no components for ⇒ nothing publishes them, other nodes keep stale samples | to be handled later (user `2026-10-03`) |
| `CE-524` | where the `NavState` write belongs | `CE-3006` composed the solver in-process (write live and correct); for the split node: the writer goes under MuscleGround and the scale-out hop reuses `PathRequestBatch`/`PathResponseBatch` (design §5.9) |
| `CE-513 (backend)` | ✅ DONE `2026-10-03` (Q80 §5) | — |
| `CE-3008` | Muscle clears the Brain's montage queue on capability loss | dormant; read the abort from the Muscle's queue state |
| `CE-3009` | animation egress translators gate on entity authority | prerequisite for composing animation replication across nodes |
| `CE-3010` | no host composes the Muscle animation pipeline ⇒ Brain montages/look-ats never play (Stride included) | compose `AnimationMuscleModule` over `StrideAnimationBackend` in editor_stride; cross-node needs `CE-3009` |
| `CE-518` (rest) | the whole `Hrot.ClusterRunner.Integration.Tests` run is order-dependent | gate by class `--filter` until someone isolates the shared state |

## 3. Tooling notes that cost time

- Multi-process cluster: `HROT_E2E_PORTS="SimHost=8102,Scenario=8101,IG=8103" python3 scripts/ownership-e2e.py …` (runbook §1.2 launch; use a non-zero `-d` domain).
- `ddsmonitor` is not preinstalled: `dotnet tool install --global cyclonedds.net.ddsmonitor`, run `~/.dotnet/tools/ddsmonitor` with `DOTNET_ROOT=/usr/local/dotnet`. A SIGINT leaves the JSON array unterminated: parse it sample by sample.
- `scripts/find.sh` reported 0/0 for a `\b…|…\b` pattern that plain grep matched in 40 files: do not trust its zero for alternations.
