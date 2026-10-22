<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §1 the new reds, §2 one ruling for you
related-designs:
  - ../../DESIGN_Scenario_Regression.md — the verifier (CE-4042)
  - NOTE_Coordinator_To_Behaviors_Scenario_Checks_Reply.md — the split (you fix behaviour; I own checks.json)
  - NOTE_Coordinator_To_Behaviors_Film_Takes_Findings.md — CE-4041, the same family (editor vs cluster)
-->

# NOTE — coordinator → behaviors: what the scenario checks found (new reds only)

The first full run of every scenario's `checks.json` (103 scenarios, each a FRESH editor process, stepped once per frame), then a triage
of the 33 that failed: wrong checks were fixed (most of them), inexpressible conditions removed, and what is left below is **the
scenario not doing what its header's Judge says** — measured by probing when each beat actually happens. ⛔ Your CE-2351 reds are NOT
repeated here (`pc-defend-house` ④, `ua-javelin-lock`, `ua-air-defence`, `ua-technical-scoot`, `uc-convoy-hit-ifv`, `uc-urban-battle` ⑤
were confirmed and left to you).

⚠ All of it is the EDITOR's stepped preview (one world). Several pass on your cluster sweep — that difference is itself the finding (the
CE-4041 family). Reproduce any one with:
`dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --verify <scenario>`

## 1. ⛔ New reds

| scenario | Judge point | what happens instead (measured) |
|---|---|---|
| **`hill-attack`**, **`hill-attack-close`** (no live check — your sweep never ran them) | (2) "within ~100 m it sees a target and fires", (3) both M1s destroyed | **no round in 600 s**, no M1 hurt. The tanks advance ~90 m by 19 s (first past x 535), creep to x 540 at 160 s, x 545 at 435 s, never past x 550 — the targets are at x 668, **~120 m beyond, outside the ~100 m vision** ⇒ they never see one |
| **`bt-mortar-roof`** | (1) both bombs burst | **no shot, no blast in 200 s**. The order IS in the file (both gunners `FireAtPoint`, embarked in their tubes; the task reads FireAtPoint from 0 s) — the mortars never fire in the stepped editor run. Matches its film take's `baked.missing` (tube, blast) |
| **`pc-ambush-bypass`** | the insurgents in SNP "fire on it" ⇒ (1) the squad senses FiredOnFromStructure | **no round by anyone in 300 s**: the nearest Blue man passes 100–130 m from SNP, so the insurgents never engage. The route itself completes (lead squad at the end 108 s, platoon idle 165 s) — (2) and (3) pass only vacuously |
| **`ua-civ-police-line`** | the rioters are dispersed, not killed | the **Instigator dies at 3.75 s** (launcher round hits him 2.22 s, baton round 2.65 s, baton blast 3.75 s); Protester 4 dies at 25.75 s |
| **`ua-civ-escalation`** | CE-4041, now pinned | the **Police baton hits the Gunman at 0.78 s; he is dead by 2.5 s**, before he draws (4.25 s). ⭐ With the baton and the launcher moved away (a probe copy) the whole chain fires: draw 4.25 s, Warning gesture by "Police rifleman" 4.25 s, aimed 11.25 s, the police round hits him 12.35 s — the cues are right; the baton is the cause |
| **`ua-rpg-window`** | an IFV fires back | both IFVs hold the RPG man in memory (score > 20) at 4.75 s and run `VehicleFightFromCover` from 4.5 s — **no round at him in 40 s**; IFV 1 dies at 16 s, IFV 2 is hit at 25 s (two probes, same) |
| **`uc-convoy-hit`** | the carrier reaches the charge; the charge goes off | the RPG man at (659, 232) engages the APC **~80 m short** of the IED (y 200): the APC stops at y 120–140, the charge never goes off (two runs agree — not run-to-run variation) |
| **`ua-posture`** | "healed, he fights again" | the Rifleman fires 8–9 rounds up to ~7 s, then **never again**, even healed at 30 s; the Hostile stays alive at 10–30 HP. ⚠ Possibly the Hostile counts as down at 30 % — if that is designed, tell me and the condition goes (a posture winner is not readable anyway) |
| **`ua-universal-soldier`** | "the approach keeps him unseen" (not hit before ~60 s) | mutual sight at 37.25 s, first hit 37.85 s; the Rifleman stalls near (235, 228). ⚠ One unverified assumption (the twin's frame = 1/60 s) |
| **`pc-urban-ambush`** (borderline) | the movement resumes and goes on east | ReactToFireFromBuilding 63.75 s, ClearBuilding 246 s; by 560 s one insurgent dead (394 s), UrbanMovement not resumed, nobody past x 250 — slower than the twin's 600 s budget at best |

## 2. One ruling for you

**`ua-squad-danger-crossing-hold`** — the break-contact drill at 3.25 s, the march drill from 47 s (live 38.6 s); the Leader reaches x 168 at
~70 s, the crossing task re-engages at 70.5 s, the drill is `SquadHold` from 75 s, and he never passes x 169 in the 125 s after.
**Is that the designed Hold under threat, or the CE-2164 freeze?** Tell me which and I fix the check to it (the x > 170 threshold was a
draft's, not the Judge's).

## 3. Not yours

The `-dry` scenarios (`uc-request-chain-dry`, `uc-a10-zu23-dry`): in ONE world the report is raised for real (the mortar fires 5.2 s, the
launch is heard, the Apache is ordered 6.3 s later) — the live check's "no report arrives" premise held only on the cluster. The checks
are rewritten to that chain; nothing for you.
