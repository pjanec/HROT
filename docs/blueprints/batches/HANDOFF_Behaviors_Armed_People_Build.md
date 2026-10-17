<!--STATUS
state: LIVE — behaviors → the next behaviors session: build the BEHAVIOUR half of the armed-people design and finish the civilians' open items.
updated: 2026-10-17
current-answer: the whole file; start at "Order".
stale-below: nothing.
known-rot: none.
known-conflict: none.
supersedes: docs/blueprints/batches/HANDOFF_Behaviors_Civilians_Takeover.md (its items 1–3 are done; its item 4 continues here).
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS everything here: §5h armed people (AW-1…AW-24, R-2025…R-2029, its six diagrams), §5c the demo table (every feature's acceptance), §5f crowds and §5g protest as built, §4 CV-8/CV-13a/CV-14.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — backend's half (B-7…B-14) and notes 1–4 (B-7 amended, B-15, B-16, the simple-path bounds).
  - docs/designs/weapons/DESIGN_Weapon_Systems.md · docs/DESIGN_Weapon_Layers.md · docs/DESIGN_Entity_Interactions.md · docs/DESIGN_Sensors_And_Doctrine.md — the neighbours §5h links both ways.
-->

# HANDOFF — behaviors: build the behaviour half of armed people *(and close the civilians' open items)*

RELEARN

**Dispatched at:** the commit that adds this file, on `behaviors`. Work ON `behaviors`. ⭐ First: read `RULINGS.md`, then merge
`origin/backend` and `origin/ui` (lane rule 7) and name them in your first commit. Backend may have landed some of B-7…B-16 by then —
`git log origin/backend --grep "B-7\|B-8\|B-11\|B-13\|B-15\|Armament\|HeldItem\|RoundPickUp"` tells you which, and that decides how far
down "Order" you can go.

## The user's standing asks *(verbatim gist)*

- *"Go autonomously, write handoffs to other lanes as needed."*
- ⭐ *"A demo scenario for each civilian feature, with a clear success condition so it is usable as a test."* — each feature = a scenario,
  a live check (`python3 scripts/utility-demo-check.py --launch <name>`) and an in-process twin in `PostureScenarioTests`. The table is §5c.
- *"I would like to prevent overcomplicated decisions where they are not necessary."* (AW-24) — ⭐ `ua-armed-status` and every soldier
  rail stay green with expectations unchanged; a soldier against an enemy combatant pays ONE status lookup.

## Where it stands *(2026-10-17)*

| item | state |
|---|---|
| `CE-2236` panic / shelter / inside / cower | ✅ live 15/15 + 11/11, both twins green, the 4 handoff mutants red |
| `CE-2237` crowds (§5f) | ✅ built; `ua-civ-gathering` live 7/7 + twin; `ua-civ-contagion` twin green, **live 1 of 6 ⇒ `CE-2246`** |
| `CE-2240` protest (§5g) | ✅ built; `ua-civ-protest` live 7/7 + twin |
| §5h armed people | ✅ DESIGNED and ruled (AW-1…AW-24); backend framed (B-7…B-16); **no behaviour code yet** |
| ids | behaviors next free **`CE-2247`**, **`R-2030`** |

## Order

⭐ **Part A — buildable NOW, no backend dependency.** Each: design check against §5h's diagrams first (UML exists — add an as-built
sub-section when you deviate), rails, the §5c demo, live + twin, mutants.

