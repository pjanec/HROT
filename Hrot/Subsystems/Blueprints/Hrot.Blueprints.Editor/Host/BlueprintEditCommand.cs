using NodeEditor.Core.Commands;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-11 transport — carries a Blueprint property edit onto NodeEdit's single
/// <see cref="NodeEditor.Core.Commands.UndoStack"/>.
///
/// <para>
/// <b>Why this exists.</b> <c>UndoStack.ApplyAndRecord</c> takes a <see cref="GraphCommand"/> pair and
/// applies the forward through the sink. Every built-in variant is a <em>data</em> description of a
/// graph mutation; none can express "run this apply, run that undo". Blueprint drawer edits are
/// multi-field bakes — selecting a component type rewrites <c>ComponentTypeFqn</c>, the whole
/// <c>Fields</c> list <em>and</em> <c>IsManaged</c> in one gesture — so the obvious candidate,
/// <c>GraphCommand.SetNodeProperty(NodeId, string, object?)</c>, cannot carry them.
/// </para>
///
/// <para>
/// <b>Why it lives here and not in NodeEdit.</b> <see cref="GraphCommand"/> is a plain
/// <c>public abstract record</c>, so a host assembly can extend the vocabulary without touching the
/// vendored <c>FDP/ExtDeps/NodeEdit</c> tree. The command is meaningful only to
/// <see cref="BlueprintCommandSink"/>, which is the only sink that will ever receive one.
/// </para>
///
/// <para>
/// ⚠ <b>The sink must have an explicit case for this type.</b> <c>BlueprintCommandSink.Apply</c>'s
/// <c>default:</c> arm returns <em>success</em> for unrecognised commands, so a missing case would
/// produce an undo that silently no-ops while reporting that it worked — the exact failure class
/// BP-11 exists to remove. <c>BlueprintEditCommandTests</c> pins the case.
/// </para>
/// </summary>
/// <param name="Label">Human-readable description, surfaced as the undo entry's label.</param>
/// <param name="Mutate">
/// The mutation to run when this command is applied. One <see cref="BlueprintEditCommand"/> carries a
/// single direction: the caller builds two — a forward and an inverse — and hands both to
/// <c>GraphView.Execute</c>.
/// </param>
public sealed record BlueprintEditCommand(string Label, Action Mutate) : GraphCommand;
