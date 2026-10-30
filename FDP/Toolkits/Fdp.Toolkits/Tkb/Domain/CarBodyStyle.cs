namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ CE-4123 (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-3, TC1) — what a wheeled road vehicle looks LIKE: a type names one in
    /// <see cref="VisualDefinitionDto.BodyStyle"/> and the 3-D map draws its own block kit instead of one car kit scaled to every size.
    /// </summary>
    public enum CarBodyStyle { Sedan, Estate, SmallHatch, Suv, Van, Bus, BoxTruck }
}
