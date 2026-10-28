<!--STATUS
state: LIVE — coordinator → ui: review of CE-4096 (`14281300c`); one small follow-up, nothing blocks CE-4097
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.6 the start gate as built
  - ../PLAN_Run_Modes_And_Content_Build.md — §2 CE-4096's done-when; §4 R-4099 (the 0.5 s settle accepted, the load stays two requests)
-->
# NOTE — coordinator → ui: CE-4096 reviewed

✅ **Accepted** against the plan §2 *done when*: one `StartGate`, one `StartSurfaceWindow`, composed once in `LocalWindowController` for every
runner host (Replay-Browser-only exempt, with a rail); the settle rails cover the two-request load (R-4099, user-approved); the switcher refuses
while `Start` shows; the bar holds only *allowed in Idle* items; AQ108 §2.6 carries the as-built. The tests assert the behaviour, not helpers.

## ⭐ One follow-up (small — fold it into your next commit, no separate batch)

| what | where | why |
|---|---|---|
| the gate rebuilds every subsystem's debug provider EVERY FRAME | `ClusterRestReader.From(subsystems)` → `CreateDebugProvider()` per subsystem, called from `LocalWindowController.TickStartGate()` each frame | `CreateDebugProvider()` is a factory (`EcsNodeDebugProvider.For` allocates ~20 delegates + a `SubsystemDebugProvider` per call; the Replay Browser builds a new one too) — it is meant to be called once (`ClusterDebugApiComposition.cs:25`). ⭐ Build the providers ONCE in `ComposeStartSurface` (the delegates read the node lazily, so a provider built before the node boots still answers later) and fold those each frame. A rail: N ticks ⇒ each subsystem's `CreateDebugProvider` called once. |

ℹ When CE-4098 lands, *Open recording* on the start surface should also be marked *allowed in Idle* (the rail
`CE4096_BothHostShapes_MarkTheSameScenarioItemsAllowedInIdle_…` is where its set grows).

_coordinator lane, 2026-10-28_
