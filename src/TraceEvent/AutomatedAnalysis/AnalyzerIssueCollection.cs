using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class AnalyzerIssueCollection : IEnumerable<KeyValuePair<AnalyzerTraceProcess, List<AnalyzerIssue>>>
    {
        private Dictionary<AnalyzerTraceProcess, List<AnalyzerIssue>> _issues = new Dictionary<AnalyzerTraceProcess, List<AnalyzerIssue>>();

        public List<AnalyzerIssue> this[AnalyzerTraceProcess process]
        {
            get
            {
                List<AnalyzerIssue> issues;
                if(!_issues.TryGetValue(process, out issues))
                {
                    issues = new List<AnalyzerIssue>();
                    _issues.Add(process, issues);
                }

                return issues;
            }
        }

        // Keep this for now because GLAD depends on it.
        public List<AnalyzerIssue> this[TraceProcess process]
        {
            get
            {
                AnalyzerTraceProcess traceProcess = new AnalyzerTraceProcess((int)process.ProcessIndex, process.ProcessID, process.CommandLine, process.ManagedProcess());
                List<AnalyzerIssue> issues;
                if (!_issues.TryGetValue(traceProcess, out issues))
                {
                    issues = new List<AnalyzerIssue>();
                    _issues.Add(traceProcess, issues);
                }

                return issues;
            }
        }

        public IEnumerator GetEnumerator()
        {
            return ((IEnumerable)_issues).GetEnumerator();
        }

        IEnumerator<KeyValuePair<AnalyzerTraceProcess, List<AnalyzerIssue>>> IEnumerable<KeyValuePair<AnalyzerTraceProcess, List<AnalyzerIssue>>>.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<AnalyzerTraceProcess, List<AnalyzerIssue>>>)_issues).GetEnumerator();
        }
    }
}
