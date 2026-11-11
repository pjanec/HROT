# PoC — the kernel on a worker thread, the presentation pulled on the main thread

**Status:** proof of concept (coordinator, `2026-11-11`), branch `poc/threaded-kernel`. Not production code. Owning design:
`docs/DESIGN_Evaluation_Cost_And_Cadence.md` §13 (the option), §13.3 (the shape), §13.4 (the numbers), §13.5 (pros and cons).

**The question it answers:** can the ECS kernel tick the simulation on a worker thread while the application thread draws the map and the
UI at its own rate, from a snapshot — and what does that cost and break?

## How it works

```
worker thread (the kernel's thread)                        main thread (window, ImGui, input)
──────────────────────────────────────────                 ─────────────────────────────────────────────
ModuleHostKernel.Update(dt)                                each frame:
  Input      CommandIngressSystem — play the UI's            lease the LATEST published snapshot
             queued EntityCommandBuffers on World A          take the events queued since the last frame
             PopulationSystem — grow / shrink                run the PresentationModule's systems on it
  Simulation BrainSystem (busy work per entity)                GizmoPrepSystem — positions → point buffers
             MoveSystem (+ Bounce events)                      InteractionSystem — pick, drag (ghost),
  Export     PublishSystem → SnapshotPort.Publish:               release ⇒ a command buffer, submitted
               sync World A into a FREE pooled repository    draw (ImGui), release the lease
               copy this tick's events into the reader queue
               make it the latest
```

| class | role |
|---|---|
| `Simulation.cs` — `SimulationModule` and its systems | an ordinary synchronous kernel module; the only thing that writes World A |
| `SnapshotPort.cs` — `SnapshotPort` | publish into a free pooled snapshot (never one that is leased); lease / release; a per-reader event queue |
| `SnapshotPort.cs` — `CommandIngress` | the UI's write path: command buffers queued on the main thread, played on the worker's next Input phase |
| `Presentation.cs` — `PulledModuleRunner` | runs an ordinary `IEcsModule`'s systems on the main thread, on the leased view — the "thread-affine, pulled" strategy the kernel does not have yet |
| `Presentation.cs` — `UiStore` | the presentation's own state (selection, drag ghost, prepared primitives) — NOT in World A |
| `Program.cs` | the window, the two modes, the bench |

## Run it

```bash
dotnet build FDP/Examples/Fdp.Examples.ThreadedKernel -c Release
# interactive (sliders for entities and brain work; drag a point on the map)
dotnet FDP/Examples/Fdp.Examples.ThreadedKernel/bin/Release/net8.0/Fdp.Examples.ThreadedKernel.dll --mode threaded --n 10000 --work 200 --payload
# bench: one RESULT line (fps, frame p50/p95, sim tick, publish, drag→visible, events published / seen)
xvfb-run -a dotnet .../Fdp.Examples.ThreadedKernel.dll --mode threaded --n 10000 --work 2000 --payload --bench 10
# the baseline — the same modules, the kernel ticked inside the frame (today's shape)
xvfb-run -a dotnet .../Fdp.Examples.ThreadedKernel.dll --mode single --n 10000 --work 2000 --payload --bench 10
```

## What it showed (cloud VM, 4 cores, software GL — the drawing cost is not representative, the threading is)

| entities | brain work | threaded UI fps | single UI fps | sim tick | publish |
|---|---|---|---|---|---|
| 1 000 | 200 | 218 | 128 | 3.6 ms | 0.08 ms |
| 10 000 | 200 | 84 | 21 | 32 ms | 1.0 ms |
| 50 000 | 200 | 69 | 4.9 | 191 ms | 3–7 ms |
| 10 000 | 2 000 | 93 | 2.5 | 408 ms | 1.4 ms |

Troubles found: events must queue per READER, not ride on a snapshot (a skipped snapshot lost its events); the event history is read on the
kernel's thread only; queries are bound to one repository; the existing double buffer overwrites in place; a stale command throws on playback.
Details and the remedies: design §13.4.

## Known PoC shortcuts

Component ids from the held-back 506–509 range · ImGui's 16-bit index limit ⇒ the map draws a sample above 12 000 points · no command validation ·
a plain continuous clock (no stepped mode, no ACKs) · per-system timings kept locally, not in the kernel's profiler.
