namespace Hrot.Editor.AiShared.Emit;

public interface IFluentCSharpEmitter<TAsset>
{
    string Emit(TAsset asset);
}
