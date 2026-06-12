# BATCH-01 — Live action/condition palette (BTree)

**Task:** TASK-BT-01 (see `.dev/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-01--live-actioncondition-palette`)
**Phase:** A (BTree) · **One objective only.** Do not start any other task.

---

## 🔒 Working agreement (MANDATORY — read first)

From `.dev/ai-hsm-btree-vis-edit-2/TASK-TRACKER.md` "Working agreement":
1. **One task only.** Touch only the files named in §Files. Do not edit files owned by other workstreams beyond the additive change named here. Do not re-format unrelated code.
2. **NO CHEATING to pass build/tests.** Never exclude/remove a file from compilation, never `#pragma warning disable`, never delete/weaken an assertion, never stub a feature to dodge an error. If blocked, STOP and write the blocker in your report.
3. **Finish without asking.** Build, run the named test projects, diagnose root causes, fix, repeat **until `Failed: 0`**, THEN write the report. No permission-asking.
4. **Tests verify behavior, not strings.** Assert actual enum/string/identity values as specified below. A broken impl MUST fail these tests.
5. **Litter-free.** No debug `File.WriteAllText`, no `Console.WriteLine`, no scratch files.
6. **Report = truth.** Your report must match your diffs.

## 📋 Onboarding
- Workflow/standards: `.dev/.guides/` (DEV guides + CODE-STANDARDS.md).
- Design of record: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §2.2, §5 (EB-C); host detail `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §5.1.
- **Locked decisions you MUST follow:** `.dev/ai-hsm-btree-vis-edit-2/DECISIONS.md` **D-01** (IsCondition discriminator) and **D-02** (encoded kind id). Do not deviate.
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-01-REPORT.md`. Questions (only if truly blocked) → `.dev/ai-hsm-btree-vis-edit-2/questions/BATCH-01-QUESTIONS.md`.

---

## 🎯 Objective

The BTree node palette currently lists only generic `Action`/`Condition` nodes (`BTreeNodeCatalog` is static-only). Make it additionally list **specific registered Actions and Conditions** (searchable by name), and make **placing a specific entry bake that method's identity** onto the new node. Bind via the catalog only — the Inspector binding path is already done and must not change.

## Files (exact, repo-relative)

1. `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` — **additive only**: append `bool IsCondition = false` as the **last** parameter of the `ActionSchemaEntry` record (default keeps all existing call sites compiling). Update its XML doc.
2. `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` — in `ProcessMethod`, set `IsCondition = true` when the method has `[BTreeConditionAttribute]`, `[SharedAiConditionAttribute]`, or `[SharedAiHeavyConditionAttribute]`; otherwise `false`. (A method with both an action and a condition attribute is not expected; if it happens, condition wins → `true`.) Do not change hosting logic.
3. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeKinds.cs` — add:
   - `public const string ActionPrefix = "bt.leaf.action::";`
   - `public const string ConditionPrefix = "bt.leaf.condition::";`
   - `public static bool TryParseLeafActionKind(string kindId, out string fqn, out bool isCondition)` — returns true and strips the prefix for encoded ids; false otherwise (out fqn = "", isCondition = false).
   - Extend `KindIdToNodeType` so an `ActionPrefix`-encoded id → `NodeType.Action` and a `ConditionPrefix`-encoded id → `NodeType.Condition`.
4. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs` — add a constructor `BTreeNodeCatalog(IActionSchemaExporter actionSchema)` (keep the existing parameterless ctor working, e.g. by making the exporter optional/null = static-only). Build **dynamic entries** from `actionSchema.All.Values` where `Hosting.HasFlag(ActionHosting.BTree)`:
   - `IsCondition == false` → kind `BTreeKinds.ActionPrefix + Fqn`, category = the existing leaf category ("Leaf"), `DisplayName` = the short method name (text after the last `.` in `Fqn`), keywords include the full `Fqn` and the short name, `IconKey` = "bt/action".
   - `IsCondition == true` → kind `BTreeKinds.ConditionPrefix + Fqn`, `IsPure: true`, `IconKey` = "bt/condition", otherwise as above.
   - Keep ALL existing static entries (including the generic `Action`/`Condition` as unbound fallbacks).
   - Dynamic entries appear in `All`, `Query`, and `QueryForPinContext` (non-decorator).
   - Subscribe to `actionSchema.Changed` and rebuild the dynamic set (the static set is fixed). Guard against null exporter.
5. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs` — in `ApplyAddNode`: if `BTreeKinds.TryParseLeafActionKind(add.Kind.Id, out var fqn, out var isCond)` is true, set `KernelType = isCond ? NodeType.Condition : NodeType.Action`, `DisplayLabel` = short name (after last `.`), and set the payload: `node.Condition = new BTreeConditionPayload { MethodFqn = fqn }` (isCond) or `node.Action = new BTreeActionPayload { MethodFqn = fqn }`. Generic (non-encoded) kinds keep current behavior (unbound).
6. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — add an `IActionSchemaExporter? actionSchema = null` parameter to `Build(...)` and pass it to `new BTreeNodeCatalog(actionSchema)` (fallback to parameterless when null).
7. Composition root `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — at the call site(s) where `BTreeDocumentFactory.Build(` is invoked, pass the already-constructed `sharedSchemaExporter` (created ~line 1883). **Wiring only** — do not restructure surrounding code.

> If any signature/field differs from the above, follow the real code and note the deviation in the report — do NOT invent members.

## 🧪 Tests (write EXACTLY these; assert the stated values)

Add to `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` (new file `Host/BTreeDynamicCatalogTests.cs`) unless noted. Provide a `FakeActionSchemaExporter : IActionSchemaExporter` test helper (backing `Dictionary<string,ActionSchemaEntry>`, `Rebuild()`/`Changed` raise-able) so tests don't depend on real assemblies.

- **T1 — exporter discriminator** (extend `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/ActionSchemaExporterTests.cs`): a fixture type with one `[BTreeAction]` method and one `[BTreeCondition]` method → after rebuild, the action entry has `IsCondition == false`, the condition entry `IsCondition == true`.
- **T2 — action entry**: exporter seeded with action `"Ns.Combat.DoThing"` (IsCondition=false, Hosting=BTree). `catalog.Query(new NodeSearchQuery{Text="DoThing"})` returns an entry with `Kind.Id == "bt.leaf.action::Ns.Combat.DoThing"` and `DisplayName == "DoThing"`.
- **T3 — condition entry**: exporter seeded with condition `"Ns.Combat.IsThing"` (IsCondition=true, Hosting=BTree). Query("IsThing") returns `Kind.Id == "bt.leaf.condition::Ns.Combat.IsThing"`, and that entry `IsPure == true`.
- **T4 — host filter**: an entry with `Hosting = ActionHosting.Hsm` only is NOT present in `catalog.All` (no encoded kind for it).
- **T5 — re-query on Changed**: add a new BTree action to the fake, raise `Changed`; `catalog.All` now contains its encoded kind.
- **T6 — kinds parse**: `BTreeKinds.TryParseLeafActionKind("bt.leaf.action::Ns.Combat.DoThing", out var f, out var c)` → `true, f=="Ns.Combat.DoThing", c==false`; condition prefix → `c==true`; `"bt.leaf.action"` (generic) and `"bt.composite.sequence"` → `false`. `KindIdToNodeType("bt.leaf.action::X")==NodeType.Action`, `KindIdToNodeType("bt.leaf.condition::X")==NodeType.Condition`.
- **T7 — placement bakes identity**: build a `BehaviorTreeAsset` + `BTreeGraphModel` + `BTreeCommandSink`; `Apply(new GraphCommand.AddNode(kind: new NodeKindKey("bt.leaf.action::Ns.Combat.DoThing"), assignedId: <guid>, position: ...))`. Assert the created node: `KernelType == NodeType.Action`, `Action != null`, `Action.MethodFqn == "Ns.Combat.DoThing"`. Repeat with the condition prefix → `KernelType == NodeType.Condition`, `Condition.MethodFqn` set.
- **T8 — generic fallback unchanged**: `AddNode` with `"bt.leaf.action"` (generic) → `KernelType == NodeType.Action` and **no** baked `MethodFqn` (Action null or `MethodFqn == ""`).

(Use the real `GraphCommand.AddNode` shape from `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Commands/GraphCommand.cs` — match the actual constructor.)

## ✅ Success criteria (DONE when ALL hold)

- [ ] Build `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in touched projects.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. T2–T8).
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests` — **Failed: 0** (T1 + existing ActionSchemaExporter/BB1 picker tests; 0 NEW failures vs baseline).
- [ ] Specific actions AND conditions appear in `BTreeNodeCatalog.Query`; placing one bakes `MethodFqn`; generic fallback unchanged.
- [ ] No file excluded from compilation; no suppressed diagnostics; no litter.
- [ ] Report written to `reports/BATCH-01-REPORT.md` (issues hit, decisions made beyond spec, suggested commit message).

## Notes / pitfalls
- `ActionSchemaEntry` is a positional record — append `IsCondition` LAST with a default so existing `new ActionSchemaEntry(...)` call sites compile.
- Do NOT touch the Inspector binding picker (`BTreePickerDrawers` / BB1) — out of scope.
- Decorators must remain attach-to-node (not free nodes) — do not add decorator entries here.
- If the `BTreeDocumentFactory.Build` call site is hard to find, grep `BTreeDocumentFactory.Build(` in `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`.
