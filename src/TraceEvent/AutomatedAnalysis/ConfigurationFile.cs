using System.Collections.Generic;
using System.Xml;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    internal sealed class ConfigurationFile
    {
        private const string AnalyzerSectionName = "Analyzer";
        private const string AnalyzerName = "Name";
        private const string PropertyTagName = "Property";
        private const string PropertyNameTagName = "Name";
        private const string PropertyValueTagName = "Value";

        internal Dictionary<string, AnalyzerConfiguration> Analyzers { get; } = new Dictionary<string, AnalyzerConfiguration>();

        internal static ConfigurationFile FromFile(string path)
        {
            ConfigurationFile file = new ConfigurationFile();
            using (XmlReader reader = XmlReader.Create(path))
            {
                while(!reader.EOF)
                {
                    if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, AnalyzerSectionName))
                    {
                        // Get the analyzer name.
                        string analyzerName = reader.GetAttribute(AnalyzerName);
                        AnalyzerConfiguration configuration = ReadAnalyzerSection(reader);
                        file.Analyzers.Add(analyzerName, configuration);
                    }
                    else
                    {
                        // Read the next node.
                        reader.Read();
                    }
                }
            }

            return file;
        }

        private static AnalyzerConfiguration ReadAnalyzerSection(XmlReader reader)
        {
            AnalyzerConfiguration configuration = new AnalyzerConfiguration();

            while (true)
            {
                SkipToNextElement(reader);
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.Name, PropertyTagName))
                    {
                        string name = reader.GetAttribute(PropertyNameTagName);
                        string value = reader.GetAttribute(PropertyValueTagName);
                        configuration.Add(name, value);
                    }
                    else
                    {
                        // We've hit the end of the analyzer tag.
                        break;
                    }
                }
                else
                {
                    // We've hit the end of the analyzer tag.
                    break;
                }
            }

            return configuration;
        }

        private static void SkipToNextElement(XmlReader reader)
        {
            while (reader.Read() && reader.NodeType != XmlNodeType.Element) ;
        }
    }
}