| # | item | design basis | demo (§5c) | notes |
|---|---|---|---|---|
| **A1** | **`CE-2246`** — contagion reliable live | §5f | `ua-civ-contagion` 3 live passes | the row has the measured trace and three candidates (fight length, poll lag, link drift); re-run the share-1.0 mutant against the fixed cap rail |
| **A2** | **AW-20 — a neutral in the line or the spread holds the shot** | §5h AW-20, the decision graph's last node | **`ua-armed-shield`** with an `Insurgent` (2003, a combatant: status target) firing from a civilian crowd — no B-7 needed | a sibling of `LineOfFire.BlockedByFriendly` at its four callers: `AimAndFireExecutor.cs:245, 416`, `FireAtPointExecutor.cs:236`, `WeaponTaskDispatcherSystem.cs:89`; it is the one new rule a SOLDIER obeys (AW-24) — re-run the soldier demos |
| **A3** | **CV-13a — hands up holds fire** (`CE-2241` ①) | §4 CV-13a, AW-5 | the hands-up half of **`ua-armed-surrender`** (the laid-down half waits on B-10) | `AimAndFireExecutor.HoldOf` (`:561`) ⇒ `HoldSurrender` when `Gestures.Of` is HandsUp (B-6 built), even Hostile; ⛔ it never flips the side |
| **A4** | **the hostile act + the side flip + Riot + melee** (`CE-2238` part 1) | CV-4 as amended (AW-16), D5, §2.2 | **`ua-civ-melee`** | `HostileAct` needs B-16 for its KIND — build the act and the flip (`EntityWriteRouter`, `AttributeIds.Affiliation` — §7.1a's exact call) now and add the kind when B-16 lands; Protest → Riot on crowd anger (instigators raise it — CV-5); `MeleeAttack` publishes a Blunt `DamageAssessedEvent` on the victim's Health owner (the `EjectionSystem.cs:86` precedent; ⚠ a non-owner's hit is dropped — scenario casts are CGF-created, so CGF owns them) |
| **A5** | **AW-22 — the known-combatant mark, per unit** | §5h AW-22 | rails now; `ua-armed-blend-back` waits on B-11 | one byte per `TargetMemory` slot (`PerceptionComponents.cs:48-104`), set by a witnessed LETHAL act, passed through `SquadPerceptionMergeSystem`; read by the status branch for `Civilian`-class targets |
| **A6** | **`Roe.ForceLevel` {Warn, NonLethal, Lethal}** and the security forces' non-lethal weapon choice (`CE-2239` part 1) | CV-8, CV-11, AW-10, AW-18 | `ua-civ-police-line` once A4's riot exists | `Roe` (`Behavior/Components/Roe.cs`, R-200) grows the field; baton rounds `6036` and stun grenades `6037` already ride the security types 1201–1203 (§7.5a); self-defence overrides the cap |

⭐ **Part B — waits on backend.** Build each as soon as its B-item is on `origin/backend` (merge it first).

| # | item | needs | demos |
|---|---|---|---|
| **B1** | gather stones, pick up a landed throwable, throw (`ThrowAt` = `FireAtPoint` on the hands mount) | B-8, B-13 (+ B-14 for the canister) | `ua-civ-riot-stone`, `ua-armed-stones`, `ua-armed-gas-throwback` |
| **B2** | the ladder's identification and intent rows (AW-15…AW-19) | B-7 amended (notes 1, 2, 4) | `ua-armed-identify`, `ua-armed-self-defence`, `ua-armed-knife`, `ua-armed-celebration`, `ua-civ-escalation` |
| **B3** | draw / stow / conceal; the covert insurgent's trigger into `ArmedHostile`, reusing the soldiers' fighting behaviours | B-11 (stations, pick-up rifle/pistol) | `ua-armed-draw-aim`, `ua-armed-blend-back` |
| **B4** | surrender: lay weapons down | B-10 | `ua-armed-surrender` (the laid-down half) |
| **B5** | detonate a worn charge | B-11 (the charge type) | `ua-armed-bomber` |
| **B6** | the player-fratricide counter (AW-23: one friend killed or incapacitated, or two wounded in 60 s ⇒ flip; the instructor event) | B-15 (the shooter on the damage) | `ua-player-fratricide` |
| **B7** | CV-14 provocation — security forces raise a crowd's anger, force on a member spikes it, a fraction riots (`CE-2241` ②) | A4, A6 | `ua-civ-provocation` |

## Facts you will need *(measured this session)*

- **The runner and its integration tests may need a RESTORE.** In this container both `project.assets.json` dated 2026-10-03 and
  predated the 2026-10-07 `Animation.Fake` reference ⇒ the runner crashed at boot (`FileNotFoundException`) under `--no-restore` builds.
  ⭐ `dotnet restore Hrot/Runner/Hrot.ClusterRunner` and `…Integration.Tests`, then build.
- ⛔ **Never launch a live check while a build is running** — the first shelter run died that way.
- ⛔ **Mutants: restore from a backup copy, never `git checkout`** — the files usually carry uncommitted work. And never commit while a
  mutant is applied (the stop hook will ask; refuse until the run restores).
- ⚠ **A rail that compares against its own constant is tautological** — the contagion cap rail asserted `<= ContagionShare` and a raised
  share moved the bound with it. Assert the design's literal.
- **Twins: run each alone** (`CE-2244`: two test-town twins in one process interfere). `--filter "FullyQualifiedName~<name>"`.
- **Live checks:** read ammo BEFORE `/sim/play` (a 40 m fight is over before one sweep of 15 civilians), and sample from play; a 0.25 s
  HTTP poll misses few-frame states — the twin samples every frame and asserts the exact state.
- **Adding a system to `CgfLogicPack` changes `CgfLogicPackTests`' count** — backend had to fix it for `CivilianFearSystem` and
  `CrowdSystem`. Update that rail in the same commit.
- **Component ids:** `CivilianState` 399, `Crowd` 400, `AreaEffectCloud` 401 (backend). Grep `GlobalComponentIds.cs` on all lanes before
  taking the next.
- **Scenario network ids:** check them against every other scenario before committing (a script did it this session: 80xx, 96xx used now).
- **test-town:** 0–400 m, the Pond 298–365 × 300–365, every building solid (⇒ panic ends in Cower); open streets y 175–225 and x 175–225.
  **bt-range:** House A 100–110 × 100–108 (enterable), House B 160–168 × 100–110 (locked in `ua-civ-shelter`).
- Fear: small arms 0.45 × (1 − ½·d/100) per shot, fades 0.05/s; AlarmAt 0.3, PanicAt 0.6, CalmBelow 0.1. Contagion: toward 0.9 × a
  frightened member's fear within 20 m, rate 1.5 × (1 − ½·d/20); crowds link within 15 m, ≥ 3.
