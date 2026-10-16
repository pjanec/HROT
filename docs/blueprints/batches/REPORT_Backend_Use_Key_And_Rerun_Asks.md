<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file — the backend report for the use key (CE-1151, CE-1152, CE-1153, CE-1156) and the re-run handoff's asks.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Control.md — §8h.7 holds the use key's as-built (this report only points there)
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Backend.md — the handoff this answers
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Rerun_Results_And_Cross_Lane_Fixes.md — the re-run handoff (asks A, B; review of CE-2224/2225/2226)
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7, the civilian detailed designs started in the same run (design only)
-->

# Report — backend: the use key's engine half, and the re-run asks

Branch **`backend`**, started at `158344280` (the started marker, on `7d1a8277f`). Nothing from `ui` / `behaviors` merged
during the run.

## 1. Use key (`CE-1151`, `CE-1152`, `CE-1153`, `CE-1156`) — built

As-built and deviations: [`DESIGN_Entity_Control.md` §8h.7](../../DESIGN_Entity_Control.md). In short:

| item | built |
|---|---|
| `CE-1151` | `UseSeq`, `UseVerb`, `UseTarget` appended to the frame, the input, the wire payload; both transport directions; `PossessionSystem`; the Debug API body (`use` / `useVerb` / `useTarget` on `control_entity`) |
| `CE-1152` | `OperatorUseActions.For(world, actor, target)` + `WriteParams` (`Fdp.Toolkits/Behavior/Possession/OperatorUse.cs`); `Embarkation.CanEmbark` shares `TryEmbark`'s one `Check` |
| `CE-1153` | `OperatorUseStatus` (component 396, Brain group), `dtOperatorUseStatus` = 141 (free on every lane at the time), egress/ingress translators with CE-4007's delete-on-wipe, wired in both packs |
| `CE-1156` | `PossessionMode.Riding = 2`; a riding unit takes no frames |

Deviations (all in §8h.7): the vehicle reach is a constant (no `PhysicsCollider` on CGF); `Drive`/`Pilot` needs THE driver
seat — found by its own rail: `FreeSeat(Driver)` falls back to a passenger seat, so `CanEmbark(Driver)` said `Applied`
after the driver sat.

⚠ **Owed:** the live IG check of `dtOperatorUseStatus` (handoff "It must reach the IG"). Not run this pass.

⭐ **Behaviors lane:** `CE-1154` (`OperatorControlBehavior.OperateUse`) starts from the commit that carries this report.

## 2. Re-run handoff

| | |
|---|---|
| review `CE-2224` / `CE-2225` / `CE-2226` | ✅ all three correct: `KeepHandles` reads the previous answer before the overwrite in both writers; the loader's range is the loader's; the `HoldUnit` remap uses the load map |
| ask A (`HoldUnit` as an `EntityRef`) | ⭐ agreed, deferred to the next route-wire change (no tracker row: it rides that change) |
| ask B (recordings fill the disk) | ✅ `CE-3218`: `ExerciseRetention` prunes the oldest exercises at runner start-up, ON by default only on a scratch temp root (keep 20); `--keep-exercises N` / `FDP_KEEP_EXERCISES` elsewhere — recordings are user data (file-sync-1) |

## 3. Also fixed on the way

- `get_render_diagnostics` (ui `CE-1145`) had a route doc but no MCP handler, no example and no catalog entry: handler,
  example, expected list and the regenerated catalog / `SKILL.md` added (cross-lane: the route doc entry is ui's).

## 4. Gates

Base: `158344280`. Each test project built before its `--no-build` run.

| # | command | result | `--no-build` |
|---|---|---|---|
| 1 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter OperatorUseActions\|Embark\|PersonCarrier\|ExerciseRetention\|WeaponMount\|Door` | 128 / 0 / 0 (after the Drive fix; 127/1 before it — that red is the Drive deviation) | yes |
| 2 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --filter PossessionReplication\|InteractionTransport\|TheDescriptorMapIsWired\|EmbarkNodes\|CgfLogicPack` | 58 / 0 / 0 | yes |
| 3 | `dotnet test Hrot/Network/Hrot.Network.NED.Tests` | 146 / 0 / 0 | yes |
| 4 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --filter EveryRouteIsDocumented` | 4 / 0 / 0 | yes |
| 5 | `node tools/ai-debug-mcp/test-catalog.mjs` | 1017 / 0 | — |
| 6 | red-proof: Riding skip, transport copy, status apply reverted ⇒ `CE1156_*`, `CE1153_*`, `InteractionTransportTests` | 3 red / 5 green; restored 8 / 0 (§4a) | yes |
| 7 | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | open 106 / done 386 · 255/255 (5 source-changed warnings, pre-existing) · OK | — |
| 8 | **row 8** `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter EntityOrdersAcrossNodes` | 4 / 0 / 0 | yes |

Ids allocated: `CE-3218`. Ids closed: `CE-1151`, `CE-1152`, `CE-1153`, `CE-1156` (ui ids, owned by backend). No golden moved;
no new skip.

### 4a. Red-proof
With the Riding skip removed (`Possession.cs`), the ingress copy of the three fields removed (`InteractionTransport.cs`) and
the status `AddComponent` turned into a discard (`EntityOperatorUseStatusTranslators.cs`), one build: **3 failed / 5 passed** —
`CE1153_OperatorUseStatus_TheBrainsVerdict_ReachesTheStation`, `CE3172_PossessControlRelease_…` (the three fields) and
`CE1156_ARidingUnit_StaysPaused_…` each red. Restored: 8 / 0. `CE-1152`'s Drive rail was red before its fix (gate row 1).
⚠ A first attempt guarded the apply with `if (false)`, which the warnings-as-errors build refused (CS0162) — its "8 passed"
was a stale binary and is not counted.
