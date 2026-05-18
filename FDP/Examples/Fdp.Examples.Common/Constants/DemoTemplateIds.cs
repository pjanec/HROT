namespace Fdp.Examples.Common.Constants
{
    /// <summary>
    /// Transient Knowledge Base (TKB) integer entity type IDs used by demo scenarios.
    /// </summary>
    public static class DemoTemplateIds
    {
        public const int CivilianPedestrian = 1001;
        public const int CivilianCar        = 1002;
        public const int MilitaryApc        = 2001;
        public const int InfantrySoldier    = 2002;
        public const int Insurgent          = 2003;
        /// <summary>Distributed tank demo — hull node.</summary>
        public const int CommandTank        = 100;
        /// <summary>Distributed tank demo — turret child node.</summary>
        public const int TankTurret         = 101;
    }
}
