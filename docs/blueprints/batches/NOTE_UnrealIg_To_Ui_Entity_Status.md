# Note: the Unreal client draws its own entity cards — it needs a small per-entity STATUS, sent on change

| | |
|---|---|
| **From** | `unreal-ig` (the Unreal image generator), 2026-11-22 |
| **For** | the HROT lane (`ui`: the card projectors, CE-11031; the gateway extension). FYI the SimNetGateway session (the topic belongs in its IDL) |
| **User, verbatim** | *"we can leave the HROT labels via Gizmo, but we do not need to subscribe to that layer, so it stays optional. And let's write a note to HROT lane about the extra status we will need and send it on change."* |
| **Asks** | one new keyed topic, `EntityStatus`, written by HROT **on change only**, **once on the world partition** (not per seat) |

## 1. Why

- A card's content — name, health, NATO symbol, side — is a fact about the entity, not something only the engine can see. Gizmos stay for what
  the client cannot derive (paths, contacts, fire traces, debug rows).
- The look is the client's (R-144 already gave it the cutoffs): depth, LOD, its own font and layout. The DD-024 card gizmos carry HROT's ImGui
  layout (pixel rows, fractions), so a layout change in HROT ripples into every client.
- The MEANING stays in HROT: how health is computed, which symbol a unit has, who is a commander — HROT sends the values, the client only lays
  them out (HROT's `EntityCardRenderer.Layout` / `SymbolGeometry` are already ported to the client, `HrotCards.cpp`).
- **DD-024 stays.** The client keeps the adapter; it just does not subscribe to `UnitCards` / `UnitSymbols` / `Labels` unless started with
  `-HrotIgWireCards`. Debug rows hung on a card (`GizmoSpace::EntityCard`) remain a good use of it.

## 2. What the wire has today (gateway `docs/clients/idl/DpsTopics.idl`)

| card field | today |
|---|---|
| name, side, domain, parent, `IsUnit` | ✅ `Entity` |
| health | ⚠ only the coarse `PartCondition Damage` in `EntityState`; a fractional `Health` only in `OwnUnit` (the player's own) |
| NATO symbol | ⛔ only as an Icon gizmo's text (DD-024) |
| commander (R-143 mode 2: "commanders + aircraft") | ⛔ none — mode 2 shows aircraft only today |
| current behaviour, stress | ⛔ none |

## 3. The ask — `EntityStatus` (a proposal; the field names are yours / the gateway's to settle)

```idl
@topic @appendable
struct EntityStatus {
    @key int32 EntityId;
    string<40> Symbol;       // HROT SymbolCode wire form: 2525C SIDC + "|mobility|mark" (e.g. "SFGPUCIZ---D---|T|AT"); "" = no symbol
    float      Health;       // 0..1, the fraction HROT's HealthBarGizmo draws; -1 = none (no bar)
    boolean    IsCommander;  // R-143 mode 2
    string<32> Behaviour;    // the behaviour HROT shows as "currently executing" (your choice of which slot / leaf); "" = none
    float      Stress;       // 0..1; -1 = none
    string<32> Task;         // optional, only if cheap: the current order / task, as HROT names it
};
```

| ⭐ rule | |
|---|---|
| **on change only** | a sample when a field changes; floats quantised (a change ≥ 0.02, or the 1/100 step) so a slow drift does not write every tick |
| **once, on the world partition** (`<base>-*`, like `Entity`) | ⛔ not per seat — every seated client reads the same sample |
| **reliable, KEEP_LAST 1, TRANSIENT_LOCAL** (as `Entity`) | a client joining late gets the last value without waiting for a change |
| **every entity that HROT would give a card** | units, vehicles, aircraft; ⛔ none for rounds / animals / seated men (HROT `Seeable.OnMap`, the client already skips them) |
| **disposed with the entity** | |

⚠ Not measured by me (your side decides): which behaviour name is the right "currently executing" one, where stress lives, and whether an
aggregate unit's health is its strength. Say what you pick and the client follows.

## 4. The client's side (done / to do)

- ✅ `unreal-ig`: the DD-024 card layers are not subscribed by default (`UHrotDpsWorld::bWireCards`, `-HrotIgWireCards` to opt in); the cards are
  built from entity data (`HrotDpsCards.cpp`).
- ⏳ when `EntityStatus` lands: the symbol cell, the health bar from `Health`, mode 2 from `IsCommander`, and optional rows for behaviour / stress.
