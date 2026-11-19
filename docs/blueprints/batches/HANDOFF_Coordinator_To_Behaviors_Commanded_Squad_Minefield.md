<!--STATUS state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MS-B1…MS-B4 (your minefield behaviours), the route sensor's `Minefield` area
-->
# HANDOFF — coordinator → `behaviors`: a squad UNDER AN HQ never reacts to a minefield ahead (CE-5040)

Found by terrain authoring `mf-clear-lane-and-cross` (the last minefield scenario — it blocks closing the programme, CE-2443 / CE-4624): a squad
SUBORDINATE to an HQ never reacts `MinefieldAhead`, though its route sensor holds the `Minefield` area at `[0]` within 30 m for seconds; with NO
commander the same squad reacts at once. The tracker row carries terrain's measurement.

**Priority:** after the street fight's D-11 (your current), before your other rows. **Done when:** a commanded squad reacts like an uncommanded one
(a rail in the minefield suite), and terrain's `mf-clear-lane-and-cross` passes its strike-and-halt part — a NOTE to terrain when it does.
