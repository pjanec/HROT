<!--STATUS
state: LIVE
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Multiplayer_Server_And_Full_Cycle.md — item ① this evidence feeds (FYI: it does not change the handoff's scope)
  - docs/designs/audio/DESIGN_FPS_Audio.md — OWNS the audio layer and its output
-->

# NOTE — coordinator → backend: CE-4269 evidence from the user's Windows run (FYI for your handoff's item ①)

| what | |
|---|---|
| Event Viewer | `.NET Runtime` 1026: *"terminated due to an unhandled exception … c0000005"* · `Application Error` 1000: **faulting module `raylib.DLL`** (`runtimes\win-x64\native\raylib.DLL`, offset `0x37c4b`) ⇒ a **native** access violation inside raylib |
| node log | ends at `[CGF] AiShared shell built` 07:27:28.6; the crash is 07:27:29.5 (before it: SimHost "Visualization ready", CGF composed, Traffic booted) |
| ⭐ lead (code, not proven) | `AudioLayer.DefaultOutput()` (`AudioLayer.cs:101-104`) creates a NEW `RaylibAudioOutput` per map host (`MapInteractionPack.cs:611`); each `Open()` calls `Raylib.InitAudioDevice()` (`AudioOutput.cs:154`) — but raylib has ONE audio device per process. `-m server` puts several map hosts in one process ⇒ the device is initialised twice, and one host's `Dispose` → `CloseAudioDevice` (`:285`) would pull it from the other. The Linux VM never opens a device, so this never ran there |
| ⭐ the user can settle it in one run | `set HROT_AUDIO=0` before `run_Server.bat` (`DefaultOutput` then returns a null output). Up ⇒ the cause is the shared device; the fix is ONE process-wide, refcounted audio device (R-174), not one per host |

The user has been asked to run it; their answer will land on the CE-4269 row.

---
_coordinator lane, 2026-11-06_

## ✅ Update `2026-11-06` 07:36 — confirmed

With `HROT_AUDIO=0` actually reaching the process the server **stays up** (bootstrap complete, the TKB join sync on every node). The earlier
"still crashes" run never got the variable — a BOM at the top of the user's batch file swallowed the first `SET`. ⇒ item ①'s cause is the
per-host audio device; the fix is **one process-wide, refcounted audio device** (a rail with two map hosts in one process). Workaround
meanwhile: `HROT_AUDIO=0` before `run_Server.bat`.
