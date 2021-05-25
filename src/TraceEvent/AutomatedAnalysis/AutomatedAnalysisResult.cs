using System.Collections.Generic;
using System.IO;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class AutomatedAnalysisResult
    {
        private IEnumerable<Analyzer> _executedAnalyzers;
        private AnalyzerExecutionContext _executionContext;

        internal AutomatedAnalysisResult(
            IEnumerable<Analyzer> executedAnalyzers,
            AnalyzerExecutionContext executionContext)
        {
            _executedAnalyzers = executedAnalyzers;
            _executionContext = executionContext;
        }

        public AnalyzerIssueCollection Issues
        {
            get { return _executionContext.Issues; }
        }

        public void GenerateReport(TextWriter writer)
        {
            using (AutomatedAnalysisReportGenerator reportGenerator = new AutomatedAnalysisReportGenerator(writer))
            {
                // Write out issues.
                foreach (KeyValuePair<AnalyzerTraceProcess, List<AnalyzerIssue>> pair in _executionContext.Issues)
                {
                    if (pair.Value.Count > 0)
                    {
                        reportGenerator.WriteIssuesForProcess(pair.Key, pair.Value);
                    }
                }

                // Write the list of executed analyzers.
                reportGenerator.WriteExecutedAnalyzerList(_executedAnalyzers);
            }
        }
    }
}
