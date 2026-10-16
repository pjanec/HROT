<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.1a and §7.2a hold the as-built this note points at
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — the handoff this answers (B-3, B-2)
-->

# NOTE — backend → behaviors: civilians B-3 is ready, B-2 is half done and waits on your CIV-0

The CV leans are ruled (`R-3013`, you approved all twelve on 2026-10-16). Ids: B-1 `CE-3219` · B-2 `CE-3220` · B-3 `CE-3221` ·
B-4 `CE-3222` · B-5 `CE-3223`.

| | what you get | what you need from it |
|---|---|---|
| **B-3** (`CE-3221`) — the side flip | ⭐ **nothing new: use `EntityWriteRouter`.** It writes on the owner (CGF owns a civilian it created, which includes every scenario-loaded one) and sends the attribute request otherwise. Call: `EntityWriteRouter.For(world).Write(civilian, new[] { new EntityAttributeChange { AttributeId = AttributeIds.Affiliation, Value = AttributeValue.FromInt((int)ForceIdentifier.Opposing) } })`. Rail: `SplitAuthoritySpawnTests.CE3221_*` (CGF → SimHost) | your `CE-2238` `HostileAct` handler calls it. ⛔ Never `SetComponent<EntityInfo>` — it never leaves the node |
| **B-2** (`CE-3220`) part 1 — the types | `CivilianTkbCatalog`: 1101 adult male, 1102 adult female, 1103 child (template name `CivilianMinor` — `CivilianChild` is still taken by 1003), 1104 elderly (BrainTier HSM); 1201–1203 security (BrainTier BTree, no weapon until B-5). Classes `Civilian` and `SecurityForce` | your `CE-2235`: set `DefaultBehaviorHash` on the four civilian types when you register `CivilianHsm` (one line each in `CivilianTkbCatalog.Civilian`, cross-lane — go ahead) |
| **B-2** part 2 — retire 1001/1003, migrate the 4 scenarios | ⏳ **waits for your `CE-2233` (CIV-0 safety gate)**: the new civilians can be shot, and without the gate the 4 scenarios' soldiers may shoot them | tell backend the sha when `CE-2233` lands |
