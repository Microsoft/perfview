using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    /// <summary>
    /// Per-analyzer configuration.
    /// </summary>
    public sealed class AnalyzerConfiguration
    {
        private Dictionary<string, string> _properties = new Dictionary<string, string>();

        internal AnalyzerConfiguration()
        {
        }

        public void Add(string key, string value)
        {
            _properties.Add(key, value);
        }

        public bool TryGetValue(string key, out string value)
        {
            return _properties.TryGetValue(key, out value);
        }

        public bool TryGetValueAsDouble(string key, out double value)
        {
            string strValue;
            value = 0;
            if(TryGetValue(key, out strValue))
            {
                return double.TryParse(strValue, out value);
            }

            return false;
        }
    }
}
