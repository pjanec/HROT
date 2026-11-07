using System;
using System.Collections.Generic;
using StructEdit.Core;
using StructEdit.Core.Plugins;
using StructEdit.Core.Text;
using StructEdit.Reflection;

namespace Fdp.Presentation.Editing.Types;

/// <summary>
/// What a <see cref="ITypeEditor"/> may use while it draws a cell — the ONE way a type's editor reaches its host's context (the session, the pick context, the asset, the registry)
/// instead of taking it in a constructor, so ONE editor instance serves every host (⭐ S3: registered once, never passed per host).
/// </summary>
public readonly struct TypeDrawContext
{
    public TypeDrawContext(EditNode node, IEditSession? session, IMapPickContext? picks, IServiceProvider? services)
    {
        Node = node; Session = session; Picks = picks; Services = services;
    }

    /// <summary>The node being drawn (its metadata, attributes, path).</summary>
    public EditNode Node { get; }
    /// <summary>The session the table draws (a session-bound editor reads its document through it).</summary>
    public IEditSession? Session { get; }
    /// <summary>The host's map pick context, null where there is no map.</summary>
    public IMapPickContext? Picks { get; }
    /// <summary>The host's services — what a domain editor needs (an HSM asset, the behaviour registry …); null ⇒ none.</summary>
    public IServiceProvider? Services { get; }

    /// <summary>The host service of type <typeparamref name="T"/>, or null.</summary>
    public T? Get<T>() where T : class => Services?.GetService(typeof(T)) as T;
}

/// <summary>
/// ⭐⭐ S3 (📄 docs/DESIGN_Struct_Editor.md §3, CE-3378) — EVERYTHING the editor needs to know about one type, in one registration (the user's "new category"):
/// <list type="bullet">
/// <item><b>Shape</b> — <see cref="Shape"/>: a leaf (an <see cref="ICustomFieldEditor"/> every document builder gets) or null (structure / list as reflection sees it);</item>
/// <item><b>Draw</b> — the ImGui cell (<see cref="CanDraw"/> false ⇒ the table's built-in for the kind);</item>
/// <item><b>Format / Parse</b> — value ⇄ text (the cell, a summary, Copy / Paste); a refusal is a message;</item>
/// <item><b>Validate</b> — a problem with the value, per field, live (null = fine);</item>
/// <item><b>Default</b> — a new list item's value, never null where the type allows.</item>
/// </list>
/// Every part is optional — derive from <see cref="TypeEditor{T}"/> and override what the type needs. ⭐ Map (⌖ / ⊕) stays <c>MapPreviews.For(type)</c>, keyed by the same type.
/// </summary>
public interface ITypeEditor
{
    Type TargetType { get; }
    ICustomFieldEditor? Shape { get; }
    bool CanDraw { get; }
    /// <summary>Draws the cell for <paramref name="value"/> (the table read it from the node's binding and writes it back when this returns true). Called only when <see cref="CanDraw"/>.</summary>
    bool Draw(in TypeDrawContext ctx, ref object value);
    string Format(object? value);
    bool TryParse(string text, out object? value, out string? problem);
    string? Validate(object? value, in TypeDrawContext ctx);
    object? NewValue();
}

/// <summary>The base of a type's registration: every part defaults to the built-ins (<see cref="ValueText"/>, <see cref="ContainerOps.DefaultOf"/>), so a type overrides only what differs.</summary>
public abstract class TypeEditor<T> : ITypeEditor
{
    public Type TargetType => typeof(T);
    public virtual ICustomFieldEditor? Shape => null;
    public virtual bool CanDraw => false;
    public virtual bool Draw(in TypeDrawContext ctx, ref object value) => false;
    public virtual string Format(object? value) => ValueText.Format(value);
    public virtual bool TryParse(string text, out object? value, out string? problem)
    {
        if (ValueText.Handles(typeof(T))) return ValueText.TryParse(typeof(T), text, out value, out problem);
        value = null; problem = "no text form for " + typeof(T).Name;
        return false;
    }
    public virtual string? Validate(object? value, in TypeDrawContext ctx) => null;
    public virtual object? NewValue() => ContainerOps.DefaultOf(typeof(T));
}

/// <summary>
/// ⭐ The bridge for an editor that still has the two old halves — a StructEdit <see cref="ICustomFieldEditor"/> (the leaf) and an ImGui <see cref="IImGuiFieldDrawer"/> (the cell) —
/// so they are registered ONCE, together, instead of the leaf in a builder and the cell in a dictionary each host passes.
/// </summary>
public sealed class FieldTypeEditor : ITypeEditor
{
    private readonly IImGuiFieldDrawer? _drawer;

    public FieldTypeEditor(Type type, ICustomFieldEditor? shape, IImGuiFieldDrawer? drawer)
    {
        TargetType = type ?? throw new ArgumentNullException(nameof(type));
        Shape = shape;
        _drawer = drawer;
    }

    public Type TargetType { get; }
    public ICustomFieldEditor? Shape { get; }
    public bool CanDraw => _drawer != null;
    public bool Draw(in TypeDrawContext ctx, ref object value) => _drawer!.DrawInput(ref value, ctx.Node);
    public string Format(object? value) => ValueText.Format(value);
    public bool TryParse(string text, out object? value, out string? problem)
    {
        if (ValueText.Handles(TargetType)) return ValueText.TryParse(TargetType, text, out value, out problem);
        value = null; problem = "no text form for " + TargetType.Name;
        return false;
    }
    public string? Validate(object? value, in TypeDrawContext ctx) => null;
    public object? NewValue() => ContainerOps.DefaultOf(TargetType);
}

