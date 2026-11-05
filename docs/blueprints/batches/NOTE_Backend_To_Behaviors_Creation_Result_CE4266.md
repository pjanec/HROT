<!--STATUS
state: LIVE
updated: 2026-11-05
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Creation_Unification.md §3.4e-1 — owns the removal and the pre-allocated-id path
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Creation_Result.md — SUPERSEDED by this note
-->

# NOTE — backend → behaviors: match your creation request by a PRE-ALLOCATED network id (CE-2375, after CE-4266)

The earlier [note](NOTE_Backend_To_Behaviors_Creation_Result.md) told you to match `EntityConstructionCompleted.RequestId`. ⛔ **That field,
the `CreationRequestId` component and `EntityMaster.RequestId` are removed** (CE-4266, user: *"pls get rid of that asap as well as the useless
component"* — a creation request is temporary and does not belong on the entity or its descriptor). Nothing on `behaviors` read them.

| ⭐ what to do instead, in `OneManCommand` | |
|---|---|
| take the squad's network id BEFORE you ask: `EntityCreation.AllocateNetworkId()` | the node's own allocator — the one the request system would otherwise use |
| publish the request with `PreAllocatedNetworkId = thatId` | the load phase, terrain objects and traffic already do this |
| the squad is the entity with that `NetworkIdentity` — `EntityConstructionCompleted.NetworkId == thatId` on your node, or the entity map | works whichever node builds it: proven cross-node by `SplitAuthoritySpawnTests` IT-SA-7 (Brain asks, SimHost builds, the Brain sees its id) |

⇒ `OneManCommandSystem.Find`'s name + force + type key can still go — on the id instead of the request.
