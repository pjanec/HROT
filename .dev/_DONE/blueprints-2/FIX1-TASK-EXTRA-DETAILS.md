Here is the detailed, step-by-step implementation packet for **TASK-K-01** that you can pass directly to your AI fixing agent.

### 🎯 ACTION PACKET: TASK-K-01 Detailed Fix Instructions

**Agent Context:** We need to ensure that the `Lane` property on the `[HsmAction]` attribute is not only defined but properly captured by the Roslyn source generator and re-emitted onto generated thunks. If the generator drops the `Lane` data, the editor's reflection-based `HsmOutputLaneMaskInferrer` will fail to compute parallel region conflicts.

Please execute the following four steps to satisfy Acceptance Criteria F0-01 and F0-02.

#### Step 1: Verify the Attribute Definition

- **Target File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs`

- **Action:** Ensure the `Lane` property is correctly defined as a mutable property with a safe default. It must look exactly like this so handwritten actions without a lane continue to compile (F0-02):

  ```
  public CommandLane Lane { get; set; } = CommandLane.None;
  ```

#### Step 2: Capture `Lane` in the Source Generator

- **Target File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs`

- **The Flaw:** Inside the `GetMethodInfo` method, the generator successfully parses the `Name` argument from the attribute but completely ignores the `Lane` argument.

- **Action:**

  1. Update the local `MethodInfo` class (at the bottom of the file) to add `public string Lane { get; set; } = "global::Fhsm.Kernel.Data.CommandLane.None";`.
  2. In `GetMethodInfo`, extract the `Lane` argument alongside `Name`:

  ```
  var laneArg = attr!.NamedArguments.FirstOrDefault(a => a.Key == "Lane");
  string laneVal = "global::Fhsm.Kernel.Data.CommandLane.None";
  if (laneArg.Key != null && laneArg.Value.Value != null)
  {
      // Extract the integer/enum value and cast it back to the enum string representation
      laneVal = $"(global::Fhsm.Kernel.Data.CommandLane){laneArg.Value.Value}";
  }
  ```

  1. Assign this `laneVal` to the `MethodInfo.Lane` property.

#### Step 3: Re-emit `[HsmAction]` on Generated Thunks

- **Target File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs` (Emission logic)

- **The Flaw:** When the generator emits wrapper thunks for `[SharedAiAction]` methods (e.g., inside `EmitSharedAiActionThunk`), it emits raw static methods without any attributes. Because the editor's `HsmOutputLaneMaskInferrer` uses .NET reflection to find `[HsmAction]` attributes, it will be completely blind to these generated thunks, breaking Output Lane inference for shared AI actions.

- **Action:** In `EmitSharedAiActionThunk`, prepend the `[HsmAction]` attribute to the emitted method signature, embedding the captured `Lane`:

  ```
  sb.AppendLine($"        [global::Fhsm.Kernel.Attributes.HsmAction(Name = \"{entry.MethodName}\", Lane = {entry.Lane})]");
  sb.AppendLine($"        private static unsafe void Action_{entry.MethodName}_At{entry.Offset}(void* instancePtr, void* contextPtr, HsmCommandWriter* writer)");
  ```

  (Note: You will need to map `WritesChannel` values to `Lane` values for `SharedAiEntry` records, or parse their lane equivalents so the emitted thunk has the correct lane).

#### Step 4: Verify Editor Reflection

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmOutputLaneMaskInferrer.cs`
- **Action:** Verify the `BuildLaneDictionary` method correctly reads the property via `var attr = method.GetCustomAttribute<HsmActionAttribute>();` and ignores the fallback via `if (attr.Lane == CommandLane.None) continue;`. This guarantees that `CommandLane.None` does not accidentally flip bit 255 in the output mask.

------



Here is the detailed implementation packet for **TASK-K-02** and **TASK-K-03**.

### 🎯 ACTION PACKET: TASK-K-02 & TASK-K-03 Detailed Fix Instructions

**Agent Context:** We must ensure the `FastHSM` kernel's fluent builder can capture the visual Guids authored in the editor (`stableId` for states, `visualId` for transitions) and successfully round-trip them through the compiler.

