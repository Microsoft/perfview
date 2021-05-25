using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public class Configuration
    {
        private Dictionary<string, AnalyzerConfiguration> _analyzerConfigurations = new Dictionary<string, AnalyzerConfiguration>();

        internal Configuration()
        {
        }

        internal void AddConfigurationFile(string path)
        {
            // Open the configuration file.
            ConfigurationFile file = ConfigurationFile.FromFile(path);
            foreach(KeyValuePair<string, AnalyzerConfiguration> config in file.Analyzers)
            {
                _analyzerConfigurations.Add(config.Key, config.Value);
            }
        }

        public bool TryGetAnalyzerConfiguration(Analyzer analyzer, out AnalyzerConfiguration config)
        {
            return _analyzerConfigurations.TryGetValue(analyzer.GetType().FullName, out config);
        }
    }
}
