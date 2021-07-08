using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.Diagnostics.Tracing.Utilities;

namespace TraceEventTests
{
    public class IndexSetTest
    {
        public const int Limit = 1000 * 1000;

        [Fact]
        public void BasicTest()
        {
            Test(0);
            Test(1);
            Test(1000);
            Test(1000 * 1000);
        }

        private void Test(int count)
        {
            HashSet<uint> intSet = new HashSet<uint>();

            IndexSet oddSet = new IndexSet();

            Random random = new Random();

            for (int i = 0; i < count; i++)
            {
                int val = random.Next(Limit);

                if ((val % 2) == 0)
                {
                    val++;
                }

                intSet.Add((uint)val);
                oddSet.Add((uint)val);
            }

            int error = 0;

            for (int i = 0; i < Limit; i += 2)
            {
                if (oddSet.Contains((uint)i))
                {
                    error++;
                }
            }

            Assert.True(error == 0);

            foreach (uint v in intSet)
            {
                if (!oddSet.Contains(v))
                {
                    error++;
                }
            }

            Assert.True(error == 0);
        }
    }
}