Currently, the fluent builder methods partially accept these Guids, but they are discarded during compilation. Because the `[HsmDefinition]` thunk returns an `HsmDefinitionBlob`, we must attach a `MachineMetadata` sidecar to the blob so the Editor's projection layer can extract the Guids via reflection.

Please execute the following four steps to satisfy Acceptance Criteria F0-03, F0-04, and F0-05.

#### Step 1: Expand `MachineMetadata`

- **Target File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/MachineMetadata.cs`

- **Action:** Add dictionaries to hold the mappings from the flattened array indices back to the original authoring Guids.

  ```
  public Dictionary<ushort, Guid> StateStableIds { get; set; } = new();
  public Dictionary<ushort, Guid> TransitionVisualIds { get; set; } = new();
  ```

#### Step 2: Expose Metadata on `HsmDefinitionBlob`

- **Target File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs`

- **Action:** Add a managed metadata property to the blob. This mirrors the `BehaviorTreeBlob.DebugMetadata` pattern, allowing the editor to read the Guids via reflection after invoking the compiled method.

  ```
  // Add this property to the class:
  public MachineMetadata? Metadata { get; set; }
  ```

#### Step 3: Update `HsmEmitter.BuildMachineMetadata`

- **Target File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs`

- **The Flaw:** `BuildMachineMetadata` only extracts string names. It drops the Guids.

- **Action:** Update the method to populate the new dictionaries. You must iterate transitions in the exact same order `HsmFlattener` uses so the indices line up.

  ```
  public static MachineMetadata BuildMachineMetadata(StateMachineGraph graph)
  {
      var meta = new MachineMetadata();
  
      // 1. States & Events (existing logic + StateStableIds)
      foreach (var state in graph.States.Values)
      {
          if (state.FlatIndex == 0xFFFF) continue;
          meta.StateNames[state.FlatIndex] = state.Name;
          meta.StateStableIds[state.FlatIndex] = state.StableId;
      }
  
      foreach (var kvp in graph.EventNameToId)
          meta.EventNames[kvp.Value] = kvp.Key;
  
      // 2. Actions (existing logic)
      ushort actionIdx = 0;
      foreach (var actionName in graph.RegisteredActions.OrderBy(n => n, StringComparer.Ordinal))
          meta.ActionNames[actionIdx++] = actionName;
  
      // 3. Transitions (MUST match HsmFlattener order: sorted states -> transitions)
      ushort transIdx = 0;
      foreach (var state in graph.States.Values.OrderBy(s => s.FlatIndex))
      {
          foreach (var t in state.Transitions)
          {
              meta.TransitionVisualIds[transIdx++] = t.VisualId;
          }
      }
  
      // Global transitions append to the end of the transition list
      foreach (var gt in graph.GlobalTransitions)
      {
          meta.TransitionVisualIds[transIdx++] = gt.VisualId;
      }
  
      return meta;
  }
  ```

#### Step 4: Ensure Builders Capture and Attach the Data

- **Target File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` (and `Graph/StateMachineGraph.cs`)

- **Action Required:**

  1. In `HsmBuilder.cs`, verify that `public StateBuilder State(string name, Guid stableId = default)` exists and passes the `stableId` parameter down to the `StateNode(name, stableId)` constructor. (Your `StateBuilder.Child` and `TransitionBuilder.GoTo` already do this perfectly).
  2. In `StateMachineGraph.cs`, update the `Compile()` convenience method so that it builds the metadata and attaches it to the returned blob:

  ```
  public HsmDefinitionBlob Compile()
  {
      HsmNormalizer.Normalize(this);
      var flat = HsmFlattener.Flatten(this);
      var blob = HsmEmitter.Emit(flat);
  
      // Attach the metadata sidecar for the editor projection layer
      blob.Metadata = HsmEmitter.BuildMachineMetadata(this);
      return blob;
  }
  ```

------



Here is the detailed implementation packet for the final Phase 0 kernel tasks: **TASK-K-05** and **TASK-K-06**. You can pass this directly to your AI fixing agent.

### 🎯 ACTION PACKET: TASK-K-05 & TASK-K-06 Detailed Fix Instructions

