using System;

namespace Hrot.Map.Definitions.Behavior
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorContractAttribute : Attribute
    {
        public int BehaviorId { get; }
        public string BehaviorName { get; }
        public BehaviorCategory ValidCategories { get; }

        public BehaviorContractAttribute(int behaviorId, string behaviorName, BehaviorCategory categories)
        {
            BehaviorId      = behaviorId;
            BehaviorName    = behaviorName;
            ValidCategories = categories;
        }
    }
}
