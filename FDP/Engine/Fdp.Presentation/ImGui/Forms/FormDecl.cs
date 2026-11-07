using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Presentation.Forms;

/// <summary>How a field's value is read, shown and edited.</summary>
public enum FieldKind : byte { Number, Text, Choice, Toggle }

/// <summary>
/// ⭐ U-12 / D1 (docs/UX/DESIGN_Details_Forms.md §2.1 — the design's <i>FormRow</i>; named <c>FormField</c> here because D0's row DRAWER already owns the name
/// <see cref="FormRow"/>) — ONE declared field: its label, the attribute it writes (a vocabulary path — ⛔ a form writes no component directly, §2.3), how it reads
/// the world (allocation-free: a number, or a fixed string), its unit and range, and whether it is an expert row. Built ONCE, never per frame.
/// </summary>
public sealed class FormField
{
    public required string Label { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Number;
    /// <summary>The attribute this field writes, by its vocabulary path (<c>Health.Current</c>); null = read-only (a derived or file-header value).</summary>
    public string? Attribute { get; init; }
    /// <summary>A number / a choice's index / a toggle (0 or 1): read from the world, 0 B. Null for a text field.</summary>
    public Func<EntityRepository, Entity, double>? ReadNumber { get; init; }
    /// <summary>A text field's value as a fixed string (a struct — comparing it allocates nothing).</summary>
    public Func<EntityRepository, Entity, FixedString64>? ReadText { get; init; }
    /// <summary>Whether this entity has the value at all (a component missing ⇒ the row is not drawn for it); null = always.</summary>
    public Func<EntityRepository, Entity, bool>? Has { get; init; }
    /// <summary>The unit the number is written in (<c>s</c>, <c>m</c>, <c>%</c> …), from the semantic type.</summary>
    public string? Unit { get; init; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    /// <summary>A choice's options, in index order (the shown words).</summary>
    public IReadOnlyList<string>? Choices { get; init; }
    /// <summary>The attribute value a choice writes for an index (default: the index as an int, or the option's word for a string attribute).</summary>
    public Func<int, AttributeValue>? ChoiceValue { get; init; }
    /// <summary>A row for experts: drawn inside <i>Details for experts</i> (§2.3).</summary>
    public bool Expert { get; init; }

    /// <summary>The vocabulary row this field writes (resolved once), or null for a read-only field.</summary>
    public AttributeDefinition? Definition => _definition ??= Attribute == null ? null : FormCatalogue.AttributeOf(Attribute);
    private AttributeDefinition? _definition;
    public bool ReadOnly => Definition == null;

    /// <summary>The change this field writes for <paramref name="number"/> / <paramref name="text"/> — by the attribute's declared kind (§2.1: "the attribute already knows").</summary>
    public EntityAttributeChange? ChangeFor(double number, string? text)
    {
        var d = Definition;
        if (d == null) return null;
        AttributeValue v = Kind switch
        {
            FieldKind.Text => AttributeValue.FromString(text),
            FieldKind.Choice when ChoiceValue != null => ChoiceValue((int)number),
            FieldKind.Choice when d.Kind == AttributeValueKind.CsString => AttributeValue.FromString(Choices != null && (int)number >= 0 && (int)number < Choices.Count ? Choices[(int)number] : null),
            FieldKind.Toggle => AttributeValue.FromBool(number != 0),
            _ => d.Kind switch
            {
                AttributeValueKind.CsInt32 => AttributeValue.FromInt((int)Math.Round(number)),
                AttributeValueKind.CsInt64 => AttributeValue.FromLong((long)Math.Round(number)),
                AttributeValueKind.CsString => AttributeValue.FromString(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => AttributeValue.FromDouble(number),
            },
        };
        return new EntityAttributeChange { AttributeId = d.AttributeId, Value = v };
    }

    /// <summary>Out of range ⇒ the reason (§2.3: red with the reason, ⛔ never silently clamped); null = fine.</summary>
    public string? RangeProblem(double v)
        => v < Min ? $"at least {Min:0.##}{(Unit == null ? "" : " " + Unit)}" : v > Max ? $"at most {Max:0.##}{(Unit == null ? "" : " " + Unit)}" : null;
}

/// <summary>⭐ U-23 / D4 — one row of a <see cref="FormList"/>: the item's role word (cached by the declarer), who it is, and the entity the row's action acts on.</summary>
public readonly record struct FormListItem(string Role, FixedString64 Who, Entity Item);

/// <summary>
/// ⭐ U-23 / D4 (docs/UX/DESIGN_Details_Forms.md §4.0a part 4) — a form's LIST of things with actions (the Crew &amp; cargo form's seats): <see cref="Fill"/> writes
/// this frame's rows into a reused buffer (0 B), each row carries <see cref="RemoveLabel"/>; <see cref="AddLabel"/> picks an entity through the one pick context
/// and hands it to <see cref="Add"/>. ⛔ A list declares no write path of its own — its actions call the domain's one path (the embark orders).
/// </summary>
public sealed class FormList
{
    public required string Label { get; init; }
    /// <summary>This frame's rows for <c>entity</c>, into the caller's reused list (cleared by the caller).</summary>
    public required Action<EntityRepository, Entity, List<FormListItem>> Fill { get; init; }
    /// <summary>A line under the rows (e.g. <i>3 passenger seats free</i>), or null.</summary>
    public Func<EntityRepository, Entity, string?>? Footer { get; init; }
    public string? RemoveLabel { get; init; }
    /// <summary>The row's action (e.g. take the item off); null = no per-row action.</summary>
    public Action<EntityRepository, FormListItem>? Remove { get; init; }
    public string? AddLabel { get; init; }
    /// <summary>The add action for a picked entity; returns the refusal reason, or null when sent.</summary>
    public Func<EntityRepository, Entity, Entity, string?>? Add { get; init; }
}

/// <summary>⭐ U-12 / D1 — one form: a title, what an entity must carry to be offered it, and its fields (common first, experts last). A declaration, like an ActionDecl.</summary>
public sealed class FormDecl
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public int Order { get; init; }
    /// <summary>The components an entity must carry for this form to be offered (§3 <i>Requires</i>); empty = the world form (nothing selected).</summary>
    public IReadOnlyList<Type> Requires { get; init; } = Array.Empty<Type>();
    public required IReadOnlyList<FormField> Fields { get; init; }
    /// <summary>⭐ U-23 / D4 — the form's lists with actions, drawn after the rows (empty for most forms).</summary>
    public IReadOnlyList<FormList> Lists { get; init; } = Array.Empty<FormList>();
    /// <summary>When the form is offered for an entity, if not by <see cref="Requires"/> alone (an any-of: the Shape form is offered for a spawn area OR a weather region).</summary>
    public Func<EntityRepository, Entity, bool>? Offers { get; init; }
    /// <summary>A WORLD form (the Scenario form): drawn when NOTHING is selected, for the entity this returns (<see cref="Entity.Null"/> = not offered).</summary>
    public Func<EntityRepository, Entity>? WorldTarget { get; init; }

    /// <summary>The labels of the common rows, then the expert rows (built once — the label column's widest).</summary>
    public string[] Labels => _labels ??= BuildLabels();
    private string[]? _labels;
    private string[] BuildLabels() { var l = new string[Fields.Count]; for (int i = 0; i < l.Length; i++) l[i] = Fields[i].Label; return l; }

    public bool OffersFor(EntityRepository world, Entity e)
    {
        if (Offers != null) return world.IsAlive(e) && Offers(world, e);
        for (int i = 0; i < Requires.Count; i++)
        {
            int id = ComponentTypeRegistry.GetId(Requires[i]);
            if (id < 0 || !world.HasComponentByTypeId(e, id)) return false;
        }
        return Requires.Count > 0;
    }
}

/// <summary>
/// ⭐ U-12 / D1 — THE forms: <see cref="Register"/> by the host that declares them (Unit, Crew &amp; cargo, Shape, Scenario — U-23), read by the Details bundle
/// (a tab per offered form). Resolves a field's attribute in the ONE vocabulary.
/// </summary>
public static class FormCatalogue
{
    private static readonly List<FormDecl> _forms = new();
    private static Dictionary<string, AttributeDefinition>? _attributes;

    public static IReadOnlyList<FormDecl> All => _forms;

    public static void Register(FormDecl form)
    {
        for (int i = 0; i < _forms.Count; i++)
            if (_forms[i].Id == form.Id) { _forms[i] = form; return; }
        _forms.Add(form);
        _forms.Sort(static (a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>The forms offered for <paramref name="e"/>, in order (into a caller's reused list — 0 B).</summary>
    public static void OfferedFor(EntityRepository world, Entity e, List<FormDecl> into)
    {
        into.Clear();
        for (int i = 0; i < _forms.Count; i++) if (_forms[i].OffersFor(world, e)) into.Add(_forms[i]);
    }

    /// <summary>The vocabulary row of <paramref name="jsonPath"/>; throws for an unknown path — a field naming no attribute is a declaration error (§2.3, Q59's rule).</summary>
    public static AttributeDefinition AttributeOf(string jsonPath)
    {
        if (_attributes == null)
        {
            var d = new Dictionary<string, AttributeDefinition>(StringComparer.Ordinal);
            foreach (var a in AttributeVocabulary.All) d[a.JsonPath] = a;
            _attributes = d;
        }
        return _attributes.TryGetValue(jsonPath, out var def) ? def
             : throw new ArgumentException($"A form field writes '{jsonPath}', which is no attribute of the vocabulary — add the vocabulary row, its setter and its handler first.");
    }
}
