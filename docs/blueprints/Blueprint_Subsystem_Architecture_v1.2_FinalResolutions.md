# Blueprint Subsystem Architecture v1.2 — Final Resolutions

> **Status:** Closes §17 of `Blueprint_Subsystem_Architecture_v1.2.md`. With these in place, **v1.2 is architect-approved and frozen** as the canonical Slice 1 architecture.

---

## Q-OPEN-D — One AiPrimitive working-state Blueprint per entity: APPROVED

The architect confirmed:

> *Because `Blackboard1024` is currently a single-typed projection slot, restricting working-state AI Blueprints to one per entity allows us to bypass retrofitting a partition allocator onto it right now. This is perfectly acceptable for the initial release, and the partition allocator for `Blackboard1024` can be safely deferred to Slice 2.*

**Action:** No change to v1.2 design. The Slice 1 constraint stands as documented in §6.5 and §14.

---

## Q-OPEN-E — Include "MoveToAndFire" demo in Slice 1: APPROVED

The architect confirmed:

> *Because it leverages channel commands, latent waits, and proves that a single authored graph can be hosted identically as both a `BTreeAction` and an `HsmAction`, it is the ideal validation of the new `AiPrimitive` dispatch capabilities.*

**Action:** The MoveToAndFire AiPrimitive becomes a required Slice 1 acceptance demo. Sample asset shape outlined in §5.4 of v1.2; full demo content lives in the Implementation Roadmap and the Compiler / Editor detailed designs.

---

## Patches to v1.2 (mechanical)

Apply at the next consolidation pass:

| Section | Edit |
|---|---|
| §17 | Replace "Q-OPEN-D" and "Q-OPEN-E" paragraphs with "(resolved — see Final Resolutions addendum)" |
| §18 | Append: "Q-OPEN-D resolved: one AiPrimitive working-state Blueprint per entity in Slice 1; `Blackboard1024` partition allocator deferred to Slice 2." |
| §18 | Append: "Q-OPEN-E resolved: MoveToAndFire AiPrimitive scenario is a required Slice 1 acceptance demo." |
| §1.3 (Slice 1 Done = Definition) | Append: "13. The MoveToAndFire AiPrimitive demo runs end-to-end under both BTree and HSM hostings from a single authored asset, demonstrating channel commands + latent waits + dual-hosting." |

These are inline edits; v1.2 does not need full regeneration.

---

*v1.2 + this addendum together = the canonical, architect-approved Slice 1 architecture. All subsequent detailed-design documents reference this combined baseline.*
