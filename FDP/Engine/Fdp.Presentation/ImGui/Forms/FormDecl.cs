using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Presentation.Forms;

/// <summary>How a field's value is read, shown and edited.</summary>
public enum FieldKind : byte { Number, Text, Choice, Toggle, Pick }

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
    /// <summary>A number / a choice's index / a toggle (0 or 1): read from the world, 0 B. Null for a text field. ⭐ R-2 (AR-4): leave it null on a field that sets <see cref="Attribute"/> and the form reads the
    /// vocabulary's reader (<see cref="AttributeReaders"/>) — set it only for a DERIVED value the vocabulary cannot read (a type default, a count, a text built from several components).</summary>
    public Func<EntityRepository, Entity, double>? ReadNumber { get; init; }
    /// <summary>A text field's value as a fixed string (a struct — comparing it allocates nothing). ⭐ R-2: null on a field with an <see cref="Attribute"/> = the reader's text.</summary>
    public Func<EntityRepository, Entity, FixedString64>? ReadText { get; init; }
    /// <summary>Whether this entity has the value at all (a component missing ⇒ the row is not drawn for it); null = always — ⭐ R-2: a field read through its attribute is drawn only where the reader has a value.</summary>
    public Func<EntityRepository, Entity, bool>? Has { get; init; }
    /// <summary>The unit the number is written in (<c>s</c>, <c>m</c>, <c>%</c> …). ⭐ R-2 (AR-7): when the field sets none, a plain number takes its attribute's own unit
    /// (<see cref="AttributeDefinition.Unit"/>); a slider (the Character rows) shows none.</summary>
    public string? Unit { get => _unit ?? (Kind == FieldKind.Number && !Slider ? Definition?.Unit : null); init => _unit = value; }
    private readonly string? _unit;
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    /// <summary>A choice's options, in index order (the shown words).</summary>
    public IReadOnlyList<string>? Choices { get; init; }
    /// <summary>The attribute value a choice writes for an index (default: the index as an int, or the option's word for a string attribute).</summary>
    public Func<int, AttributeValue>? ChoiceValue { get; init; }
    /// <summary>A row for experts: drawn inside <i>Details for experts</i> (§2.3).</summary>
    public bool Expert { get; init; }
    /// <summary>⭐ U-23 fidelity (details-forms-visual.svg) — this field STARTS a captioned section (<i>Identity</i>, <i>State</i>, <i>Character</i>); null = continues the current one.</summary>
    public string? Section { get; init; }
    /// <summary>⭐ U-24 / S6 — the value this field's TYPE gives the entity, or null when nothing is authored over it (no mark): when it differs from the shown value the row's status slot shows the blue reset arrow
    /// (<see cref="FormMark.DiffersFromType"/>, the tooltip <i>type 0.50 — reset</i>) and a click writes <see cref="ResetValue"/> through <see cref="ChangeFor"/>. Called per entity per frame: 0 B.</summary>
    public Func<EntityRepository, Entity, double?>? TypeValue { get; init; }
    /// <summary>What the reset writes (the attribute's own "back to the type" value — the Character attributes: a NEGATIVE number clears the authored bit). ⛔ Not range-checked: it is not a typed value.</summary>
    public double ResetValue { get; init; } = -1;
    /// <summary>⭐ U-23 part 5 — a number shown as <i>value / max</i> (Strength: <c>80 / 100</c>); the write stays on <see cref="Attribute"/>. Single selection only (a multi-selection shows the plain value / <i>mixed</i>). Cached per change: 0 B.</summary>
    public Func<EntityRepository, Entity, double>? MaxOf { get; init; }
    /// <summary>⭐ U-24 / S5 — the attribute's SUB-INDEX this field writes (<c>Damage.Part.*</c>: the part id; <c>Ammo.*</c>: the mount): carried into <see cref="EntityAttributeChange.SubIndex1"/> by <see cref="ChangeFor"/>. 0 = none.</summary>
    public short SubIndex { get; init; }
    /// <summary>⭐ U-24 / S5 — the label SHOWN for the first selected entity when it depends on the entity (the damage rows name the PART of this body: <i>Torso</i>, <i>Hull</i>); null = <see cref="Label"/>.
    /// Must return a cached string (called per frame: 0 B). <see cref="Label"/> stays the declaration's key and the label column's width.</summary>
    public Func<EntityRepository, Entity, string>? LabelFor { get; init; }
    /// <summary>⭐ U-24 / S5 (details-forms-visual.svg) — this field is drawn on the SAME row as the one before it, after a small <see cref="InlineCaption"/> (<i>Weapon</i> [Aimed] <i>at</i> [pick]; <i>Playable</i> ☑ <i>slot</i> [..]):
    /// generic — the view knows no field by label. The row's mark slot, label and status belong to the first field.</summary>
    public bool Inline { get; init; }
    /// <summary>⭐ U-24 / S5 — a <see cref="FieldKind.Pick"/> field's picked entity (map pick or the entity list, through the form's one pick context): returns the refusal, or null. The field shows <see cref="ReadText"/> (the picked one's name; empty = none, the button reads <i>Pick on map…</i>) and clears through <see cref="Apply"/>.</summary>
    public Func<EntityRepository, Entity, Entity, string?>? PickApply { get; init; }
    /// <summary>The small caption drawn before an <see cref="Inline"/> field (<i>at</i>, <i>slot</i>).</summary>
    public string? InlineCaption { get; init; }

    /// <summary>A bounded number drawn as a slider (the mockup's Character rows) instead of an input; written when the drag ends.</summary>
    public bool Slider { get; init; }

    /// <summary>The vocabulary row this field writes (resolved once), or null for a read-only field.</summary>
    public AttributeDefinition? Definition => _definition ??= Attribute == null ? null : FormCatalogue.AttributeOf(Attribute);
    private AttributeDefinition? _definition;
    public bool ReadOnly => Definition == null && Apply == null;

    // ── ⭐ R-2 (AR-4, docs/UX/DESIGN_Review_Perspective.md §6.13a) — the ONE default read: a field with an Attribute and no explicit delegate reads through the vocabulary's reader ──

    private ushort _attributeId; private bool _attributeResolved;
    private FixedString64[]? _choiceTokens;

    private bool TryReadAttribute(EntityRepository w, Entity e, out AttributeValue value)
    {
        if (!_attributeResolved)
        {
            _attributeResolved = true;
            if (Attribute == null || !AttributeReaders.TryResolve(Attribute, out _attributeId, out _)) _attributeId = 0;
        }
        if (_attributeId == 0) { value = default; return false; }
        return AttributeReaders.TryRead(w, e, _attributeId, SubIndex, out value);
    }

    /// <summary>The value as the number this field shows: explicit <see cref="ReadNumber"/>, else the attribute's reader (a text attribute of a Choice — a side's <c>FORCE_*</c> token — becomes the index of the choice
    /// whose <see cref="ChoiceValue"/> writes it). <c>false</c> = the entity has no such value. 0 B.</summary>
    public bool TryReadNumber(EntityRepository w, Entity e, out double number)
    {
        if (ReadNumber != null) { number = ReadNumber(w, e); return true; }
        number = 0;
        if (!TryReadAttribute(w, e, out var v)) return false;
        number = v.Kind switch
        {
            AttributeValueKind.CsInt32 => v.IntValue,
            AttributeValueKind.CsInt64 => v.LongValue,
            AttributeValueKind.CsFloat32 => v.FloatValue,
            AttributeValueKind.CsFloat64 => v.DoubleValue,
            AttributeValueKind.Bool => v.BoolValue ? 1 : 0,
            AttributeValueKind.CsString => ChoiceIndexOf(v.Text),
            _ => 0,
        };
        return true;
    }

    /// <summary>The text this field shows: explicit <see cref="ReadText"/>, else the attribute's reader. 0 B.</summary>
    public bool TryReadText(EntityRepository w, Entity e, out FixedString64 text)
    {
        if (ReadText != null) { text = ReadText(w, e); return true; }
        text = default;
        if (!TryReadAttribute(w, e, out var v)) return false;
        text = v.Text;
        return true;
    }

    /// <summary>The index of the choice that writes <paramref name="token"/> (its <see cref="ChoiceValue"/>'s string, else its word); -1 = none. The tokens are built once.</summary>
    private int ChoiceIndexOf(in FixedString64 token)
    {
        if (Choices == null) return -1;
        if (_choiceTokens == null)
        {
            var t = new FixedString64[Choices.Count];
            for (int i = 0; i < t.Length; i++)
            {
                var cv = ChoiceValue != null ? ChoiceValue(i) : default;
                t[i] = new FixedString64(cv.Kind == AttributeValueKind.CsString && cv.StringValue != null ? cv.StringValue : Choices[i]);
            }
            _choiceTokens = t;
        }
        for (int i = 0; i < _choiceTokens.Length; i++) if (_choiceTokens[i].Equals(token)) return i;
        return -1;
    }

    /// <summary>⭐ U-23 part 5 (docs/UX/DESIGN_Details_Forms.md §4.0a) — a field whose write is a DOMAIN op instead of an attribute (the Unit form's <i>Playable</i>
    /// toggle = the edit framework's owner-side add / remove of the component): called per selected entity with the new value; returns the refusal, or null.
    /// ⛔ Only for a value no attribute can carry — an attribute field writes through <see cref="ChangeFor"/>.</summary>
    public Func<EntityRepository, Entity, double, string?, string?>? Apply { get; init; }

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
            FieldKind.Toggle => d.Kind == AttributeValueKind.CsInt32 ? AttributeValue.FromInt(number != 0 ? 1 : 0) : AttributeValue.FromBool(number != 0),   // a switch attribute declared Int32 (1 / 0) takes the int
            _ => d.Kind switch
            {
                AttributeValueKind.CsInt32 => AttributeValue.FromInt((int)Math.Round(number)),
                AttributeValueKind.CsInt64 => AttributeValue.FromLong((long)Math.Round(number)),
                AttributeValueKind.CsString => AttributeValue.FromString(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => AttributeValue.FromDouble(number),
            },
        };
        return new EntityAttributeChange { AttributeId = d.AttributeId, SubIndex1 = SubIndex, Value = v };
    }

    /// <summary>Out of range ⇒ the reason (§2.3: red with the reason, ⛔ never silently clamped); null = fine.</summary>
    public string? RangeProblem(double v)
        => v < Min ? $"at least {Min:0.##}{(Unit == null ? "" : " " + Unit)}" : v > Max ? $"at most {Max:0.##}{(Unit == null ? "" : " " + Unit)}" : null;
}

