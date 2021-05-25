using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class AnalyzerProviderAttribute : Attribute
    {
        public AnalyzerProviderAttribute(Type providerType)
        {
            ProviderType = providerType;
        }

        public Type ProviderType { get; }
    }

    public interface IAnalyzerProvider
    {
        IEnumerable<Analyzer> GetAnalyzers();
    }
}
