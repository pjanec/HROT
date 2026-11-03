using System;
using System.Linq;
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
        // ⭐ CE-4218 (RE-3 / A4): gated by what the member DECLARES ([PredicateValue]), not by its name — so it can be a standard leaf of every form
        if (!metadata.CustomAttributes.OfType<PredicateValueAttribute>().Any())
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
