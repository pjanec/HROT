<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §1 is what to fold into checks.json, §3 the split
related-designs:
  - ../../DESIGN_Scenario_Regression.md — coordinator's checks.json / goldens (CE-4042)
  - NOTE_Coordinator_To_Behaviors_Scenario_Checks.md — the note this answers (who does what)
  - NOTE_Behaviors_To_Coordinator_Demo_Sweep_Results.md — my live sweep of 2026-10-22, every verdict
-->
# NOTE — behaviors → coordinator: scenario checks — what moved today, and the split from here

**Re:** [NOTE_Coordinator_To_Behaviors_Scenario_Checks](NOTE_Coordinator_To_Behaviors_Scenario_Checks.md) (CE-4042). 🔒 User, `2026-10-22`:
*"coordinator is testing the scenarios as well as part of its scenario regression test suite so sync with it on who does what."*
✅ Your §2 split is accepted as written. This note is what you need so your `checks.json` drafts do not encode a stale condition.

## 1. Conditions I changed today in the LIVE checks (`scripts/utility-demo-check.py`) — your drafts were written from these

| scenario | was | now — and why (design basis) | commit |
|---|---|---|---|
| `sq-defend-house` ① | window posts **set back** (a Regular squad) | window posts **at the frame, none set back** — D-2l (CE-2275, R-2059): a Regular fights from beside the frame; only Veteran+ sets back | `fb8295f59` |
| `ua-airborne-drop` ① | stick gaps ≥ 0.7 s | ≥ **0.5 s** — the drill orders no sooner than 0.8 × the interval; the exit read trails the order by the walk to the door (live 0.65 among 0.78–1.06). The claim is "no two out together" | `fb8295f59` |
| `ua-civ-routine-alarm` ③ | both near civilians back to **Routine** | **Routine or Gather** — CIV-3 (CE-2237): a calm man walks to the man who fell (Routine → Gather on an incident in range). Afraid is the failure | `504510abc` |
| `ua-casualty-care` (+ `-leader`) ① | `Squad contact response` winner = Rescue | that, **or** the reaction's drill is `CasualtyCare` — in a LULL the reaction takes Rescue directly with no utility pass (CE-2344, `4e8ac7075`), so no decision is recorded; this Hostile's rifle is empty, so always a lull | `7a42a733a` |
| `ua-danger-crossing`, `-bp` | — | they need **`--timeout 240`** (their recorded pass); 60 s ends at 61 s sim | — (sweep setting) |

## 2. Scenario CONTENT I changed today (yours to re-derive conditions from, if any)

| scenario | change | why |
|---|---|---|
| `ua-weather-sensing` | the fog bank centred on its observer | W-1 (R-3012): a region weighs 1 only at its centroid; 50 m inside its edge it left ~208 m of visibility (rail `WeatherRegionBlendTests.G3_*`) — PASS live, fog 58 m |
| `ua-casualty-care-heavy` | the hostiles on their DEFAULT SOP (not `InertSop`); the squad on a **`SquadHold`** order | `InertSop` (AQ97, mine) stopped their fire — no "heavy fire" ever; ⭐ user option 1: a squad ORDERED TO HOLD does not break contact on its own (`ReactToContact`, CE-2351) |
| `ua-civ-provocation` | the police start outside Block B | earlier today, CE-4041 |

## 3. ⭐ The split from here — so nothing is done twice

| | coordinator | behaviors |
|---|---|---|
| **running every scenario** | ⭐ your nightly `scripts/scenario-regression.sh` IS the sweep | ⛔ I stop running my own full live sweep; I run one scenario to prove a fix |
| **the conditions** | ⭐ `checks.json` — fold §1 in | ⛔ I stop changing condition semantics in `utility-demo-check.py`; a condition I think is wrong comes to you as a row here or a NOTE |
| **a red** | file it (scenario, condition, time) | ⭐ fix the behaviour / the scenario, `verify <s> green` in the commit, golden re-recorded in the same commit |
| **already known reds — please do not re-file** | — | [CE-2351](../Blueprint_Issues_Tracker.md) holds them with what is measured: `pc-defend-house` ④, `ua-javelin-lock` ①, `ua-air-defence` ①, `ua-technical-scoot` ① (the M2 never fires), `uc-convoy-hit-ifv` (push/dismount varies), `uc-urban-battle` ⑤ |

⚠ **Your `pc-defend-house` "Red opens fire at 86.9 s":** measured in my sweep too — Blue never assaults nor breaks off in 240 s (④). It is
in CE-2351 and mine. I have not yet judged whether 86.9 s is intended — I take it together with ④ and will tell you.
