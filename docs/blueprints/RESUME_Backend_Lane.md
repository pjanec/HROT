<!--STATUS
state: LIVE
updated: 2026-10-10
current-answer: ⭐ §00 — NOW (2026-10-10): the BEHAVIOUR LIBRARY programme (review R-258/R-256, squad-aware PeekAndFire R-257, CE-3158 ready to build). §0 (2026-10-06 utility demos) and §1 below are HISTORY for this lane's current work.
stale-below: §0 (2026-10-06) and §1 (2026-10-03) — history for this lane's current work
related-designs:
  - docs/REVIEW_Behaviour_Library_Genericity.md — the CURRENT programme's verdict + decisions (§5) and retreat concepts (§6).
  - docs/DESIGN_Peek_And_Fire.md §10 — the approved generalisation (G1–G8, R-257) and §10.7 CoverClaim; CE-3158 builds it.
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the programme's owning design (push-only ownership, S1–S8, §5.7.1 live matrix, §5.9/§5.10 deferred designs).
  - docs/DESIGN_Subsystem_Composition_Unification.md — B5 (not started) gates CE-3006 / CE-524 / CE-513.
-->
# RESUME — backend lane

⚠ A STATE doc: verify every line against git before acting on it.

## 00. NOW (`2026-10-10`) — the behaviour LIBRARY: generic, squad-aware *(this session is the ACTIVE backend session; 🔒 user: "the other one is on hold")*

| what | where | state |
|---|---|---|
| **the review** — is the behaviour library generic? inventory (88 node classes), overlap clusters, PeekAndFire audit | [`REVIEW_Behaviour_Library_Genericity.md`](../REVIEW_Behaviour_Library_Genericity.md) §1–§6 | ✅ decided: **R-256** (L1–L4 + L5 accepted) · **R-258** (the library is BTREES — C#-built allowed; blueprint/HSM copies go *only where redundant and not the best host*; the three-host DEMO stays, on the library implementation) |
| **PeekAndFire generalised, squad-aware** — G1 peek from the point · G2 honest Failure · G3 friendly line + ROE on bursts · G4 locked target · G5 `CoverClaim` · G6 `MayExpose` seam · G7 `SimRng` fix · G8 infantry scope | [`DESIGN_Peek_And_Fire.md`](../DESIGN_Peek_And_Fire.md) §10 (+ §10.7 CoverClaim) | ✅ **APPROVED R-257** · **`CE-3158` READY-TO-BUILD** (slices G-1…G-6, each red-proved) · ⛔ **user said "not implementing yet"** before approving — build only when told |
| the universal-soldier demo | `scenarios/ua-universal-soldier` · rail `PostureScenarioTests.CE3094_*` · [`TUTORIAL_Universal_Soldier.md`](../TUTORIAL_Universal_Soldier.md) | rail LOCKED (hidden >4000 f, leg 2, his rounds reach SimHost, a hostile hurt, defensive once wounded, ≥2 m moved); he still dies in cover ⇒ `CE-3157` (closed by CE-3158 slice G-6) |
| done this run | `CE-3092` (mobility kill = TKB opt-in) · `CE-3095` (fire events Reliable/KeepAll) · AQ85 hit chance (built by the other session, R-216) | ✅ |
| synced with ui | merged `ui@7648a2ff6` at `3ac4baefc` (world-query seam CE-1035, terrain height CE-1034, 3-D map). ⚠ ui took R-248…R-255 ⇒ our library ruling is **R-258**. No CE-3158 file touched; the ui rules binding it (G3 friendly test joins `Trace(Fire)` with WQ-F · `ClaimRadius` 3-D · `CoverClaim` in the shared registry · brain names the target only) are in [`DESIGN_Peek_And_Fire.md`](../DESIGN_Peek_And_Fire.md) §10.8 | ✅ |
| open findings | `CE-3097` units move at ~⅓ of `Speed` (unexplained) · the `DefendAreaMapper` routes to tree-less `InfantryCombat`/`ConvoyEscort` (L4 — file + measure first) · `Hrot.NED.Tests` does not compile (pre-existing, unrelated) | ⏳ |

**Waiting on the user:** L7 lean — ONE retreat = `FallBack`, `FleeExecutor` → legacy (review §6).
**Work order when told to build:** L1 (library list + promotion rule in the Overview) → retire redundant blueprint/HSM library copies (corpus check per item) → **CE-3158** G-1…G-6 → L4 registry / L5 `FireAtTarget` routing.

**Traps met this run:** the graph goes stale after a big merge — re-index with the CLI (`codebase-memory-mcp cli index_repository --repo_path <root> --mode moderate`; the MCP call times out at 60 s) · two cluster test processes started together share a DDS domain and corrupt each other (a rifleman "dead at f0") · a `;` inside a Mermaid sequence `Note` breaks the parse · ruling ids collide across lanes — fetch `ui`/`behaviors` RULINGS before allocating (`ui` took R-254 while this lane held it; ours became R-256).

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
