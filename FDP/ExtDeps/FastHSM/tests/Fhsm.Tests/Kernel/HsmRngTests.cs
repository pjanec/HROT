using System;
using Fhsm.Kernel;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    public unsafe class HsmRngTests
    {
        [Fact]
        public void NextFloat_Produces_Deterministic_Sequence()
        {
            // Setup: Two RNG with same seed
            uint seed1 = 12345;
            uint seed2 = 12345;
            
            var rng1 = new HsmRng(&seed1);
            var rng2 = new HsmRng(&seed2);
            
            // Generate 10 values from each
            for (int i = 0; i < 10; i++)
            {
                float v1 = rng1.NextFloat();
                float v2 = rng2.NextFloat();
                
                Assert.Equal(v1, v2);
            }
        }

        [Fact]
        public void NextFloat_Returns_Values_In_Range()
        {
            uint seed = 54321;
            var rng = new HsmRng(&seed);
            
            for (int i = 0; i < 100; i++)
            {
                float v = rng.NextFloat();
                Assert.True(v >= 0.0f && v < 1.0f);
            }
        }

        [Fact]
        public void NextInt_Returns_Values_In_Range()
        {
            uint seed = 99999;
            var rng = new HsmRng(&seed);
            
            for (int i = 0; i < 100; i++)
            {
                int v = rng.NextInt(10, 20);
                Assert.True(v >= 10 && v < 20);
            }
        }

        [Fact]
        public void NextBool_Returns_Bool()
        {
            uint seed = 11111;
            var rng = new HsmRng(&seed);
            
            int trueCount = 0;
            int falseCount = 0;
            
            for (int i = 0; i < 100; i++)
            {
                if (rng.NextBool()) trueCount++;
                else falseCount++;
            }
            
            // Should be roughly 50/50 (allow 30-70 range for randomness)
            Assert.True(trueCount >= 30 && trueCount <= 70);
        }

        [Fact]
        public void Seed_Advances_On_Each_Call()
        {
            uint seed = 42;
            var rng = new HsmRng(&seed);
            
            uint initial = rng.CurrentSeed;
            rng.NextFloat();
            uint after1 = rng.CurrentSeed;
            rng.NextFloat();
            uint after2 = rng.CurrentSeed;
            
            Assert.NotEqual(initial, after1);
            Assert.NotEqual(after1, after2);
        }

        #if DEBUG
        [Fact]
        public void Debug_Tracking_Increments_On_Each_Call()
        {
            uint seed = 77777;
            int accessCount = 0;
            var rng = new HsmRng(&seed, &accessCount);
            
            Assert.Equal(0, accessCount);
            
            rng.NextFloat();
            Assert.Equal(1, accessCount);
            
            rng.NextInt(0, 10);
            Assert.Equal(2, accessCount); // NextInt calls NextFloat
            
            rng.NextBool();
            Assert.Equal(3, accessCount); // NextBool calls NextFloat
        }
        #endif
    }
}
