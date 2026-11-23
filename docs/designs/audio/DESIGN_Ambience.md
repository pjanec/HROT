<!--STATUS
state: LIVE
updated: 2026-11-22
build-state: BUILT — slice 1 (CE-11034, ui, 2026-11-22; as built §6) · slices 2 and 3 READY-TO-BUILD (R-11014 approved by the user, 2026-11-22: "approved, write the design and reply to Unreal")
current-answer: the whole file — §1 the model, §2 the diagrams, §3 the kinds, §4 decisions, §5 slices, §6 AS BUILT (slice 1: where it deviates from §1–§2)
stale-below: nothing (§2's module diagram said the expiry system's pack was "chosen at build" — §6 names it)
known-rot: none
known-conflict: none
related-designs:
  - docs/designs/audio/DESIGN_FPS_Audio.md — HROT's OWN ambient beds (wind, birds, town, the battle rumble from shots out of earshot, AU-6); this file adds AUTHORED layers any renderer reads; slice A3 there may follow a battle.* layer (§5 slice 3)
  - docs/designs/weather/DESIGN_Weather_And_Time.md — the precedent shape (authored state on positionless / area entities, replicated, a late joiner gets it); weather reaches DPS with this file's gateway half
  - docs/DESIGN_Scenario_Rules.md — SR-93, the ONE action vocabulary: SetAmbience joins it
  - SimNetGateway docs/design/DD-028-Dps-Ambience-And-Weather.md — the DPS half: the Ambience and Weather topics
-->

# Ambience — the battle around the scenario, alarms and sirens, as kinds a renderer plays *(ui, `2026-11-22`; `CE-11034`, R-11014)*

🔒 User, `2026-11-22`: *"If there is some way how to indicate to the Unreal client what kind of ambient sound to play without knowing the exact
name of the sound. We might invent some identifiers or short texts, but not a concrete sample, more like a kind ID."* → *"approved, write the
design and reply to Unreal"*. Asked by the Unreal lane's `NOTE_UnrealIg_To_Hrot_Battle_Ambience.md` (`unreal-ig` `0f2d9b4f1`): a scenario-level
"battle around us", scenario cues (air raid, alarms, evacuation), and weather on the feed.

## 0. INVENTORY (measured `2026-11-22`)

