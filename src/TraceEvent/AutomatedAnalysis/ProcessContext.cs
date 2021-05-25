using System.Collections.Generic;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class ProcessContext
    {
        private StackView _cpuStacks;
        private SymbolReader _symbolReader;
        private AnalyzerExecutionContext _executionContext;

        public ProcessContext(AnalyzerExecutionContext executionContext, AnalyzerTraceProcess process)
        {
            _executionContext = executionContext;
            AnalyzerProcess = process;
            AutomatedAnalysisTraceLog traceLog = executionContext.Trace as AutomatedAnalysisTraceLog;
            if(traceLog != null)
            {
                Process = traceLog.TraceLog.Processes[(ProcessIndex)process.UniqueID];
            }

            Issues = executionContext.Issues[process];
            _symbolReader = executionContext.SymbolReader;
        }

        public AnalyzerTraceProcess AnalyzerProcess { get; }

        public TraceProcess Process { get; }

        public List<AnalyzerIssue> Issues { get; }

        public StackView CPUStacks
        {
            get
            {
                if (_cpuStacks == null)
                {
                    _cpuStacks = _executionContext.Trace.GetCPUStacks(AnalyzerProcess);
                }
                return _cpuStacks;
            }
        }
    }
}
