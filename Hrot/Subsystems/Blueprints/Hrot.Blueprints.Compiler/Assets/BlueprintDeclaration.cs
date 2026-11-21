namespace Hrot.Blueprints.Core.Assets;

/// <summary>
/// <b>U-9 / D1 — which of the asset's three declaration lists a declaration lives in.</b>
///
/// <para>
/// ⭐ <b>Declared in STORAGE order</b>, which is the order the three structs are laid out in
/// (<c>Params</c> @0, working state @8, <c>State</c> @16) and the order
/// <see cref="BlueprintAsset.Declarations"/> enumerates.
/// </para>
///
/// <para>
/// ⚠⚠ <b>This is NOT <c>Compiler.Ir.VariableKind</c>, and the difference is deliberate.</b> That enum
/// carries an <c>Unresolved = 0</c> sentinel whose whole job is to make a forgotten assignment throw
/// at emit; a stored declaration is never unresolved, so a model type carrying that member would be
/// inviting a state it cannot be in. The two are bridged by an <b>explicit, total</b> mapping in
/// <c>Compiler.Ir.DeclarationRefs</c> — enumerated rather than assumed, and asserted by
/// <c>TaggedDeclarationTests</c> to cover every member of both.
/// </para>
///
/// <para>
/// 📌 <b>Graph locals (<c>BP-57</c>) are deliberately NOT a kind here.</b> <c>Q27-C1</c> makes a local
/// legally <b>shadow</b> an asset variable — the two live in disjoint spaces and resolve as disjoint
/// IR ops. Folding them into this union would make <c>U-14</c>'s cross-kind uniqueness rule
/// (<c>BP-232</c>) reach into a space where duplicate names are the rule rather than the defect.
/// </para>
/// </summary>
public enum DeclarationKind
{
    /// <summary><see cref="BlueprintAsset.Parameters"/> — the <c>Params</c> struct (offset 0).</summary>
    Parameter,

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE state kind</b> — the cell <c>(Role=State, Scope=Asset)</c>.
    ///
    /// <para>📌 <b><c>R-01</c>:</b> <i>"`Variable` ≡ `WorkingState`. Two names, ONE concept."</i> ·
    /// 📌 <b><c>R-02</c>, the user's own words:</b> <i>"as the global vars and working state vars are
    /// the same stuff, it makes no sense to emit them differently."</i></para>
    ///
    /// <para>⭐ <b>Layout was already kind-agnostic before this.</b> 📐 Batch 56 made
    /// <c>FieldLayout</c> lay out <c>IrAsset.StateDeclarations</c> in ONE run from one struct base —
    /// the <c>@8</c> vs <c>@16</c> difference is <c>StateStructBase</c>, which follows
    /// <c>Dispatch</c> *(AiPrimitive vs Instance)*, ⛔ never the kind. ⇒ ⭐⭐ <b>Batch 85 MEASURED the
    /// collapse hash-neutral for all 43 compiled assets</b>, so 📌 <c>R-24</c>'s hard reset cannot
    /// fire.</para>
    ///
    /// <para>⚠ <b><c>WorkingState</c> is retired as a KIND and as an on-disk TAG.</b> ⭐ Batch 86
    /// rewrote the 16 source assets that carried it *(one word per declaration)* — 📌 the tag is not in
    /// <c>StructureHash</c>, so that rewrite moved no hash and no emit golden. ⛔ Keeping it readable
    /// but unwritable was measured impossible: with two kinds there is no information left to CHOOSE
    /// the old tag at save, so every such declaration re-spelled anyway and broke byte-stability.</para>
    /// </summary>
    Variable,

}

