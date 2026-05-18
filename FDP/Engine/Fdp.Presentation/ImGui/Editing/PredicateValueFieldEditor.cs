using System;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

public sealed class PredicateValueFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(SearchPredicateDto);

    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        if (name != nameof(BehaviorParamPredicateDto.Predicate))
            return null;

        return new EditNode(
            id,
            name,
            jsonPath,
            EditNodeKind.Custom,
            typeof(SearchPredicateDto),
            binding,
            null,
            metadata);
    }
}
