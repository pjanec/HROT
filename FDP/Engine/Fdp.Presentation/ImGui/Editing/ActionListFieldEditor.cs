using System;
using System.Collections.Generic;
using Fdp.Toolkit.ScenarioRules.Actions;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐⭐ CE-4220 (rules view R5b, RE-4; docs/DESIGN_Rules_View_UI.md §7.3) — a trigger's <c>do</c> list (<c>List&lt;ActionStep&gt;</c>) as ONE standard leaf BY TYPE: the document holds the whole list as a single value
/// (an <see cref="ActionStep"/> is immutable, so an edit builds a NEW list and replaces it — CLAUDE.md "NEW COMPONENTS"), and a host that can draw it supplies the drawer (the rules editor's <c>ActionListFieldDrawer</c>).
/// A host with no drawer shows the list read-only, as any unsupported leaf. Registered in <see cref="ComponentEditServices"/> beside the predicate value leaf.
/// </summary>
public sealed class ActionListFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(List<ActionStep>);

    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath, IValueBinding binding, EditNodeMetadata metadata)
        => new(id, name, jsonPath, EditNodeKind.Custom, typeof(List<ActionStep>), binding, null, metadata);
}