/// <summary>
/// <b>U-9 / D1 — one declaration, carrying which list it came from.</b>
///
/// <para>
/// ⭐⭐ <b>It is a FACADE over the stored declaration, not a copy of it.</b> Every member reads and
/// writes straight through to the backing <see cref="VariableDecl"/> or <see cref="ParameterDecl"/>.
/// ⛔ <b>A value copy would have been the defect this programme keeps filing:</b> <c>U-11</c> moves
/// ~34 consumers onto this type while the three lists are still the storage, so a copy would accept
/// <c>decl.Name = "x"</c>, report success, and discard it — trap #5, at the scale of the whole editor.
/// </para>
///
/// <para>
/// ⭐ <b>And the tag cannot reach JSON, structurally rather than by discipline:</b> the storage is
/// untouched. This type owns no state of its own beyond a reference and a tag, so there is nothing
/// for the serializer to write even if it could see it.
/// </para>
///
/// <para>
/// ⚠⚠ <b><see cref="ParameterDecl"/> is NOT the same shape as <see cref="VariableDecl"/></b> — it
/// lacks <see cref="IsEditable"/>, <see cref="IsExposedOnSpawn"/> and <see cref="Category"/>. Ruling
/// (handoff §1, option <b>a</b>): those three are <b>editor-presentation</b> members, meaningless for
/// a call parameter, and <c>U-9</c> must not touch persistence — so the absence is <b>declared</b>,
/// not patched. It is enumerated once, in <see cref="MembersAParameterDoesNotCarry"/>, and
/// <c>TaggedDeclarationTests</c> derives the same set by reflection over the two types and asserts
/// they agree, so a member added to either side cannot quietly join or leave the exclusion.
/// </para>
///
/// <para>
/// ⭐ <b>Read returns the documented default; WRITE refuses</b> — the <c>U-5</c> /
/// <see cref="CarriesEditorPresentation"/> shape. Reading <c>null</c> for a parameter's category is
/// true (it has none); <i>accepting</i> a category and dropping it is the lie.
/// </para>
/// </summary>
public sealed class BlueprintDeclaration
{
    private readonly VariableDecl?  _variable;
    private readonly ParameterDecl? _parameter;

    private BlueprintDeclaration(DeclarationKind kind, VariableDecl? variable, ParameterDecl? parameter)
    {
        Kind       = kind;
        _variable  = variable;
        _parameter = parameter;
    }

    /// <summary>Which list this declaration is stored in.</summary>
    public DeclarationKind Kind { get; }

    /// <summary>
    /// Wraps a stored <see cref="VariableDecl"/>. <paramref name="kind"/> is the caller's, because the
    /// decl itself cannot know which of the two variable lists holds it.
    /// </summary>
    public static BlueprintDeclaration For(DeclarationKind kind, VariableDecl decl)
    {
        if (kind == DeclarationKind.Parameter)
            throw new ArgumentException(
                "A Parameter declaration is backed by ParameterDecl, not VariableDecl.", nameof(kind));
        return new BlueprintDeclaration(kind, decl ?? throw new ArgumentNullException(nameof(decl)), null);
    }

    /// <summary>Wraps a stored <see cref="ParameterDecl"/>.</summary>
    public static BlueprintDeclaration For(ParameterDecl decl)
        => new(DeclarationKind.Parameter, null, decl ?? throw new ArgumentNullException(nameof(decl)));

    /// <summary>
    /// A brand-new declaration of <paramref name="kind"/>, with a freshly allocated backing object of
    /// the right shape. ⭐ The one way to build one without first knowing which concrete type to make.
    /// </summary>
    public static BlueprintDeclaration Create(DeclarationKind kind, Guid id, string name, BlueprintTypeRef? type = null)
        => kind == DeclarationKind.Parameter
            ? For(new ParameterDecl { Id = id, Name = name, Type = type ?? new BlueprintTypeRef() })
            : For(kind, new VariableDecl { Id = id, Name = name, Type = type ?? new BlueprintTypeRef() });

    /// <summary>The stored object this facade writes through to — the identity the view compares on.</summary>
    public object Backing => (object?)_variable ?? _parameter!;

    /// <summary>The backing declaration when this is a <c>Variable</c> or <c>WorkingState</c>; else null.</summary>
    public VariableDecl? AsVariableDecl => _variable;

