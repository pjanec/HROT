<!--STATUS
state: LIVE
updated: 2026-11-06
current-answer: the whole file (a batch report)
stale-below: none
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Entity_Control.md §8r.2 — CE-1299 and CE-2317 as built
  - docs/DESIGN_Gizmo_Anchor_Identity.md §6.7a — CE-1300 as built
-->
# REPORT — ui → coordinator: possessed mounted guns *(answers the handoff, dispatched at `22b255f3b`; merged `coordinator@7652b2c33`)*

| item | state | evidence | commits |
|---|---|---|---|
| ① CE-2317 a possessed ZU-23 never fires | ✅ not reproduced on `ui` today + the HUD names a hold | 📐 live (editor, `test-move` preview, possessed `Technical_Zu23` / `Technical_Hmg`, trigger held at a truck 120 m off, 240 frames, slot 32): **ZU-23 24 rounds, M2 35, the truck dead**. Likeliest difference from `2026-10-20`: CE-1206 (a possessed host's station needs no gunner) was not on `ui` then. ⭐ HUD `WEAPON HOLD: <reason>` from `ActionStatus` (rail `PossessionStationTests.CE2317_*`) | `7e912d8b8` |
| ② CE-1299 HMG rounds at the feet | ✅ fixed — CE-4143 had NOT fixed it | a fixed 300 m point on the look ray, from a muzzle below the eye, passes (eye − muzzle)·(1 − r/300) under the crosshair (~1.8 m at 30 m). `OperatorFireModes.SightPoint` zeroes at the crosshair's own distance (< 300 m), still on the ray, no lock; far terrain keeps 300 m (CE-4143's lead). Rail `EmbarkNodesTests.CE1299_*`. ⚠ **live check not done** — the HTTP control frame has no look ray | `7e912d8b8` |
| ③ CE-1300 gun / gunner selectable apart | ✅ (worker) | `NetworkIdResolver.ResolveAnchorId` → `CarrierOf` via `IsEmbarkedTag`: every pick consumer (selection, drag, menu, mission picks) gets the carrier by construction; rails `SelectionInteractionSystemTests.CE1300_*` ×4 + `GizmosSystemTests` drag rail. ⚠ in-editor click/drag not checked live | `0ce6c6d59` |

**Cross-lane edits, said:** `OperatorControlBehavior.cs` (behaviors) · `WeaponBrainProfiles.cs`, `NetworkIdResolver.cs` (FDP toolkits).
**Found on the way:** CE-1464 (ui) — HTTP control frames lose to a windowed editor's own possession station ("newest seq wins"), so an
HTTP-possessed unit never fires there; and the route carries no look ray.

## Gates

| gate | result |
|---|---|
| `Hrot.SimHost.Tests` `EmbarkNodesTests` (built, `--no-build`) | 26 / 0 |
| `Hrot.Presentation.Tests` `PossessionStationTests` | 65 / 0 |
| CE-1300: `Fdp.Toolkits.Tests` Gizmos 206 / 0 · `Hrot.Presentation.Tests` selection + gizmo classes under `xvfb-run -a` 54 / 0 | worker |
| `Hrot.Network.NED.Tests` `GizmoInteraction` | 20 / 0 — ⚠ first a build break (`DdsTopicName`): the restore trap after the CycloneDDS 1.2 merge (`f6b97d151`), not a code red; `dotnet restore` fixed it |
| red-proofs | CE-1299, CE-2317, CE-1300 — each a compiling mutant, red, restored, green |
| `tracker-counts.py --check` | OK |

Ids allocated: **CE-1464**.
