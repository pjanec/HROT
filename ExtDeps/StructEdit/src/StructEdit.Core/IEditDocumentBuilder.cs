namespace StructEdit.Core;

public interface IEditDocumentBuilder
{
    EditDocument Build(IEditBuffer buffer, Type componentType, EditScope scope, EditContext? context);
}
