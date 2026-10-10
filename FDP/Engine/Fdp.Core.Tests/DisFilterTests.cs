using Fdp.Core;
using Xunit;
using System;
using System.Collections.Generic;

namespace Fdp.Tests
{
    public class DisFilterTests : IDisposable
    {
        private EntityRepository _repo;

        public DisFilterTests()
        {
            _repo = new EntityRepository();
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ⭐ CE-1017 S0 — the SISO dotted text form (what TkbMasterDto.DisType carries) round-trips.
        [Theory]
        [InlineData("1.1.225.1.1.0.0", 1, 1, 225, 1, 1)]
        [InlineData("3.1", 3, 1, 0, 0, 0)]
        [InlineData("1.1.65535.255", 1, 1, 65535, 255, 0)]
        public void CE1017_DisText_Parses_AndFormatsBack(string text, int kind, int domain, int country, int category, int sub)
        {
            Assert.True(DISEntityType.TryParse(text, out var t));
            Assert.Equal((byte)kind, t.Kind);
            Assert.Equal((byte)domain, t.Domain);
            Assert.Equal((ushort)country, t.Country);
            Assert.Equal((byte)category, t.Category);
            Assert.Equal((byte)sub, t.Subcategory);
            Assert.True(DISEntityType.TryParse(t.ToString(), out var again));
            Assert.Equal(t.Value, again.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1.x")]
        [InlineData("1.256")]
        [InlineData("1.1.65536")]
        [InlineData("1.1.1.1.1.1.1.1")]
        [InlineData("-1.1")]
        public void CE1017_DisText_Rejects_Malformed(string text) => Assert.False(DISEntityType.TryParse(text, out _));

        [Fact]
        public void SetGetDisType_StoredCorrectlyInHeader()
        {
            var entity = _repo.CreateEntity();
            
            var type = new DISEntityType 
            { 
                Kind = 1,       // Platform
                Domain = 2,     // Air
                Country = 225,  // USA
                Category = 50   // Fighter
            };
            
            _repo.SetDisType(entity, type);
            
            ref readonly var metaDis = ref _repo.GetMetadata(entity.Index);
            Assert.Equal(type.Value, metaDis.DisType.Value);
            
            // Verify individual fields via struct mapping
            Assert.Equal(1, metaDis.DisType.Kind);
            Assert.Equal(2, metaDis.DisType.Domain);
            Assert.Equal(225, metaDis.DisType.Country);
        }

        [Fact]
        public void Query_DisType_FilterExactMatch()
        {
            var e1 = _repo.CreateEntity();
            var e2 = _repo.CreateEntity();
            
            // E1 is an M1A1 Tank (Land, Platform, USA)
            var tankType = new DISEntityType { Kind=1, Domain=1, Country=225, Category=1, Subcategory=1 };
            _repo.SetDisType(e1, tankType);
            
            // E2 is an F16 (Air, Platform, USA)
            var jetType = new DISEntityType { Kind=1, Domain=2, Country=225, Category=50 };
            _repo.SetDisType(e2, jetType);
            
            // Query for EXACTLY M1A1
            ulong mask = 0xFF_FF_FF_FF_FF_FF_FF_FF; // Check all bytes
            var query = _repo.Query().WithDisType(tankType, mask).Build();
            
            Assert.Equal(1, query.Count());
            Assert.Equal(e1.Index, query.FirstOrNull().Index); // Must match Tank
        }
        
        [Fact]
        public void Query_DisType_FilterBroadCategory()
        {
            var e1 = _repo.CreateEntity();
            var e2 = _repo.CreateEntity();
            var e3 = _repo.CreateEntity();
            
            // E1: Air Platform
            _repo.SetDisType(e1, new DISEntityType { Kind=1, Domain=2, Category=10 }); 
            // E2: Air Platform (Different Category)
            _repo.SetDisType(e2, new DISEntityType { Kind=1, Domain=2, Category=20 });
            // E3: Land Platform
            _repo.SetDisType(e3, new DISEntityType { Kind=1, Domain=1, Category=10 });
            
            // Query for "All Air Platforms" (Kind=1, Domain=2)
            // Mask: FF (Kind) FF (Domain) 00...
            var filterValue = new DISEntityType { Kind=1, Domain=2 };
            var maskStruct = new DISEntityType { Kind=0xFF, Domain=0xFF }; 
            
            var query = _repo.Query().WithDisType(filterValue, maskStruct.Value).Build();
            
            Assert.Equal(2, query.Count()); // Should match E1 and E2
            
            // Verify
            var results = new HashSet<int>();
            foreach (var e in query)
            {
                results.Add(e.Index);
            }
            
            Assert.Contains(e1.Index, results);
            Assert.Contains(e2.Index, results);
            Assert.DoesNotContain(e3.Index, results);
        }

        [Fact]
        public void Query_DisType_ZeroValues()
        {
            var e1 = _repo.CreateEntity();
            _repo.SetDisType(e1, new DISEntityType { Kind=0, Domain=0 }); // Empty/Other
            
            var e2 = _repo.CreateEntity();
            _repo.SetDisType(e2, new DISEntityType { Kind=1 }); // Platform
            
            // Query for Kind=0
            var maskStruct = new DISEntityType { Kind=0xFF };
            var query = _repo.Query().WithDisType(new DISEntityType { Kind=0 }, maskStruct.Value).Build();
            
            Assert.Equal(1, query.Count());
            Assert.Equal(e1.Index, query.FirstOrNull().Index);
        }
        
        [Fact]
        public void Query_DisType_CombinedWithComponents()
        {
             // Verify that DIS filter works alongside component masks
             _repo.RegisterComponent<Position>();
             var e1 = _repo.CreateEntity();
             _repo.SetDisType(e1, new DISEntityType { Kind=1 });
             _repo.AddComponent(e1, new Position());
             
             var e2 = _repo.CreateEntity();
             _repo.SetDisType(e2, new DISEntityType { Kind=1 });
             // e2 has no Position
             
             var query = _repo.Query()
                 .With<Position>()
                 .WithDisType(new DISEntityType { Kind=1 }, new DISEntityType { Kind=0xFF }.Value)
                 .Build();
                 
             Assert.Equal(1, query.Count());
             Assert.Equal(e1.Index, query.FirstOrNull().Index);
        }
    }
}
