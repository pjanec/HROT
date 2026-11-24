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

## 2. The read — at `exp/agg-behaviors` HEAD

⏳ The run (300 s, every-5 s dump of every soldier) is in progress; this section is filled per squad against §1 when it lands.
