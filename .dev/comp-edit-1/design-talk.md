Dnes • 14:40

Pls teake a look at the StructEdit part of the codebase (also the DESIGN.md in the StructEdit). I would like to integrate this structure editing library into our Entity Inspector and Entity watch panel as an editor for components and their fields/subfields. That will require to write an ImGui renderer for editing the component. In the version 1 i would like to implement something simple as a modal imgui window based editor that would pop up if component imgui table row is doubleclicked. The modal should contain just the stuff corresponding to the line clicked. It could be just a single edit control containing the primitive value editing (if the primitive value row was clicked), or a full imgui hierarchical table for the whole component or its subpart if structural row (having subfields) is clicked.

Also there should be OK and Cancel buttons (also tied to Enter and Escape keys).

OK button should write the new state of the component 9after the editing) to the ECS, overwriting the original.

pls suggest how we could approach the component editing using the StructEdit library.

This is an excellent use case for the `StructEdit` library. Its architecture was specifically designed for exactly this kind of UI pattern: it operates on a cloned edit buffer of the **entire** component to guarantee atomic ECS commits, while its `EditScope` feature allows you to restrict the UI to just the double-clicked field or subfield.

Here is a step-by-step approach to integrating `StructEdit` as a modal component editor in your ImGui Entity Inspector.

1\. State Management

First, introduce a small state object in your `EntityInspectorPanel` or `ComponentReflector` to hold the active editing session. Because `StructEdit` is UI-agnostic, you need to retain the `IEditSession` across frames while the ImGui modal is open.

```
private class ActiveEditState
{
    public IEditSession Session { get; set; }
    public Entity TargetEntity { get; set; }
    public Type ComponentType { get; set; }
    public string ErrorMessage { get; set; }
}

private ActiveEditState? _activeEdit;
private readonly IComponentEditService _editService = new ComponentEditServiceBuilder().Build();
```

2\. Triggering the Edit Session (Double-Click)

When rendering the component tree in your `ComponentReflector`, track the JSON-like path of the current property (e.g., `$.SimTransform.Position`). When a double-click is detected on a row, grab a copy of the component from the ECS and open a **scoped** edit session.

```
// Inside your ImGui property tree drawing loop:
if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
{
    // 1. Get the current value of the entire component from the ECS
    object componentData = session.GetComponent(entity, componentType);
    
    // 2. Define the scope to ONLY include the clicked row and its children
    EditScope scope = EditScope.ForField(EditPath.Parse(currentJsonPath));
    
    // 3. Open the session
    _activeEdit = new ActiveEditState
    {
        Session = _editService.Open(componentData, componentType, scope),
        TargetEntity = entity,
        ComponentType = componentType
    };
    
    ImGui.OpenPopup("Edit Component");
}
```

_Note: Because_ _EditScope.ForField_ _sets_ _IncludeChildren = true_ _by default__, double-clicking a struct (like_ _Position__) will automatically generate an_ _EditDocument_ _containing its_ _X__,_ _Y__, and_ _Z_ _subfields, whereas clicking a primitive leaf node will result in a document with just that single scalar node._

3\. Rendering the Modal & Node Tree

Next, handle the modal rendering. You will recursively traverse the `EditDocument.Root` node and map the `EditNodeKind` to the appropriate Dear ImGui controls.

```
if (ImGui.BeginPopupModal("Edit Component", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
{
    if (_activeEdit != null)
    {
        // Recursively draw the nodes
        DrawEditNode(_activeEdit.Session.Document.Root);

        // Display validation errors if any
        if (_activeEdit.ErrorMessage != null)
            ImGui.TextColored(new Vector4(1, 0, 0, 1), _activeEdit.ErrorMessage);

        ImGui.Separator();

        // 4. Handle OK / Cancel & Keybinds
        bool isOk = ImGui.Button("OK") || ImGui.IsKeyPressed(ImGuiKey.Enter);
        ImGui.SameLine();
        bool isCancel = ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape);

        if (isOk)
        {
            try
            {
                // Commit validates the buffer and returns the WHOLE reconstructed component
                object newComponentState = _activeEdit.Session.Commit(); 
                
                // Write atomically back to the ECS
                inspectableSession.SetComponent(_activeEdit.TargetEntity, _activeEdit.ComponentType, newComponentState);
                
                CloseModal();
            }
            catch (EditValidationException ex)
            {
                // Catch any validation errors (e.g. out of range)
                _activeEdit.ErrorMessage = ex.Result.Errors.Message;
            }
        }
        else if (isCancel)
        {
            CloseModal();
        }
    }
    ImGui.EndPopup();
}

private void CloseModal()
{
    _activeEdit?.Session.Dispose(); // Cleans up the native/managed edit buffers safely
    _activeEdit = null;
    ImGui.CloseCurrentPopup();
}
```

4\. Mapping `EditNodeKind` to ImGui Controls

To draw the actual edit controls without using reflection every frame, you use the `EditNode.Binding.GetBoxed()` and `SetBoxed()` methods. `StructEdit` stabilizes identity using `EditNodeId`, making it safe and performant for immediate-mode UI rendering.

```
private void DrawEditNode(EditNode node)
{
    // Handle synthetic roots (when multiple unrelated fields are scoped) or Structs/Classes
    if (node.Kind == EditNodeKind.SelectionRoot || 
        node.Kind == EditNodeKind.Struct || 
        node.Kind == EditNodeKind.Class || 
        node.Kind == EditNodeKind.Record)
    {
        if (ImGui.BeginTable($"##table_{node.Id.Value}", 2, ImGuiTableFlags.BordersInnerV))
        {
            foreach (var child in node.Children)
                DrawEditNode(child);
            ImGui.EndTable();
        }
        return;
    }

    ImGui.TableNextRow();
    ImGui.TableSetColumnIndex(0);
    ImGui.Text(node.Name);

    ImGui.TableSetColumnIndex(1);
    ImGui.PushID(node.Id.Value); // Zero string allocation frame ID

    // Read current value from the edit buffer
    object currentValue = node.Binding!.GetBoxed();

    // Render appropriate ImGui input based on StructEdit Kind
    switch (node.Kind)
    {
        case EditNodeKind.Scalar:
            if (node.ClrType == typeof(float))
            {
                float val = (float)currentValue;
                if (ImGui.InputFloat("", ref val))
                    node.Binding.SetBoxed(val);
            }
            else if (node.ClrType == typeof(int))
            {
                int val = (int)currentValue;
                if (ImGui.InputInt("", ref val))
                    node.Binding.SetBoxed(val);
            }
            // ... handle double, long, byte etc.
            break;

        case EditNodeKind.Boolean:
            bool bVal = (bool)currentValue;
            if (ImGui.Checkbox("", ref bVal))
                node.Binding.SetBoxed(bVal);
            break;

        case EditNodeKind.String:
            string sVal = (string)currentValue ?? "";
            if (ImGui.InputText("", ref sVal, 256))
                node.Binding.SetBoxed(sVal);
            break;

        case EditNodeKind.Enum:
            // Render Enum Combo Box
            break;
    }

    ImGui.PopID();
}
```

Key Benefits of this Approach

-   **Atomic Updates:** Because the `IEditBuffer` inside `IEditSession` always clones and holds the _entire_ component behind the scenes, your `OK` button takes the output of `Session.Commit()` and pushes a fully intact component back to the ECS. Partial or corrupted component states are never written to the ECS.-   **No Reflection Overhead:** `StructEdit` computes `EditDocument` and handles memory offsets exactly _once_ when `editService.Open()` is called. The render loop only deals with `GetBoxed()` and `SetBoxed()`, which rely on cached delegates or `Unsafe.Read/Write`.-   **Built-in Validation:** If you eventually register `IComponentValidator`s (e.g., checking that a `SimVelocity` is within a maximum threshold), `StructEdit` will automatically validate against the whole component when the user clicks **OK**, blocking the commit and allowing your modal to show the error message.

