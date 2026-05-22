using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public interface ITypeRegistry
{
    bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType);
    bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression);
}
