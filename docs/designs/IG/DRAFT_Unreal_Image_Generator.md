<!--STATUS
state: LIVE — a DRAFT (recorded direction, not a buildable design)
updated: 2026-11-17
current-answer: the whole file
stale-below: none
known-rot: none
known-conflict: none
related-designs:
  - ../../reference/NED_DDS_Protocol.md — OWNS the wire an outside node reads and writes (its first outside reader is the DIS/HLA gateway)
  - DESIGN-IG.md — the image-generator node role (IG) inside HROT, today raylib / the 3-D map; the Unreal client is another IG
  - ../thermal/DESIGN_Thermal_Drivers.md — the external INFRARED image generator connects the same way (DDS, the drivers on the entities)
  - ../weapons/DRAFT_Weapon_Malfunctions_And_Physical_Devices.md — physical devices in the loop: another outside node on the same bus
-->

# DRAFT — Unreal Engine as an image generator next to HROT (option B) — CE-4651

🔒 User, `2026-11-16`: *"Is there any real possibility how to embed the HROT engine into an Unreal based application?"* — then, on the two options:
*"Yes, the option B looks very attractive."* · `2026-11-17`: *"we talked about Unreal Engine integration using option B, which is actually HROT engine running
in parallel to Unreal Engine plugin, and they talk over DDS. Is that recorded somewhere?"* — recorded here.

## The direction (option B)

**Unreal runs next to a background HROT and joins its cluster as an IMAGE-GENERATOR node** — just another node on the bus:

| part | how |
|---|---|
| process | the Unreal plugin starts `Hrot.ClusterRunner` (headless) as a background process, or connects to a cluster already running elsewhere |
| data in | the plugin subscribes to HROT's DDS topics (positions and rotations, damage, effects, weather, …) through CycloneDDS's native C / C++ library — the protocol is already written up for outsiders (`NED_DDS_Protocol.md`) |
| same machine | CycloneDDS can carry traffic over SHARED MEMORY on one host instead of local sockets — ⚠ NOT measured whether our CycloneDDS build has it; local sockets are fast enough for entity state either way |
| control | the existing HTTP debug API (load, play, pause, possess) or the command topics |
| what HROT changes | nothing — the IG role exists; Unreal only subscribes and draws |

**Why B, not A** (A = the .NET runtime hosted INSIDE the Unreal process via `nethost` / `hostfxr`, the way the Stride host composes a node in-process):
B keeps two processes (a crash in one does not kill the other; each debugs normally), changes nothing in HROT, fits the cluster design, and the same client
can later point at a remote cluster or join a multiplayer session. ⇒ A only for a specific need: one executable to ship, the lowest possible latency for a
first-person player, or game code that must call the simulation synchronously (and even then HROT steps on its own threads, Unreal reads snapshots).

## Open — to measure before a design

| # | question |
|---|---|
| U-1 | does our CycloneDDS build support the shared-memory transport (iceoryx), and what does it cost to enable? |
| U-2 | the minimal entity snapshot an Unreal client needs (pose, type → its Unreal asset, damage state, pose of a person, effects) — which topics, which rates |
| U-3 | the type → Unreal asset mapping: the DIS type / the TKB class chain (the same key the 3-D map's families use) |
| U-4 | the possessed player: input from Unreal back to HROT (the existing `ManualDrive` / station commands) and the latency it adds |
| U-5 | the terrain: the same height field and buildings as HROT (the terrain package export) so a body stands on the same ground on both sides |

⛔ Not approved as a build; a design comes when the user asks for one.