Note that the StructEdit library supports attributes that can define how to handle individual fields. The imGui renderer should use them. I also need much richer imgui UI for showing array items and editing them, as well as manupulating arrays (add new item, delete item...) . Special support for fixed size arrays or c# 12 inline arrays is needed as well

To implement rich support for attributes (metadata) and array manipulation, you can leverage the `EditNodeMetadata` attached to each node and the `IContainerBinding` interface used by `StructEdit` for collections.

Here is how you can upgrade your ImGui renderer to handle these requirements.

1\. Utilizing Node Metadata (Attributes)

`StructEdit` decodes attributes like `[EditRange]`, `[EditUnit]`, and `[EditDisplayName]` into the `EditNode.Metadata` property. You can read these properties in your renderer to swap standard inputs for sliders and append units to your labels.

```
// Inside DrawEditNode...
string displayName = node.Metadata.DisplayName ?? node.Name;
string unitDisplay = node.Metadata.Unit != null ? $" {node.Metadata.Unit}" : "";

switch (node.Kind)
{
    case EditNodeKind.Scalar:
        if (node.ClrType == typeof(float))
        {
            float val = (float)currentValue;
            
            // If we have a range, render an ImGui Slider instead of an Input
            if (node.Metadata.Min.HasValue && node.Metadata.Max.HasValue)
            {
                if (ImGui.SliderFloat($"{displayName}{unitDisplay}", ref val, 
                    (float)node.Metadata.Min, (float)node.Metadata.Max))
                {
                    node.Binding.SetBoxed(val);
                }
            }
            else
            {
                if (ImGui.InputFloat($"{displayName}{unitDisplay}", ref val))
                    node.Binding.SetBoxed(val);
            }
        }
        // ... handle other scalar types (int, double, etc.) similarly
        break;
//...
```

2\. Handling Arrays and Collections