    /// <summary>The backing declaration when this is a <c>Parameter</c>; else null.</summary>
    public ParameterDecl? AsParameterDecl => _parameter;

    // ── the members both shapes carry ───────────────────────────────────────

    public Guid Id
    {
        get => _variable?.Id ?? _parameter!.Id;
        set { if (_variable is not null) _variable.Id = value; else _parameter!.Id = value; }
    }

    public string Name
    {
        get => _variable?.Name ?? _parameter!.Name;
        set { if (_variable is not null) _variable.Name = value; else _parameter!.Name = value; }
    }

    public BlueprintTypeRef Type
    {
        get => _variable?.Type ?? _parameter!.Type;
        set { if (_variable is not null) _variable.Type = value; else _parameter!.Type = value; }
    }

    public string? DefaultValueJson
    {
        get => _variable is not null ? _variable.DefaultValueJson : _parameter!.DefaultValueJson;
        set { if (_variable is not null) _variable.DefaultValueJson = value; else _parameter!.DefaultValueJson = value; }
    }

    public string? Tooltip
    {
        get => _variable is not null ? _variable.Tooltip : _parameter!.Tooltip;
        set { if (_variable is not null) _variable.Tooltip = value; else _parameter!.Tooltip = value; }
    }

    public string? Comment
    {
        get => _variable is not null ? _variable.Comment : _parameter!.Comment;
        set { if (_variable is not null) _variable.Comment = value; else _parameter!.Comment = value; }
    }

    // ── the three a ParameterDecl does not have ─────────────────────────────

    /// <summary>
    /// ⭐ <b>The capability, so a caller can ask instead of discovering by exception</b> — the
    /// <c>U-5</c> / <c>SupportsRoleScopeEditing</c> shape, one level down.
    /// </summary>
    public bool CarriesEditorPresentation => Kind != DeclarationKind.Parameter;

    /// <summary>
    /// ⭐⭐ <b>The enumerated drop.</b> §1's ruling in code rather than in a mapping that forgot a
    /// line — and cross-checked against reflection over the two backing types by the tests, so it
    /// cannot drift from the truth in either direction.
    /// </summary>
    public static IReadOnlyList<string> MembersAParameterDoesNotCarry { get; } = new[]
    {
        nameof(IsEditable), nameof(IsExposedOnSpawn), nameof(Category),
    };

    public bool IsEditable
    {
        get => _variable?.IsEditable ?? false;
        set { RequireEditorPresentation(nameof(IsEditable)); _variable!.IsEditable = value; }
    }

    public bool IsExposedOnSpawn
    {
        get => _variable?.IsExposedOnSpawn ?? false;
        set { RequireEditorPresentation(nameof(IsExposedOnSpawn)); _variable!.IsExposedOnSpawn = value; }
    }

    public string? Category
    {
        get => _variable?.Category;
        set { RequireEditorPresentation(nameof(Category)); _variable!.Category = value; }
    }

    private void RequireEditorPresentation(string member)
    {
        if (_variable is null)
            throw new NotSupportedException(
                $"'{member}' is not carried by a {nameof(DeclarationKind.Parameter)} declaration — "
                + $"{nameof(ParameterDecl)} has no such member, so the write would be discarded. "
                + $"Ask {nameof(CarriesEditorPresentation)} first.");
    }

    // ── identity ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>Identity is the BACKING object, not this wrapper.</b> The view builds a fresh facade on
    /// every read, so <c>list.Remove(list[0])</c> would otherwise never match anything — a silent
    /// no-op, which is the exact defect shape the facade exists to avoid.
    /// </summary>
    public override bool Equals(object? obj)
        => obj is BlueprintDeclaration other && other.Kind == Kind && ReferenceEquals(other.Backing, Backing);

    public override int GetHashCode()
        => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Backing);

    public override string ToString() => $"{Kind} {Name} : {Type.TypeId}";
}
