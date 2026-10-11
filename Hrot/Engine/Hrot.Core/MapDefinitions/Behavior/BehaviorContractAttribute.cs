using System;

namespace Hrot.Map.Definitions.Behavior
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorContractAttribute : Attribute
    {
        public string BehaviorName { get; }
        public BehaviorCategory ValidCategories { get; }

        /// <summary>⭐ <c>CE-3160</c> (L4, R-266) — a demo or example behaviour: it still runs when a scenario names it, but the
        /// behaviour pickers do not offer it (<c>BehaviorCatalog.IsDemo</c>). Library behaviours leave this false.</summary>
        public bool Demo { get; set; }

        public BehaviorContractAttribute(string behaviorName, BehaviorCategory categories)
        {
            BehaviorName    = behaviorName;
            ValidCategories = categories;
        }
    }
}
