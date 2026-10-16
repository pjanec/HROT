<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file (a frame — the design carries the substance)
stale-below: nothing
related-designs:
  - docs/DESIGN_Entity_Control.md — §8h, the owning design (U-1..U-7, O-1..O-6, items §8h.4, the verdict path §8h.6); §8e the frame counters; §8d the station HUD
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Backend.md — the frame fields, the rule, the verdict component, Riding (this builds on them)
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Behaviors.md — the brain's half (doors, bodies, weapons, driving)
-->

# Handoff: the FPS use key, ui half (`CE-1158`)

**Dispatched at `a1a909bde` (branch `ui`).** 🔒 User, `2026-10-16`: *"pls records into items and write handoff to the backend node and
behavior node to implement"* … *"and for your lane of course as well"*. **Your scope is FROZEN at `a1a909bde`.** Documents that change
after it are FYI only; if a later document invalidates an item, stop and report it. ⛔ Do not adapt to it.

**First step:** merge `backend` once it has pushed CE-1151 / CE-1152 / CE-1153 / CE-1156, and `behaviors` once it has pushed CE-1154
(CE-1155 / CE-1157 for the weapon and driving parts). Both lanes name their sha in their reports. Then push a
`chore: started use-key ui at <sha>` commit (rule 1b).

⚠ **Dependency:** every part of CE-1158 needs the backend fields and rule. The parts that can start before backend lands, against a local
stub: the hold-`E` list drawing, the controlled-mount count, and the help line.

## What it is

In FPS mode, `E` acts on what the crosshair rests on. The station counts presses, sends verb and target in the control frame, draws the
prompt from the shared rule on its own copy of the world, shows the brain's verdict, and swaps possession between soldier and vehicle on
Drive / Get out. Read [`DESIGN_Entity_Control.md`](../../DESIGN_Entity_Control.md) §8h end to end, especially the driving sequence (the
station orchestrates it) and §8h.6 (prediction + verdict).

## The item: build against §8h's diagrams, report any deviation, and fold it back into §8h (obligation ⑤)

| part | build | where (measured at `a1a909bde`) | design |
|---|---|---|---|
| **① the press** | `E` pressed ⇒ `_useSeq++` with `UseVerb` = 0 and `UseTarget` = `AimTarget`, the same counter pattern as `_shotSeq` / `_ammoSeq`. `E` is unused today. | `FpsCameraDriver.HandleInput` (`PossessionStation.cs:484-528`); frame built at `:556` | §8h U-1, U-2; §8e D-1 |
| **② hold-`E`** | Holding `E` past ~0.3 s shows a short list of the other verbs by the crosshair (O-1: a list, not a radial). Mouse wheel or 1–4 picks one, and releasing `E` sends it as `UseVerb`. A tap still sends the primary verb. ⚠ 1–4 are the weapon keys, so they pick a verb only while the list is open. | `FpsCameraDriver`, HUD drawing (`PossessionStation.cs:137-147`, `Hud`) | §8h U-1, O-1 |
| **③ the prompt** | `OperatorUseActions.For(world, unit, AimTarget)` on the LOCAL world gives the primary verb's label, e.g. *E Open door · E Drag · E Drive BMP · E Man ZU-23*. It is greyed with its reason when unavailable (too far, locked, not down, seat taken). | `Hud.Lines` | §8h U-3, §8h.6 ① |
| **④ the verdict** | `OperatorUseStatus` on the held unit. When its `UseSeq` equals our last press, show one line for ~2 s: done, or refused + reason. Running shows nothing extra. | `Hud.Lines` / `Hud.ColourOf` | §8h.6 ② |
| **⑤ controlled mounts** | The weapon count and keys 1–4 include the weapons the man controls from a Gunner seat (`WeaponSystems.Controlled` / `ControlledAt`, index 32+k), so a manned ZU-23 is reachable. Today `MountCount` counts only the man's own `WeaponAmmo`. | `FpsCameraDriver.MountCount` (`PossessionStation.cs:589-591`), `Mount` sent at `:510` | §8h U-5; module-graph red box GAP1 |
| **⑥ the hand-over** | Drive: when the verdict is Done and the replicated passenger list shows the soldier in the Driver/Pilot seat, possess the soldier as **Riding**, then possess the vehicle as Fps, and remember the rider. Get out (`E` in a vehicle with a rider): possess the rider as Fps, release the vehicle, then send a frame with the Get-out verb. Escape in a vehicle with a rider = Get out, not release. | `PossessionStation.Update` / `Enter` / `Leave` (`:60-100`) | §8h U-4, driving sequence |
| **⑦ orders** | `EntityOrders.Possess(unit, mode = Fps)`. A new **Fps** possession releases this station's other Fps unit (GAP2: today a second `Possess` leaves two held). `HeldHere` skips Riding units, and when two Fps units are held it takes the newest (`PossessedBy.Epoch`). | `EntityOrders.cs:180-185`; `PossessionStation.HeldHere` (`:68-81`) | §8h module-graph GAP2 |
| **⑧ help** | Add `E` use and hold-`E` more to the FPS controls help (CE-1143). | the FPS help lines | §8h U-1 |

## Key decisions, made with the user. Do not reopen them.

- ⭐ **The station never sends door or embark commands itself.** It only sends the press; the brain decides (rejected in §8h.3, because it
  would skip the executors' reach, timing and the counters' loss-proofing).
- ⭐ **The station alone swaps possession**, and only after the boarding shows in replicated state. The soldier and the vehicle may be
  brained on different nodes.
- ⭐ **The prompt comes from the same rule the brain uses.** No station-side copy of "what can I do here".
- ⭐ One human drives and shoots a vehicle; crew stays abstract (B5). A second human who boards becomes a passenger (O-4).

## Fences

- ⛔ Don't change the frame or wire fields, `OperatorUseActions`, `OperatorUseStatus` or `PossessionMode`: they are backend's
  (CE-1151…1153, CE-1156). Don't change the operator behaviour: it is behaviors'. Report anything missing to the owning lane.
- ⚠ **Every map host:** the station runs on the Editor (networkless, one world), CGF, SimHost and IG. Check the verdict line on an IG
  (CE-1139 found shared data that never reached it).

## Acceptance

- [ ] Rails in `PossessionStationTests`:
  - an `E` press bumps `UseSeq` once per press and carries `AimTarget`;
  - hold-`E` sends the chosen verb;
  - the prompt label matches `OperatorUseActions` for a door, a man down, a vehicle and a weapon, greyed with its reason;
  - the verdict line appears only for our own `UseSeq`;
  - `MountCount` includes a controlled mount;
  - Drive and Get out end with the right unit held (Riding / Fps);
  - a second Fps `Possess` leaves one held.
- [ ] Frame rails (Xvfb `:99`): screenshots of the prompt, the greyed prompt, the hold-`E` list and the verdict line, saved under
      `docs/assets/map-3d/`.
- [ ] Live on a cluster (runbook): open a door, drag a body, man a ZU-23 and fire it, drive a BMP and get out — from a CGF station and an IG
      station.
- [ ] Red-proof each new rail.
- [ ] Gate report per CLAUDE.md "THE GATE REPORT CONTRACT", including **row 8** (`EntityOrdersAcrossNodesTests`).
- [ ] §8h folded with the as-built, CE-1144 and CE-1158 moved, ids stated.
