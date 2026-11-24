<!--STATUS
state: LIVE — in progress
updated: 2026-11-24
current-answer: §1 the doctrine yardstick · §2 the read (filled from the run)
stale-below: nothing
related-designs:
  - HANDOFF_User_To_Behaviors_Village_Street_Fight.md — the ask this answers
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS PlatoonDefend / PlatoonAttack, posts, the base of fire
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS SupportByFire / Suppress and the squad drills
  - ../../DESIGN_Decision_Layer.md — §3.3g′ the individual suppressor (D-15)
-->
# REPORT — `behaviors`: is the `village-street` fight a modern infantry fight?

🔒 User, `2026-11-24`: *"Realism should be judged from what a usual military doctrine says about such a fight."*
⇒ The verdict is the DOCTRINE below, not the `checks.json` rows. The rows are a regression floor; each realism defect found gets a row when the grammar can say it.

## 1. The yardstick — what doctrine says about this fight

📄 Sources (public, generic): US Army ATP 3-06.11 *Combined Arms Operations in Urban Terrain* (ex FM 3-06.11), ATP 3-21.8 *Infantry Platoon and Squad*
(ex FM 3-21.8), ATP 3-21.71 / FM 3-20.12 (infantry with armour); other NATO doctrine says the same in substance.

| # | the situation here | doctrine | what we look for in the run |
|---|---|---|---|
| **A1** | the attacker on an open, straight street | ⛔ **avoid the street** — move along the building line and through buildings/gardens; cross the street only at a chosen point, fast, under smoke/covering fire | men in the street centre, walking or standing |
| **A2** | moving toward a defended position | **fire and movement / bounding overwatch**: one element moves while another overwatches from covered positions; bounds short (3–5 s rush, 10–30 m); never everyone moving at once | both halves moving · bounds longer than ~30 m · nobody overwatching |
| **A3** | the base of fire | in or beside buildings (windows, corners, walls), **covered, with a line of fire** to the objective; it stays there while the assault element moves | a base of fire in the open street, or leaving its position for the street centre (the original report) |
| **A4** | the squad's formation | **dispersion** ~5–10 m between men (a burst or grenade takes one man, not three); the squad stays within the leader's voice/sight | bunching (< 2–3 m) · stragglers far off |
| **A5** | contact (fired on) | **react to contact**: return fire, get down / into cover at once, locate the enemy, then the leader decides: suppress + flank, or break contact | men standing in fire · no return fire · the whole squad lying still for minutes |
| **A6** | the attack on a defended block | **isolate → suppress → assault**: suppress the defended building, manoeuvre an element to a FLANK (side street, gardens, backyards), enter from the least-defended side, clear room by room | frontal assault straight up the street · no flank use of side streets |
| **A7** | armour with infantry (M1, Bradley on the street) | **infantry and armour support each other**: tanks overwatch/suppress from cover, infantry moves ahead/alongside to protect them from close AT fire; tanks do not drive alone up a street | tanks ahead of the infantry, alone, or idle behind |
| **D1** | the defender (Red) | **defend FROM buildings**: positions inside, set back from windows, interlocking fields of fire down the street; never in the open street | Red men in the street or in gardens in the open |
| **D2** | the defender's posture | **mutually supporting positions**, alternate positions, a reserve held back under cover for the counterattack | all of Red on one line · the reserve in the open · nobody displacing when a position is suppressed |
| **D3** | defender's vehicles (T-72, ZU-23) | hull-down / behind buildings, covering the approach, shoot and move | vehicles parked in the open street, never moving |
| **G1** | everyone | fire discipline: aimed fire at seen enemies, suppressive fire only at a known/likely position; **stop when the target is dead / out of ammo** (CE-4667) | firing at dead men, at nothing, with an empty weapon |
| **G2** | everyone | casualties: the dead stop; the wounded are covered and pulled to cover (CE-4668) | dead men acting |

## 2. The read — `exp/agg-behaviors@a85c62963` (coordinator merged), 300 s, every soldier every 5 s

📐 Source: `scenario-regression.sh village-street-fight` + a full `HROT_VERIFY_REVIEW` dump (positions, stances, behaviours, every round). Street band =
y 246–264.

### 2a. Verdict per doctrine point

