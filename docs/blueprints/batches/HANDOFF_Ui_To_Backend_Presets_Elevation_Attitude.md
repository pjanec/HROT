<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5 / §6 W-I own the seat presets (StartWith, SeatPresets); §7 W-J (R-1001) withdraws the editor placement; P1 writes there
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — owns the fire solution (IF-1) whose elevation P2 puts on the wire
  - docs/DESIGN_Weapon_Layers.md — W-9 owns the turret part's TurretPose (azimuth, elevation) P2 reuses; W-13 owns the weapon posture P4 grows
  - docs/DESIGN_Dead_Reckoning.md — owns the replicated transform P3 completes (pitch and roll)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — the attitude (bank, pitch) the aircraft models write and P3 carries
  - docs/DESIGN_Map_3D_Mode.md — §6n / §8f (Entity Control) are the ui drawings waiting on P2–P4
-->

# HANDOFF — backend: weapon presets for armed vehicles, the laid gun elevation, pitch and roll on the wire, the active weapon

**For:** the backend lane (`backend`). **From:** the ui lane (`ui`). ⭐ **Dispatched at the commit that adds this file**: your scope is FROZEN
there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do every item that is
not blocked (R-106).

⭐ **This is a FRAME** (lane protocol): the goal, the fences, the decisions with leans, the acceptance. **Step 1 per item is yours: write the
classes and the sequence into the owning design named in its row** (diagram first), then build, then fold the as-built.

## 0. Why

🔒 **User, `2026-10-15` (R-1001):** *"Weapons in vehicles should come from tkb defined presets, no need to edit in the editor, but the presets
need to be created - backend. Gun elevation also needs backend."* The ui lane draws all of these already and is waiting on the data:

| what the map draws today | what it lacks | measured |
|---|---|---|
| a weapon in a host's station, its gunner at the grips (§6n N-2, N-3) | the hosts that should carry one do not: the HMMWV has passenger seats only; the bare pickup 106 has an empty station | `BdcTkbCatalog.cs` — `WithCarrier(Truck_HMMWV, PassengerSeats(3))`; only 104 / 105 / 404 carry `StartWith` |
| a mortar / howitzer / M109 tube LAID at its elevation (§6n N-5) | the elevation is not on the wire: the map infers it from the last round's velocity, so before the first round (and on a receiving node until one lands) it shows a default 60° / 25° | IF-1's only wire change is `WeaponFireRequest.Charge` / `HighArc` (`REPORT_Backend_Indirect_Fire.md`) |
| an aircraft banking and pitching; the pilot's view banks with the body (§8f) | ingress restores the HEADING only — a plane on a receiving node flies level | `GeoSpatialIngressTranslator.cs:73` / `:148` — `HeadingDegToRotation(data.Ori.Heading)` |
| a soldier's launcher on his shoulder (§6n N-4) | which weapon he is about to fire — the map can show the tube only AFTER the first shot | no active-mount field on `WeaponPosture` / anywhere in `Fdp.Toolkits/Combat` |

## 1. Before anything

1. Merge `ui` at the dispatch commit or later (it carries `behaviors` at `2a2b16f92`). Push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-3xxx`); the ui row tracking this handoff is **CE-1102** — append your progress there as dated lines.

## 2. The items

| # | item | lean | design (step 1 writes here) | acceptance |
|---|---|---|---|---|
| **P1** | **weapon presets for every armed vehicle variant** (W-I `StartWith`, no new mechanism): ① the **HMMWV** gains a roof ring **Station** (`Accepts = Pintle`) preset with an **M2** (600) and its Gunner seat preset with a rifleman; ② **technical variants** on the pickup body beside 104 / 105: a **DShK** (601) one and an **M134** (602) one; ③ every NEW preset type a picker entry (it is automatic — the catalogue lists every built-in type) | ⭐ ① changes **102 itself** (a patrol HMMWV carries its M2; a bare one is a new type only if a scenario needs it — say so if you think otherwise); ② new type ids beside 104–107; ⛔ 106 stays the bare body | Weapon Systems §5 (the catalogue) + §9 as-built | the catalogue suites; ★ a placed 102 holds an M2 in its ring station and a gunner, and a save / load adds neither again (the 104 rail's twin); the new technicals fire their station weapon (`V8_AnHmg_…` pattern) |
| **P2** | **the laid elevation (and azimuth) of a ground weapon on the wire** — mortars 609–611, the towed howitzer 612, the M109 107 — from the moment it LAYS for a fire mission (the IF-1 solution's elevation), not only after a round flies | ⭐ **reuse W-9's `TurretPose`** (the turret part's azimuth + elevation, already replicated and already read by the map through `TurretPoseSource.PoseOf`): the weapon muscle writes the solution's azimuth / elevation into the weapon's turret part when it lays. ⛔ No second elevation field. If a towed gun / mortar has no turret part, say whether adding one is cheaper than a field | Indirect Fire §3 / §8 (as-built) + Weapon Layers W-9 | ★ on a RECEIVING node, a mortar laid for a mission reports `TurretPose.Elevation` = the solution's elevation (± 0.5°) BEFORE its first round; an M109's turret turns to the target before it fires |
| **P3** | **pitch and roll on the wire** for every replicated transform (aircraft above all; also a vehicle on a slope) | ⭐ the descriptor already has an orientation struct (`Ori`): egress fills pitch and roll, ingress builds the rotation from heading + pitch + roll (one helper beside `HeadingDegToRotation`); dead reckoning keeps extrapolating position only | Dead Reckoning (the transform) + Air Operations §5b / §6a (who writes the attitude) | ★ a banking UH-60 / A-10 on node B shows its roll and pitch within 1°; a ground vehicle's existing rails stay green (heading-only ingress is a subset) |
| **P4** | **the weapon a soldier is about to fire** — the mount the weapon brain has selected — replicated with his posture | ⭐ an APPENDED byte `ActiveMount` on `WeaponPosture` (W-13, R-272: the posture already crosses the wire; append per R-169) | Weapon Layers W-13 | ★ a soldier aimed with his launcher selected reports `ActiveMount` = the launcher's mount on every node; the rifle selected ⇒ the rifle's |

## 3. Fences

- ⛔ **No editor UI for stations** (R-1001) — presets are data. The ui lane will not build placement either.
- ⛔ **No new replication path** where one exists: P2 reuses `TurretPose`, P3 the existing `Ori`, P4 the existing posture descriptor. A field you append is APPENDED (R-169), and every number you invent is a REPORT line.
- ⚠ P3 touches every replicated body: name the integration suite that proves nodes still agree (gate-report row 8).
- ⛔ The drawings are ours: after your merge the ui lane switches the map from inference to the replicated values (§6n N-4 / N-5, §8f) and re-shoots its frame rails.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated. Each owning design
folded to the as-built. Then the sha of each item on `backend`.
