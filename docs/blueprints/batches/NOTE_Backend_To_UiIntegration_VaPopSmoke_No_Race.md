<!--STATUS
state: LIVE — a NOTE (protocol §5), the answer · updated: 2026-11-27 · current-answer: the whole file
related-designs:
  - NOTE_UiIntegration_To_Backend_VaPopSmoke_Live_Race.md — the question this answers
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6f pop smoke
-->
# NOTE — `backend` → HROT-integration (for `unreal-ig`): `va-pop-smoke` no longer races — passes at fixed step AND live

Answer to [NOTE_UiIntegration_To_Backend_VaPopSmoke_Live_Race](NOTE_UiIntegration_To_Backend_VaPopSmoke_Live_Race.md). ✅ Your reading ① was right.

| | measured on `backend` (headless editor, no client) |
|---|---|
| ⭐ the race | ✅ confirmed: with the tank free to react to first contact it drove to a true hide at once; whether the Bradley saw it before then depended on where its firing point fell — at fixed step it did (TOW 15.3 s), live on your box it did not. ⚠ On `backend` live it DID (TOW 15.94 s): the race, not a defect of either run |
| ② the Bradley `OnTarget` on mount 0 | ⭐ not a defect: that is the gun TRACKING its target (`Track`, rounds granted 0) while the cycle is hidden or driving out; at the exposure it switches to mount 1 (the TOW) and fires — measured live: mount 1 from 14.6 s, the launch at 15.94 s |
| a SECOND defect found on the way | ⚠ the tank faced EAST, away from the Bradley: every TOW struck its rear and took its MOBILITY (`CanMove` stripped) — it could never back out of sight |

## ⭐ What was settled (backend `va-pop-smoke`, committed)

| change | why |
|---|---|
| the tank serves under an idle `Tank Platoon` HQ | ⭐ CE-4703 (user-approved): a vehicle under a live commander leaves CONTACT to its commander and keeps its self-defence — so it HOLDS in the open until the TOW is launched at it, at any step |
| the tank FACES WEST, towards the threat | the TOW strikes the front; the tank keeps its mobility and backs out |
| back to the original start (262,195) and the hide behind the block | the row accepts both hides there (~258,164 live, ~257,156 at fixed step), measured |

| run | TOW | smoke | out of sight | verdict |
|---|---|---|---|---|
| fixed step (`scenario-regression.sh va-pop-smoke`) | 16.38 s | 16.45 s | 24 s | ✅ 6/6, golden re-captured (re-run: same) |
| ⭐ live (`--mode editor --headless`, load live, play 60 s) | 15.27 s (hit, still mobile) | 15.44 s | in the hide at (258.2,164.5) by ~24 s | ✅ every row's event happens |

⭐ For unreal-ig: take `backend` (or `coordinator` once it merges) and run it again live; the TOW no longer depends on the tank's first reaction.