/// <summary>⭐ U-23 / D4 — one row of a <see cref="FormList"/>: the item's role word (cached by the declarer), who it is, and the entity the row's action acts on.</summary>
public readonly record struct FormListItem(string Role, FixedString64 Who, Entity Item, bool Passive = false);

/// <summary>
/// ⭐ U-23 / D4 (docs/UX/DESIGN_Details_Forms.md §4.0a part 4) — a form's LIST of things with actions (the Crew &amp; cargo form's seats): <see cref="Fill"/> writes
/// this frame's rows into a reused buffer (0 B), each row carries <see cref="RemoveLabel"/>; <see cref="AddLabel"/> picks an entity through the one pick context
/// and hands it to <see cref="Add"/>. ⛔ A list declares no write path of its own — its actions call the domain's one path (the embark orders).
/// </summary>
public sealed class FormList
{
    public required string Label { get; init; }
    /// <summary>Every label a row of this list can carry (static) — folded into the form's label column so the rows line up with the fields.</summary>
    public IReadOnlyList<string>? RowLabels { get; init; }
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
    /// <summary>⭐ CE-4346 — the form's own block, drawn after its rows and lists for a single target (the Scenario form's wind levels and cloud layers:
    /// list values that travel WHOLE, Details forms Q8, so they are not attribute rows). Gets the world, the entity and whether it may be edited.</summary>
    public Action<EntityRepository, Entity, bool>? After { get; init; }

    /// <summary>The labels of the common rows, then the expert rows (built once — the label column's widest).</summary>
    public string[] Labels => _labels ??= BuildLabels();
    private string[]? _labels;
    private string[] BuildLabels()
    {
        var l = new List<string>(Fields.Count);
        for (int i = 0; i < Fields.Count; i++) l.Add(Fields[i].Label);
        for (int i = 0; i < Lists.Count; i++) if (Lists[i].RowLabels is { } rl) for (int j = 0; j < rl.Count; j++) l.Add(rl[j]);
        return l.ToArray();
    }

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
