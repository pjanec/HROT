<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — ⭐ §10 THE SPEC: the before / after components and the gates the compiler does not cover
  - ../../DESIGN_Civilians_And_Crowd_Control.md — the civilian readers
-->
# HANDOFF — coordinator → `ui`: SHARED CREATURE COMPONENTS — civilians and animals (CE-4628, + CE-2250)

🔒 User, `2026-11-19`: *"yes, hand it to ui. I hope it is largely a mechanical stuff, because we are not changing any logic, correct?"* — ⭐ correct:
NO logic changes; fields move between components. ⚠ But NOT all compiler-caught — the design §10 lists what is not, and each is a gate below.

## Priority
After CE-5039 (the run gate). One piece, no waiting on another lane (the street-fight work is soldiers' code — no overlap).

## The work (design §10 is the spec)
1. ⭐ **Enumerate first** — every reader and writer of each moved field: `search_graph` + `scripts/find.sh` (C#), and grep for the TEXT paths (scenario
   `checks.json`, goldens, the HTTP debug API, inspector / Watch, scripts). Put the counts in your status.
2. Build the shared components (`Fear`, `Home`, `GroupMember`, `Group`, `Mood`, `AnimalTasks`), move every reader, trim `CivilianState`,
   `AnimalState`, `HerdState`, `Crowd`; species from a per-type table. `Mood` is replicated (a new descriptor; ids from the free set — the design's §3
   census method in the vehicle-appearance design; measure on every lane branch) and builds CE-2250's civilian mood: the face / mood dot read it.
3. ⛔ Never a text search-and-replace for a C# symbol (CLAUDE.md ②) — Roslyn's rename for the renames (`AnimalHome` → `Home`, `HerdMember` → `GroupMember`).

## Done when (the gates the compiler does not cover)
- old saved scenarios and recordings with animals and civilians still LOAD (a rail per format);
- the text paths updated: `ua-civ-gathering` (`CivilianState.CrowdId`), `df-civilian-ordered` (`CivilianState.Fear` ×2) and any others the sweep finds;
- `Mood` reaches every node (a replication rail) and the face / mood dot / animal alert look read it;
- every touched behaviour system's rails pass under `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + the world guard;
- a civilian and an animal get `Fear` and `Home` on spawn (a rail per kind — no silent default);
- `Home`'s save policy chosen and stated;
- the feature scenarios green UNCHANGED in behaviour: the civilian (`ua-civ-*`, `civ-*`, `df-civilian-ordered`) and animal (`an-*`, `rabbit-hunt`,
  `war-party`, `bw-mounted-archers`) sets — goldens re-captured where the layout moved them, reported as a DIFF SHAPE (t = 0 layout only).
