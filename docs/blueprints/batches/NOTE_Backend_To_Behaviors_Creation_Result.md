<!--STATUS
state: LIVE
updated: 2026-10-25
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Creation_Unification.md §3.4e — owns the two notifications, what they carry, and the request id's path
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Creation_Result.md — the ask this answers (CE-2375)
-->

# NOTE — backend → behaviors: your creation result is built (CE-2375 → backend `CE-3323`)

**Answers** [HANDOFF_Behaviors_To_Backend_Creation_Result.md](HANDOFF_Behaviors_To_Backend_Creation_Result.md) item 1.
🔒 The user redirected it from a requester-only answer to a **generic** mechanism (R-3051): *"nothing ad hoc, but generic system
mechanism; and same goes for EntityDestroyed"* — and named the events: *"EntityConstructionCompleted, EntityDestructionCompleted."*

## What you can read now, on the node that published the request

| | |
|---|---|
| ⭐ **`Fdp.Toolkit.Lifecycle.Events.EntityConstructionCompleted`** (9005) | published by the lifecycle module on **every** node the entity reaches, the frame it goes Active there — `Entity` (yours, on your node), `NetworkId`, `TkbType`, **`RequestId`**, `IsLocallyOwned`, `Succeeded`, `Resumed`, `Failure`, `FrameNumber` |
| `EntityDestructionCompleted` (9006) | the same, when it is gone (carries the ids — the handle is dead when you read it) |
| `Fdp.Toolkit.Lifecycle.Components.CreationRequestId` | the request id stays on the entity on every node, if you would rather look it up than listen |

⇒ **`OneManCommand`:** keep the `RequestId` you publish; read `EntityConstructionCompleted` and match `RequestId` — that is your
squad, on your node, whichever node built it. `OneManCommandSystem.Find`'s name + force + type key can go.

## Things to know

- ⚠ `Succeeded = false` means the construction was refused or timed out — the answer to your request is "no squad".
- ⚠ `Resumed = true` is a construction re-opened after a restore, not a new entity — ignore it if you already resolved the request.
- ⚠ Lifecycle-managed entities only (anything made by a creation request is). ⚠ A BDC-replicated entity carries `Guid.Empty`.
- ⭐ Proved across two nodes: IT-SA-7 (`SplitAuthoritySpawnTests.AQ107_ARequestFromTheBrain_ServicedOnSimHost_ComesBackToTheBrain_NamingItsRequest`)
  — the Brain asks, SimHost builds, both announce the entity with the request's id.
