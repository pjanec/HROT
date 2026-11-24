# Note from the IG client: say on the wire what made a breach (explosion or vehicle)

| | |
|---|---|
| **From** | the DPS image-generator client (IG lane), 2026-11-25 |
| **To** | ui (BuildingDamage owner); gateway lane for the wire side |
| **Context** | HROT `ui` `5c3a40aea`; gateway `ccr-458b1360-afzl8f` `155f3e3`; `DPS_BuildingDamage` as in DPS-Client-Guide "Building damage" |

## The gap

A `Breach` carries `MadeAt` and `Blast`. Per `BuildingDamage.cs` (`BlastX/Y/Z`), `Blast` is *"the detonation's world point ... for a hull's
ram or a blade, the hull's centre"*. Nothing on the record says **which of the two** it is. The same holds for a collapse: it has
`CollapsedAt` but no point and no cause.

The client draws these two very differently:

| cause | what the client does at `MadeAt` |
|---|---|
| detonation | debris thrown radially from the burst, energy from the charge; flash and smoke are already the `Effect` |
| ram / blade / push | the wall pushed in along the hull's motion, debris falls inward, dust, no flash |

Today the only way to tell them apart is to guess: look for an `Explosion` effect near `Blast` at `MadeAt`, or a ground vehicle near it.
That is a re-derivation of a decision HROT has already made (`breaches.json` row vs `ContactBreachTable` row), and it goes wrong when a
vehicle rams a wall next to a burst or the effect arrives in another frame. Per the guide's own rule ("draw it, do not re-derive"), the
cause should be on the wire.

## Ask

Per breach (in `BuildingDamage` and on `DPS_Breach`, appendable, so old clients are unaffected):

1. **`Cause`** - an enum: `Unknown` (0, also every `MadeAt == 0` breach), `Detonation`, `Ram`, `Blade`, `Push`. The names are yours to
   choose; take whatever `DESIGN_Vehicle_Breaching.md` and `ContactBreachKind` already use. What the client needs is only "explosion" vs
   "which kind of vehicle contact".
2. **`ExplosiveKg`** - for `Detonation`: the explosive mass the breach rule used (the `kg` of Z = d / cbrt(kg)), 0 otherwise. The client
   scales debris energy from it. The `Effect` size class is too coarse for this, and it is a separate event.
3. **`Actor`** - for a vehicle cause: the vehicle's entity id (0 otherwise), so the client can take its heading and speed at `MadeAt`.
   Optionally a **`Push`** direction (unit vector, sim frame) if HROT has one at hand; otherwise the client uses hull centre -> breach.

The same three for the **collapse** (`CollapseCause`, `CollapseBlast` point, `CollapseExplosiveKg`), so a client knows where a building
went down from. Today only detonations of 80 kg or more collapse a building, but once vehicles can, the client must not have to guess
there either.

Optional, would help every client: the breach's **centre and wall normal in the sim frame**. Then a client can place the damage without
rebuilding HROT's panel geometry from the terrain files.

## What stays as is

- `MadeAt == 0` (from a save): draw without animation, as now; `Cause` = `Unknown` is fine there.
- `ContactBreachEvent` stays local; its dust already arrives as an `Effect`. This ask is only about the replicated breach record.

## Reply

A line back (a NOTE to the IG client, or a reply in this file) with the names you picked and the HROT / gateway commits is enough. The
client will read the new fields when they appear and keep the guess only for older producers.
