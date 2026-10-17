<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: the whole file (a note: what is built, for whom)
stale-below: nothing
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.3a (B-1 as-built), §7.5a (B-5 as-built), §7.6 (ui's findings answered)
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Civilians_Findings.md — the findings this answers
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Civilians_B3_B2.md — the earlier note (side flip, the types)
-->

# Note: backend → behaviors + ui — the civilians' effects, rounds and person descriptor are built

**Not orders.** This lists what you can now build on. Design: [§7.3a, §7.5a, §7.6](../../DESIGN_Civilians_And_Crowd_Control.md).

| for | built | how to use it |
|---|---|---|
| **behaviors** — CivilianHsm's Cower state (CE-2236) | `EffectState.Cowering`, on the wire (`dtEffectState`) | `Effects.SetCowering(repo, unit, true)` on entering Cower, `false` on leaving. Call it on the brain owner; it leaves the timed effects alone. Read with `Effects.IsCowering` |
| **behaviors** — any person's brain | the stored effects of a hit | `Effects.IsKnockedDown` / `IsStunned` / `IsIrritated(repo, unit)`. A stunned or knocked-down man should not act until they clear |
| **behaviors** — the security forces' behaviours | loadouts: shield and baton men carry 2 stun grenades (6037); the launcher man a 40 mm baton-round launcher (6036, mount 0) and stun grenades (mount 1) | the existing fire nodes; a baton round hit knocks down (4 s), a stun grenade stuns within 8 m (up to 8 s, less behind walls) |
| **ui** — U-3 poses (CE-1164) | `EffectState` with `Cowering` (F-3) | `FigurePose` can read it as U3-a says |
| **ui** — U-1 elderly look (CE-1162) | the TKB descriptor `PersonDto` (`Body.Person`, F-4): `Age` Adult / Child / Elderly, `Sex` Unspecified / Male / Female | `template.GetDescriptor<PersonDto>()`; absent ⇒ adult, sex unspecified |

⏳ **Still not built:** the gas cloud (tear gas, the Molotov's fire) — B-1 part 3, waiting on a decision about a system on every
terrain host. The stone has no thrower until a civilian can pick one up (CE-2236).
