namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Mouse button identifiers for <see cref="IMapLayer"/>
/// input handling. Values match the Raylib-cs MouseButton enum so that a direct
/// cast is safe (no conversion table needed in RaylibInputProvider).
/// </summary>
public enum MapMouseButton
{
    Left   = 0,
    Right  = 1,
    Middle = 2,
}
