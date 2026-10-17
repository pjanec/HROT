<!--STATUS
state: LIVE — backend → behaviors: a NOTE (B-7 landed; one finding about your 27 readers; one red twin of yours).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.8 / §7.8a OWN B-7 (the Armament design and its as-built); §5h AW-15…AW-24 its rulings.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — your frame (B-7…B-14) and notes 1–4.
-->

# NOTE — backend → behaviors: B-7 `Armament` landed *(CE-3229)*

**Landed at:** `backend@ba606a554` ([CE-3229](../Blueprint_Issues_Tracker.md)); your `f848905c1` merged in at `backend@4d89561af`.
Design + as-built: [Civilians §7.8 / §7.8a](../../DESIGN_Civilians_And_Crowd_Control.md).

| what you can call now | what it answers |
|---|---|
| `Armament.Judge(view, observer, man)` | `Nothing` · `Unidentified` · `IdentifiedLethal` — a combatant on STATUS (today's test), a `Civilian`-class man of any side or a neutral non-civilian by what `observer` can identify (300 m × size × light × optics, capped by visibility; a reach item only within its reach). ⭐ The ladder (`CE-2239`) reads this |
| `Armament.Judge(world, observer, man, seeing)` | the same with explicit `SeeingConditions` (binoculars until B-12 lands, a test's night) |
| `Armament.IsCombatant`, `ArmedByStatus`, `LethalInHand` | the status branch, today's test, and the truth form (non-lethal rounds and dispensers arm nobody) |
| `Armament.ConductJudgements` | the AW-24 counter — process-wide; ⚠ a rail that reads it must run in a non-parallel collection (see `ArmamentTests`) |

**① Your "27 readers" are 27 READS, and only TWO mean "armed"** — `ThreatDanger.Of` (the target) and `ThreatDanger.OwnStrength` (self);
both now route through `Armament`, so every threat reader downstream of `ThreatDanger` follows. The other 25 ask *can this mount fire / how
much ammo* (executors, fire processing, weapon choice, squad ammo, the ammo HUD) and keep their test. The figure (`PersonBody.PoseOf`) asks
*does he hold a weapon* and stays too. Full table in §7.8.

**② `ua-armed-status` does not exist** (§5c lists it ⏳). Its in-process twin is `ArmamentTests.CE3229_AnEnemyRiflemanAtNight_…`
(status at 300 m at night, danger 1, the counter unmoved). If you build the live scenario, it should pass unchanged.

**③ Your new twin `AW20_ArmedShield_…` is RED on your own branch** — `origin/behaviors@f848905c1` in a clean worktree, 2 of 2:
*"① he holds with a civilian in the line at least once; held 0 frames, rounds 14, through a civilian 0, insurgent hp 0"*. Same numbers on
the merged `backend`. No civilian was hurt, so the hold does not misfire — the scenario never puts a civilian on the line while he
fires (geometry or timing). Yours to fix; it does not block B-8.

**Gates:** `ArmamentTests` 9/9 (red-proved twice) · `Fdp.Toolkits.Tests` 3763/0/1 on the merged tree · twins U1 posture, CivNeutralGate,
WindowDuel, UniversalSoldier, CE2073 green · U2 threat ranking 4 of 5 (the red is CE-2244, seen once even alone — noted in that row).

**Next from backend:** B-8 `CE-3230` (the hands mount + `Indirect` on 6037 / 6038).
