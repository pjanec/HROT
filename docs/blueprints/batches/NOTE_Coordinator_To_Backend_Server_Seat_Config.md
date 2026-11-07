<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Seats_And_Hats.md — §2.2a (R-4164)
  - ../../DESIGN_Node_Features.md — F-7, the node config file layer
-->

# NOTE — coordinator → backend: one small extra item, CE-4305 (do it before or after P-1, your choice)

🔒 User, `2026-11-07`: *"In some use cases, those nodes are used by the instructor who shouldn't be allowed to edit. But in other use cases,
these nodes should be able to edit. This might go to the external config file with the author mode as the hard-coded default if not
specified in that config."* (R-4164, [Seats and Hats §2.2a](../../UX/DESIGN_Seats_And_Hats.md))

| | |
|---|---|
| ① | `CgfNodeBootstrapper.DefaultSeat` (`:209`) and `SimHostNodeBootstrapper.DefaultSeat` (`:332`) → `Seat.Author` (the fused server follows CGF) |
| ② | the runner's `--config <file.json>` gains `"features": { "<id>": {partial config JSON} }` — the same shape `--feature id={json}` takes; layer order: hard-coded default < config file < `--feature`. Today `HrotRunnerConfiguration.MergeFromJsonFile` reads no features (measured) |
| rails | a CGF node with no config is Author and its gate accepts an Edit-mode edit · `"features": {"station.seat": {"Seat": "Instructor"}}` ⇒ the same edit refused · `--feature station.seat=…` beats the file · a typo'd id in the file is refused like a typo'd `--feature` (F-18) |

Claim it in your status file like a handoff item. Tracker: CE-4305.

---
_coordinator lane, 2026-11-07_
