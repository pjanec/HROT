# BATCH-06C — CORRECTIVE #2 for BATCH-06 (revert production hack; drop bound condition)

**Task:** TASK-BT-06 corrective. **One objective.** See decision **D-05** in `.dev/_DONE/ai-hsm-btree-vis-edit-2/DECISIONS.md` and **VE-DEBT-002**.

## 🔒 Working agreement (MANDATORY)
**NO cheating / NO unauthorized production edits.** If blocked, STOP and report — do NOT invent code to force a compile. Finish without asking until build clean + `Failed: 0`. Litter-free. Report = diffs.

## 🐛 Issue found in review (BATCH-06B was rejected)
BATCH-06B added a 4-param `Condition_TargetAliveAndVisible(ref BrainBlackboard, …)` **overload to production `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`** with a bogus `Unsafe.As<BrainBlackboard, FireAtTargetParams>` reinterpret, purely to make the generated showcase compile. This is REJECTED: it's an unauthorized production change, creates a **duplicate-FQN `[BTreeCondition]`**, and the reinterpret is meaningless at runtime.

**Root cause (D-05):** no real `[BTreeCondition]` has a 4-param `NodeLogicDelegate<BrainBlackboard,BTreeContext>` shape (all take a typed DTO param), and `BrainBlackboard` exposes no DTO field for the expression-target path. A real condition therefore cannot be bound into this generated tree without machinery that doesn't exist yet (VE-DEBT-002).

## ✅ Fix (exact)
1. **Revert `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` to HEAD** (remove the added overload entirely): `git checkout HEAD -- Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`. Confirm the file has NO `Condition_TargetAliveAndVisible(ref BrainBlackboard, …)` overload afterward.
2. **`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json`** — DROP the bound Condition guard:
   - Remove the **Condition** node entirely (`VisualId` `30000000-0000-0000-0000-000000000001`).
   - Remove `"30000000-0000-0000-0000-000000000001"` from the **ObserverSelector**'s `ChildVisualIds` (it then has only the Sequence child `40000000-...`).
   - Leave everything else unchanged: ObserverSelector, Sequence, Action(`Action_Wander`) with the Repeater+Cooldown pills, Wait(1.5), Subtree→SampleScout, Canvas, Suppressions, Blackboard.
   - File MUST round-trip byte-stable and the **generated** `CombatShowcase.g.cs` MUST compile (no condition → no 4-param-delegate problem).
3. **`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Persistence/BTreeShowcaseAndStarterTests.cs`** — update the projection test: REMOVE the Condition-leaf assertions (no condition now). KEEP/assert: ObserverSelector present; Action leaf bound to a method containing `"Action"` (Action_Wander) carrying 2 pills (Repeater + Cooldown); Wait leaf; Subtree leaf referencing `SampleScout`. Keep the deserialize / round-trip-byte-stable / Starter-recipe tests.

## ✅ Success criteria
- [ ] `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` identical to HEAD (no added overload). Verify: `git diff -- Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` is EMPTY.
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0**.
- [ ] Showcase has no Condition node; ObserverSelector → Sequence only; everything else intact; round-trips byte-stable; `CombatShowcase.g.cs` compiles.
- [ ] Update `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-06-REPORT.md` (note the two correctives + that real-condition binding is deferred to VE-DEBT-002).

## Notes
- Do NOT add any production helper/overload. Do NOT exclude the showcase from compilation. If something still won't compile, STOP and report the exact error — do not work around it.
