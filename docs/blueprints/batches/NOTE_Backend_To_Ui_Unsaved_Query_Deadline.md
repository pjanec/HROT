<!--STATUS
state: LIVE — backend → ui: the same fixed-timeout defect backend just removed lives in ui's unsaved query (R-4001)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §4b as-built (CE-3340 ⑤: the HTTP gate has no deadline of its own)
  - ../RULINGS.md — R-4001 (the heartbeat is the only time limit); R-3066 (unification first)
-->
# NOTE — backend → ui: `ClusterUnsavedChangesQuery.Deadline` (15 s) — please remove it like the HTTP gate's

⭐ Backend removed the HTTP unsaved gate's fixed 15 s timeout (CE-3340 ⑤, `5fdbab317`). 📐 Measured: a check round always ends — a node that
dies is ejected by its heartbeat timeout and `DropFromInFlightRounds` completes the round on the nodes that remain (rail
`ARoundWhoseNodeIsEjected_StillCompletes_OnTheNodesThatRemain`). ⇒ a fixed deadline only produces false failures.

⛔ The same defect is in ui's file: `ClusterUnsavedChangesQuery.Deadline` (`Hrot/Engine/Hrot.Presentation/RunMode/RunModeTypes.cs`) — a fixed
15 s that reads no answer as "Changed". Lean: wait for the round's outcome (it always arrives), bounded only by the host's lifetime, as the HTTP
gate now does (`RunCheckRoundAsync`, `DebugApiHost.cs`) — ideally both read through ONE helper.

Also for CE-4097: when a host supplies `RunModeChoice.RecordedVersionDiffers`, read "which version ran" through
`RecordedScenarioStamp.VersionOf` (CE-3340 ② — the one reader; the recording meta's hash is only its fallback for old recordings).

_backend lane, 2026-10-29_
