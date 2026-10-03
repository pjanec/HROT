<!--STATUS
state: LIVE
updated: 2026-10-03
current-answer: §2 — the terrain-world design (CE-3006 row) waits on the user's nod to §7.2 W1/W9/W10/W11 of docs/DESIGN_Terrain_World.md; nothing is building.
stale-below: nothing yet
related-designs:
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the programme's owning design (push-only ownership, S1–S8, §5.7.1 live matrix, §5.9/§5.10 deferred designs).
  - docs/DESIGN_Subsystem_Composition_Unification.md — B5 (not started) gates CE-3006 / CE-524 / CE-513.
-->
# RESUME — backend lane

⚠ A STATE doc: verify every line against git before acting on it.

## 1. Where it stands (`2026-10-02`)

- Branch **`backend`**. Last batch: [`HANDOFF_Ownership_Remaining_Work.md`](batches/HANDOFF_Ownership_Remaining_Work.md) → report [`REPORT_Ownership_Remaining_Work.md`](batches/REPORT_Ownership_Remaining_Work.md).
- Ownership programme: S1–S8 built; live matrix §5.7.1 **E1–E8 all ✅** (E8 = the multi-process crash reclaim, +10.2 s, no message).
- Done this batch: `CE-3003` (debug writes ask the owner), `CE-3004` (CGF polls mission acks), `CE-516` (editor uses the injected offline factory), `CE-518`'s 11 unit reds (all stale tests).
- Backend id block `CE-3000`–`CE-3999`, **next free `CE-3016`**. Every `behaviors` merge conflicts on the id-block table: keep their behaviors row and our backend row.

## 2. Waiting on the user

| id | question | lean |
|---|---|---|
| `CE-3006` | no host composes `NavigationSolverModule` ⇒ path requests are never answered, vehicles steer `Direct` | ⭐ DESIGNED `2026-10-03`: [`Q81`](Architect_Question_81_SimHost_Test_Terrain_World.md) approved (`R-181`); [`DESIGN_Terrain_World.md`](../DESIGN_Terrain_World.md) §7.1 W2–W8 RULED (`R-182`); W1/W9/W10/W11 await the nod; slice = steps 0/1a/1b/2/3 (§8). Found on the way: `CE-3011` (Y-up nav APIs), `CE-3012` (editor never loads terrain), `CE-3013` (LIVE route axis swap on SimHost), `CE-3014` (area fill never drawn) |
| `CE-3007` | after a crash an IG creator reclaims Muscle/Perception descriptors it has no components for ⇒ nothing publishes them, other nodes keep stale samples | to be handled later (user `2026-10-03`) |
| `CE-524` | where the `NavState` write belongs | wait for `CE-3006`; then the writer goes under MuscleGround and the scale-out hop reuses `PathRequestBatch`/`PathResponseBatch` (design §5.9) |
| `CE-513 (backend)` | ✅ DONE `2026-10-03` (Q80 §5) | — |
| `CE-3008` | Muscle clears the Brain's montage queue on capability loss | dormant; read the abort from the Muscle's queue state |
| `CE-3009` | animation egress translators gate on entity authority | prerequisite for composing animation replication across nodes |
| `CE-3010` | no host composes the Muscle animation pipeline ⇒ Brain montages/look-ats never play (Stride included) | compose `AnimationMuscleModule` over `StrideAnimationBackend` in editor_stride; cross-node needs `CE-3009` |
| `CE-518` (rest) | the whole `Hrot.ClusterRunner.Integration.Tests` run is order-dependent | gate by class `--filter` until someone isolates the shared state |

## 3. Tooling notes that cost time

- Multi-process cluster: `HROT_E2E_PORTS="SimHost=8102,Scenario=8101,IG=8103" python3 scripts/ownership-e2e.py …` (runbook §1.2 launch; use a non-zero `-d` domain).
- `ddsmonitor` is not preinstalled: `dotnet tool install --global cyclonedds.net.ddsmonitor`, run `~/.dotnet/tools/ddsmonitor` with `DOTNET_ROOT=/usr/local/dotnet`. A SIGINT leaves the JSON array unterminated: parse it sample by sample.
- `scripts/find.sh` reported 0/0 for a `\b…|…\b` pattern that plain grep matched in 40 files: do not trust its zero for alternations.
