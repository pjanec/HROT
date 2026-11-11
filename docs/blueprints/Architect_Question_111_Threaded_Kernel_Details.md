<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: §2 — nine sub-questions, ✅ all approved as recommended (R-4206, `2026-11-11`)
stale-below: nothing
related-designs:
  - ../DESIGN_Threaded_Simulation_Presentation.md — OWNS the concept (R-4205); this question settles its open details (its §10) before S-1
  - ../../FDP/Engine/docs/FDP-GDB-SoD-unified.md — FDP-DES-012, the kernel's snapshot model; its §8 (added with this question) describes the self-paced reader
  - DESIGN_Time_Architecture.md — §17 stepped mode's determinism, which Q111-D must keep
-->

# Architect Question 111 — the threaded kernel: the details to settle before building (S-0 of R-4205)

🔒 User, `2026-11-11`: *"Approved the concept."* (R-4205). The concept: the ONE kernel on a worker, the presentation pulled on the main thread from a
published snapshot, the UI writing commands — [DESIGN_Threaded_Simulation_Presentation](../DESIGN_Threaded_Simulation_Presentation.md). These are the
decisions the build needs that the concept did not fix. ⭐ Each has a recommendation — reply "approved", or name the one to change.

## 1. INVENTORY

The design's §1 (queries: `search_graph` `.*(SnapshotProvider|SnapshotPool|DoubleBuffer|OnDemandProvider).*` → 11,
`.*(MainThreadJobQueue|PresentationShell|FrameStats|KernelFrameTimings).*` → 4, `.*(InspectableSession|RepositoryAdapter|EntityComponentWriter|EntityWriteRouter).*`
→ 5), plus: `ExecutionPolicy` / `RunMode { Synchronous, FrameSynced, Asynchronous }` and `DataStrategy { Direct, GDB, SoD }`
(`Fdp.ModuleHost/Abstractions/ExecutionPolicy.cs:177-219`); the edit outcome path `EditOutcomes.Report` (`Fdp.Toolkits/Replication/Edits/Edits.cs`);
the time controller `MasterSyncController` (`Fdp.Toolkits/Time/Controllers/`). ⚠ Coverage could not be checked.

## 2. The sub-questions

| # | question | ⭐ recommendation | why | blast radius |
|---|---|---|---|---|
| **A** | **what the published snapshot holds** — every snapshotable component, or only what the presentation reads | **everything snapshotable first**; a presentation component mask only if the publish costs more than ~5 ms per tick at 1 000 men | simple, cannot miss a component a panel reads; the PoC measured ~1 ms at 10 000 light entities, ui ~6.5 ms per background module's mask — measure before narrowing | kernel (S-2) |
| **B** | **when to publish** — every tick, or only when the presentation has taken the previous one | **only when the latest has been leased at least once** — an unread snapshot is not re-copied; events still queue every tick | when the simulation ticks faster than the frames, half the copies are never read; costs one flag | kernel (S-2) |
| **C** | **a command whose target changed or died in between** | **refused, and reported through the existing edit outcome** (`EditOutcomes.Report` → the notification the user already sees, "Delete refused — …") | the PoC's stale command threw (T-6); the edit notifications are the one place outcomes are shown | kernel + UI (S-2, S-3) |
| **D** | **the run controls and stepped mode with the kernel on the worker** | **the time controller lives with the kernel on the worker; Play / Pause / Step / Run-to become commands** like any other UI write; stepped mode's rule (Time Architecture §17) is unchanged | the clock is World A's; keeping it on the kernel's thread keeps §17's determinism by construction | kernel, run controls (S-2, S-5) |
| **E** | **the HTTP routes' default queue** | **a route is simulation-side unless marked UI-side** (map layers, camera, film, screenshots …) | most of the 77 read or write the world; a wrong default on a world route would race — a wrong default on a UI route only answers one tick late | debug API (S-4) |
| **F** | **the inline scheduling's view** (tests, rails, headless) — a real publish or the live world | **the live world through a READ-ONLY wrapper** (zero copy; throws on any write); one test class runs the real publish path | tests stay as cheap as today and still catch every missed write (design §8 ①) | test harness (S-1) |
| **G** | **where the new pieces live** | **in the kernel (Fdp.ModuleHost)**: a new `RunMode.Pulled` for a module the host runs itself; the snapshot port and the command ingress owned by the kernel; the pulled module's timings in the kernel's profiler | one registry, one diagnostics surface (the user's "not twice"); a pulled module is registered like any module | kernel (S-2) |
| **H** | **the presentation's working memory** — a plain store, or a second presentation-only `EntityRepository` | **a plain store** (`PresentationStore`, keyed by entity), and the 21 world-keyed side tables re-keyed to a `PresentationContext` object | a second repository is a second world to sync and diagnose; the state is small and never recorded | presentation (S-3) |
| **I** | **who builds what** | **backend:** S-1 (guards, the switch), S-2 (the kernel pieces), S-4's kernel half, S-5 (threaded ON, equivalence). **ui:** S-3 (the presentation migration), S-4's route labels, S-6 (Windows). **coordinator:** S-0, reviews | the kernel is backend's; the presentation and the Windows machine are ui's | lanes |

## 3. Answers

✅ **All nine approved** — 🔒 user, `2026-11-11`: *"Approved."* ⇒ R-4206. The design's §4 / §9 follow them.
