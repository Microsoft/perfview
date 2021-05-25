using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PerfViewTests.Utilities
{
    public static class RangeUtilitiesTests
    {
        [Theory]
        [InlineData(TestCultureInfo.enUSCulture, "", default(double), default(double), false)]
        [InlineData(TestCultureInfo.enUSCulture, "XXXXXXXXXXXXXXX 234,567,890.123", default(double), default(double), false)]
        [InlineData(TestCultureInfo.enUSCulture, "123,456,789.123 XXXXXXXXXXXXXXX", default(double), default(double), false)]
        [InlineData(TestCultureInfo.enUSCulture, "123,456,789.123 234,567,890.123", 123456789.123, 234567890.123, true)]
        [InlineData(TestCultureInfo.ruRUCulture, "", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ruRUCulture, "XXXXXXXXXXXXXXX|234 567\u00A0890,123", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ruRUCulture, "123\u00A0456 789,123|XXXXXXXXXXXXXXX", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ruRUCulture, "123\u00A0456 789,123|234\u00A0567\u00A0890,123", 123456789.123, 234567890.123, true)]
        [InlineData(TestCultureInfo.ptPTCulture, "", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ptPTCulture, "XXXXXXXXXXXXXXX|234 567 890,123", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ptPTCulture, "123 456 789,123|XXXXXXXXXXXXXXX", default(double), default(double), false)]
        [InlineData(TestCultureInfo.ptPTCulture, "123 456 789,123|234 567 890,123", 123456789.123, 234567890.123, true)]
        [InlineData(TestCultureInfo.customCulture1, "", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture1, "XXXXXXXXXXXXXXX|234 567 890,123", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture1, "123 456 789,123|XXXXXXXXXXXXXXX", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture1, "123 456 789,123|234 567 890,123", 123456789.123, 234567890.123, true)]
        [InlineData(TestCultureInfo.customCulture2, "", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture2, "XXXXXXXXXXXXXXX,234 567 890.123", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture2, "123 456 789.123|XXXXXXXXXXXXXXX", default(double), default(double), false)]
        [InlineData(TestCultureInfo.customCulture2, "123 456 789.123|234 567 890.123", 123456789.123, 234567890.123, true)]
        public static void TryParseTests(string culture, string text, double expectedStart, double expectedEnd, bool expectedResult)
        {
            var runner = RangeUtilitiesRunner.Create(culture);

            var actual = runner.TryParse(text, out var actualStart, out var actualEnd);

            Assert.Equal(expectedResult, actual);
            Assert.Equal(expectedStart, actualStart);
            Assert.Equal(expectedEnd, actualEnd);
        }

        [Theory]
        [InlineData(TestCultureInfo.enUSCulture, default(double), default(double), "0.000 0.000")]
        [InlineData(TestCultureInfo.enUSCulture, 123456789.123456, 234567890.123456, "123,456,789.123 234,567,890.123")]
        [InlineData(TestCultureInfo.ruRUCulture, default(double), default(double), "0,000|0,000")]
        [InlineData(TestCultureInfo.ruRUCulture, 123456789.123456, 234567890.123456, "123\u00A0456\u00A0789,123|234\u00A0567\u00A0890,123")]
        [InlineData(TestCultureInfo.ptPTCulture, default(double), default(double), "0,000|0,000")]
        [InlineData(TestCultureInfo.ptPTCulture, 123456789.123456, 234567890.123456, "123\u00A0456\u00A0789,123|234\u00A0567\u00A0890,123")]
        [InlineData(TestCultureInfo.customCulture1, default(double), default(double), "0,000|0,000")]
        [InlineData(TestCultureInfo.customCulture1, 123456789.123456, 234567890.123456, "123 456 789,123|234 567 890,123")]
        [InlineData(TestCultureInfo.customCulture2, default(double), default(double), "0.000|0.000")]
        [InlineData(TestCultureInfo.customCulture2, 123456789.123456, 234567890.123456, "123 456 789.123|234 567 890.123")]
        public static void ToStringTests(string culture, double start, double end, string expected)
        {
            var runner = RangeUtilitiesRunner.Create(culture);

            var actual = runner.ToString(start, end);

            Assert.Equal(expected, actual);
        }
    }

    [Serializable]
    public class RangeUtilitiesRunner : MarshalByRefObject
    {
        private static readonly string assemblyFullName = typeof(RangeUtilitiesRunner).Assembly.FullName;
        private static readonly string typeFullName = typeof(RangeUtilitiesRunner).FullName;
        private static readonly AppDomainSetup appDomainSetup = new AppDomainSetup
        {
            ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
            ApplicationTrust = AppDomain.CurrentDomain.ApplicationTrust,
        };

        internal static RangeUtilitiesRunner Create(string culture)
        {
            var appDomain = AppDomain.CreateDomain($"RangeUtilities_{culture}_{Guid.NewGuid().ToString("N")}", AppDomain.CurrentDomain.Evidence, appDomainSetup);

            return (RangeUtilitiesRunner)appDomain.CreateInstanceAndUnwrap(
                assemblyName: assemblyFullName,
                typeName: typeFullName,
                ignoreCase: false,
                bindingAttr: BindingFlags.CreateInstance,
                binder: null,
                args: new object[] { culture },
                culture: null,
                activationAttributes: null);
        }

        public RangeUtilitiesRunner(string culture)
        {
            CultureInfo.DefaultThreadCurrentCulture = TestCultureInfo.Get(culture);
        }

        public bool TryParse(string text, out double start, out double end)
        {
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            return RangeUtilities.TryParse(text, out start, out end);
        }

        public string ToString(double start, double end)
        {
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            return RangeUtilities.ToString(start, end);
        }
    }

    [Serializable]
    public class TestCultureInfo : CultureInfo
    {
        internal const string enUSCulture = "en-US";
        internal const string ruRUCulture = "ru-RU";
        internal const string ptPTCulture = "pt-PT";
        internal const string customCulture1 = "custom1";
        internal const string customCulture2 = "custom2";
        
        private static TestCultureInfo CreateCultureInfo(string name, string listSeparator, string numberGroupSeparator, string numberDecimalSeparator)
        {
            var cultureInfo = new TestCultureInfo(enUSCulture);
            cultureInfo.TextInfo.ListSeparator = listSeparator;
            cultureInfo.NumberFormat.NumberGroupSeparator = numberGroupSeparator;
            cultureInfo.NumberFormat.NumberDecimalSeparator = numberDecimalSeparator;

            return cultureInfo;
        }

        public static TestCultureInfo Get(string name)
        {
            switch (name)
            {
                case customCulture1: return new TestCultureInfo(customCulture1, ",", " ", ",");
                case customCulture2: return new TestCultureInfo(customCulture2, ",", " ", ".");
                default: return new TestCultureInfo(name);
            }
        }

        public TestCultureInfo(string name)
            : base(name, false)
        {
            this.Name = name;
        }

        public TestCultureInfo(string name, string listSeparator, string numberGroupSeparator, string numberDecimalSeparator)
            : base(enUSCulture, false)
        {
            this.Name = name;
            this.TextInfo.ListSeparator = listSeparator;
            this.NumberFormat.NumberGroupSeparator = numberGroupSeparator;
            this.NumberFormat.NumberDecimalSeparator = numberDecimalSeparator;
        }

        public override string Name { get; }

        public override string ToString() => base.Name;
    }
}
