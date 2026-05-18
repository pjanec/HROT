namespace StructEdit.Core;

public interface IContainerBinding : IValueBinding
{
    int Count { get; }
    bool CanResize { get; }
    IValueBinding GetElementBinding(int index);
    void Resize(int newCount);
}
