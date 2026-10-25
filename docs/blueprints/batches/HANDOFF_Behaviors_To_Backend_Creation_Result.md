<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md — §7.3a / §7.3b: A-1 (R-2081), the lone man's squad made through the creation request
  - docs/DESIGN_Entity_Creation_Unification.md — OWNS the creation request, its routing (§3.4b) and the ack (§3.4d); this asks for the result to reach an in-process requester
-->

# HANDOFF — backend: tell the requester which entity its creation request made

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). **Dispatched at the commit that adds this file** (its
`git log -1` sha). Your scope is FROZEN there — later changes are FYI; one that invalidates the item ⇒ stop and report. Tracker: `CE-2375`
(behaviors' block); number your rows from yours.

| # | item | measured / why | acceptance |
|---|---|---|---|
| **1** | ⭐ **a creation result the REQUESTER can read** — on the node that published the `EntityCreationRequest`, whichever node serviced it | 📐 A-1 (R-2081): a lone man's area order publishes a request for his squad (`OneManCommand.Request`) and must then assign him under it. The ack goes only to the wire's external requesters (`EntityRequestFinalizationSystem` → `IEntityAckSink.WriteAck`), and with `OwnerAppInstanceId = 0` the spawn may run on another node — so the brain cannot learn the new entity's id. ⚠ Today behaviors recognises the squad by its replicated NAME + force + type (`OneManCommandSystem.Find`) — a stand-in | the requester can map `RequestId` → the created entity on its own node once that entity exists there (e.g. the request id carried onto the entity as a small replicated or locally-derived marker, or a bus event `EntityCreated {RequestId, Entity}` raised where the entity first appears) — your call which; behaviors then drops the name key. Rail: a request published on node A, serviced on node B, is resolved on A |

⭐ Report: the as-built in the owning design, the rail and its red-proof, the sha. Questions ⇒ a NOTE to behaviors.
🔒 User, `2026-10-25`: *"creating an entity is a cross cluster operation initiated via sending creation request event and is acked by other
nodes … the code needs to wait for confirmation from the local node anyway"*.