**Agent Context:** We are wrapping up the Phase 0 kernel prerequisites. We must ensure the `FastBTree` kernel properly halts execution when a breakpoint is hit (by respecting the `Paused` flag) and that the fluent compiler captures the `visualId` for all composite and decorator nodes to allow the editor to map runtime states back to the visual canvas.

Please execute the following steps to satisfy Acceptance Criteria F0-06 and F0-08.

#### Step 1: Enforce `Paused` Flag in `BTreeTickSystem` (TASK-K-05)

- **Target File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs`

- **The Flaw:** While `Fbt.Kernel` has `BehaviorInstanceFlags.Paused` defined, and `Interpreter.Tick` may abort early, `BTreeTickSystem` will still blindly call `Tick`, evaluate traces, and potentially overwrite the `RunningNodeIndex` or spam the trace logs with idle ticks while the debugger is holding the entity.

- **Action Required:** Inside `BTreeTickSystem.Execute`, right before the call to `def.BTreeInterpreter!.Tick(...)`, check the instance's pause state.

  ```
  // Add this before def.BTreeInterpreter!.Tick(...)
  if ((btState.State.InstanceFlags & Fbt.BehaviorInstanceFlags.Paused) != 0)
  {
      // Entity is held by the debugger. Skip ticking the interpreter
      // to prevent trace log spam and state mutation.
      continue;
  }
  ```

#### Step 2: Add `visualId` to BTree Composite Builders (TASK-K-06)

- **Target File:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`

- **The Flaw:** The `Wait`, `Action`, and `Cooldown` methods successfully accept a `Guid visualId = default` parameter, but the structural nodes (like `Sequence`, `Selector`, `Repeater`, `Inverter`) are missing it. If these nodes don't capture their `visualId`, the editor cannot render breakpoints or active-state glows on them.

- **Action Required:** Update the method signatures for all remaining builder methods to accept `Guid visualId = default` just before the `[CallerFilePath]` parameters. Then, pass that `visualId` down into the internal node creation methods.

  *Example for Sequence/Selector:*

  ```
  public BTreeBuilder<TBlackboard, TContext> Sequence(
      Action<BTreeBuilder<TBlackboard, TContext>> children,
      Guid visualId = default,
      [CallerFilePath] string sourceFile = "",
      [CallerLineNumber] int lineNumber = 0)
  {
      var node = new BuilderNode { Type = NodeType.Sequence };
      var meta = BuildMeta("Sequence", sourceFile, lineNumber, visualId);
      // ... rest of method
  }
  ```

  *Apply this fix to:* `Sequence`, `Selector`, `ObserverSelector`, `Parallel`, `Inverter`, `Repeater`, `ForceSuccess`, `ForceFailure`, `UntilSuccess`, and `UntilFailure`.

#### Step 3: Ensure `BuildMeta` consumes the `visualId`

- **Target File:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`

- **Action Required:** Verify the `BuildMeta` private helper correctly stamps the `visualId` into the `NodeDebugMetadata`.

  ```
  private NodeDebugMetadata BuildMeta(string label, string file, int line, Guid visualId)
  {
      return new NodeDebugMetadata
      {
          Label = label,
          SourceFile = file,
          LineNumber = line,
          VisualId = visualId.ToString("D") // Must be preserved for the editor!
      };
  }
  ```

------

Pass this to your agent. Once it confirms these steps are complete, **Phase 0 (Kernel Prerequisites) is officially 100% finished!**

Let me know when you are ready, and we will move to the first Shared Infrastructure task: **TASK-S1-03 (`EditorSelectionStore` Sub-Selections)**.





Good news first: I did a deeper dive into your `v225` code dump, and I must apologize—I initially flagged `EditorSelectionStore` (`TASK-S1-03`) as a stub, but looking closely at `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs`, I see you **already flawlessly implemented it**.

You perfectly wired up the `Dictionary<Guid, IAssetSubSelection?> _subSelectionsByAsset` and properly routed the `ActiveSubSelection` property to swap contexts based on `ActiveAsset.AssetId`. We can cross `TASK-S1-03` off the list entirely!

Let's move straight into the remaining Phase 1 tasks. Here is the detailed implementation packet for your agent to finish wiring the Shared Infrastructure layer.

------

### 🎯 ACTION PACKET: Phase 1 — Shared Infrastructure (Remaining Fixes)

**Agent Context:** We are finalizing the Phase 1 Shared Infrastructure layer. The following three systems require their functional implementations to replace the placeholders left for Slice 1. Please execute the following fixes to satisfy Acceptance Criteria F1-05, F1-09, and F1-10.

#### TASK-S1-05: `ReferenceCatalog` Rebuild Trigger

- **Target File:** `Hrot/Editor/Hrot.Editor.AiShared/References/ReferenceCatalog.cs`

- **The Flaw:** Inside `OnCatalogChanged()`, there is a comment: `// Full rebuild from contributors will be wired here in Phase 5/6.` It currently just fires `Changed?.Invoke()` without actually rebuilding the index, making cross-asset refactoring blind to hot-reloaded changes.

