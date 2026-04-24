using StructEdit.Core;
using StructEdit.Core.Memory;
using StructEdit.Core.Plugins;
using StructEdit.Core.UnionSupport;

namespace StructEdit.Reflection;

/// <summary>
/// Default implementation of <see cref="IComponentEditService"/>.
/// Constructed by <see cref="ComponentEditServiceBuilder.Build"/>.
/// </summary>
internal sealed class ComponentEditService : IComponentEditService
{
    private readonly IReadOnlyList<IBufferViewProvider> _viewProviders;
    private readonly IReadOnlyDictionary<Type, ICustomFieldEditor> _fieldEditors;
    private readonly IReadOnlyDictionary<Type, ICustomComponentEditor> _componentEditors;
    private readonly IReadOnlyDictionary<Type, IComponentValidator> _validators;
    private readonly DefaultComponentMemoryClassifier _classifier = new();

    internal ComponentEditService(
        IReadOnlyList<IBufferViewProvider> viewProviders,
        IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
        IReadOnlyDictionary<Type, ICustomComponentEditor> componentEditors,
        IReadOnlyDictionary<Type, IComponentValidator> validators)
    {
        _viewProviders = viewProviders;
        _fieldEditors = fieldEditors;
        _componentEditors = componentEditors;
        _validators = validators;
    }

    public IEditSession Open(
        object component,
        Type componentType,
        EditScope? scope = null,
        EditContext? context = null)
    {
        if (component == null) throw new ArgumentNullException(nameof(component));
        if (componentType == null) throw new ArgumentNullException(nameof(componentType));
        scope ??= EditScope.WholeComponent;

        var buffer = CreateBuffer(componentType, component);

        var builder = new ReflectionEditDocumentBuilder(_viewProviders, _fieldEditors);
        EditDocument document;
        if (_componentEditors.TryGetValue(componentType, out var customEditor))
            document = customEditor.BuildDocument(buffer, scope, context);
        else
            document = builder.Build(buffer, componentType, scope, context);

        _validators.TryGetValue(componentType, out var validator);

        return new EditSession(buffer, builder, scope, context, validator, document);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private IEditBuffer CreateBuffer(Type componentType, object component)
    {
        var kind = _classifier.Classify(componentType);
        return kind switch
        {
            ComponentMemoryKind.UnmanagedBlittableStruct =>
                new NativeStructEditBuffer(componentType, component, RuntimeTypeOpsFactory.Get(componentType)),
            ComponentMemoryKind.ManagedReference =>
                new ManagedObjectEditBuffer(componentType, component),
            ComponentMemoryKind.NonBlittableStruct =>
                new BoxedStructEditBuffer(componentType, component),
            _ => throw new NotSupportedException(
                $"Cannot open edit session for component type '{componentType.FullName}': " +
                $"unsupported memory kind '{kind}'."),
        };
    }
}
