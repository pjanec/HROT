namespace StructEdit.Core.Memory;

/// <summary>
/// Classifies a CLR type into a <see cref="ComponentMemoryKind"/>.
/// </summary>
public interface IComponentMemoryClassifier
{
    ComponentMemoryKind Classify(Type type);
}