- **Action Required:**

  1. Update the constructor to accept both the catalog and the subsystem contributors: `public ReferenceCatalog(IAssetCatalog catalog, IEnumerable<IReferenceCatalogContributor> contributors)`. Save these to private fields.
  2. Update `OnCatalogChanged()` to actually rebuild the multi-index:

  ```
  private void OnCatalogChanged()
  {
      _elements.Clear();
      _references.Clear();
  
      foreach (var asset in _catalog.All)
      {
          foreach (var contributor in _contributors)
          {
              foreach (var el in contributor.EnumerateElements(asset))
              {
                  _elements[el.Key] = el;
              }
              _references.AddRange(contributor.EnumerateReferences(asset));
          }
      }
      Changed?.Invoke();
  }
  ```

#### TASK-S1-08: Engine-to-Editor Selection Sync (FdpEventBus)

- **Target File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (or your DI composition root where the editor is initialized).

- **The Flaw:** The shared layer correctly provides a decoupled `CallbackSelectionBridge`, but the editor subsystem never actually instantiates it to listen to the engine's `SelectionChangedEventDto` on the `FdpEventBus`. Therefore, clicking an entity in the 3D map does not update the AI Editor.

- **Action Required:**

  1. In the editor's initialization or DI setup, instantiate the `CallbackSelectionBridge`.
  2. Pass a factory lambda that subscribes to the engine's selection event:

  ```
  var selectionBridge = new CallbackSelectionBridge(onEditorSelectionSet =>
  {
      return _world.Bus.SubscribeManaged<SelectionChangedEventDto>(evt =>
      {
          // Extract the entity ID from the DDS/Event bus payload
          if (evt.SelectedEntityIds != null && evt.SelectedEntityIds.Count > 0)
          {
              long netId = evt.SelectedEntityIds;
              if (_entityMap.TryGetEntity(netId, out var entity) && _world.IsAlive(entity))
              {
                  onEditorSelectionSet(entity);
                  return;
              }
          }
          onEditorSelectionSet(null);
      });
  });
  selectionBridge.Connect(_selectionStore);
  ```

#### TASK-S1-11 & TASK-S1-12: `DebugSessionRegistry` Exclusivity

- **Target File:** `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs` (or wherever your `IDebugSessionRegistry` is implemented).

- **The Flaw:** The registry currently hands out control sessions blindly. The spec mandates a strict split: *many* observers are allowed, but exactly *one* active control session (debugger) can exist per subsystem.

- **Action Required:**

  1. Add a private field to track the active session: `private IAiDebugSession? _activeControlSession;`.
  2. In `TryAcquireSession<T>(out T session)`, enforce the lock:

  ```
  public bool TryAcquireSession<T>(out T? session) where T : class, IAiDebugSession
  {
      if (_activeControlSession != null)
      {
          session = null;
          return false; // Lock is held by another tool
      }
  
      // Resolve from DI or instantiate
      session = _serviceProvider.GetRequiredService<T>();
      _activeControlSession = session;
      return true;
  }
  ```

  1. Ensure `ReleaseSession(IAiDebugSession session)` clears the lock: `if (_activeControlSession == session) _activeControlSession = null;`.

------





Here is the detailed implementation packet for **Phases 2, 3, and 4**, which will fix the interaction and hit-testing gaps for the three visual canvas extensions.

You can pass this directly to your AI fixing agent.

------

### 🎯 ACTION PACKET 3: Phases 2, 3, & 4 — NodeEditor Extensions (Interaction & Hit-Testing)

