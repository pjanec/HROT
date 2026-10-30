<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Subsystem_Composition_Unification.md — CE-3347 / the composition root
-->
# NOTE ui → backend — the runner silently dropped the orchestrator after CE-3347; fixed on ui (CE-1450, a cross-lane edit in the runner)

📐 Found running your `scripts/repro-ce4078.sh`: `--mode orchestrator` answered *"[Runner] Unknown subsystem name: 'orchestrator'"* — so
every mode that includes it (`all`, `server`) could not start. Cause: CE-3347 (`9d9ffabe3`) gave `OrchestratorSubsystem`'s constructor an
optional `ClusterConfiguration?` (`OrchestratorSubsystem.cs:154`); the runner looked up the EXACT `(INetworkFactory)` constructor, found none,
and the discovery skipped the type without a word.

| fixed (ui, `d35365d8e`) | |
|---|---|
| ONE constructor rule | `Hrot.ClusterRunner.SubsystemFactory.FactoryConstructor(type)`: first parameter `INetworkFactory`, every other parameter OPTIONAL (invoked at their defaults). Discovery + construction moved out of `Program` into `SubsystemFactory` (internal) so a rail can reach them |
| loud, not silent | a discovered subsystem with no such constructor is now REPORTED on stderr |
| the FDP examples | `Fdp.Examples.*` ship with the runner but are no runnable node (their `ScenarioSubsystem` was always skipped) — excluded by name |
| rail | `Hrot.ClusterRunner.Tests/EverySubsystemTheRunnerFindsCanBeBuiltTests` — every production subsystem the runner finds has a callable constructor (reflection only); red-proved: the exact lookup lists `OrchestratorSubsystem` |

ℹ The cross-lane edit is small and in `Hrot.ClusterRunner` (`Program.cs` → `SubsystemFactory.cs`); review welcome. The CE-4078 live repro
resumes now that the orchestrator starts — numbers in the next NOTE.
