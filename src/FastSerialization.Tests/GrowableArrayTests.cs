using System;
using System.Collections.Generic;
using Xunit;

namespace FastSerializationTests
{
    public class GrowableArrayTests
    {
        [Fact]
        public void TestGrowableArray()
        {
            GrowableArray<double> testArray = new GrowableArray<double>();
            for (double i = 1.1; i < 10; i += 2)
            {
                int successes = TestBinarySearch(testArray);
                Assert.Equal(((int)i) / 2, successes);
                testArray.Add(i);
            }

            for (double i = 0.1; i < 11; i += 2)
            {
                int index;
                bool result = testArray.BinarySearch(i, out index, delegate(double key, double elem) { return (int)key - (int)elem; });
                Assert.False(result);
                testArray.Insert(index + 1, i);
            }

            int lastSuccesses = TestBinarySearch(testArray);
            Assert.Equal(11, lastSuccesses);

            for (double i = 0; i < 11; i += 1)
            {
                int index;
                bool result = testArray.BinarySearch(i, out index, delegate(double key, double elem) { return (int)key - (int)elem; });
                Assert.True(result);
                testArray.Insert(index + 1, i);
            }

            lastSuccesses = TestBinarySearch(testArray);
            Assert.Equal(11, lastSuccesses);

            // We always get the last one when the equality comparison allows multiple items to match.  
            for (double i = 0; i < 11; i += 1)
            {
                int index;
                bool result = testArray.BinarySearch(i, out index, delegate(double key, double elem) { return (int)key - (int)elem; });
                Assert.True(result);
                Assert.Equal(i, testArray[index]);
            }
            Console.WriteLine("Done");
        }

        private static int TestBinarySearch(GrowableArray<double> testArray)
        {
            int successes = 0;
            for (int i = 0; i < 30; i++)
            {
                int index;
                if (testArray.BinarySearch(i, out index, delegate(double key, double elem) { return (int)key - (int)elem; }))
                {
                    successes++;
                    Assert.Equal(i, (int)testArray[index]);
                }
                else
                {
                    Assert.True(index + 1 <= testArray.Count);
                }
            }
            return successes;
        }
    }
}