**Agent Context:** We are evaluating the three NodeEditor extensions (NodeAttachments, ContainerNodes, and CustomCanvasRenderer). The rendering pipeline for these is structurally in place, but the canvas interaction layer (`HitTester` and `CanvasInput`) does not properly evaluate them, leaving them unclickable and preventing reparenting. Please execute the following fixes to satisfy Acceptance Criteria F2-02, F3-03, F4-03, and F4-05.

#### TASK-NEA-03: Missing Attachment Change Notifications

- **Target File:** `src/NodeEditor.Core/Interfaces/IGraphModel.cs`
- **The Flaw:** The `GraphChangeNotification` record does not contain `AffectedAttachments`, meaning the UI layer cannot selectively invalidate or animate hot-reload badges for attachments.
- **Action Required:**
  1. Update the record to include the missing property as an optional parameter: `IReadOnlySet<AttachmentId>? AffectedAttachments = null`. *(Note: We will update the BTree and HSM command sinks to actually populate this field in the next Action Packet).*

#### TASK-NEA-06, TASK-NEC-05, TASK-NER-04: Hit-Testing Z-Order Convergence

- **Target File:** `src/NodeEditor.UI/Canvas/HitTester.cs`
- **The Flaw:** The canvas hit-tester currently only checks standard NodeEditor elements (nodes, pins, wires, comments). It ignores Attachments, Container regions, and all CustomCanvasRenderer output.
- **Action Required:** Completely rewrite the hit-test evaluation sequence inside `UpdateHover` (or wherever hit intersections are resolved) to test in exactly this 15-step priority order (highest priority wins):
  1. Reroutes (z=15)
  2. Pins (z=14)
  3. Wires (z=13)
  4. Custom `TopMost` render pass elements (z=12)
  5. Attachments (highest StackIndex first) (z=11)
  6. Custom `AfterNodes` render pass elements (z=10)
  7. Container collapse-arrow chevrons (z=9)
  8. Container header strips (z=8)
  9. Comment title bars (z=7)
  10. Custom `AfterWires` render pass elements (z=6)
  11. Node bodies (regular nodes and container children) (z=5)
  12. Custom `BeforeContent` render pass elements (z=4)
  13. Container interiors (empty area not covered by children) (z=3)
  14. Comment bodies (pass-through) (z=2)
  15. Empty Canvas (z=1)

#### TASK-NEC-06: Container Reparenting via Drag-and-Drop

- **Target File:** `src/NodeEditor.UI/Canvas/CanvasInput.cs`
- **The Flaw:** When concluding a node drag, the editor emits a `GraphCommand.MoveNodes` command. It fails to detect if the node was dragged across a container boundary, preventing reparenting.
- **Action Required:**
  1. In the `MouseButton.Left` release handler (when `Mode == InteractionMode.DraggingNodes`), call the existing `UpdateContainerDropTarget` helper to find the innermost valid `IContainerNodeModel` under the cursor.
  2. If the resolved target container ID (or null, for root canvas) differs from the node's current `ParentContainerId`, you must emit a `GraphCommand.ChangeParent` command instead of `MoveNodes`.
  3. Calculate the `NewLocalPosition` for the command by subtracting the new parent container's interior origin (its canvas position + header height + top/left padding) from the node's dropped canvas position.

#### TASK-NER-07: Custom Element Context Menus

- **Target File:** `src/NodeEditor.UI/Canvas/CanvasInput.cs`
- **The Flaw:** Right-clicking a custom-rendered element (like an HSM transition label) does nothing.
- **Action Required:**
  1. In the right-click handler, check if the active hover state is `HoverKind.CustomElement`.
  2. If so, extract the `CustomElementRef` and check if the host has a registered `ICustomElementContextMenuProvider` matching that `RendererId`.
  3. Retrieve the `ContextMenuItem` list using the `ElementKey` and display them using the standard ImGui context menu builder.

------





Here is the detailed implementation packet for **Phases 5 & 6**, which will fix the specific authoring commands and custom canvas renderers for the BTree and HSM editors.

You can pass this directly to your AI fixing agent.

------

### 🎯 ACTION PACKET 4: Phases 5 & 6 — BTree & HSM Authoring Hosts