| query | result |
|---|---|
| `scripts/cbm.sh search_graph name_pattern ".*(Weather\|Ambien\|Soundscape\|Cue).*" label Class` | 23 — `EntityWeather` (+ its translators), `WeatherConditions`; `SoundTrack.Cue` (the film's log), `ContactCues`, `ScenarioChecks.CheckCue`, `EntityBodyLayer3D.AmbientBody` — ⛔ none is an authored ambience |
| grep `docs/` for ambience / soundscape / siren / air raid | `DESIGN_FPS_Audio.md` (HROT's own beds, AU-6, clips 7361–7365) · the convoy drafts (prose) — ⛔ no authored ambience design |
| `SoundSources.cs:477-481` | HROT's own beds: Wind, Birds, BattleRumble (from fire out of earshot), Town — clip ids, ⛔ not kinds |
| `SimDescriptors.cs` `EntityWeather` | weather replicates inside HROT (Reliable, TransientLocal, KeepLast 1); the gateway sends none (grep of SimNetGateway `src`: no weather topic) |

## 1. The model

An **ambience layer** is an entity carrying `Ambience { Kind, Intensity, Radius, Until }`: positionless ⇒ heard everywhere (the war around the
scenario); with a `SimTransform` ⇒ heard within `Radius` of it (a town's air-raid siren, a base alarm). `Kind` is a short dotted name from one
list (§3), never a sample: a renderer maps the name to whatever it has, and an unknown name falls back to its parent (`alarm.airraid.preliminary`
→ `alarm.airraid` → `alarm`). Layers are STATE (R-11007): a late joiner hears what is sounding, a replay restores it.

## 2. The diagrams

```mermaid
classDiagram
    class Ambience {
        <<new unmanaged component, recorded, saved>>
        +FixedString32 Kind
        +float Intensity
        +float Radius
        +double Until
    }
    class AmbienceKinds { <<new static, Hrot.Core - the ONE list>> +All +Parent(kind) +IsKnown(kind) }
    class SetAmbience { <<new stateful SharedAiAction, SR-93>> Kind, Intensity, Place, Radius, Duration }
    class AmbienceExpirySystem { <<new, the owner>> destroys a layer whose Until passed }
    class EntityAmbience { <<new NED descriptor>> keyed by entity, Reliable, TransientLocal, KeepLast 1 }
    class WeatherConditions { <<existing, the precedent>> }
    SetAmbience ..> Ambience : creates or updates a layer (a creation request, never a direct write)
    SetAmbience ..> AmbienceKinds : validates
    AmbienceExpirySystem ..> Ambience
    Ambience ..> EntityAmbience : egress by the owner
```

*What the picture shows that prose hid:* nothing writes `Ambience` but its layer's owner; the action reaches it through the creation path, so a
trigger, a behaviour and the scenario author build the same entity.

```mermaid
sequenceDiagram
    participant T as a trigger (or a behaviour)
    participant A as SetAmbience
    participant O as the layer's owner (Brain)
    participant N as every node
    participant G as the gateway (DD-028)
    T->>A: SetAmbience(alarm.airraid, 1.0, town, 800 m, 90 s)
    A->>O: create the layer entity WITH its final Ambience and SimTransform
    O-->>A: created ⇒ Success
    O->>N: EntityAmbience (state)
    N->>G: the layer, as every node sees it
    G->>G: DPS Ambience row (Kind, Intensity, Position, Radius, Until)
    Note over O: sim time passes Until
    O->>N: the layer destroyed ⇒ the row disposed
```

```mermaid
graph TD
    subgraph Brain["Brain / the editor alone"]
      ACT[SetAmbience - any trigger, BTree, HSM, director]
      EXP[AmbienceExpirySystem - every frame on the owner; its registering pack is chosen at build (beside the weather's)]
    end
    subgraph Wire["NED"]
      DESC[EntityAmbience descriptor]
    end
    subgraph Readers
      GW[SimNetGateway host - DPS Ambience]
      FPS[HROT FPS audio - may follow battle.* later]
    end
    ACT --> EXP
    EXP --> DESC
    DESC --> GW
    DESC -.-> FPS
```

*Caption:* the dashed edge is NOT built by slice 1 — HROT's own mixer keeps its derived rumble (AU-6) until slice 3.

## 3. The kinds (first values — the Unreal lane may propose more from its pack)

| family | kinds | the Unreal pack's sound (their note §2) |
|---|---|---|
| `battle` (a bed, loops) | `battle.distant` · `battle.city` · `battle.night` · `battle.heavy` (tanks, artillery) · `battle.sniper` · `battle.ambush` · `battle.aftermath` (burning, few shots) | A |
| `event` (a moment) | `event.airstrike` · `event.drone` · `event.rocket` · `event.convoy` · `event.evacuation` · `event.evacuation.city` · `event.nuclear` | B |
| `alarm` | `alarm.airraid` · `alarm.airraid.preliminary` · `alarm.bell` · `alarm.intruder` · `alarm.emergency` (sirens with explosions) | C |
| `naval` | `naval.ship` · `naval.warship` · `naval.underwater` | E (no use today) |

⛔ Not kinds: the listener's own state (inside a tank, wounded — their D) is the client's, from possession and `EntityStatus`; distant shots to
compose a war (their F) are the client's way to play `battle.distant`.

## 4. Decisions

| # | decision | rejected (one line each) |
|---|---|---|
| **AM-1** | a kind is a dotted text name (`FixedString32`) from `AmbienceKinds`, with parent fallback | a numbered enum — every new kind a contract change for every client · free text — no list, drift · sample names — the recordings stay in the client |
| **AM-2** | a layer is an entity with `Ambience` (positionless = everywhere, positioned = within `Radius`) | one global setting on the World Environment — cannot hold a siren at a town; a separate event topic — a late joiner misses a siren already sounding |
| **AM-3** | `SetAmbience` is a stateful `[SharedAiAction]` (SR-93): creates the layer with its final value, or — the same `Kind` at the same `Place` — replaces it; `Intensity 0` removes it; `Duration 0` = until removed | a trigger-only verb — behaviours could not raise an alarm |
| **AM-4** | unknown kinds are ALLOWED at run time (logged once) and refused by the editor's picker | refused at run time — a newer scenario on an older HROT would fail |
| **AM-5** | authored layers are ordinary scenario entities (the editor's add-entity picker, a kind dropdown, an intensity, a radius) | a header field — would need its own save path (SR-71 is moving weather to the header; ambience has no clock or global singleton to justify it) |
| **AM-6** | HROT derives no moments in slice 1 (hostile aircraft near a town ⇒ siren) — a behaviour or a rule calls `SetAmbience` later | derived in the engine now — a rule nobody asked for yet |

## 5. Slices and acceptance (R-4277)

1. ✅ **Slice 1 (CE-11034) — BUILT `2026-11-22`, §6:** `Ambience`, `AmbienceKinds`, `SetAmbience`, `AmbienceExpirySystem`, `EntityAmbience` + its translators; rails:
   create / replace / remove / expire, positionless vs positioned, the descriptor round trip, a late joiner gets the layer, 0 B per steady frame
   for the expiry system, `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`; a scenario `ambience-air-raid` (a positionless `battle.distant` from the
   start; a trigger at 30 s raises `alarm.airraid` at the town for 60 s) with `checks.json` expect rows on the layers present at each time.
2. **Slice 2 (ui):** the editor's picker entry and the Details form (kind dropdown from `AmbienceKinds`, intensity, radius, until).
3. **Slice 3 (later):** HROT's FPS audio plays the kinds it has a clip for (`battle.*` lifts the rumble bed).

## 6. AS BUILT — slice 1 (`CE-11034`, ui, `2026-11-22`)

⭐ The build matches §1–§2 except where this table says it does not; each deviation is a finding, not a silent choice.

| § | the design said | as built | why |
|---|---|---|---|
| §1 | `Ambience { FixedString32 Kind; float Intensity; float Radius; double Until }` | `Until` is a **`SimInstant`** (the same 8 bytes; JSON and the dump read it as a number; the wire carries a plain `double`) | CE-4201 / SV-1: an instant is a `SimInstant`, never a `double` (`AreaEffectCloud.BornAtSimSeconds` is one) |
| §2 | `AmbienceKinds` and the component in `Hrot.Core` | the component, `AmbienceExpirySystem` in **`Fdp.Toolkits`** (`Fdp.Toolkit.Ambient` — not `…Ambience`: a namespace named like its type); `AmbienceKinds` in **`Hrot.Core`** (`Hrot.Core.Ambience`) as drawn | the toolkit owns what runs on every node (the cloud's shape); the kinds list stays with the products that read it |
| §2 | component id | **266** | the ui block (460–475) and the new-lane block (476–495) are full; of the production-free ids only 266 and 328 are held by no test struct and named by no comment — 328 sits in the retired agg-ui range; 266 was NavFakeIds' note *"formerly FakePathPoolEntry — not an ECS component"* |
| §2 | TKB type, descriptor | TKB type **8841** `AmbienceLayer` (a bare template, like the World Environment's; no palette row until slice 2); descriptor **`dtAmbience` 246**, topic `EntityAmbience` (`[DdsManaged]`: `Kind` is a string), **no ownership group** — the layer's creator keeps it (the `dtAreaEffectCloud` shape), mapped in `NedOwnershipGroupBinding` so it is ingressible | all three measured free on every origin branch, 2026-11-22 |
| §2 | the forwarded creation request | the request carries the layer as a **`dtAmbience` descriptor** (a union case + `DescriptorMapper` + `CreateEntityRequestDescriptorBuilder`) | `owner 0` ⇒ the default processor, which may be another node: without it the creator builds a bare 8841 entity that sounds nothing (the cloud's CE-3228 finding) |
| §2 module diagram | "its registering pack is chosen at build" | `AmbienceExpirySystem` is in the **`Interactions` capability** beside `AreaEffectCloudSystem` — scheduled on every node, gated on `HasAuthority` — so whoever owns a layer ends it | the owner-side systems of replicated things live there (its remark) |
| §2 sequence | create ⇒ Success | `SetAmbience` returns **Running until the new layer appears on this node** (derived `EntityGuid`; fails after 30 s sim: *"the ambience layer never appeared"*), an update or a removal is Success the same tick | so a following step finds the layer, and two steps one tick apart cannot both create it |
| AM-3 | same `Kind` at the same `Place` replaces it | **updates in place** when this node owns it; a replica (another node's layer) is **ended through the network and asked for again** — only its owner writes it; the layer is named `"Ambience <kind>"` (`EntityInfo`) so a check, the map's list and the editor can call it | the one-writer rule; a name is how `checks.json` reaches a layer |
| AM-3 | `Place` | no place = positionless; ⚠ coordinates written exactly **(0,0,0)** read as "no place" (key it `"@Key"` if it must be the origin) | a `PointLink` has no "absent" state |
| AM-4 | an unknown kind is logged once | logged **once per run of the step** (a rule's edge, never every frame) — a process-wide log-once set would be static mutable state in behaviour code | CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL" |
| §5 | — | the ingress **holds** a sample whose entity is not known yet (the weather's shape: the topic and the creation are not ordered) | a late joiner's TransientLocal sample can arrive first |
| — | — | `AiBrainReads` declares `Ambience` a stable read (`SetAmbience` asks whether a layer sounds; nothing writes it inside the brains' phase) | `HROT_READ_CHECK` |

### 6a. The rails (red-proofs on each)

| suite | what |
|---|---|
| `Fdp.Toolkits.Tests` `AmbienceExpirySystemTests` | unmanaged, id 266, saved + recorded · ends AT `Until`, never before, `Until` 0 never · sim clock only · positioned too · a replica is not ended · a replicated layer ends through ONE `DestroyEntityCommand` + `TearDown` · **0 B** in a steady frame (`GC.GetAllocatedBytesForCurrentThread`) |
| `Hrot.Core.Tests` `AmbienceKindsTests` | the list is §3's table · every kind resolves to itself and has a chain ending at a family · `alarm.airraid.preliminary.v2` → `alarm.airraid.preliminary` → … → `alarm` · a totally unknown kind resolves to nothing and `Check` flags it · empty / over-long |
| `Hrot.SimHost.Tests` `EntityAmbienceTranslatorTests` | every field both ways, each a new sample · on-change only, a non-owner sends nothing, an ended layer is disposed on the wire · positionless and positioned both publish · a sample before its entity is held, the owner never takes its own back · ordinal 246 / id 266 |
| `Hrot.SimHost.Tests` `ScenarioRulesTests.Ambience` | (the `SetAmbience` action) a transient positioned layer with its final value, waits for it · positionless with no end · a second call updates, never duplicates · intensity 0 removes, nothing asked for · a replica is replaced · unknown kind runs, empty / long refused · a layer that never appears fails the step |
| `Hrot.Map.Common.Tests` `CE11034_AnAmbienceLayer_SurvivesTheWireRoundTrip` | the forwarded request carries the layer |
| scenario `ambience-air-raid` | see §6b |

### 6b. The scenario

`scenarios/demos/sensing-and-weather/ambience-air-raid` (generated by `scripts/gen-ambience-air-raid-scenario.py`): the authored positionless `Distant War` (`battle.distant`) sounds from the start; the trigger `Air raid at 30 s` raises `alarm.airraid` at (300, 300) for 60 s; `checks.json` states the same list as the `description`'s Judge (the layers present at each time, the siren's place and radius, its end, its absence after 95 s). ✅ `scripts/scenario-regression.sh ambience-air-raid`: 11/11 rows pass (the siren appears at 30.25 s, still sounds at 79 s, is gone by 95 s), golden captured and re-run `GOLDEN same`. ⚠ Judged in the deterministic editor tier only (one node: the creation request is served where it is asked); the cluster tier (replication with the gateway reading `dtAmbience`) is the DD-028 half and is not built.

