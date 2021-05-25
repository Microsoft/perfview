using Microsoft.Diagnostics.Tracing.Etlx;
using System.IO;
using Microsoft.Diagnostics.Symbols;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class AnalyzerExecutionContext
    {
        internal AnalyzerExecutionContext(Configuration configuration, ITrace trace, TextWriter textLog)
        {
            Configuration = configuration;
            Trace = trace;
            TextLog = textLog;

            AutomatedAnalysisTraceLog traceLog = trace as AutomatedAnalysisTraceLog;
            if(traceLog != null)
            {
                TraceLog = traceLog.TraceLog;
                SymbolReader = traceLog.SymbolReader;
            }
        }

        public Configuration Configuration { get; }

        public SymbolReader SymbolReader { get; }

        public TraceLog TraceLog { get; }

        public ITrace Trace { get; }

        public TextWriter TextLog { get; }

        public AnalyzerIssueCollection Issues { get; } = new AnalyzerIssueCollection();
    }
}