**Agent Context:** We are evaluating the Phase 5 (BTree) and Phase 6 (HSM) authoring hosts. The projection and layout models are largely correct, but several command sinks are stubbed out and specific visual requirements from the specs were missed. Please execute the following fixes to satisfy Acceptance Criteria F5-13, F6-05, F6-18, and F6-19.

#### TASK-BT-S1-11: BTree Observer Guard Badges

- **Target File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/ObserverGuardBadgeRenderer.cs` (or the renderer mapped to `btree.observer_guard_badges`).
- **The Flaw:** The custom canvas renderer is registered for the `AfterWires` pass but its `Render` loop does not actually draw the badges on the canvas.
- **Action Required:**
  1. Inside `Render(ICanvasRenderContext ctx)`, iterate through `ctx.Graph.Links`.
  2. Resolve the source node (`FromNode`) and target node (`ToNode`) for each link.
  3. If `FromNode.Kind.Id == "bt.composite.observerSelector"` AND `ToNode.Kind.Id` is `"bt.leaf.condition"` (or observer):
  4. Calculate the visual midpoint of the link biased towards the parent (e.g., `t = 0.3f` on the bezier curve).
  5. Render a small ImGui filled rect/pill containing the text `👁 OBSERVES` at that coordinate.

#### TASK-HS-S1-08 & TASK-HS-S1-10: Implement `HsmCommandSink` Stubs

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs`
- **The Flaw:** The command sink processes standard node moves and links, but the container and attachment methods (`ApplyAddRegion`, `ApplyRemoveRegion`, `ApplyReorderRegions`, `ApplyAddAttachment`, `ApplyRemoveAttachments`) contain literal `/* TODO */` comments.
- **Action Required:** Replace the stubs with actual implementations.
  1. **For Regions:** Resolve `cmd.ContainerId` to the `StateNode`. Mutate its `Regions` list based on the command payload (e.g., `Regions.Insert(cmd.Index, new RegionDescriptor(...))`).
  2. **For Attachments:** Resolve `cmd.HostNodeId`, add/remove the attachment record from the `HsmAsset.Attachments` dictionary or list.
  3. **Critical:** For all of these, invoke `_asset.MarkDirty()` (or trigger `IGraphModel.Changed`) so the editor queues a save and NodeEditor re-renders the new layout. Ensure attachment notifications populate the new `AffectedAttachments` set from our previous packet!

