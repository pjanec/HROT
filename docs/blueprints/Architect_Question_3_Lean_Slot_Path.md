# Architect question #3 — lean slot path for Hill-attack (and: is there an existing runner-list?)

**Context / what we found.** We checked whether the Platoon Hill-attack's wave/slot logic fits your
generic `SquadCognitiveState` + squad primitives. **It doesn't** — `SquadCognitiveState` models
infantry fire-and-movement doctrine (bounding-overwatch elements, roles, phase machine, greedy
threat-matrix), whereas Hill-attack is wave-based armor runs. Two Hill-attack concepts have **no home**
in it — baseline-slot reservation, and the compacting ≤8 **active-runner SoA** (`ActiveEntityPacked` +
`HasStartedRun` latch + swap-remove) — and the rest map only as square pegs (wave parity, wave-as-
phase, round-robin vs greedy). So we plan to go **lean**, and reserve `SquadCognitiveState` for a
behavior that's actually element/role/phase-shaped.

**Great catch already:** we found `SlotRotation`/`SlotRotationState` (Used+Burned masks; its doc says
it *"generalizes `HillAttackMutableState`'s masks"*). We'll expose that as Blueprint `AcquireSlot`/
`ReleaseSlot`/`BurnSlot` nodes (two instances: firing = burn-on-death, baseline = release-on-return).

**The lean plan:** `SlotRotation` (slots) + a **`MemberSlotList`** (the active-runner list) + a plain
`int` wave counter + generic `ForEach`/component-read/`PublishEvent`. That covers all four hard
commander nodes with no Hill-attack-specific C#.

## Questions

**Q1 (the one you're best at) — is there already an engine construct for the runner-list?** Before we
add a new `MemberSlotList`, is there an existing primitive/struct for a **fixed-capacity list of Entity
records with O(1) swap-remove and a few parallel scalar columns** (entity + per-row bytes like
firing-slot / baseline-slot / started-flag), the way `SlotRotation` already existed for the masks? We'd
much rather reuse than invent. (Anything like a `SwapBackList`, `EntitySlotRoster`, an inline
fixed-array list helper, etc.?)

**Q2 — bless the lean call?** Agree that (a) Hill-attack should use the lean `SlotRotation` +
runner-list path rather than being bent onto `SquadCognitiveState`, and (b) `SquadCognitiveState`'s
first real use is better proven on a genuine bounding-overwatch/element behavior?

**Q3 — is the squad layer's state intended to be dormant?** We found the whole squad layer
(`SquadCognitiveState` + maneuvers + systems + the four squad Blueprint node kinds) is library-complete
but **unwired** — driven only by tests, no scheduler registration — and the squad nodes
(`PartitionElements`/`AssignRoles`/`AdvancePhase`/`AcquireSlot`) have **no IR lowering** at all. Is that
a deliberate "designed-ahead, wire later" state, or drift we should flag? (Affects whether we ever wire
the squad nodes vs treat them as reserved.)

**Q4 — if `MemberSlotList` is genuinely new:** preferred shape? We'd model it as a blittable
`[BlackboardDtoStruct]` a Blueprint declares as WorkingState, operated on by nodes (`Add` /
`SwapRemoveAt` / `Count` / `Get` / `Set`) — the exact same pattern as `SlotRotationState`. Column model:
**fixed named columns** vs **N generic scalar columns**? Capacity (Hill-attack needs 16 rows ×
{Entity + 3 bytes})?

**Q5 (bonus) — any other "we already have X" we should reuse** for Hill-attack's remaining pieces —
e.g. a round-robin/target-cycling helper, an EQS target-pool accessor, or a baseline-reservation
construct — before we build generic nodes for them?
