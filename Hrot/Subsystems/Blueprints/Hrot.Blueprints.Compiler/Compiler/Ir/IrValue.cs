namespace Hrot.Blueprints.Core.Compiler.Ir;

public readonly record struct IrValue(int Index, IrTypeRef Type);

public readonly record struct IrBlockId(int Value);
