<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: the whole file — how to launch the ported (Bullet) Stride host and what a human
  should see. Written for the user's visual check; the MCP harness is deliberately NOT wired to it.
known-conflict: none.
-->
# Stride host — how to launch it, and what to look at

> ⛔⛔ **WINDOWS ONLY, and that is a platform fact, not a missing step.** The Stride host is
> `net8.0-windows` and its asset pipeline compiles Direct3D11 shaders. It was **never** launchable
> from the Linux cloud session — measured at the base commit, before any port file existed
> (`ST-006`).

## 1. Run it

```powershell
# from the repo root, on Windows
dotnet run --project Stride\HrotStrideApp.Windows\HrotStrideApp.Windows.csproj -c Debug
```

⭐ First run compiles the Stride assets (models, animations, materials under `Stride/Assets/`) and
takes a few minutes. Subsequent runs are fast.

⚠ **If it fails on `app.manifest`** — that file is matched by `Stride/.gitignore` and is committed on
neither branch, so a clean clone has never had one (`ST-008`). The project now builds without it; if
your tree still references one, delete the stale `obj/` and rebuild.

## 2. What you should see

| # | look for | what it proves |
|---|---|---|
| **①** | entities render as **3-D models**, not flat markers | `StrideVisualBindingSystem` resolved `StrideRenderModelDefDto` and attached a model |
| **②** | a walking entity **blends idle → walk → run** as it speeds up and slows down | `StrideAnimationBridge` reads `SimVelocity` as plain floats and drives `LocomotionBlend` through `IAnimationBackend.UpdateLocomotionInputs` |
| **③** | crossing an off-mesh link plays a **jump montage** (start / loop / end), not a slide | the discrete path: `OffMeshTraversalStartedEvent` → `DispatchTraversals` → `PlayMontageOnSlot` |
| **④** | agents **walk around each other** instead of through | the real DotRecast crowd — this is the first build where the crowd is not a no-op stub |
| **⑤** | agents stand **on** ramps and stairs, and are stopped by walls | `BulletCharacterMotor` consumes `CrowdMotorIntent`; `BulletReverseSyncSystem` writes the pose back |

## 3. What is NOT in this build

| ⛔ | why |
|---|---|
| ⚠ **the mannequin's ANIMATION DESCRIPTOR** | `ST-011` — the `CharacterAnimationDefDto` family does not exist on this line, so the humanoid templates carry no montage/slot definitions. ⭐⭐ **Expect ② and ③ below to be the weak spots**: entities should still render and move, but idle/walk/run may not blend and the jump montage may not fire. **That is a known gap, not a new bug** |
| **the MCP-driven test harness** | out of scope by instruction — this batch only makes the host launchable |

⭐ **The hosted-real-editor mode IS built** *(`ST-010`)* — the twelve `EditorSubsystem` members it needs
were ported after measuring that they are the Stride integration's own host seam, five of them already
existed here as `internal`, and the UI lane had no conflicting edit.

## 4. If something looks wrong

⭐ The three ported suites are the fastest triage, and they **do** run on Windows:

```powershell
dotnet test Stride\Hrot.Stride.Core.Tests
dotnet test Stride\Hrot.Stride.Animation.Tests
dotnet test Stride\HrotStrideApp.Game.Tests
```

⚠ **They have not been executed anywhere yet** — the Linux session can compile them but cannot run
them (no `Microsoft.WindowsDesktop.App` runtime for linux-x64). ⭐ Their first run is on your machine;
a red there is information about the port, not necessarily about the build you are looking at.

⛔ **Non-Stride nodes are covered and green here** — Navigation 295/0, Physics 31/0, animation
195/0 + 15/0 + 31/0. If a SimHost/editor behaviour looks changed, that is a finding worth reporting,
because nothing in this port was supposed to touch it.
