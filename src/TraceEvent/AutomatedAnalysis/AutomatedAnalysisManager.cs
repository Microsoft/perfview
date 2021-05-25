using System;
using System.IO;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class AutomatedAnalysisManager
    {
        private IEnumerable<Analyzer> _analyzers;
        private Configuration _configuration;

        public AutomatedAnalysisManager()
        {
            _analyzers = AnalyzerResolver.GetAnalyzers();
            _configuration = AnalyzerResolver.GetConfiguration();

        }

        public AutomatedAnalysisResult ProcessTrace(ITrace trace, TextWriter textLog)
        {
            List<PerProcessAnalyzer> perProcessAnalyzers = new List<PerProcessAnalyzer>();

            // Run global analyzers, deferring per-process analyzers.
            AnalyzerExecutionContext executionContext = new AnalyzerExecutionContext(_configuration, trace, textLog);
            foreach (Analyzer analyzer in _analyzers)
            {
                if (analyzer is PerProcessAnalyzer)
                {
                    // Defer per-process analyzers.
                    perProcessAnalyzers.Add((PerProcessAnalyzer)analyzer);
                }
                else
                {
                    // Execute the analyzer.
                    try
                    {
                        analyzer.RunAnalyzer(executionContext, null);
                    }
                    catch (Exception ex)
                    {
                        textLog.WriteLine($"Error while executing analyzer '{analyzer.GetType().FullName}': {ex}");
                    }
                }
            }

            // Run per-process analyzers.
            foreach (AnalyzerTraceProcess process in executionContext.Trace.Processes)
            {
                if (process.ContainsManagedCode)
                {
                    // Create the process context.
                    ProcessContext processContext = new ProcessContext(executionContext, process);

                    foreach (PerProcessAnalyzer analyzer in perProcessAnalyzers)
                    {
                        try
                        {
                            analyzer.RunAnalyzer(executionContext, processContext);
                        }
                        catch (Exception ex)
                        {
                            textLog.WriteLine($"Error while executing analyzer '{analyzer.GetType().FullName}': {ex}");
                        }
                    }
                }
            }

            return new AutomatedAnalysisResult(_analyzers, executionContext);
        }
    }
}