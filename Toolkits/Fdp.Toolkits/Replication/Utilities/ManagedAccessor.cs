using System;
using System.Reflection;
using System.Linq.Expressions;

namespace FDP.Toolkit.Replication.Utilities
{
    /// <summary>
    /// High-performance field access for Managed Types using Compiled Expression Trees.
    /// </summary>
    public static class ManagedAccessor<T>
    {
        // Delegates for fast access
        public delegate long GetIdDelegate(T instance);
        public delegate void SetIdDelegate(T instance, long id);

        public static readonly GetIdDelegate GetId = default!;
        public static readonly SetIdDelegate SetId = default!;
        public static readonly bool IsValid;

        static ManagedAccessor()
        {
            var type = typeof(T);
            // Look for EntityId field or property
            MemberInfo? member = type.GetField("EntityId") ?? (MemberInfo?)type.GetProperty("EntityId");

            if (member != null)
            {
                // 1. Compile Getter: (obj) => obj.EntityId
                var targetParam = Expression.Parameter(type, "target");
                var access = Expression.MakeMemberAccess(targetParam, member);
                var toLong = Expression.Convert(access, typeof(long));
                
                GetId = Expression.Lambda<GetIdDelegate>(toLong, targetParam).Compile();

                // 2. Compile Setter: (obj, val) => obj.EntityId = val
                var valParam = Expression.Parameter(typeof(long), "val");
                var convertedVal = Expression.Convert(valParam, GetMemberType(member));
                var assign = Expression.Assign(access, convertedVal);
                
                SetId = Expression.Lambda<SetIdDelegate>(assign, targetParam, valParam).Compile();

                IsValid = true;
            }
            else
            {
                IsValid = false;
            }
        }

        private static Type GetMemberType(MemberInfo m) => 
            m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
    }
}
