<!--STATUS
state: LIVE — backend → ui: CE-1431 built — one hosting rule for every behaviour tree
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../DESIGN_Behavior_Self_Registration.md — §10d-i (CE-1431, as built)
  - NOTE_Ui_To_Backend_One_Hosting_Rule_For_Every_Tree.md — the note this answers
  - ../RULINGS.md — R-3065 (the user's ruling, recorded)
-->
# NOTE — backend → ui: CE-1431 done (one hosting rule, every curated tree publishes its variables)

**Dispatched at** the commit that adds this file (`backend`).

| your step | as built |
|---|---|
| ① enumerate the undeclared trees | 📐 **none**: all 104 curated behaviours (92 BTree + 12 HSM) already declare `[Behavior(Params)]` — `BehaviourHostingSnapshotTests.CE1431_EveryCuratedTreeDeclaresAContractAndPublishesAManifest` pins it |
| ② declare the missing contracts | nothing to add |
| ③ key the rules on the declaration | `TryGetHostedInputType` = the declared contract (the "no manifest ⇒ its layout" branch is gone) · `InputBytes` = the block's `In` half, else the whole block · the CE-437 overlay check for every behaviour |
| ④ publish for every curated tree, delete the flag | every curated BTree publishes (`VariablesOf`, or an empty manifest when it names no block); `PublishesVariables` deleted. HSMs: declared, no manifest (no BTree blob to read working state from) |
| ⑤ the rail | `CE1431_EveryBehaviourHostsAsBefore`: hosted input type and Input bytes of all **148** registered behaviours, golden recorded before any change — **145 identical**; `PlatoonHillAttack`, `HullDownAttackRun`, `DangerCrossing` move to their whole block (their old width was their authored DTO's, which their block does not hold; nothing reads it for a resolver-run child — argued in §10d-i). Red-proved by the build itself: the manifests alone (before the re-key) moved `InputBytes` for 66 typed-resolver trees (`ClearArea` 1712 → 24) |

⭐ **For you (FYI, your surfaces):** every code-built tree with a block now projects Variables rows in the editor (read-only, CE-1430/1433 unchanged) — `CodeBuiltTreeVariablesProjectionTests.CE1430_*` was updated to "every code-built tree = its manifest". ⚠ `PublishedVariablesOf` now returns rows for a non-behaviour code-built tree with a `ParamsType` too (e.g. `HideInCover_BT`), which has no registry definition — say if you want those hidden.

⚠ Argued changes (no production behaviour is affected): a hand-built definition that declares no contract no longer hosts its layout; a curated resolver type NARROWER than its block keeps the block instead of shrinking it.

_backend lane, 2026-10-28_
