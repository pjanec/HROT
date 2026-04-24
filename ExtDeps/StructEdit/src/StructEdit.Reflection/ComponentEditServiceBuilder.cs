using StructEdit.Core;
using StructEdit.Core.Plugins;
using StructEdit.Core.UnionSupport;

namespace StructEdit.Reflection;

/// <summary>
/// Fluent builder for <see cref="IComponentEditService"/>.
/// </summary>
/// <example>
/// <code>
/// var service = new ComponentEditServiceBuilder()
///     .RegisterBufferViewProvider(new MyPayloadProvider())
///     .RegisterValidator&lt;WeaponComponent&gt;(new WeaponValidator())
///     .Build();
/// </code>
/// </example>
public sealed class ComponentEditServiceBuilder
{
    private readonly List<IBufferViewProvider> _viewProviders = new();
    private readonly Dictionary<Type, ICustomFieldEditor> _fieldEditors = new();
    private readonly Dictionary<Type, ICustomComponentEditor> _componentEditors = new();
    private readonly Dictionary<Type, IComponentValidator> _validators = new();

    /// <summary>Registers a <see cref="IBufferViewProvider"/> for union/chameleon buffer projections.</summary>
    public ComponentEditServiceBuilder RegisterBufferViewProvider(IBufferViewProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        _viewProviders.Add(provider);
        return this;
    }

    /// <summary>Registers a custom field editor for the given CLR type.</summary>
    public ComponentEditServiceBuilder RegisterFieldEditor<T>(ICustomFieldEditor editor)
        => RegisterFieldEditor(typeof(T), editor);

    /// <summary>Registers a custom field editor for the given CLR type.</summary>
    public ComponentEditServiceBuilder RegisterFieldEditor(Type type, ICustomFieldEditor editor)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (editor == null) throw new ArgumentNullException(nameof(editor));
        _fieldEditors[type] = editor;
        return this;
    }

    /// <summary>Registers a custom whole-component editor. Overrides reflection-based building.</summary>
    public ComponentEditServiceBuilder RegisterComponentEditor(ICustomComponentEditor editor)
    {
        if (editor == null) throw new ArgumentNullException(nameof(editor));
        _componentEditors[editor.ComponentType] = editor;
        return this;
    }

    /// <summary>Registers a validator for the given component type.</summary>
    public ComponentEditServiceBuilder RegisterValidator<T>(IComponentValidator validator)
        => RegisterValidator(typeof(T), validator);

    /// <summary>Registers a validator for the given component type.</summary>
    public ComponentEditServiceBuilder RegisterValidator(Type type, IComponentValidator validator)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (validator == null) throw new ArgumentNullException(nameof(validator));
        _validators[type] = validator;
        return this;
    }

    /// <summary>Builds and returns the <see cref="IComponentEditService"/>.</summary>
    public IComponentEditService Build()
    {
        return new ComponentEditService(
            _viewProviders.ToArray(),
            new Dictionary<Type, ICustomFieldEditor>(_fieldEditors),
            new Dictionary<Type, ICustomComponentEditor>(_componentEditors),
            new Dictionary<Type, IComponentValidator>(_validators));
    }
}
