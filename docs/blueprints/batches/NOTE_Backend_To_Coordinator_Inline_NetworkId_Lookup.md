<!--STATUS
state: LIVE — backend → coordinator: one FINDING from backend's T2 sweep after merging behaviors (which carries coordinator's traffic work); no action taken by backend.
updated: 2026-10-18
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — OWNS the traffic node whose replica code the rail flags
-->

# FINDING — backend → coordinator: `ThereIsOneNetworkIdResolverTests` is red on `TrafficReplicas.cs`

**Measured** on `backend` after merging `behaviors@597203300` (it carries coordinator's CE-4024 / CE-4031):

```
Hrot.Editor.Tests.ThereIsOneNetworkIdResolverTests.NoNewInlineNetworkIdLookupAppears [FAIL]
Assert.Empty() Failure: Collection was not empty
Collection: ["Hrot/Engine/Hrot.Traffic/TrafficReplicas.cs"]
```

The rail forbids a hand-written "find the entity whose `NetworkIdentity.Value` equals this id" outside the one resolver. It matches
`Hrot/Engine/Hrot.Traffic/TrafficReplicas.cs:93-94`:

```csharp
if (_world.IsAlive(kv.Value) && _world.HasComponent<NetworkIdentity>(kv.Value)
    && _world.GetComponentRO<NetworkIdentity>(kv.Value).Value == networkId) Destroy(kv.Key, kv.Value);
```

⭐ Lean: resolve through the node's `NetworkEntityMap` (`TryGetEntity(networkId, out e)`) as the rest of the file already does at `:206`,
or — if this check is deliberately a VERIFY of a cached entity, not a lookup — extend the rail's allow-list with the reason. Backend did
not edit the file (coordinator's lane). Not caused by backend's `CE-3249` / `CE-3250` / `CE-3251`.
