using System;

namespace Hrot.Map.Definitions.Doctrine
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class DoctrineContractAttribute : Attribute
    {
        public int DoctrineId { get; }
        public string BehaviorId { get; }
        public DoctrineCategory ValidCategories { get; }

        public DoctrineContractAttribute(int doctrineId, string behaviorId, DoctrineCategory categories)
        {
            DoctrineId      = doctrineId;
            BehaviorId      = behaviorId;
            ValidCategories = categories;
        }
    }
}