| # | doctrine | what the run shows | verdict | cause | id |
|---|---|---|---|---|---|
| **A1** | avoid the street | Blue's squads are in the street band **0 of 4 men** almost the whole run — they move along the south gardens (Squads 1, 3) and sit north of it (Squad 2) | ✅ | — | |
| **A2** | fire and movement, short bounds | Blue Squads 1 and 3 bound in pairs from 90 s (two `AdvanceTo`, two `Suppress`) ✅ — but a bounding man runs on ALONE: Squad 3's Rifleman 1 ends **100 m** ahead of his squad (240 s) and dies; Squad 1's Rifleman 3 the same (285 s) | ⚠ | the pair's bound has no length cap / no "wait for your buddy" | `CE-8321` |
| **A3** | base of fire covered, with a line of fire | Blue Squad 2 (SupportByFire) sits north of the street at x 375–380 for the whole 300 s, prone/crouched — and **all 178 of its rounds strike terrain** (aimed at a reported POINT 170–260 m away it cannot see). It never displaces as the assault moves 150 m on | ❌ | `SuppressContact`'s point fire is plain-aimed (§11.6b's opening rule not applied there) and the base of fire never checks that its rounds arrive, nor moves | `CE-8319` |
| **A4** | dispersion 5–10 m | no squad bunched (spread 4–10 m while together) | ✅ | | |
| **A5** | react to contact | Blue fires first (1.6 s) and hits a Red man at 1.65 s. Squads 1 and 3 then HOLD 90 s in `SquadDefend` (Squad 3 **standing** 75 s) before the attack starts | ⚠ | the platoon attack's first phase holds the assault squads until the support is set; 90 s is long and a standing hold in contact is wrong | `CE-8322` |
| **A6** | suppress + flank, enter from the side | Squad 3 swings SOUTH (y 145–210) — a flank ✅ — but nobody enters a building; the assault ends in gardens | ⚠ | the attack's assault point is the street objective; no building is the objective | (watch) |
| **A7** | armour with infantry | the **M1 drives up the street ALONE** to x 510 by 60 s — 40 m from Red, 100 m ahead of its infantry — and stops there for 4 min **without firing a round**; the Bradley stops at x 410, silent; the Apache hovers at x 140, silent | ❌ | the vehicles have NO SOP (`Sop` absent; the catalog gives tank / IFV / helicopter types no `DefaultSop`, unlike squads — CE-2358), so their brain never fires or reacts; `FollowRoute` alone does not wait for infantry | `CE-8318` |
| **D1** | defend FROM buildings | Red starts IN the street (the scenario's posts) and is shot there: Red Squad 2 loses 3 of 4 men in **15 s**. Red Squad 1 withdraws (`BreakContact`, 30 s) **up the street** and ends at x 635–650 in the street band; Red Reserve stands **in the street** (4 of 4) from 105 s to the end | ❌ | ① the authored posts are on the open street and the fight starts with Blue seeing them; ② `PlatoonDefend`'s posts are not pulled into the houses (SearchRadius 25); ③ the break-contact route and the hold after it pick the street | `CE-8320` |
| **D2** | mutual support, reserve under cover | the reserve recovers casualties (good: `CarryCasualty` at 30–90 s) then stands in the street | ❌ | as D1 | `CE-8320` |
| **D3** | vehicles hull-down, shoot and move | the T-72 (65 m from the M1, sees it) never fires; the ZU-23 and the SAM never act | ❌ | no SOP — as A7 | `CE-8318` |
| **G1** | fire at what you can hurt | **riflemen fire rifles at tanks: Red 517 rounds at the M1 (Red Squad 1 305 of its 316), Blue Squad 1 158 at the T-72** — all harmless, while enemy infantry is in sight | ❌ | the fire step shoots the THREAT ranking's top (`ContactThreatLevel`: danger to ME — the tank), with no "can my weapon hurt it" term (`ThreatDanger.CanHurt` / the M8 effectiveness input exist but the fire target ignores them) | `CE-8317` |
| **G2** | the dead stop; the wounded are pulled in | dead men run nothing ✅ (CE-4668 holds); the Red Reserve drags a casualty ✅; ⚠ incapacitated men (< 30 %) keep their `AdvanceTo` order for 100 s while bleeding out (they do not move) | ⚠ | the order is not cleared below the incapacitation line | (minor, with CE-4668) |

### 2b. CE-4666…CE-4669 on real streets
| row | on village-street |
|---|---|
| CE-4666 bunched in the open, never take cover | **Blue: holds** (off the street, dispersed, bounding). **Red: does not** — in the street from the start, withdrawing along it (CE-8320) |
| CE-4667 suppression with no ammo / no live target | partly: no empty-magazine suppression seen; but the base of fire suppresses a POINT into terrain for 4 min (CE-8319) |
| CE-4668 dead men run behaviours | **holds** (row green); incapacitated men keep their order (minor) |
| CE-4669 squads idle with no behaviour | partly: Red Squad 2's squad level runs nothing 75–120 s (its last man left) — then `InfantrySquadHold` |

### 2c. New, only real geometry (and a full force list) shows
The ARMOUR is the biggest gap — inert vehicles (CE-8318) and riflemen emptying magazines at tanks (CE-8317) did not show on `wide-corridor` (no
vehicles there). Real house lines and gardens are USED by Blue (A1 ✅); the slope and trees caused nothing measurable. The ground is fine for a fight —
no terrain NOTE needed.

## 3. Leans (the user decides the two with a wide blast radius)
| id | lean | blast radius |
|---|---|---|
| `CE-8317` | ⭐ the FIRE target = the highest-ranked threat this man's weapons can HURT (`ThreatDanger.CanHurt`); he still HIDES from the top threat (the tank). A rifleman with only a rifle never fires at a tank while anything else is in reach | ⚠ every shooter in every scenario — **user** |
| `CE-8318` | ⭐ every armed ground vehicle and helicopter type gets `BasicVehicleSop` as its catalog `DefaultSop` (as CE-2358 did for squads) — a vehicle with no authored SOP then fights | ⚠ every scenario with an unauthored vehicle (cross-lane: the TKB catalog) — **user** |
| `CE-8319` | ⭐ route `SuppressContact`'s point fire through `AimPoint.BlindAt` (one blind aim), and have the base of fire move to a firing position on the point when its rounds keep striking terrain | this lane |
| `CE-8320` | ⭐ `PlatoonDefend` pulls each squad's posts into the buildings within its SearchRadius (window posts, as SquadDefend's house defence does), and break contact withdraws along the building line, never the street centre | this lane |
| `CE-8321` | ⭐ a bounding pair moves together and a bound stops at ≤ 30 m (doctrine 3–5 s rush) | this lane |
| `CE-8322` | ⭐ in contact, the assault squads wait for the support in cover, prone/crouched (never standing), and no longer than the support needs | this lane |