#### TASK-HS-S1-14: Internal Transition Rendering

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/` (Transition Renderer / Custom Canvas Renderers).
- **The Flaw:** Internal transitions are currently rendered using standard NodeEditor links, causing them to draw as arcs that loop *outside* the state node. The spec explicitly requires them to be strictly *inside* the source state (`HSM_Editor_NodeEditor_Host_Design.md §7.4`).
- **Action Required:**
  1. Check if the transition `Kind == TransitionKind.Internal`.
  2. If true, set the underlying NodeEditor `ILinkModel.Style` to a hidden/transparent style (or collapse its routing to a single point) so the default wire renderer ignores it.
  3. In your `hsm.transition_labels` (or a dedicated internal transition) custom renderer pass, draw a dashed curved path (or a small looping arrow) contained entirely within the `NodeInteriorBounds` of the source state.
  4. Render the event/action label directly next to this inner loop.

------





Here is the detailed implementation packet for **Phases 8 & 9 (Runtime Read-Only Inspection)**. This will replace the stubs in the debug sessions and UI panels so they actually pull live data out of the ECS memory.

You can pass this directly to your AI fixing agent.

------

### 🎯 ACTION PACKET 5: Phases 8 & 9 — Runtime Inspection Detailed Fixes

**Agent Context:** We are implementing the read-only runtime inspection for the BTree and HSM debug sessions. Currently, the UI overlays and runtime inspector panes are empty because the debug sessions return `null` for snapshots and do not poll the trace buffers. Please execute the following fixes to satisfy Acceptance Criteria F8-01 through F8-06 and F9-01 through F9-06.

#### TASK-BT-S2-01 & TASK-HS-S2-01: Session ECS Injection & Snapshot Generation

- **Target Files:** `src/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` & `src/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`
- **The Flaw:** The sessions currently lack access to the `EntityRepository` and the active `Entity`, so `GetCurrentStateSnapshot()` returns `null`.
- **Action Required:**
  1. **Dependency Injection:** Add `EntityRepository _repo` and `EditorSelectionStore _selection` to both session constructors, or expose an `Update(EntityRepository repo, Entity activeEntity)` method that the editor frame loop calls.
  2. **BTree Snapshot:** Implement `GetCurrentStateSnapshot()`.
     - Get the selected entity. If `_repo.HasComponent<BrainBTreeState>(entity)` is true, read the component via `GetComponentRO<BrainBTreeState>`.
     - Extract `RunningNodeIndex`, `StackPointer`, `TreeVersion`.
     - Copy the `NodeIndexStack`, `LocalRegisters`, and `AsyncHandles` fixed arrays into managed arrays/lists.
     - Attempt to map the `RunningNodeIndex` to a Visual Guid using the asset's `NodeDebugMetadata` (if available). Return a `BehaviorTreeStateSnapshot`.
  3. **HSM Snapshot:** Implement `GetCurrentStateSnapshot()`.
     - Get the selected entity. Check `_repo.HasComponent<BehaviorState>(entity)`.
     - Check `BehaviorState.BrainTier`. Based on the tier or component presence, read `BrainHsm64`, `BrainHsm128`, or `BrainHsm256`.
     - Cast the component memory to `InstanceHeader*`. Extract `Phase`, `MicroStep`, `Generation`, `Flags`, `RngState`.
     - Extract `ActiveLeafIds`, `TimerDeadlines`, `HistorySlots`, and `EventQueue` using the Tier-specific byte offsets (e.g., Tier 1 has 1 event, Tier 2 has a ring buffer). Return an `HsmInstanceSnapshot`.

#### TASK-BT-S2-05 & TASK-HS-S2-05: Unmanaged Trace Buffer Polling

- **Target Files:** `BTreeDebugSession.cs` & `HsmDebugSession.cs`
- **The Flaw:** The trace buffers (`BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024`) are being populated by the engine, but the editor never reads them to update the Trace Timeline.
- **Action Required:**
  1. In both session classes, add a state field: `private ushort _lastReadPos;`.
  2. Inside the session's per-frame `Update` method, check if the selected entity has the trace component (e.g., `BTreeTraceWorkingMemory1024`).
  3. Read the component. If `trace.WritePos == _lastReadPos`, return (nothing new).
  4. Iterate from `_lastReadPos` to `trace.WritePos` by increments of `16` (the `RecordStride`). Handle wrapping: if you reach `trace.CapacityBytes` (1008), wrap back to 0.
  5. At each 16-byte offset inside `trace.Buffer`, cast the pointer to `BTreeTraceRecord*` (or `TraceRecord*` for HSM).
  6. Depending on the `OpCode`, route the record into the session's history lists (e.g., call `RecordNodeExecuted`, `RecordAsyncEvent`, or `RecordTrace`).
  7. Update `_lastReadPos = trace.WritePos;`.

#### TASK-BT-S2-03: Live Blackboard Values in Inspector

- **Target File:** `src/Hrot.BTree.Editor/Blackboard/LiveBlackboardPanel.cs` (or equivalent file)
- **The Flaw:** The panel iterates the blackboard schema fields but renders a disabled `"--"` for the live values.
- **Action Required:**
  1. Ensure the panel has access to `EntityRepository` and the selected `Entity`.
  2. Check `if (repo.HasComponent<BrainBlackboard>(entity))`. If true, get a reference to `BrainBlackboard.BehaviorParameters`.
  3. Also check if the entity has `Blackboard1024`. If true, get a reference to `Blackboard1024.Memory`.
  4. For each field in the schema:
     - Determine if it lives in `BehaviorParameters` (the light DTO) or `Blackboard1024` (the heavy DTO) based on the schema reflection data.
     - Calculate the exact byte pointer: `byte* fieldPtr = basePtr + field.FieldOffset;`.
     - Use `System.Runtime.InteropServices.MemoryMarshal.Read<T>(...)` or `Unsafe.ReadUnaligned<T>(fieldPtr)` to extract the primitive value (int, float, bool, etc.) based on `field.FieldType`.
     - Render the actual value string in the ImGui table (e.g., `ImGui.TextUnformatted(val.ToString())`).

------



Here is the detailed implementation packet for the final set of tasks: **Phase 10 (Stepping & Breakpoints)**.

This packet will complete the debug session logic, connect the step buttons to the engine's time controller, and finalize the interactive runtime overlays. You can pass this directly to your AI fixing agent.

------

### 🎯 ACTION PACKET 6: Phase 10 — Stepping & Breakpoints (Detailed Fixes)

**Agent Context:** We are finalizing the debugging suite for both the BTree and HSM editors (Phase 10). The step control buttons currently do nothing, and several advanced custom renderers lack their required hit-testing and rendering bypasses. Please execute the following fixes to satisfy Acceptance Criteria F10-01 through F10-12.

#### TASK-HS-S3-01: Transition Breakpoint Rendering

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs` (or your transition label renderer)
- **The Flaw:** The breakpoint gutter renderer skips transition breakpoints.
- **Action Required:**
  1. Iterate over `_session.GetBreakpoints()`. If the breakpoint does not match a state, try `_asset.FindTransitionByVisualId(bp.ElementId)`.
  2. If it matches a transition, use the NodeEditor's bezier math (`LinkBezier.GetPointAt(0.5)`) to locate the transition's midpoint in canvas space.
  3. Render a small red filled circle (affordance dot) next to the transition label to indicate the active breakpoint.

