using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Microsoft.Diagnostics.Tracing.AutomatedAnalysis
{
    public sealed class AutomatedAnalysisReportGenerator : IDisposable
    {
        private TextWriter _writer;

        public AutomatedAnalysisReportGenerator(TextWriter writer)
        {
            _writer = writer;
            StartReport();
        }

        void IDisposable.Dispose()
        {
            EndReport();
        }

        private void StartReport()
        {
            _writer.WriteLine("<html>");
            _writer.WriteLine("<head>");
            _writer.WriteLine("<title>Automated CPU Analysis</title>");
            _writer.WriteLine("<meta charset=\"UTF-8\"/>");
            _writer.WriteLine("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/>");
            _writer.WriteLine("</head>");
            _writer.WriteLine("<body>");
            _writer.WriteLine("<H2>Automated CPU Analysis</H2>");
        }

        private void EndReport()
        {
            _writer.WriteLine("</body>");
            _writer.WriteLine("</html>");
        }

        public void WriteIssuesForProcess(AnalyzerTraceProcess process, List<AnalyzerIssue> issues)
        {
            _writer.WriteLine($"<H3>Process {process.DisplayID}: {process.Description}</H3>");
            _writer.WriteLine("<Table Border=\"1\">");
            _writer.WriteLine("<TR><TH>Issue Title</TH><TH>Notes</TH></TR>");
            foreach(AnalyzerIssue issue in issues)
            {
                _writer.WriteLine($"<TR><TD>{issue.Title}</TD><TD>{issue.Description}<BR/><BR/>More details: <A HREF=\"{issue.URL}\">{issue.URL}</A></TD></TR>");
            }
            _writer.WriteLine("</Table>");
        }

        public void WriteExecutedAnalyzerList(IEnumerable<Analyzer> analyzers)
        {
            if(analyzers.Count() > 0)
            {
                _writer.WriteLine("<H3>Analyzers Executed:</H3>");
                _writer.WriteLine("<ul style=\"list-style-type:circle\">");
                foreach (Analyzer analyzer in analyzers)
                {
                    _writer.WriteLine($"<li>{analyzer.GetType().AssemblyQualifiedName}</li>");
                }
                _writer.WriteLine("</ul>");
            }
            else
            {
                _writer.WriteLine($"<H3>No analyzers were executed.  Check '{AnalyzerResolver.AnalyzersDirectory}' for DLLs containing analyzers.</H3>");
            }
        }
    }
}