## 4. `village-street-mg-flank` — the platoon attack on a machine gun *(2026-11-24, user: "if there is an enemy with some machine gun … they should try to flank, which should be the goal of the platoon leaders")*

📄 `scenarios/tests/village-street-mg-flank` — a PKM team holds the 3-storey house at (530,228) beside the main street; Blue Platoon (3 squads) attacks it
from 150 m west. Its rows state doctrine (ATP 3-21.8 / ATP 3-06.11): off the street while the gun fights · by the walls (cover cue, upright) · no bunching
until the clearing stack · the base of fire lands rounds on the gun · the assault reaches its flank (south of the house line, within ~40 m) · the gun
silenced · the assault clears the house.

| run | what the platoon did | fixed then |
|---|---|---|
| 1 | assault bounds south through the gardens and drops the gunner at 213 s ✅ — but the base of fire never left its start house, 150 m back across the street: **207 rounds, 0 hits**; the gun opened only at 131 s | — |
| 2–3 | the base of fire is ordered to its point, but its men never walk there; its rounds "expire" | `CE-8319` ①–④ — posts planned AT the point, fire from the posts |
| 4 | it moves up — and lies **in the street**; the gun, unsuppressed, cuts down the bounding squads | `CE-8319` ⑤ 2-D points on the GROUND (the ground is 176 m up: moves and aims were under it) |
| 5 | **the doctrine sequence:** the gun engages at 10 s and the base of fire answers at once from an upstairs window; the assault flanks south and silences the gunner at **95 s**; the house is cleared by 240 s; **Blue loses nobody** | `CE-8319` ⑥ 40 m post search (the houses beside the axis) |
| 6 | the same sequence; the gunner survives (he does not show at a window), two Blue men fall at the door of the house | `CE-8322` dispersal into DEFILADE (a waiting man no longer lies in the street) |

**Open, by doctrine:**
| item | measured | lean |
|---|---|---|
| the ENTRY | the assault stacks at the door (210 s) and loses men going in; rifle rounds at men in windows strike the walls (82 / 84) | ⭐ grenade before entry ("frag and clear") — read `ClearBuilding`'s entry and the riflemen's grenade load next |
| the base of fire's rounds | from 155 m into the gun house: most "expire" or strike the wall, ~1 hit — it suppresses (the gun fires ~70 rounds in 240 s) but rarely wounds | ⭐ acceptable for suppression; measure the gun's near-misses before changing anything |
| the first minute | a base-of-fire man crosses the street on his way to his post (20 s); Squad 1 is in the open as its first bound starts (60 s) | ⭐ the way to the support point should go by the building line — the move-in path, not the post |