#### TASK-BT-S3-02 & TASK-HS-S3-02: Implement Step Control State Machines

- **Target Files:** `src/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` & `src/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`
- **The Flaw:** `OnStepOverImpl`, `OnStepIntoImpl`, and `OnStepOutImpl` are empty stubs.
- **Action Required:**
  1. Add tracking fields to the sessions (e.g., `_stepMode`, and the starting depth or microstep).
  2. In the step implementations, set the `_stepMode`, record the current execution depth, and call the injected time controller's `RequestStepOneTick()` method to advance the engine.
  3. Update the trace buffer polling loop: after a step is executed, evaluate the new state. If the step condition is met (e.g., returning to the previous stack depth for `StepOver`), request a pause again via the time controller.

#### TASK-BT-S3-03: Subtree Boundary AABB Computation

- **Target File:** `src/Hrot.BTree.Editor/Renderers/SubtreeBoundaryRenderer.cs`
- **The Flaw:** The renderer does not dynamically compute the bounding box of the active subtree nodes.
- **Action Required:**
  1. Read the `BehaviorTreeStateSnapshot`. If the simulation is paused inside a subtree (`StackPointer > 0`), extract the subtree's root entry node using `NodeIndexStack`.
  2. Traverse the children of that root node via the `IGraphModel`.
  3. Compute a combined Axis-Aligned Bounding Box (AABB) of their `NodeInteriorBounds`.
  4. Render a faint blue dashed rectangle encompassing this combined area in the `BeforeContent` pass.

#### TASK-HS-S3-03: Region Conflicts Hit-Testing & Popup

- **Target File:** `src/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs`
- **The Flaw:** The renderer draws the yellow line and ⚠ glyph, but it is not clickable.
- **Action Required:**
  1. Implement the `ICustomCanvasHitTester` interface.
  2. Return a valid `CustomElementHit` if the mouse intersection falls on the ⚠ glyph.
  3. Wire the selection of this element to open an ImGui popup (or Details panel routing). The popup must list the conflicting `CommandLane` and actions, and offer a "Suppress this warning" button.

#### TASK-HS-S3-04: History & Final States Rendering Bypass

- **Target File:** `src/Hrot.Hsm.Editor/Renderers/HsmHistoryGlyphsRenderer.cs`
- **The Flaw:** History (H, H*) and Final (⊙) states render their custom glyphs, but the standard rectangular node body still draws behind them.
- **Action Required:**
  1. Assign a specific theme `Category` to the `StateNode` for history and final states.
  2. In the editor theme definitions, map this category to have a completely transparent background and border (`Vector4.Zero`).
  3. The custom renderer will draw the 20px circle glyph at the node's center coordinate. This ensures the standard node rectangle is bypassed visually, but NodeEditor still natively handles the selection outline and hit-testing.





