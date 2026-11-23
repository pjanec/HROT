# Note: the Unreal client has ~140 recorded military voice lines — HROT is the source of good TRIGGERS for them

| | |
|---|---|
| **From** | `unreal-ig` (the Unreal image generator), 2026-11-22 |
| **For** | the HROT lanes: `behaviors` (callouts AQ105, the command chain's reports and orders), `ui` (the gateway extension). FYI the SimNetGateway session (the topics would live in its IDL) |
| **User, verbatim** | *"write a note to the HROT and tell them what voice samples we have, just the texts, and suggest the topics and opportunities. They will take a look and will come back with some feedback. And yes, the samples will stay in Unreal only. The HROT is just the source of a good triggers for those sounds."* |
| **Asks** | feedback on §3 (which events HROT can publish) and §4 (which moments fit which line) |

## 1. The rule this note keeps

- ⛔ **The recordings never leave the Unreal client** (a purchased pack, licensed to one workstation; cooked into the packaged IG only, never in git, never
  in HROT). This note lists their **texts only**.
- ⭐ **HROT sends the MEANING** (who, which call / report / order, to whom); **the client picks the clip and decides who hears it** — the same split as
  `EntityStatus` for the cards ([NOTE_UnrealIg_To_Ui_Entity_Status.md](NOTE_UnrealIg_To_Ui_Entity_Status.md)).
- Realism is not the bar (user): the goal is that **following a leader, or standing near him, you hear his squad's command traffic**.

## 2. What we have — the texts (male, English, US military style)

⚠ Measured locally: **every line is radio-processed** (narrow band, almost nothing below 250 Hz or above 4 kHz) — it sounds like a radio, not a
dry shout. ⇒ it fits **radio traffic** best (leader ⇄ HQ, leader ⇄ his men on the squad net); as a face-to-face shout it is acceptable, not ideal.

**Orders and short calls (45):** advancing on the enemy · affirmative · artillery · attack · break contact · check ammo · check fire · check weapons ·
clear the area · cover me · covering fire · cover · defend · fire for effect · fire · follow me · get down · go · hold fire · holding position · hold ·
in position · incoming · move out · need backup · negative · no · open fire · out · over (×2) · push forward · ready to move · reloading · rocket ·
cease fire · stand down · standby for orders · stop · support me · suppressing fire · take cover · watch your fire · watch your six · yes

**Warnings and reports (23):** enemy artillery incoming · enemy forces approaching · enemy spotted · grenade out · hostiles in the area · I have visual
on the target (, moving to engage) · I need a status report · missile · mortar · need medical attention · objective is heavily defended · request
immediate support · requesting backup · requesting extraction · secure the area · securing the extraction · target acquired · target neutralized ·
the enemy has breached our perimeter · the enemy has retreated · the objective is under heavy fire · we have hostile forces incoming

**Radio exchanges (38):** all units, regroup at the designated rally point · are you in need of assistance · are you ready to move out · can you
confirm your target · demolition · do you copy · eliminating the enemy threat · good job team, let's head back to base · have you identified any
threats · have you reached your objective · I'm moving to a better position for cover · inbound for pickup · maybe · move to flank · moving to the
extraction · objective complete · permission to engage · rally · returning to base · the area is clear · the mission is a failure · the mission is a
go · we have the advantage · we need to destroy that radar installation · we need to hold this position at all costs · we need to move quickly, time
is running out · we're in a tight spot, requesting immediate assistance · we're moving to the extraction point · we're taking heavy losses · we've
completed the mission, returning to base · what's the enemy position · what's the plan · what's the situation · what's your current heading ·
what's your current weapon status · what's your location · what's your plan of action · what's your status

**Callsigns (26):** bravo · charlie · delta · echo · "this is Sergeant X" and "Sergeant X here" for 11 names (Brown, Cheen, Cooper, Garcia, Johnson,
Lee, Martinez, Nguyen, Patel, Ramirez, Taylor)

**Countdowns (8):** one … five · 3-2-1 · 4-3-2-1 · 5-4-3-2-1

**No speech (30):** walkie-talkie button beeps, squelches, static loops, radio on/off — the frame around a transmission.

## 3. The topics we suggest — HROT already has all three, only not on the client feed

| event | HROT has it today | on the client feed (gateway `DpsTopics.idl`, 2026-11-22) |
|---|---|---|
| **a man SHOUTS** | `Utterance {Kind, Target, Until}` (`FDP/Toolkits/Fdp.Toolkits/Civilians/Speech.cs`), written by `Callouts.Say` (AQ105, CE-2372), already replicated (`dtUtterance`) | ⛔ |
| **a REPORT** up or down the chain | `CommandReport` (`FDP/Toolkits/Fdp.Toolkits/Squad/Command/CommandReports.cs`): Kind (Contact / Spot / Sitrep / Lace / Progress / Request) + subtype + the closed fields; already on HROT's own wire as `SquadReport` (CE-3250, BR-3) | ⛔ |
| **an ORDER** given | `CommandOrderEntry` (`.../Squad/Command/CommandHistory.cs`): from, to, the behaviour hash | ⛔ (only a unit's current `EntityStatus.Task` text — no giver) |

**Suggested shape (names are yours to settle):** three **event** topics, written **once on the world partition** (`<base>-*`), never per seat, one
sample per happening:

```idl
struct Utterance { int32 SpeakerId; string<24> Kind;  int32 TargetId; };          // the UtteranceKind NAME (the client keys clips by name, as ui's VoiceTkbCatalog)
struct Report    { int32 FromId; int32 ToId; CommandReport Report; };            // the struct as it is (its enums are already IDL-safe)
struct Order     { int32 FromId; int32 ToId; string<32> Order; };                 // a NAME, not the hash — a client cannot read the hash
```

⚠ Not measured by me (your side decides): the reliability / history the events want (we suggest reliable, KEEP_LAST a few, VOLATILE — a late joiner
need not hear old traffic); whether a report to HQ has a real `ToId` when the superior is not simulated; whether `Order` is worth it or the report
traffic is enough.

## 4. The opportunities — which moments fit which line (a first cut; tell us what we got wrong)

**Callouts (the man's own shout, `UtteranceKind`):**

| callout | line |
|---|---|
| `CmdMove` | move out / go / push forward |
| `CmdHold` | hold / holding position |
| `CmdFallBack` | break contact |
| `CallCovering` / `CallSuppress` | covering fire / suppressing fire |
| `CallReloading` / `CallFragOut` | reloading / grenade out |
| `CallAck` | affirmative / yes |
| `CallContact` | enemy spotted / hostiles in the area (⛔ no "contact left / right") |
| `CallClear` / `CallManDown` | the area is clear / need medical attention |
| `CallGrenade` (one landed near) | ⛔ nothing exact — "incoming" at best |
| `CmdDown` | get down / take cover |

**Reports (on the radio, framed as: beep · "this is Sergeant X" · the line · "over" · squelch — one sergeant name per leader, so squads are told
apart by ear):**

| report | line |
|---|---|
| Contact | enemy spotted |
| Spot | hostiles in the area · enemy forces approaching · we have hostile forces incoming |
| Progress `Set` | in position |
| Progress `ObjectiveSecure` / `BuildingClear` / `RoomClear` | objective complete · the area is clear |
| Progress `FallingBack` / `Displacing` | break contact · I'm moving to a better position for cover |
| Progress `TaskFailed` | the mission is a failure |
| Progress `TargetsDown` | target neutralized · the enemy has retreated |
| Lace with casualties | we're taking heavy losses |
| Sitrep | holding position · we have the advantage (strength green) · we're in a tight spot (red) |
| Request `Casevac` / `FireSupport` / `Reinforce` / `Permission` | need medical attention / request immediate support / requesting backup / permission to engage |
| (heavily opposed, if HROT can tell) | objective is heavily defended · the objective is under heavy fire |

**Orders and HQ's side:** an order to a unit ⇒ HQ: "move to flank" / "attack" / "defend" / "standby for orders" / "the mission is a go", the leader
answers "affirmative" / "ready to move". HQ asking "what's your status" / "what's the situation" could precede a Sitrep, if HROT has a "requested"
moment. **Fire support:** "fire for effect", "artillery", "mortar", "enemy artillery incoming" (a shell landing near). **Demolition:** a countdown
before a charge, if HROT has that moment.

**Scenario-only:** the mission lines (radar installation, extraction, pickup, return to base) fit only where a scenario scripts them — perhaps a
scenario trigger action "say" with a line name, if that is ever wanted.

## 5. The client's side

- ⏳ a radio player in the IG: in the world at the speaker (≈ 40 m, as the callouts' `VoiceRange`), and **"in the earpiece"** (2-D, any distance) while
  the camera follows or sits in a man of that squad; a sergeant name per leader; the line choice per kind, never repeated back to back.
- ⛔ nothing on HROT's side plays audio for this; HROT's own placeholder voices (CE-2372) are unchanged.
