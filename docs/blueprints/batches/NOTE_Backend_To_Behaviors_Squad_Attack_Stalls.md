<!--STATUS
state: LIVE — an observation for behaviors, cause NOT measured
updated: 2026-11-22
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md §5a-2 — the commander seat whose orders showed it (CE-4679, L-2)
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md — owns the squad orders a platoon commander gives (§7a)
-->

# NOTE — backend → behaviors: a squad's `Attack` stalls in `llm-platoon-attack` (seen through the LLM commander, CE-4679)

⭐ Not a request to change anything yet: three runs on one scenario show the same two stalls, and the cause is **not measured**. The command chain is
yours; the seat only gives your orders (`CommandOrders.Order(…, "Attack", {"Position": house})`, origin Superior).

| what | seen | runs |
|---|---|---|
| ① a squad given `Attack` on the house from ~230 m away (the start, `(300,140)` → house `(64,108)`) **does not move** | Bravo's SITREP still says "at start" 60 s after the order (`00:01:35 ORDER Attack Bravo Squad → house: given` … `00:02:33 BRAVO SQUAD: sitrep … at start`); it moved only when re-ordered `Move` | Haiku run 2 and run 3 (both commanders hit it) |
| ② a squad attacking the house from the Low Wall (~110 m) reports **Set** at the objective and stays there — no assault, no fire after ~2:20 | `00:02:00 ALPHA SQUAD: progress, Set, at objective`; re-ordering `Attack` at 4:30 gives `Set` again at once (`00:04:33 … progress, Set`) | Haiku runs 2, 3; the scripted run has the same stand-off (Alpha Set from 1:50 to the end) |
| ③ `SupportByFire` (Attack + `HoldBack`) given at the start stays ~60 m from the start (~190 m from Red) | scripted runs: `BRAVO SQUAD: sitrep … at 60 m SW of start` from 1:33 to 7:00 | the scripted runs |

**Reproduce:** `scripts/scenario-regression.sh llm-platoon-attack` (scripted mind; ② and ③), or the scenario with `"CommanderSeat": {"Mind": "External"}` and
an outside mind on `/commanders/{id}/act` (① — order `Attack` to a squad at the start).

⚠ My guesses, unmeasured — do not build on them: ① an Attack whose position is beyond some engagement / survey range waits for a picture that never
comes; ② "Set" is the attack's support-position arrival and the assault waits for a second condition (a commander's go?) that a body-less platoon with
`InertSop` never gives.

## 2. ⭐ A second, unrelated ask — a report cannot say "seen" or "only heard" (CE-4680 B-5)

`CommandReport` (FDP/Toolkits/Fdp.Toolkits/Squad/Command/CommandReports.cs) has Size, Activity, Equipment, ObservedAt — ⛔ but **no seen / heard bit**.
`Radius` cannot stand in for it: it is the group's spread on a SEEN spot (`StaffSystems.cs:243-260`) and the sound's uncertainty on a HEARD one
(`StaffSystems.cs:307`). The LLM commander's design (§5b B-5) wants the briefing to say *"heard, not seen"*. ⇒ ⭐ ask: one byte on `CommandReport`
(and `ReportedElement`) — `Source: Seen | Heard | Both` — set where the report is built. Until then the seat shows size, activity, equipment and age only.
