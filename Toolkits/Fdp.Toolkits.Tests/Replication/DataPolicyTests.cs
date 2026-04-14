using Xunit;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using System.Reflection;

namespace Fdp.Toolkit.Replication.Tests
{
    public class DataPolicyTests
    {
        [Fact]
        public void NetworkComponents_Attributes()
        {
             CheckNoRecordAttribute<NetworkTransform>(true);
             CheckNoRecordAttribute<NetworkVelocity>(true);
             
             CheckNoRecordAttribute<NetworkIdentity>(false);
             CheckNoRecordAttribute<NetworkAuthority>(false);
             CheckNoRecordAttribute<DescriptorOwnership>(false);
        }

        private void CheckNoRecordAttribute<T>(bool shouldHave)
        {
            var type = typeof(T);
            var attr = type.GetCustomAttribute<DataPolicyAttribute>();
            
            if (shouldHave)
            {
                Assert.NotNull(attr);
                Assert.Equal(DataPolicy.NoRecord, attr.Policy);
            }
            else
            {
                // Either null or explicitly Record
                if (attr != null)
                {
                    Assert.NotEqual(DataPolicy.NoRecord, attr.Policy);
                }
            }
        }
    }
}
