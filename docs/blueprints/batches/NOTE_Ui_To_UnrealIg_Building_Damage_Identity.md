# Note: drawing building damage — holes, broken windows, collapse — by HROT's own ids (a proposal; your feedback first)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig`; FYI the terrain lane (the export script) and the gateway |
| **User, verbatim** | *"I would like the Unreal client be able to render building destructions and window destructions and holes. Best if we can send these as a special topics."* · *"the Unreal needs to identify the building in probably the same way as HROT is doing that, and the same for the Windows … I guess it is not optimal to look for that using world coordinates."* · *"please write a note to the Unreal about this, and we will come back with some feedback before we design the final solution."* |
| **Asks** | §4 — your feedback on the proposal; nothing is built yet |

## 1. What is already on the wire

The gateway publishes DPS **`BuildingDamage`** (DD-018 / DD-020; `SimNetGateway` `src/Protocols/SimNetGateway.Protocol.DdsPlayer/DpsTopics.cs`):
one instance per damaged building, Reliable + TransientLocal, disposed when the building has no damage.

| field | meaning |
|---|---|
| `Building` (key) | the building's terrain object label, `"<terrain>/<building label>"` |
| `Collapsed` | the whole building is down |
| `Breaches` (≤ 64) | a HOLE: `Panel` (the wall panel's index within the building), `From` / `To` (metres along the panel from its A end), `Bottom` / `Top` (absolute heights), `Kind` |
| `Windows` (≤ 256) | a hit WINDOW: `Panel`, `Opening` (the opening's index on that panel), `Hits` 1–2 = cracked (crack points `U0 V0 U1 V1`, 0–255 across the pane), 3 = broken |

`Session.TerrainExport` names the export format the keys refer to (`"hrot-terrain/1"`). Your client does not read `BuildingDamage` yet.

## 2. What blocks you from drawing it

- ✅ **Buildings**: you already match HROT's buildings BY ID (your `STATUS_Windows_Unreal_Terrain.md` 3d+: *"By id: every building built and
  matched"*) ⇒ `Building` finds the building directly; `Collapsed` is drawable today (swap for rubble).
- ⛔ **Panels and openings**: a hole and a window are addressed by **panel index** and **opening index** in HROT's own order — but the export
  (`scripts/terrain-to-unreal.py`) carries footprints, heights and materials, **no wall panels and no openings**; your facades generate their
  windows procedurally, so they are not HROT's windows and have no HROT ids (your draft also notes Stechovice missing opened windows).

## 3. The proposal (our lean — identity, never a search by world coordinates)

1. **The terrain export carries HROT's panels and openings, in HROT's order, with HROT's indices** — per building: each wall panel (its two
   ends, base and top, material) and on each panel its openings (window or door, position along the panel, sill and head heights), as HROT's
   `*.building.json` already holds them. ⇒ you build the windows (and doors) where HROT has them, keyed `building + panel + opening`.
2. **`BuildingDamage` stays as it is** — a broken window = `building + panel + opening` (a direct lookup); a hole = `building + panel + span`
   (cut into a panel you now know); a collapse = the building key. No world coordinates.
3. **Craters — a new `GroundDamage` topic** (centre, radius, depth; one instance per crater): a crater is not part of any object, so its
   position IS its identity. (Felled trees: later, when a scenario fells them.)

## 4. Questions for you

- Can your facade builder place its windows / doors at given positions on given panels (instead of, or in addition to, its procedural
  pattern)? What does it need per opening (size, frame, kind)?
- Do your buildings' walls map to HROT's panels one to one (HROT's panels follow the footprint's edges, split where the generator split
  them), or do you need the panel geometry itself in the export?
- A hole: is a cut into a known panel (span + heights) enough for Chaos / a mesh swap, or do you want a precomputed quad?
- Anything else in `BuildingDamage` you would want (e.g. a breach's direction, the crack points' meaning)?

We design the final form after your reply.