`StructEdit` categorizes arrays into three node kinds: `DynamicArray` (`List<T>`, `T[]`), `InlineArray` (C# 12 `[InlineArray]`), and `FixedBuffer` (C# `fixed` keyword). All three of these expose an `IContainerBinding` which allows you to inspect elements and resize the container.

Because collection elements are dynamic, `StructEdit` does not generate static child `EditNode` objects for them. Instead, you read and write elements directly via `IContainerBinding.GetElementBinding(index)`.

Add this block to your `switch(node.Kind)` statement to render arrays:

    ```
    case EditNodeKind.DynamicArray:
    case EditNodeKind.InlineArray:
    case EditNodeKind.FixedBuffer:
        if (node.Binding is IContainerBinding container)
        {
            string header = $"{displayName} [{container.Count}]###{node.Id.Value}";
            if (ImGui.TreeNodeEx(header, ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                // Show FixedCapacity hint for Inline/Fixed arrays if present
                if (node.Metadata.FixedLength.HasValue)
                    ImGui.TextDisabled($"Fixed Capacity: {node.Metadata.FixedLength.Value}");
    
                // 1. Draw existing elements
                for (int i = 0; i < container.Count; i++)
                {
                    IValueBinding elemBinding = container.GetElementBinding(i);
                    ImGui.PushID(i);
                    DrawArrayElement(elemBinding, i); 
                    
                    // Specific item deletion (shifting elements down)
                    if (container.CanResize) 
                    {
                        ImGui.SameLine();
                        if (ImGui.Button("X"))
                        {
                            RemoveElementAtIndex(container, i);
                            // Mark session dirty so the UI rebuilds with the new count
                            _activeEdit.Session.MarkStructuralChange();
                            _activeEdit.Session.RebuildDocument();
                        }
                    }
                    ImGui.PopID();
                }
    
                // 2. Container Manipulation (List<T> and T[])
                if (container.CanResize)
                {
                    if (ImGui.Button("+ Add Item"))
                    {
                        // Resizing creates a new collection under the hood and pushes it to the ECS buffer
                        container.Resize(container.Count + 1);
                        _activeEdit.Session.MarkStructuralChange();
                        _activeEdit.Session.RebuildDocument();
                    }
                }
                ImGui.TreePop();
            }
        }
        break;
```

3\. Array Element Helpers

Because elements use `IValueBinding` instead of a full `EditNode` tree, you will need a small helper to draw the primitive element rows, or to delete elements from the middle of the array (since `StructEdit`'s `Resize` method always truncates from the end).

```
private void DrawArrayElement(IValueBinding binding, int index)
{
    object elemValue = binding.GetBoxed();
    
    ImGui.TableNextRow();
    ImGui.TableSetColumnIndex(0);
    ImGui.Text($"[{index}]");
    ImGui.TableSetColumnIndex(1);
    
    // Basic scalar rendering for array elements
    if (binding.ValueType == typeof(float))
    {
        float val = (float)(elemValue ?? 0f);
        if (ImGui.InputFloat("", ref val))
            binding.SetBoxed(val);
    }
    else if (binding.ValueType == typeof(int))
    {
        int val = (int)(elemValue ?? 0);
        if (ImGui.InputInt("", ref val))
            binding.SetBoxed(val);
    }
    // Note: If you have arrays of complex Structs, you'll need to use reflection here 
    // to iterate over the struct's fields, or call your existing ImGui property tree drawer.
}

private void RemoveElementAtIndex(IContainerBinding container, int indexToRemove)
{
    // Shift elements down to overwrite the removed index
    for (int i = indexToRemove; i < container.Count - 1; i++)
    {
        object nextVal = container.GetElementBinding(i + 1).GetBoxed();
        container.GetElementBinding(i).SetBoxed(nextVal);
    }
    // Truncate the last element
    container.Resize(container.Count - 1);
}
```

Why `MarkStructuralChange` and `RebuildDocument`?

When you call `Resize` on a `DynamicArrayBinding`, it creates a newly sized array/list, copies the old values over, and automatically writes the new collection reference back to the parent property in the edit buffer.

However, because the UI renders based on the instruction tree (`EditDocument`), the tree itself must be told to refresh to display the new array count. Calling `Session.MarkStructuralChange()` flags the session, and `Session.RebuildDocument()` safely regenerates the UI tree while **preserving all unsaved edits** currently sitting in the buffer.

can we sync the visual look and feel of the property table used for editing with the look and feel of the imgui table used today for component field visualization? i.e. 2 column imgui table with borders etc?

Yes, you can absolutely sync the visual style to match your existing component field visualization. Based on the **ImGuiPropertyTree** implementation in your codebase, the current visualization relies on a specific 2-column table layout with alternating row backgrounds, borders, and tree-node-based indentation.

To achieve the exact same look and feel for your `StructEdit` editor, we need to apply the same `ImGuiTableFlags` and use `ImGui.TreeNodeEx` in the first column for both foldable parents and non-foldable leaves (using the `Leaf | NoTreePushOnOpen` trick to align indentation perfectly).

Here is how you can update the modal table setup and the `DrawEditNode` recursive function to match `ImGuiPropertyTree`.

1\. The Modal Table Setup

Replace the table initialization inside your modal with the exact flags used by `ImGuiPropertyTree`.

```
if (ImGui.BeginPopupModal("Edit Component", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
{
    if (_activeEdit != null)
    {
        // Use the exact flags from ImGuiPropertyTree
        if (ImGui.BeginTable("EditPropertyTree", 2, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | 
            ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            // ImGui.TableHeadersRow(); // Optional, ImGuiPropertyTree leaves this commented out

            DrawEditNode(_activeEdit.Session.Document.Root);
    
            ImGui.EndTable();
        }
    
        // ... Validation errors and OK/Cancel buttons ...
    }
    ImGui.EndPopup();
}
```

2\. The Updated `DrawEditNode` Method

We will modify the renderer so that the first column draws the hierarchical tree structure (just like `ImGuiPropertyTree` does) and the second column hosts the interactive `StructEdit` controls.

```
private void DrawEditNode(EditNode node)
{
    // SelectionRoot is just a synthetic wrapper for scoped multi-field edits; skip drawing it
    if (node.Kind == EditNodeKind.SelectionRoot)
    {
        foreach (var child in node.Children) DrawEditNode(child);
        return;
    }

    bool isContainer = node.Kind == EditNodeKind.Struct || 
                       node.Kind == EditNodeKind.Class || 
                       node.Kind == EditNodeKind.Record ||
                       node.Kind == EditNodeKind.DynamicArray || 
                       node.Kind == EditNodeKind.InlineArray || 
                       node.Kind == EditNodeKind.FixedBuffer;
    
    ImGui.TableNextRow();
    
    // ── Column 0: Property Name & Tree Node ──
    ImGui.TableSetColumnIndex(0);
    
    string displayName = node.Metadata.DisplayName ?? node.Name;
    string unitDisplay = node.Metadata.Unit != null ? $" {node.Metadata.Unit}" : "";
    bool opened = false;
    
    if (isContainer)
    {
        // Standard foldable tree node
        opened = ImGui.TreeNodeEx($"{displayName}##{node.Id.Value}", ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);
    }
    else
    {
        // Leaf nodes use NoTreePushOnOpen so they indent properly but don't act as folders
        ImGui.TreeNodeEx($"{displayName}{unitDisplay}##{node.Id.Value}", 
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    }
    
    // ── Column 1: Value Input ──
    ImGui.TableSetColumnIndex(1);
    ImGui.SetNextItemWidth(-float.Epsilon); // Stretch input controls to fill the column width
    
    if (!isContainer)
    {
        ImGui.PushID(node.Id.Value);
        object currentValue = node.Binding!.GetBoxed();
    
        switch (node.Kind)
        {
            case EditNodeKind.Scalar:
                if (node.ClrType == typeof(float))
                {
                    float val = (float)currentValue;
                    // Note: Use "##input" for the label so the label is hidden but the ID is stable
                    if (node.Metadata.Min.HasValue && node.Metadata.Max.HasValue)
                    {
                        if (ImGui.SliderFloat("##input", ref val, (float)node.Metadata.Min, (float)node.Metadata.Max))
                            node.Binding.SetBoxed(val);
                    }
                    else
                    {
                        if (ImGui.InputFloat("##input", ref val))
                            node.Binding.SetBoxed(val);
                    }
                }
                // ... handle int, double, etc.
                break;
            case EditNodeKind.Boolean:
                bool bVal = (bool)currentValue;
                if (ImGui.Checkbox("##input", ref bVal))
                    node.Binding.SetBoxed(bVal);
                break;
            case EditNodeKind.String:
                string sVal = (string)currentValue ?? "";
                if (ImGui.InputText("##input", ref sVal, 256))
                    node.Binding.SetBoxed(sVal);
                break;
        }
        ImGui.PopID();
    }
    else if (node.Binding is IContainerBinding container)
    {
        // Display array count in the value column of the header row (like ImGuiPropertyTree does)
        ImGui.TextDisabled($"[{container.Count}]");
        
        if (container.CanResize)
        {
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60);
            if (ImGui.Button($"+ Add##{node.Id.Value}"))
            {
                container.Resize(container.Count + 1);
                _activeEdit.Session.MarkStructuralChange();
                _activeEdit.Session.RebuildDocument();
            }
        }
    }
    
    // ── Recurse into Children ──
    if (opened)
    {
        if (node.Binding is IContainerBinding containerBinding)
        {
            // Draw array elements
            for (int i = 0; i < containerBinding.Count; i++)
            {
                DrawArrayElement(containerBinding, i, node.Id.Value);
            }
        }
        else
        {
            // Draw struct/class fields
            foreach (var child in node.Children)
                DrawEditNode(child);
        }
        ImGui.TreePop();
    }
}
```

3\. The Updated Array Element Drawer

To maintain the same visual layout, individual array elements must also output row elements that match `ImGuiPropertyTree`'s styling.

```
private void DrawArrayElement(IContainerBinding container, int index, int parentId)
{
    IValueBinding elemBinding = container.GetElementBinding(index);
    object elemValue = elemBinding.GetBoxed();
    
    ImGui.TableNextRow();
    
    // Column 0: Index Label
    ImGui.TableSetColumnIndex(0);
    ImGui.TreeNodeEx($"[{index}]##{parentId}_{index}", 
        ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    
    // Column 1: Input Control & Delete Button
    ImGui.TableSetColumnIndex(1);
    ImGui.PushID($"elem_{parentId}_{index}");
    
    // Give room for the 'X' button if resizable
    float inputWidth = container.CanResize ? ImGui.GetContentRegionAvail().X - 30 : -float.Epsilon;
    ImGui.SetNextItemWidth(inputWidth);
    
    if (elemBinding.ValueType == typeof(float))
    {
        float val = (float)(elemValue ?? 0f);
        if (ImGui.InputFloat("##val", ref val))
            elemBinding.SetBoxed(val);
    }
    // ... handle other primitive element types
    
    if (container.CanResize)
    {
        ImGui.SameLine();
        if (ImGui.Button("X"))
        {
            RemoveElementAtIndex(container, index);
            _activeEdit.Session.MarkStructuralChange();
            _activeEdit.Session.RebuildDocument();
        }
    }
    
    ImGui.PopID();
}
```

**Why this aligns perfectly:** By using `ImGui.TableNextRow()`, switching to `ColumnIndex(0)`, and utilizing `ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen`, Dear ImGui will handle all tree indentations natively. Your editable inputs in `ColumnIndex(1)` will perfectly match the right-side values in your current read-only visualization.

the usual arrays/lists for sure can contain complex struct fields, not just primitive values. The renderers/editors for the complex field (also for primitive ones) should be reused, no duplication!!! now way ow writing specific code just because of array item rendering!

You are absolutely right to call out code duplication. Writing separate input rendering logic for array items versus root fields is a bad UI pattern.

To achieve 100% code reuse, we need to decouple the ImGui rendering from the static `EditNode` tree and instead base it purely on **IValueBinding** and **Type**.

The Missing Link: Structs Inside Dynamic Arrays

As seen in `StructEdit`'s source code, the builder intentionally **stops** generating `EditNode`s when it hits a dynamic array because the element count varies at runtime.

To reuse your editor code for complex structs inside arrays, you must fall back to cached reflection (just like `ImGuiPropertyTree` does) to traverse the struct's fields. However, to maintain `StructEdit`'s atomic updates, we must wrap those fields in a **SyntheticMemberBinding**. This ensures that mutating a field deep inside an array element correctly bubbles the boxed struct back up into the array slot.

Here is the complete, zero-duplication refactoring.

1\. The Reusable Primitive Input

Extract the actual ImGui control logic into a single method that takes a type and returns true if modified.

```
private bool DrawPrimitiveInput(Type type, ref object value, EditNodeMetadata metadata)
{
    bool changed = false;
    ImGui.SetNextItemWidth(-float.Epsilon); // Stretch to fill column 1

    if (type == typeof(float))
    {
        float v = (float)(value ?? 0f);
        if (metadata.Min.HasValue && metadata.Max.HasValue)
            changed = ImGui.SliderFloat("##in", ref v, (float)metadata.Min, (float)metadata.Max);
        else
            changed = ImGui.InputFloat("##in", ref v);
        if (changed) value = v;
    }
    else if (type == typeof(int))
    {
        int v = (int)(value ?? 0);
        changed = ImGui.InputInt("##in", ref v);
        if (changed) value = v;
    }
    else if (type == typeof(bool))
    {
        bool v = (bool)(value ?? false);
        changed = ImGui.Checkbox("##in", ref v);
        if (changed) value = v;
    }
    else if (type == typeof(string))
    {
        string v = (string)(value ?? string.Empty);
        changed = ImGui.InputText("##in", ref v, 256);
        if (changed) value = v;
    }
    // Note: If you want to support Custom IImGuiRenderers here later, you can!
    else
    {
        ImGui.TextDisabled(value?.ToString() ?? "null");
    }
    
    return changed;
}
```

2\. The Universal Row Drawer

Replace `DrawEditNode` and `DrawArrayElement` entirely with this single recursive method. It seamlessly handles root nodes, array elements, and dynamically discovered nested struct fields.

```
// Kickoff from your modal: 
// DrawEditorRow(rootNode.Name, rootNode.ClrType, rootNode.Binding, rootNode.Metadata, rootNode.Children);

private void DrawEditorRow(
    string name, 
    Type type, 
    IValueBinding binding, 
    EditNodeMetadata metadata, 
    IReadOnlyList<EditNode>? staticChildren = null, 
    IContainerBinding? parentContainer = null, 
    int elementIndex = -1, 
    int id = 0)
{
    bool isContainer = !ImGuiPropertyTree.IsLeafType(type);

    ImGui.TableNextRow();
    
    // ── Column 0: Property Name & Tree Node ──
    ImGui.TableSetColumnIndex(0);
    string displayName = metadata.DisplayName ?? name;
    string unitDisplay = metadata.Unit != null ? $" {metadata.Unit}" : "";
    
    bool opened = false;
    if (isContainer)
    {
        opened = ImGui.TreeNodeEx($"{displayName}##{id}", ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);
    }
    else
    {
        ImGui.TreeNodeEx($"{displayName}{unitDisplay}##{id}", 
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    }
    
    // ── Column 1: Value Input & Array Controls ──
    ImGui.TableSetColumnIndex(1);
    ImGui.PushID(id);
    
    if (!isContainer)
    {
        object currentValue = binding.GetBoxed();
        if (DrawPrimitiveInput(type, ref currentValue, metadata))
        {
            binding.SetBoxed(currentValue);
        }
    }
    else if (binding is IContainerBinding container)
    {
        ImGui.TextDisabled($"[{container.Count}]");
        if (container.CanResize)
        {
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60);
            if (ImGui.Button($"+ Add##btn"))
            {
                container.Resize(container.Count + 1);
                _activeEdit.Session.MarkStructuralChange();
                _activeEdit.Session.RebuildDocument();
            }
        }
    }
    
    // Deletion button for array elements
    if (parentContainer != null && parentContainer.CanResize && elementIndex >= 0)
    {
        ImGui.SameLine();
        if (ImGui.Button("X##del"))
        {
            RemoveElementAtIndex(parentContainer, elementIndex);
            _activeEdit.Session.MarkStructuralChange();
            _activeEdit.Session.RebuildDocument();
        }
    }
    
    // ── Recurse into Children ──
    if (opened)
    {
        if (binding is IContainerBinding collection)
        {
            // Traverse array elements (no pre-computed EditNodes exist here)
            for (int i = 0; i < collection.Count; i++)
            {
                var elemBinding = collection.GetElementBinding(i);
                DrawEditorRow($"[{i}]", elemBinding.ValueType, elemBinding, EditNodeMetadata.Empty, null, collection, i, HashCode.Combine(id, i));
            }
        }
        else if (staticChildren != null && staticChildren.Count > 0)
        {
            // Path A: We have StructEdit's pre-computed reflection nodes
            foreach (var child in staticChildren)
            {
                DrawEditorRow(child.Name, child.ClrType, child.Binding!, child.Metadata, child.Children, null, -1, child.Id.Value);
            }
        }
        else
        {
            // Path B: Dynamic traversal for complex Structs inside arrays
            // Re-use ImGuiPropertyTree's cached reflection to discover fields
            var members = GetMembers(type); 
            foreach (var member in members)
            {
                var syntheticBinding = new SyntheticMemberBinding(member, binding);
                DrawEditorRow(member.Name, GetMemberType(member), syntheticBinding, EditNodeMetadata.Empty, null, null, -1, HashCode.Combine(id, member.Name.GetHashCode()));
            }
        }
        ImGui.TreePop();
    }
    
    ImGui.PopID();
}
```

3\. The Synthetic Binding (The Magic Trick)

This tiny class allows you to drill into complex array elements dynamically. It grabs a field from the boxed array item, and if you modify it, **it writes the newly boxed struct all the way back up to the parent array binding.**

_(Note: Add this inside your renderer class alongside_ _RemoveElementAtIndex__)_

```
private class SyntheticMemberBinding : IValueBinding
{
    private readonly MemberInfo _member;
    private readonly IValueBinding _parent;

    public Type ValueType => _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
    
    public SyntheticMemberBinding(MemberInfo member, IValueBinding parent)
    {
        _member = member;
        _parent = parent;
    }
    
    public object? GetBoxed()
    {
        object? parentObj = _parent.GetBoxed();
        if (parentObj == null) return null;
        return _member is FieldInfo f ? f.GetValue(parentObj) : ((PropertyInfo)_member).GetValue(parentObj);
    }
    
    public void SetBoxed(object? value)
    {
        object? parentObj = _parent.GetBoxed();
        if (parentObj == null) return;
    
        // Mutate the field on the boxed instance
        if (_member is FieldInfo f) f.SetValue(parentObj, value);
        else if (_member is PropertyInfo p) p.SetValue(parentObj, value);
    
        // CRITICAL: If the parent is a value type (struct), mutating its boxed 
        // copy does not modify the array element itself. We MUST write the 
        // boxed object back to the parent array binding!
        if (_parent.ValueType.IsValueType)
        {
            _parent.SetBoxed(parentObj);
        }
    }
    
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
```

_(Note: To make_ _GetMembers_ _and_ _GetMemberType_ _work, you can just duplicate those two tiny cached reflection helpers from your existing_ _ImGuiPropertyTree_ _class or make them internal/public there)._

wait i want the structEdit to generate editNodes for all array items, and re-generate if the count changes

You make an excellent architectural point. By forcing the ImGui renderer to fall back on reflection and synthetic bindings, we were leaking domain responsibilities into the UI layer. A much cleaner, more robust architecture is to upgrade `StructEdit` so that its `EditDocument` natively builds the entire instruction tree—including array elements and their nested structs—down to the last leaf.

If `StructEdit` generates `EditNode`s for array items, your ImGui renderer becomes completely decoupled from memory layout and reflection. If the array size changes, `Session.RebuildDocument()` simply regenerates the tree, and the UI immediately renders the new exact node count.

Here is how we refactor `StructEdit` to natively generate the full array tree.

1\. The Missing Link: `NestedMemberBinding`

To support mutating value-types (structs) inside of boxed array elements, we need to add a formal nested binding type to `StructEdit.Core`.

Create a new file **StructEdit.Core/Bindings/NestedMemberBinding.cs**:

```
using System;
using System.Reflection;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binds to a field/property inside an existing parent binding (like an array element).
/// Safely handles struct mutation by pushing modified value types back up the tree.
/// </summary>
internal sealed class NestedMemberBinding : IValueBinding
{
    private readonly IValueBinding _parent;
    private readonly MemberInfo _member;
    private readonly Action? _markDirty;

    public Type ValueType => _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
    
    public NestedMemberBinding(IValueBinding parent, MemberInfo member, Action? markDirty = null)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _member = member ?? throw new ArgumentNullException(nameof(member));
        _markDirty = markDirty;
    }
    
    public object? GetBoxed()
    {
        object? p = _parent.GetBoxed();
        if (p == null) return null;
        return _member is FieldInfo f ? f.GetValue(p) : ((PropertyInfo)_member).GetValue(p);
    }
    
    public void SetBoxed(object? value)
    {
        object? p = _parent.GetBoxed();
        if (p == null) return;
    
        if (_member is FieldInfo f) f.SetValue(p, value);
        else if (_member is PropertyInfo pi) pi.SetValue(p, value);
    
        // Crucial: If parent is a value type, mutating its boxed copy does nothing 
        // to the actual array. We MUST push the boxed object back to the parent.
        if (_parent.ValueType.IsValueType)
        {
            _parent.SetBoxed(p);
        }
        
        _markDirty?.Invoke();
    }
    
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
```

2\. Patching `ReflectionEditDocumentBuilder`

Now, we modify `StructEdit.Reflection` to flow parent bindings downward and recursively generate child nodes for arrays.

Open **ReflectionEditDocumentBuilder.cs** and apply these three precise changes:

**A. Update** **BuildNode** **Signature and Switch logic:** We add `explicitBinding` and `parentBinding` to pass context down.

```
// Update the internal BuildNode signature:
private static EditNode BuildNode(
    IEditBuffer buffer, string jsonPath, string name, Type nodeType,
    int nativeOffset, FieldInfo? fi, PropertyInfo? pi,
    IdAllocator idAlloc, HashSet<Type> visited,
    IReadOnlyList<IBufferViewProvider> providers,
    IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
    EditContext? context,
    IValueBinding? explicitBinding = null,     // NEW
    IValueBinding? parentBinding = null)       // NEW
{
    // ... circular reference guard stays the same ...
    IValueBinding? binding = explicitBinding;
    List<EditNode>? children = null;

    // ... Custom field editor check stays the same ...
    
    switch (kind)
    {
        case EditNodeKind.Struct:
        case EditNodeKind.Class:
        case EditNodeKind.Record:
            binding ??= CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType, parentBinding);
            // Pass binding down as the parent for children
            children = BuildChildren(buffer, jsonPath, nodeType, nativeOffset, idAlloc, visited, providers, fieldEditors, context, binding);
            break;
    
        case EditNodeKind.InlineArray:
            var iaAttr = nodeType.GetCustomAttribute<InlineArrayAttribute>()!;
            var iaElemType = GetInlineArrayElementType(nodeType);
            if (binding == null && buffer.IsNative && TryGetSizeOf(iaElemType, out int iaElemSize))
                binding = new InlineArrayBinding((NativeStructEditBuffer)buffer, nativeOffset, iaElemType, iaElemSize, iaAttr.Length);
            
            if (binding is IContainerBinding iaCb)
                children = BuildArrayElements(buffer, jsonPath, iaCb, iaElemType, idAlloc, visited, providers, fieldEditors, context);
            break;
    
        case EditNodeKind.FixedBuffer:
            var fbAttr = fi!.GetCustomAttribute<FixedBufferAttribute>()!;
            if (binding == null && buffer.IsNative && TryGetSizeOf(fbAttr.ElementType, out int fbElemSize))
            {
                var fbBinding = new FixedBufferBinding((NativeStructEditBuffer)buffer, nativeOffset, fbAttr.ElementType, fbElemSize, fbAttr.Length);
                binding = fbBinding;
                // ... buffer view provider check stays the same ...
            }
            if (binding is IContainerBinding fbCb)
                children = BuildArrayElements(buffer, jsonPath, fbCb, fbAttr.ElementType, idAlloc, visited, providers, fieldEditors, context);
            break;
    
        case EditNodeKind.DynamicArray:
            if (binding == null)
            {
                var pb = CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType, parentBinding);
                if (pb != null && pb.GetBoxed() is object container)
                    binding = new DynamicArrayBinding(container, pb, GetArrayElementType(nodeType));
            }
            if (binding is IContainerBinding dynCb)
                children = BuildArrayElements(buffer, jsonPath, dynCb, GetArrayElementType(nodeType), idAlloc, visited, providers, fieldEditors, context);
            break;
    
        default:
            binding ??= CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType, parentBinding);
            break;
    }
    
    var metadata = ReadMetadata(fi, pi);
    return new EditNode(new EditNodeId(idAlloc.Next()), name, jsonPath, kind, nodeType, binding, children, metadata);
}
```

**B. Add the** **BuildArrayElements** **Method:** This iterates over the `IContainerBinding` and generates nodes for each index.

```
private static List<EditNode> BuildArrayElements(
    IEditBuffer buffer, string parentPath, IContainerBinding cb, Type elementType,
    IdAllocator idAlloc, HashSet<Type> visited,
    IReadOnlyList<IBufferViewProvider> providers,
    IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
    EditContext? context)
{
    var result = new List<EditNode>();
    for (int i = 0; i < cb.Count; i++)
    {
        var elemBinding = cb.GetElementBinding(i);
        result.Add(BuildNode(
            buffer, $"{parentPath}[{i}]", $"[{i}]", elementType,
            nativeOffset: -1, fi: null, pi: null,
            idAlloc, visited, providers, fieldEditors, context,
            explicitBinding: elemBinding, parentBinding: null));
    }
    return result;
}
```

**C. Update** **BuildChildren** **and** **CreateLeafBinding** **to propagate parent context:**

```
// Update BuildChildren signature to accept parentBinding
private static List<EditNode> BuildChildren(
    IEditBuffer buffer, string parentPath, Type parentType, int parentNativeOffset,
    IdAllocator idAlloc, HashSet<Type> visited,
    IReadOnlyList<IBufferViewProvider> providers,
    IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
    EditContext? context,
    IValueBinding? parentBinding = null)  // NEW
{
    var result = new List<EditNode>();
    var flags = BindingFlags.Public | BindingFlags.Instance;

    foreach (var fi in parentType.GetFields(flags))
    {
        if (fi.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;
    
        int childOffset = -1;
        // Only calculate physical offsets if the parent is actually stored physically in unmanaged memory
        if (parentNativeOffset >= 0 && parentType.IsValueType)
        {
            try { childOffset = parentNativeOffset + (int)(nint)Marshal.OffsetOf(parentType, fi.Name); } catch { }
        }
    
        result.Add(BuildNode(buffer, $"{parentPath}.{fi.Name}", fi.Name, fi.FieldType,
            childOffset, fi, null, idAlloc, visited, providers, fieldEditors, context, null, parentBinding));
    }
    // ... Do the exact same pass for properties using parentBinding ...
```
```
// Update CreateLeafBinding to use NestedMemberBinding when deep inside a managed branch
private static IValueBinding? CreateLeafBinding(
    IEditBuffer buffer, int nativeOffset, FieldInfo? fi, PropertyInfo? pi, Type valueType,
    IValueBinding? parentBinding)
{
    if (fi == null && pi == null) return null;

    if (buffer.IsNative && fi != null && nativeOffset >= 0)
    {
        if (!TryGetSizeOf(valueType, out int fieldSize)) return null;
        return new NativeFieldBinding((NativeStructEditBuffer)buffer, nativeOffset, fieldSize, valueType);
    }
    
    if (parentBinding != null)
    {
        return new NestedMemberBinding(parentBinding, (MemberInfo?)fi ?? pi!, buffer.MarkDirty);
    }
    
    var owner = buffer.Box();
    if (fi != null) return new ManagedFieldBinding(fi, owner, buffer.MarkDirty);
    if (pi != null) return new ManagedPropertyBinding(pi, owner, buffer.MarkDirty);
    return null;
}
```

_(Note: These updates are completely compatible with_ _EditDocumentJsonSerializer_ _because it intentionally bypasses node iteration for array_ _Kind__s when reading/writing JSON)._

\--------------------------------------------------------------------------------

3\. The Radically Simplified ImGui Renderer

With `StructEdit` now doing all the heavy lifting, your ImGui UI is just a beautiful, dumb recursive tree view. It doesn't need to know anything about C# reflection, unboxing, or complex struct nesting.

```
private void DrawEditNode(EditNode node, IContainerBinding? parentContainer = null, int elementIndex = -1)
{
    if (node.Kind == EditNodeKind.SelectionRoot)
    {
        foreach (var child in node.Children) DrawEditNode(child);
        return;
    }

    bool isContainer = node.Children.Count > 0 || node.Binding is IContainerBinding;
    
    ImGui.TableNextRow();
    ImGui.TableSetColumnIndex(0);
    
    string displayName = node.Metadata.DisplayName ?? node.Name;
    string unitDisplay = node.Metadata.Unit != null ? $" {node.Metadata.Unit}" : "";
    
    bool opened = false;
    if (isContainer)
    {
        opened = ImGui.TreeNodeEx($"{displayName}##{node.Id.Value}", ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen);
    }
    else
    {
        ImGui.TreeNodeEx($"{displayName}{unitDisplay}##{node.Id.Value}", 
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    }
    
    ImGui.TableSetColumnIndex(1);
    ImGui.PushID(node.Id.Value);
    
    // 1. Array Control Buttons
    if (node.Binding is IContainerBinding container)
    {
        ImGui.TextDisabled($"[{container.Count}]");
        if (container.CanResize)
        {
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60);
            if (ImGui.Button("+ Add"))
            {
                container.Resize(container.Count + 1);
                _activeEdit.Session.MarkStructuralChange();
                _activeEdit.Session.RebuildDocument();
            }
        }
    }
    // 2. Standard Value Edit Input
    else if (!isContainer && node.Binding != null)
    {
        object currentValue = node.Binding.GetBoxed();
        if (DrawPrimitiveInput(node.ClrType, ref currentValue, node.Metadata))
            node.Binding.SetBoxed(currentValue);
    }
    
    // 3. Array Element Deletion Control
    if (parentContainer != null && parentContainer.CanResize && elementIndex >= 0)
    {
        ImGui.SameLine();
        if (ImGui.Button("X##del"))
        {
            RemoveElementAtIndex(parentContainer, elementIndex);
            _activeEdit.Session.MarkStructuralChange();
            _activeEdit.Session.RebuildDocument();
        }
    }
    
    // 4. Clean recursive rendering of children (fields or array elements!)
    if (opened)
    {
        var currentContainer = node.Binding as IContainerBinding;
        for (int i = 0; i < node.Children.Count; i++)
        {
            DrawEditNode(node.Children[i], currentContainer, currentContainer != null ? i : -1);
        }
        ImGui.TreePop();
    }
    
    ImGui.PopID();
}
```

In the component fields there are often entity references or map/world coordinates. How to enable injecting special pickers for these? We can mark those pickable fields by special attributes but we need to be able to inject the picker handlers. For example entity could be selected from a list of existing entities or clicked on the map. World location can be edited as separate X/Y/Z (or lat/lon/alt) numbers or it can be clicked on the map.

To maintain a clean architecture, we must ensure the `StructEdit` data model and the ImGui rendering loop remain completely decoupled from the application's spatial logic and entity selection mechanics. Because interactive picking (like clicking on a 2D/3D map) spans multiple frames and is inherently asynchronous, we need to bridge the gap using the Dependency Inversion Principle.

We can achieve this by feeding custom attributes through `StructEdit`'s metadata pipeline and injecting a stateful picker facade into the ImGui renderer.

Here is the architectural approach to implement this cleanly.

1\. Upgrade `StructEdit` Metadata to Carry Custom Attributes

Currently, `StructEdit` decodes specific attributes into `EditNodeMetadata` properties (like `Min`, `Max`, and `Unit`). To avoid hardcoding domain-specific attributes into the core library, upgrade `EditNodeMetadata` to carry a generic collection of custom attributes.

**Modify** **EditNodeMetadata****:**

```
public sealed record EditNodeMetadata
{
    // ... existing properties ...
    public IReadOnlyList<Attribute> CustomAttributes { get; init; } = Array.Empty<Attribute>();
    public static EditNodeMetadata Empty { get; } = new EditNodeMetadata();
}
```

**Update** **ReflectionEditDocumentBuilder****:** During the one-time reflection pass, scoop up all attributes and attach them to the metadata.

```
private static EditNodeMetadata ReadMetadata(FieldInfo? fi, PropertyInfo? pi)
{
    if (fi == null && pi == null) return EditNodeMetadata.Empty;
    var provider = (ICustomAttributeProvider?)fi ?? pi!;
    
    // ... existing specific attribute decoding ...
    
    return new EditNodeMetadata
    {
        // ... existing assignments ...
        CustomAttributes = provider.GetCustomAttributes(false).Cast<Attribute>().ToList()
    };
}
```

2\. Define the Domain Attributes

You can now use your existing attributes, such as `[MapPickableEntity]` and `[MapPickableWorldLocation]`, on your ECS components.

```
public struct ArtilleryCommand
{
    [MapPickableEntity("hostile_units")]
    public Entity TargetEntity;

    [MapPickableWorldLocation]
    public Vector3 StrikeCoordinate;
}
```

3\. Define the UI Abstraction Port (The Picker Context)

Because picking an entity or location requires the operator to interact with the map, the operation is asynchronous. The ImGui renderer needs an injected context to request a pick, display a "Pending" state across frames, and consume the result once it arrives.

Define this interface in your UI layer and implement it using your actual map/input services (e.g., bridging to `IMapPickService`). We use the stable `EditNodeId` to uniquely identify which field requested the pick.

```
public interface IComponentPickerContext
{
    bool IsPickPendingFor(int nodeId);
    
    void RequestEntityPick(int nodeId, string[]? filterPresets);
    void RequestLocationPick(int nodeId);
    
    bool TryConsumeEntityPick(int nodeId, out Entity pickedEntity);
    bool TryConsumeLocationPick(int nodeId, out Vector3 location);
}
```

4\. Inject and Render the Pickers

Now, pass the `IComponentPickerContext` into your `EntityWatchPanel` or `EntityInspectorPanel` and update your primitive drawing logic to look for the custom attributes. If a picker attribute is found, append the appropriate UI controls and handle the asynchronous consumption of the result.

```
private bool DrawPrimitiveInput(int nodeId, Type type, ref object value, EditNodeMetadata metadata, IComponentPickerContext pickerCtx)
{
    bool changed = false;
    
    // Render the standard text/numeric input first...
    // (Your existing ImGui.InputFloat / ImGui.InputText logic goes here)
    
    // ── Check for Entity Picker Attribute ──
    var entityAttr = metadata.CustomAttributes.OfType<MapPickableEntityAttribute>().FirstOrDefault();
    if (entityAttr != null)
    {
        ImGui.SameLine();
        if (pickerCtx.IsPickPendingFor(nodeId))
        {
            ImGui.TextDisabled("[Picking...]");
        }
        else if (ImGui.Button($"Pick Entity##{nodeId}"))
        {
            pickerCtx.RequestEntityPick(nodeId, entityAttr.FilterPresets);
        }
    
        // Asynchronously consume the pick result if it arrived this frame
        if (pickerCtx.TryConsumeEntityPick(nodeId, out Entity picked))
        {
            value = picked;
            changed = true;
        }
    }
    
    // ── Check for World Location Picker Attribute ──
    var locAttr = metadata.CustomAttributes.OfType<MapPickableWorldLocationAttribute>().FirstOrDefault();
    if (locAttr != null)
    {
        ImGui.SameLine();
        if (pickerCtx.IsPickPendingFor(nodeId))
        {
            ImGui.TextDisabled("[Picking...]");
        }
        else if (ImGui.Button($"Pick Map##{nodeId}"))
        {
            pickerCtx.RequestLocationPick(nodeId);
        }
    
        // Asynchronously consume the pick result if it arrived this frame
        if (pickerCtx.TryConsumeLocationPick(nodeId, out Vector3 pickedLoc))
        {
            value = pickedLoc;
            changed = true;
        }
    }
    
    return changed;
}
```

Architectural Summary

-   **Zero Domain Leakage:** `StructEdit` only holds a list of opaque `Attribute` objects. It knows nothing about maps or entities.-   **Zero Reflection in the Render Loop:** Because `ReflectionEditDocumentBuilder` extracts the attributes once at session open, the ImGui renderer only does a fast `.OfType<T>()` check on a pre-populated list every frame.-   **Async UI Safety:** By tying the pending pick request to the `EditNodeId`, the operator can safely request a pick, leave the mouse over the map, and the ImGui table will automatically update the exact row's `IValueBinding` when `TryConsumeLocationPick` resolves, preserving the atomic edit buffer integrity.

This pickers need to work with other part of the UI like map. This means they can't be used if we show the edits in a modal window. So they should be rendered only if we know the other part of UI is not completely frozen.

You are absolutely right to identify this architectural conflict. Dear ImGui's modal popups (`ImGui.BeginPopupModal`) capture all input routing, completely blocking interaction with the `MapCanvas` and making asynchronous world-picking impossible.

To solve this cleanly, we must abandon the modal popup approach and instead leverage the engine's existing `WindowManager` architecture. Specifically, the codebase already defines a pattern for spawning non-blocking, dynamically created windows using **Volatile** **ManagedWindow** instances. This is the exact pattern used by the `FdpEntityWatchWindow`.

By wrapping the `StructEdit` session in a volatile `ManagedWindow`, the editor floats freely over the application, the `MapCanvas` remains fully interactive, and our injected `IComponentPickerContext` can safely broker the async pick requests across frames.

Here is the clean architecture approach to implement this.

1\. Create a Volatile `ManagedWindow` for Editing

Instead of drawing the editor inside an `EntityInspectorPanel` modal, encapsulate the active edit state and the drawing logic into a dedicated `ManagedWindow` implementation. Setting `IsVolatile = true` ensures the `WindowManager` automatically cleans up and unregisters the window the frame after `IsOpen` becomes `false`.

```
using Fdp.Presentation.WindowManager;
using StructEdit.Core;

public sealed class FdpComponentEditWindow : ManagedWindow
{
    private readonly ActiveEditState _activeEdit;
    private readonly IComponentPickerContext _pickerCtx;
    private readonly IInspectableSession _ecsSession;

    public FdpComponentEditWindow(
        string id, 
        string title, 
        string owningPerspective,
        ActiveEditState activeEdit,
        IComponentPickerContext pickerCtx,
        IInspectableSession ecsSession)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _activeEdit = activeEdit;
        _pickerCtx = pickerCtx;
        _ecsSession = ecsSession;
        
        IsOpen = true;
        IsVolatile = true;   // Auto-unregisters when closed
        ShowInMenu = false;  // Do not clutter the global Windows menu
    }
    
    protected override void DrawClientArea()
    {
        // 1. Draw the StructEdit property tree
        if (ImGui.BeginTable("EditPropertyTree", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
    
            // Call the recursive universal row drawer we built previously
            DrawEditorRow(_activeEdit.Session.Document.Root.Name, 
                          _activeEdit.Session.Document.Root.ClrType, 
                          _activeEdit.Session.Document.Root.Binding!, 
                          _activeEdit.Session.Document.Root.Metadata, 
                          _activeEdit.Session.Document.Root.Children);
            
            ImGui.EndTable();
        }
    
        if (_activeEdit.ErrorMessage != null)
            ImGui.TextColored(new Vector4(1, 0, 0, 1), _activeEdit.ErrorMessage);
    
        ImGui.Separator();
    
        // 2. Handle OK / Cancel
        if (ImGui.Button("OK") || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            try
            {
                object newComponentState = _activeEdit.Session.Commit(); 
                _ecsSession.SetComponent(_activeEdit.TargetEntity, _activeEdit.ComponentType, newComponentState);
                
                CloseAndCleanup();
            }
            catch (EditValidationException ex)
            {
                _activeEdit.ErrorMessage = ex.Result.Errors.Message;
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CloseAndCleanup();
        }
    }
    
    private void CloseAndCleanup()
    {
        _activeEdit.Session.Dispose();
        IsOpen = false; // Signals the WindowManager to drop this volatile window
    }
    
    // ... [Include the DrawEditorRow and SyntheticMemberBinding methods here] ...
}
```

2\. Triggering the Window on Double-Click

Update your `ComponentReflector` or `EntityInspectorPanel` to require the `WindowManager` as a dependency. When a double-click occurs, generate a unique ID and instantiate the new volatile window instead of opening an ImGui popup.

```
// Inside your ImGui property tree drawing loop:
if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
{
    object componentData = session.GetComponent(entity, componentType);
    EditScope scope = EditScope.ForField(EditPath.Parse(currentJsonPath));
    
    var activeEdit = new ActiveEditState
    {
        Session = _editService.Open(componentData, componentType, scope),
        TargetEntity = entity,
        ComponentType = componentType
    };
    
    // Ensure the ID is globally unique so multiple edit windows can safely coexist
    string windowId = $"edit_{entity.Index}_{entity.Generation}_{componentType.Name}_{Guid.NewGuid()}";
    string windowTitle = $"Edit {componentType.Name} [{entity.Index}]";
    
    // Register the floating, non-blocking window
    _windowManager.RegisterWindow(new FdpComponentEditWindow(
        windowId, 
        windowTitle, 
        _windowManager.CurrentPerspective, // Bind to current perspective
        activeEdit, 
        _pickerContext, 
        session));
}
```

Why this is the structurally sound approach:

-   **True Asynchrony:** Because `FdpComponentEditWindow` is just a standard floating ImGui window registered with the `WindowManager`, the operator can easily move it aside, hover over the `MapCanvas`, and interact with the 2D/3D map.-   **Context Preservation:** Your `IComponentPickerContext` handles the state transition. The operator clicks "Pick Map", the context buffers the request, the operator clicks the map, and on the very next frame, the `FdpComponentEditWindow` reads the resolved vector from the context and applies it to the `StructEdit` buffer.-   **Graceful Teardown:** By inheriting from `ManagedWindow` and setting `IsVolatile = true`, the `WindowManager` lifecycle natively handles window destruction the moment `IsOpen` is set to `false`, requiring no messy boolean cleanup in your inspector logic. Multiple isolated edit windows can even be open simultaneously.

But how to make sure we do not end up with many different floating volatile windows if we click another fireld in the entity component inspector table? Is some cases this is a very welcome feature (having multiple edit windows at once) but as each is likely keeping its own full edit buffer of the whole component, we shoudl never allow opening multiple windows for same component ot their sub parts. Also wne the entity gets deleted, the window sould need to invalidate or close automatically.

To enforce a strict one-to-one relationship between a component instance and its edit window, and to handle the lifecycle of the underlying entity, we must establish a **deterministic identity** for the window and enforce a **liveness guard** during rendering.

Here is how to structure this cleanly using the existing `WindowManager` and `IInspectableSession` APIs.

1\. Deterministic Window Identity

In the previous example, we used a `Guid.NewGuid()` to generate the window ID. To prevent multiple edit sessions for the same component, we must strip the GUID and use a strictly deterministic composite key based on the entity and component type.

When the user double-clicks a field, we ask the `WindowManager` if this exact window ID already exists. If it does, we simply bring it to the front and abort opening a new edit buffer.

Update your `ComponentReflector` / double-click logic:

```
// Inside your ImGui property tree drawing loop:
if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
{
    // 1. Generate a deterministic ID tied to the exact entity generation and component
    string windowId = $"edit_{entity.Index}_{entity.Generation}_{componentType.Name}";

    // 2. Prevent duplicate buffers: if the window is already open, just focus it and exit
    if (_windowManager.TryGetWindow(windowId, out _))
    {
        _windowManager.FocusWindow(windowId);
        return;
    }
    
    // 3. Otherwise, fetch the data and open a new session
    object componentData = session.GetComponent(entity, componentType);
    EditScope scope = EditScope.ForField(EditPath.Parse(currentJsonPath));
    
    var activeEdit = new ActiveEditState
    {
        Session = _editService.Open(componentData, componentType, scope),
        TargetEntity = entity,
        ComponentType = componentType
    };
    
    string windowTitle = $"Edit {componentType.Name} [{entity.Index}]";
    
    _windowManager.RegisterWindow(new FdpComponentEditWindow(
        windowId, 
        windowTitle, 
        _windowManager.CurrentPerspective, 
        activeEdit, 
        _pickerContext, 
        session));
}
```

2\. The Entity Liveness Guard

Because the `FdpComponentEditWindow` floats independently across frames, the entity it targets could be destroyed by a system update, a network message, or the operator pressing the Delete key.

To prevent committing data to a dead entity (or crashing when trying to read from it), inject a liveness check at the very top of `DrawClientArea`. Your `IInspectableSession` already exposes `IsAlive(Entity)`. If the entity is dead, we forcefully close the window. Because the window has `IsVolatile = true`, the `WindowManager` will automatically unregister and dispose of it on the next frame.

Update the `FdpComponentEditWindow` implementation:

```
protected override void DrawClientArea()
{
    // 1. Liveness Guard: If the entity was destroyed elsewhere, close this window safely.
    if (!_ecsSession.IsAlive(_activeEdit.TargetEntity))
    {
        CloseAndCleanup();
        return;
    }

    // 2. Draw the StructEdit property tree
    if (ImGui.BeginTable("EditPropertyTree", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
    {
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
    
        DrawEditorRow(_activeEdit.Session.Document.Root.Name, 
                      _activeEdit.Session.Document.Root.ClrType, 
                      _activeEdit.Session.Document.Root.Binding!, 
                      _activeEdit.Session.Document.Root.Metadata, 
                      _activeEdit.Session.Document.Root.Children);
        
        ImGui.EndTable();
    }
    
    if (_activeEdit.ErrorMessage != null)
        ImGui.TextColored(new Vector4(1, 0, 0, 1), _activeEdit.ErrorMessage);
    
    ImGui.Separator();
    
    // 3. Handle Commit / Cancel
    if (ImGui.Button("OK") || ImGui.IsKeyPressed(ImGuiKey.Enter))
    {
        try
        {
            // The liveness guard above guarantees the entity still exists here
            object newComponentState = _activeEdit.Session.Commit(); 
            _ecsSession.SetComponent(_activeEdit.TargetEntity, _activeEdit.ComponentType, newComponentState);
            
            CloseAndCleanup();
        }
        catch (EditValidationException ex)
        {
            _activeEdit.ErrorMessage = ex.Result.Errors.Message;
        }
    }
    
    ImGui.SameLine();
    if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
    {
        CloseAndCleanup();
    }
}

private void CloseAndCleanup()
{
    _activeEdit.Session.Dispose(); // Free the memory buffer safely
    IsOpen = false;                // Signals WindowManager to drop this volatile window
}
```

Architectural Benefits

By enforcing identity through the `WindowManager` rather than managing a list of active editors locally:

-   **Single Source of Truth:** `WindowManager.TryGetWindow` handles the de-duplication natively. You never risk state tearing from having two edit buffers for the same component in memory.-   **Graceful Sub-Field Edits:** If the operator opens an editor for `Transform.Position.X` and then double-clicks `Transform.Position.Y`, the deterministic ID resolves to the exact same window, bringing the existing scoped edit session to focus.-   **No Memory Leaks:** By tying the `CloseAndCleanup()` execution to the `IsAlive` check, the `StructEdit` buffer memory is immediately returned to the GC or native allocator the moment the entity is flagged as destroyed.

p\[ls summarize the final idea of your approach, how it will work once implemented and what needs to be done to make it happen

This is exactly the kind of elegant, decoupled architecture I love to see. By moving away from a blocking ImGui modal and pushing the heavy lifting down into the `StructEdit` core, we have arrived at a solution that respects the separation of concerns, guarantees atomic state transitions, and integrates seamlessly with your engine's asynchronous workflows.

Here is the summary of our final architectural design, how it operates at runtime, and the concrete steps required to implement it.

The Final Architecture: How It Works

**1\. The Data Model (Zero-Reflection UI)** Instead of forcing the ImGui layer to use reflection to discover fields inside arrays, `StructEdit`'s `ReflectionEditDocumentBuilder` will natively traverse arrays and generate a complete instruction tree of `EditNode` objects down to the deepest leaf. The ImGui renderer becomes a "dumb" recursive view that simply reads from and writes to `IValueBinding` instances, completely decoupled from memory layout and unboxing logic.

**2\. Asynchronous Window Management** To support multi-frame operations like map-picking (`[MapPickableWorldLocation]`), the editor runs as a floating, non-blocking `ManagedWindow` rather than an `ImGui.BeginPopupModal`. By setting `IsVolatile = true`, the window automatically unregisters itself from the `WindowManager` the moment the user commits, cancels, or the target entity is destroyed.

**3\. Deterministic Identity & Liveness** When an operator double-clicks a component row, we generate a deterministic window ID (e.g., `edit_Index_Generation_ComponentType`). This strictly prevents duplicate edit buffers for the same component. On every frame, the window validates `_ecsSession.IsAlive(TargetEntity)`; if the entity was destroyed elsewhere (e.g., by a system update or network message), the window safely self-terminates and releases its cloned memory buffer.

**4\. Atomic ECS Commits** Under the hood, the `IEditSession` clones and holds the _entire_ component, even if the user double-clicked a specific sub-field scope. When the user clicks "OK", `StructEdit` validates the entire buffer and pushes the fully intact, mutated component back to the ECS. There are no partial overwrites or corrupted states.

The Implementation Action Plan

To make this a reality, we need to execute the following steps:

**1\. Upgrade** **StructEdit.Core** **for Nested Array Elements**

-   Create the `NestedMemberBinding` class to safely handle struct mutations deep inside boxed array elements.-   Modify `ReflectionEditDocumentBuilder.BuildNode` to flow parent bindings downward and recursively generate static `EditNode` children for `DynamicArray`, `InlineArray`, and `FixedBuffer` types.-   Update `EditNodeMetadata` to carry a list of arbitrary custom `Attribute` objects so domain-specific markers like `[MapPickableEntity]` pass through to the UI.

**2\. Implement the Picker Context Abstraction**

-   Define the `IComponentPickerContext` interface to broker asynchronous map selections without leaking spatial logic into the ECS or UI layers.-   Inject this context into your `EntityInspectorPanel` or globally via your composition root.

**3\. Implement the Volatile** **ManagedWindow**

-   Create `FdpComponentEditWindow` inheriting from `ManagedWindow`.-   Set `IsVolatile = true` and `ShowInMenu = false` in the constructor.-   Implement `DrawClientArea()` with the top-level liveness guard: `if (!_ecsSession.IsAlive(_activeEdit.TargetEntity)) { CloseAndCleanup(); return; }`.

**4\. Write the Universal ImGui Renderer**

-   Implement the `DrawEditorRow` recursive method.-   Configure the ImGui table with `ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg` and `ImGuiTableFlags.SizingFixedFit` to perfectly match your existing read-only `ImGuiPropertyTree` aesthetic.-   Map primitive `EditNodeKind`s (Scalar, Boolean, String) to standard ImGui inputs.-   If a node's metadata contains a picker attribute, render the "Pick" button and poll the `IComponentPickerContext` to safely consume the async result.-   For collection nodes, render an `+ Add` button and per-element `X` buttons that call `IContainerBinding.Resize()` followed by `IEditSession.MarkStructuralChange()` and `RebuildDocument()` to refresh the UI tree.

**5\. Wire the Double-Click Trigger**

-   In your existing `ComponentReflector`, detect `ImGui.IsMouseDoubleClicked` on a property row.-   Format the deterministic window ID.-   If `WindowManager.TryGetWindow` returns true, focus it. Otherwise, call `IComponentEditService.Open()`, instantiate the `FdpComponentEditWindow`, and register it.