/// <summary>
/// ⭐⭐ S3 (📄 docs/DESIGN_Struct_Editor.md §2, §3, CE-3378) — THE catalogue of type editors. <see cref="Standard"/> holds the engine's types, registered ONCE (here and, for domain
/// types, by <see cref="Register"/> at start-up — the <c>ParamTypes.RegisterEnum</c> precedent); every document builder takes its leaves from <see cref="FieldEditors"/> and every table
/// draws its cells from <see cref="TryGet"/> ⇒ a host may ADD types (<see cref="With"/>, an overlay it builds once), never must.
/// <para>⛔ Replaces the two registries: <c>ComponentEditServiceBuilder.RegisterFieldEditor</c> lists and the <c>IImGuiFieldDrawer</c> dictionaries hosts passed (most passed none, so Quaternion,
/// Guid and Type drew as grey <c>ToString()</c>).</para>
/// </summary>
public sealed class TypeCatalogue
{
    private static readonly object Gate = new();
    private static TypeCatalogue? _standard;

    private readonly TypeCatalogue? _parent;
    private readonly Dictionary<Type, ITypeEditor> _own = new();
    private readonly List<(Type Attribute, ITypeEditor Editor)> _rules = new();
    private Dictionary<Type, ICustomFieldEditor>? _fieldEditors;
    private int _version;

    private TypeCatalogue(TypeCatalogue? parent) => _parent = parent;

    /// <summary>The engine's catalogue (built on first use; <see cref="Register"/> adds to it).</summary>
    public static TypeCatalogue Standard
    {
        get
        {
            if (_standard != null) return _standard;
            lock (Gate) return _standard ??= StandardTypes.Build(new TypeCatalogue(null));
        }
    }

    /// <summary>Adds (or replaces) <paramref name="editor"/> in <see cref="Standard"/> — a domain type registered once at start-up. Idempotent by type.</summary>
    public static void Register(ITypeEditor editor) => Standard.Add(editor);

    /// <summary>A catalogue that answers <paramref name="extra"/> first, then this one — a host's own types (build it ONCE, keep it).</summary>
    public TypeCatalogue With(params ITypeEditor[] extra)
    {
        var c = new TypeCatalogue(this);
        foreach (var e in extra) c.Add(e);
        return c;
    }

    internal TypeCatalogue Add(ITypeEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        lock (Gate)
        {
            _own[editor.TargetType] = editor;
            _fieldEditors = null;
            _version++;
        }
        return this;
    }

    /// <summary>
    /// ⭐ A FIELD rule: <paramref name="editor"/> draws a field of <c>editor.TargetType</c> that carries an attribute of <paramref name="attributeType"/> (a picker on a <c>string</c> —
    /// <c>[HsmStateSelector]</c>, <c>[BehaviorHash]</c> — where the attribute, not the type, says what the value means). A rule wins over the type's own editor.
    /// </summary>
    public TypeCatalogue AddRule(Type attributeType, ITypeEditor editor)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        ArgumentNullException.ThrowIfNull(editor);
        lock (Gate) { _rules.Add((attributeType, editor)); _version++; }
        return this;
    }

    /// <summary>Registers a field rule in <see cref="Standard"/> (start-up, once).</summary>
    public static void RegisterRule(Type attributeType, ITypeEditor editor) => Standard.AddRule(attributeType, editor);

    /// <summary>The editor of <paramref name="node"/>: a rule whose attribute the node carries (this catalogue's first, then the parent's), else its type's.</summary>
    public bool TryGetFor(EditNode node, out ITypeEditor editor)
    {
        if (TryRule(node, out editor)) return true;
        return TryGet(node.ClrType, out editor);
    }

    private bool TryRule(EditNode node, out ITypeEditor editor)
    {
        var attrs = node.Metadata.CustomAttributes;
        if (attrs.Count > 0)
            for (int r = 0; r < _rules.Count; r++)
            {
                var (attr, e) = _rules[r];
                if (e.TargetType != node.ClrType) continue;
                for (int a = 0; a < attrs.Count; a++)
                    if (attr.IsInstanceOfType(attrs[a])) { editor = e; return true; }
            }
        if (_parent != null) return _parent.TryRule(node, out editor);
        editor = null!;
        return false;
    }

    /// <summary>Changes whenever a type is added here or below (a cache keyed on it rebuilds).</summary>
    public int Version => _version + (_parent?.Version ?? 0);

    /// <summary>The editor of <paramref name="type"/> — this catalogue's own first, then its parent's.</summary>
    public bool TryGet(Type type, out ITypeEditor editor)
    {
        if (_own.TryGetValue(type, out editor!)) return true;
        if (_parent != null) return _parent.TryGet(type, out editor);
        editor = null!;
        return false;
    }

    /// <summary>The leaves (S3 Shape) every document builder registers — this catalogue and its parents.</summary>
    public IReadOnlyDictionary<Type, ICustomFieldEditor> FieldEditors
    {
        get
        {
            var cached = _fieldEditors;
            if (cached != null && _parent == null) return cached;
            var d = new Dictionary<Type, ICustomFieldEditor>();
            if (_parent != null) foreach (var kv in _parent.FieldEditors) d[kv.Key] = kv.Value;
            lock (Gate) foreach (var kv in _own) if (kv.Value.Shape is { } s) d[kv.Key] = s;
            if (_parent == null) _fieldEditors = d;
            return d;
        }
    }

    /// <summary>A service builder with this catalogue's leaves registered — what <c>ComponentEditServices.CreateBuilder</c> returns.</summary>
    public ComponentEditServiceBuilder CreateBuilder()
    {
        var b = new ComponentEditServiceBuilder();
        foreach (var kv in FieldEditors) b.RegisterFieldEditor(kv.Key, kv.Value);
        return b;
    }
}
