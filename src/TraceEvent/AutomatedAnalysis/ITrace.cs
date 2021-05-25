using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public interface ITrace
    {
        IEnumerable<AnalyzerTraceProcess> Processes { get; }

        StackView GetCPUStacks(AnalyzerTraceProcess process);
    }
}
