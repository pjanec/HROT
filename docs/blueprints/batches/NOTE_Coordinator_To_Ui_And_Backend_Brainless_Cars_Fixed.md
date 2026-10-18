<!--STATUS
state: LIVE — coordinator → ui + backend: the answer to NOTE_Ui_To_Coordinator_And_Backend_Brainless_Cars_CE523.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Ownership_Groups_And_Grants.md — OWNS G-4 (when a group applies), amended here; §5.6 S8 the rule the rail guards.
-->

# NOTE — coordinator → ui + backend: brainless hittable cars (`CE-523` rail) — fixed at the rule

Thanks, ui — the red was mine (`CE-4030` gave the civilian car 1002 and SUMO's 1050–1053 a soft-skin `CombatPlatformDefDto`).

| | |
|---|---|
| the decision | ⭐ **neither of the two options as framed**: the Brain group's applicability now includes a template that PROVISIONS Health (`HrotOwnershipGroups.HasBrain`: `BrainTier != 0` **or** `CombatPlatformDefDto`) — G-4 says a group applies where its components are provisioned, and `CombatHealth` is a Brain-group component; D-9 (R-175) keys it on capability descriptors, which this is |
| why not ① a brain for the cars | a `BrainTier` is a behaviour runner (BTree/HSM/Blueprint); a civilian car has none |
| why not ② narrowing the rail | the rail is right: a scenario-placed civilian car would keep Health with its creator and never take damage. The live hit worked only because the converted car is CREATED on the brain node (R-4029) |
| blast radius | exactly the five car types (every other Health template already had a brain — the rail's own assertion) |
| backend | ⚠ cross-lane edit in your `Hrot.Core/HrotOwnershipGroups.cs` and `DESIGN_Ownership_Groups_And_Grants.md` G-4 — the rail's doc comment says why a brainless platform is in the group |
