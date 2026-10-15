<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file. §1 is the queue, in order; §3 the traps that cost this session time.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/RESUME_Backend_Lane.md — the lane's state doc; its §00 points here while this handoff is live
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Bomb_Release_Latency.md — item 1 (CE-2180), the behaviours lane's frame
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §8a OWNS the bomb and its release cue (item 1 folds there)
  - docs/DESIGN_Building_Interiors.md — §3k.1 OWNS the one fragment law (CE-3203, just built)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5b OWNS how each weapon type breaks (CE-3203)
  - docs/blueprints/batches/REPORT_Backend_Fragment_Law_And_Weapon_Damage.md — the last report; its §4 holds the leans the user has not answered
-->

# HANDOFF — backend → backend: the continuation (a new session takes the lane)

**For:** a new backend session on branch **`backend`**. **From:** the backend session that built CE-3203. ⭐ **You are the ACTIVE backend
session; the sender is ON HOLD until you finish** (🔒 user, `2026-10-15`: *"please write a continuation handoff for another backend
session, you will stay on hold until the other one finishes."*). Dispatched at the commit that adds this file; scope frozen there.

## 0. Start here

1. RULE ZERO: read [`RULINGS.md`](../RULINGS.md), run `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`, and open the
   reply with the per-lane `DESIGN BRIEF` (lane: backend).
2. `git fetch origin backend ui behaviors` → you start at **`backend@`** the commit that adds this file. Then **merge `origin/behaviors`**
   (it holds `5ac98d77a` — the CE-2180 handoff + an orders fix — and `57601eacd`, the IF-4 scenario) and `origin/ui` if it moved. Name both
   shas in a `chore: started …` marker commit and push it before writing code (rule 1b).
3. Ids: tracker **`CE-3204`** is the backend's next free; rulings **`R-3008`**. Allocate only from the backend blocks (`CE-3000…3999`,
   `R-3000…3999`); new tracker rows go at the END of the backend area (after `CE-3203`).

## 1. The queue, in order

| # | item | what it is | state · lean |
|---|---|---|---|
| **1** | ⭐ **CE-2180** — [HANDOFF_Behaviors_To_Backend_Bomb_Release_Latency.md](HANDOFF_Behaviors_To_Backend_Bomb_Release_Latency.md) | a Mk-82 lands **20–27 m long** live: the cue fires on the BRAIN node, the bomb starts from the carrier as the SimHost has it when the intent ARRIVES (≈ 0.14–0.18 s × 150 m/s) | **not started.** Step 1 is yours: MEASURE first (the intent's publish time vs the spawn time, the carrier's position at each), then the design step in Air Operations §8a (UML), then build. Behaviours' lean: the intent carries the release's sim time (+ carrier pose / velocity if the SimHost cannot rebuild it); `FireProcessingSystem` starts the bomb from that state and flies it forward to now (`BombBallistics` integrates a free fall). Acceptance: ≤ 10 m live (they re-run `ua-air-strike`); a rail where an intent read N frames late lands the same |
| **2** | **CE-3203 follow-up C4** — a weapon CARRIED by a soldier | is a Javelin on a man's back struck when he is? `PhysicalPresence.IsHidden` hides an `IsEmbarkedTag` with `Exposed = 0`, set from the carrier's seat `ExposedMask` (`Embarkation.SeatExposed`) — **not measured for the soldier types** | measurement only (no user decision needed): build a rifleman + carried Javelin in the area-effect suite and read the record. Fold the answer into Weapon Systems §5b's as-built line; ask the user only if it is hidden |
| **3** | CE-3203 leans **C1, C2, C3, C5** | body-armour coverage 35 % · the per-type weapon numbers · a shaped charge fragments 2 % of its body · the 25 / 30 mm HE numbers unchecked | **waiting on the user** — ⛔ do not change them unasked. Their table: [the report §4](REPORT_Backend_Fragment_Law_And_Weapon_Damage.md) |
| **4** | the open backend rows | the lane's backlog | the tracker's backend area (rows `CE-3xxx` marked `[ ]`) and [RESUME_Backend_Lane.md](../RESUME_Backend_Lane.md) §2 "waiting on the user". Natural next ones close to this work: **CE-3200** (the DDS path request drops `TurnRadius`, so a scale-out solver plans a vehicle's corners sharp) and **CE-3198** (the NLAW's overfly top attack arrives on the face). Ask the user before picking past item 2 |

⛔ Not yours: **CE-1110** (ui — answered by `c313ba0d7`, ui may close it) · **CE-1109** (ui → behaviors, FM-4's lay time).

## 2. What the last session changed that you will meet

| change | where it bites |
|---|---|
| `WarheadDto.FragmentRadiusM` now means **the law's hazard radius** (where a standing man in the open expects < 1 damage): M67 54 m, 81 mm 131 m, Mk-82 442 m | anything that reads it as "the fragments' reach" — its one engine reader is `AreaEffectSystem` (a cull) |
| a man's fragment damage = hits × wound per hit × armour share; **AE5's linear fade is gone** (drag `e^(−r/λ)`) | live demos show fewer casualties at range — a man 121 m from a Mk-82 takes 25, not 1 253. ⚠ Relevant to item 1's live run: the bomb's burst, not its effect, is under test |
| `WeaponHold.Destroyed`; `PrimaryMount` / `WeaponOf` / `WeaponChoice` skip a broken weapon system | a host whose only weapon is broken has no primary (mount 0, nothing to fire) |
| `BdcTkbCatalog.WeaponSystemType(…, WeaponBreaks breaks, …)` | a new weapon type MUST state its `WeaponBreaks` (`NedTkbBuilderCombatTests.CE3203_EveryWeaponSystem_StatesHowItBreaks` lists all 13) |

## 3. Traps that cost this session time

| trap | do this |
|---|---|
| a red-proof edit `return;` is CS0162 (unreachable code is an error here) — the build fails and `--no-build` runs the STALE binary, printing green | use a non-constant guard (`if (x.Index >= 0) return;`) and read the build output every time |
| build the PRODUCTION project, then `test --no-build` the test project ⇒ the test `bin` holds the old dll | build the TEST project (`CLAUDE.md`, the second stale-binary trap) |
| a background job's output file ends with `[exited with code N]` | never `tail -1` it, never poll — the harness notifies you |
| the tracker conflicts on the air rows when both lanes append to one long line | keep the side that already folds both lanes, append the other side's NEW sentence; never re-split by `" · "` (the segment boundaries differ and you duplicate text) |
| the drag in the fragment law is NOT pinned by a rail on its own (removing it moves a man ≤ 40 % inside 2 × casualty radius, inside the bands) | if you touch the drag, add a rail that pins it (e.g. a hull's penetration at range) |
| the stop hook nags about uncommitted work | commit only gated work; answer the nag with why |
| codebase-memory MCP drops mid-session | use its CLI (`codebase-memory-mcp cli …`) and say so in any inventory claim |

## 4. Report back

The gate report contract (`CLAUDE.md` rule 8) for each item. Progress on CE-2180 as an appended dated line on its row (the behaviours lane's
row — say it in the commit). When you stop, update [RESUME_Backend_Lane.md](../RESUME_Backend_Lane.md) §00 and tell the user, so the
on-hold session can merge `backend` and resume.
