using FastSerialization;
using Graphs;
using Xunit;

namespace PerfViewTests
{
    public class MemoryGraphTests
    {
        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void TestCompressedIntegerSerialization(int value)
        {
            using (var streamWriter = new MemoryStreamWriter())
            {
                Node.WriteCompressedInt(streamWriter, value);
                var streamReader = streamWriter.GetReader();
                Assert.Equal(value, Node.ReadCompressedInt(streamReader));
            }
        }
    }
}
