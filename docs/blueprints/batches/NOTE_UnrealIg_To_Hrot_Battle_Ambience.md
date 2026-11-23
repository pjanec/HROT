# Note: the Unreal client has ~45 recorded war ambiences — what they are, and what could trigger them

| | |
|---|---|
| **From** | `unreal-ig` (the Unreal image generator), 2026-11-22 |
| **For** | the HROT lanes: `ui` (the scenario's environment, the gateway extension), whoever owns scenario triggers / macros. FYI the SimNetGateway session |
| **User, verbatim** | *"What about the ambient sounds from the battlefield? Do we have some? That could be some settings in the HROT scenario, or maybe they could be derived from something, I don't know. Let's please start with what we have and let's write another note with the list, if we can identify what kind of war or battle or shootout ambient sounds we have."* |
| **Companion** | [NOTE_UnrealIg_To_Hrot_Squad_Voice.md](NOTE_UnrealIg_To_Hrot_Squad_Voice.md) — the voice lines; the same rule |
| **Asks** | feedback on §3: which triggers HROT can give (a scenario setting, scenario events), and whether weather can reach the client feed |

## 1. The rule

⛔ The recordings stay in the Unreal client (a purchased pack: cooked into the packaged IG only, never in git, never in HROT). This note lists their
**names** only. ⭐ HROT sends the MEANING (a setting, an event); the client picks the sound.

⚠ **How sure we are:** unlike the voices, these names are descriptions, not transcripts. The figures below are a local measurement — length, events
per minute (a crude count of loud onsets: shots, blasts) and level — not a listen. A bed may contain voices or sirens its name does not mention.

## 2. What we have

**A. Battle beds — continuous fighting, could loop under the scene**

| name | length | events / min | level | character (from the numbers) |
|---|---|---|---|---|
| Intense mid range battlefield | 30 s | 142 | -24 dB | dense, mid distance |
| City battlefield ambience | 29 s | 31 | -20 dB | urban, steadier |
| Distant gunshots and indoor battlefield | 29 s | 69 | -24 dB | distant / heard from inside |
| Nighttime battlefield with distant shots | 30 s | 34 | -34 dB | quiet, sparse — night |
| Sniper shooting in the distance | 30 s | 96 | -23 dB | distant single shots |
| Tank fire on the battlefield | 30 s | 62 | -20 dB | heavy guns |
| Gunfight with electrical disturbances | 28 s | 52 | -25 dB | firefight + crackle |
| Jungle patrol ambush ambience | 39 s | 32 | -29 dB | ambush in vegetation |
| Battlefield assault rifle firefight (×2) | 7 s, 12 s | 186–189 | -25 dB | close, bright, short — a burst, not a bed |
| Burning house war zone ambience (×3) | 95–125 s | 0–13 | -18 dB | long, almost no shots — fire, aftermath |

**B. Events — a moment with a start and an end (one-shots, 24–30 s)**

Tactical airstrike · Drone attack and explosion · Nighttime rocket launch and explosion · Atomic bomb drop and explosion · Armored convoy approaching ·
Military trucks evacuation ambience · Citywide evacuation · City emergency sirens and explosions · Hostage situation with bomb ticking

**C. Alarms and sirens**

War air raid siren (×3, 64–93 s) · War air raid siren preliminary alert (×2) · Emergency alarm bell ring (×4) · Enemy base intruder alarm (×3)

**D. A listener's state (point of view)**

Battlefield tank interior perspective · Flashbang wounded soldier perspective · Injured soldier moving through grass · Night vision team battlefield
preparation

**E. Naval (no use today)**

Naval ship ambience with aircraft taking off · Underwater battlefield with explosions · Military ship at sea ambience loop (×3) · Warship rumbling
ambience (×2)

**F. Building blocks for a "distant war" the client composes itself**

Distant bomb explosion (×5) · Machine gun burst distant perspective · Pistol distant shot (×8) · Beretta handgun distant shot (×2) · Shotgun blast
distant perspective (×4) — scattered at random, far off, at a chosen intensity.

The client already has (U-18n, the free set) calm beds — countryside, forest, wind, town day / night, traffic, rain — crossfaded by what is near the
view and by night. The war beds above would sit on top of those.

## 3. What could trigger them — three sources, our lean first

| # | source | what it gives | needs from HROT |
|---|---|---|---|
| **①** ⭐ | **derived in the client** from what the feed already carries: `Fire` / `Detonation` rate around the view ⇒ a combat intensity; `FireSpots` / burning buildings near ⇒ the burning-house bed; the sun ⇒ the night variant | the battle beds (A) fade in, faint and "beyond what you see", while the simulated fight is on, and fade out after it | nothing |
| **②** | **a scenario setting** — the battle that is NOT simulated (the war around the scenario): e.g. *off / distant war / city under fire / aftermath*, + an intensity, maybe a direction | a soundscape for a scenario that shows a small piece of a big battle | a setting saved in the scenario (the weather is already a scenario world entity — the same home?) and its value on the client feed |
| **③** | **scenario events** — an air raid, an alarm at a base, an evacuation, an airstrike: a cue fired by a scenario trigger, or derived by HROT (hostile aircraft near a town ⇒ the air-raid siren; an enemy inside a base zone ⇒ the intruder alarm) | the event sounds (B, C) at the right moment | a "cue" event on the feed (a name, a place) and a trigger action that raises it |

⚠ **The trap in ①:** a bed has shots and blasts baked in that the simulation did not fire. Up close they would contradict what is on screen; so the
beds stay quiet and distant, under the simulated (positional) shots, and are strongest where the view sees no fight at all (option ②).

Category D is client-side only (in a tank ⇒ the interior bed; a possessed man badly hurt ⇒ the wounded perspective) — from the possession and
`EntityStatus.Health` the client already has.

## 4. Questions for you

1. ② — is a scenario-level "battle around us" setting worth having, and where would it live (next to the weather)?
2. ③ — do scenario triggers / macros have (or could they have) an action that raises a named cue the clients can hear? Is there an HROT moment for
   "air attack on a town" or "the enemy entered our base"?
3. FYI — the weather (rain, wind) is not on the client feed today (`EntityWeather` replicates inside HROT only); the client's rain and wind beds
   would follow it if it were.
