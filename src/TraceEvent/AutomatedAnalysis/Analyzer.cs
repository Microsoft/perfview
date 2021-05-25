namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public abstract class Analyzer
    {
        protected abstract AnalyzerExecutionResult Execute(AnalyzerExecutionContext executionContext);

        internal virtual AnalyzerExecutionResult RunAnalyzer(AnalyzerExecutionContext executionContext, ProcessContext processContext)
        {
            return Execute(executionContext);
        }
    }
}