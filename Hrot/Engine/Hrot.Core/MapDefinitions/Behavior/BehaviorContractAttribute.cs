using System;

namespace Hrot.Map.Definitions.Behavior
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorContractAttribute : Attribute
    {
        public string BehaviorName { get; }
        public BehaviorCategory ValidCategories { get; }

        public BehaviorContractAttribute(string behaviorName, BehaviorCategory categories)
        {
            BehaviorName    = behaviorName;
            ValidCategories = categories;
        }
    }
}
