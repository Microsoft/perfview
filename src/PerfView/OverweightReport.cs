using Microsoft.Diagnostics.Tracing.Stacks;
using PerfView.GuiUtilities;
using PerfViewExtensibility;
using System;
using System.Collections.Generic;
using System.IO;

namespace PerfView
{
    internal class OverWeigthReport
    {
        private static StackWindow _sourceWindow;
        private static StackWindow _baselineWindow;

        public static void ViewOverweightReport(string htmlReport, string reportName)
        {
            CommandEnvironment.OpenHtmlReport(htmlReport, reportName,
                delegate (string command, TextWriter log, WebBrowserWindow window)
                {
                    // We are not on the GUI thread, so any time we interact with the GUI thread we must dispatch to it.  
                    window.Dispatcher.BeginInvoke((Action)delegate
                    {
                        // If you have any active hyperlinks, they come here.  
                        ProcessHyperlinkCommand(command);



                        // If you want to invoke other GUI Actions do them here.  
                    });
                });
        }

        private static void ProcessHyperlinkCommand(string command)
        {
            GuiApp.MainWindow.StatusBar.LogWriter.WriteLine("Got command " + command);
            string[] args = command.Split(new char[] { ',' });

            StackWindow stackWindow = null;
            switch (args[0])
            {
                case "ShowBaseStacks":
                    stackWindow = _baselineWindow;
                    break;
                case "ShowStacks":
                    stackWindow = _sourceWindow;
                    break;
            }

            try
            {
                stackWindow.SetFocus(args[1]);
                stackWindow.CallersTab.IsSelected = true;
                stackWindow.Focus();
            }
            catch (System.NullReferenceException)
            {
                GuiApp.MainWindow.StatusBar.LogWriter.WriteLine("Failed to find Stack Window. (Has it been closed?)");
            }
        }

        public static void GenerateOverweightReport(string outputHtmlReport, StackWindow sourceWindow, StackWindow baselineWindow)
        {
            _sourceWindow = sourceWindow;
            _baselineWindow = baselineWindow;
            StackSource source = sourceWindow.CallTree.StackSource;
            StackSource baselineSource = baselineWindow.CallTree.StackSource;

            TextWriter log = GuiApp.MainWindow.StatusBar.LogWriter;

            var d1 = new Dictionary<string, double>();
            var d2 = new Dictionary<string, double>();
            var results = new List<Result>();
            double total1 = LoadOneTrace(baselineSource, d1);
            double total2 = LoadOneTrace(source, d2);

            if (total1 != total2)
            {
                ComputeOverweights(d1, d2, results, total1, total2);
            }
            using (var w = File.CreateText(outputHtmlReport))
            {
                w.WriteLine("<h1>Overweight report for symbols common between both files</h1>");
                w.WriteLine("<br>");

                double delta = total2 - total1;
                double growthPercent = delta / total1 * 100;

                w.WriteLine("<table style=\"font-size:10pt;\" border=\"1\">");
                w.WriteLine("<tr><td>Base (old) Time:</td><td>{0:f1}</td></tr>", total1);
                w.WriteLine("<tr><td>Test (new) Time:</td><td>{0:f1}</td></tr>", total2);
                w.WriteLine("<tr><td>Delta:</td><td>{0:f1}</td></tr>", delta);
                w.WriteLine("<tr><td>Delta %:</td><td>{0:f1}</td></tr>", growthPercent);
                w.WriteLine("</table>");

                w.WriteLine("<br>");
                w.WriteLine("In this report, overweight is ratio of actual growth compared to {0:f1}%.<br>", growthPercent);
                w.WriteLine("Interest level attempts to identify smaller methods which changed a lot. These are likely the most interesting frames to sart investigating<br>");
                w.WriteLine("An overweight of greater than 100% indicates the symbol grew in cost more than average.<br>");
                w.WriteLine("High overweights are a likely source of regressions and represent good places to investigate.<br>");
                w.WriteLine("Only symbols that have at least 2% impact are shown.<br>");
                w.WriteLine("<br>");

                if (results.Count == 0)
                {
                    w.WriteLine("There was no growth or no matching symbols, overweight analysis not possible<br>");
                }
                else
                {
                    w.WriteLine("<table style=\"font-size:10pt;\" border=\"1\">");
                    w.WriteLine("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>",
                        "<b>Name</b>",
                        "<b>Base</b>",
                        "<b>Test</b>",
                        "<b>Delta</b>",
                        "<b>Responsibility %</b>",
                        "<b>Overweight %</b>",
                        "<b>Interest Level</b>"
                        );
                    foreach (var r in results)
                    {
                        // filter out the small stuff
                        if (Math.Abs(r.percent) < 2)
                        {
                            continue;
                        }

                        w.WriteLine("<tr><td>{0}</td><td><a href='command:ShowBaseStacks,{0}' title='View Callers of {0} in Base Stacks'>{1:f1}</a></td><td><a href='command:ShowStacks,{0}' title='View Callers of {0} in Test Stacks'>{2:f1}</a></td><td>{3:f1}</td><td>{4:f2}</td><td>{5:f2}</td><td>{6}</td></tr>",
                            r.name,
                            r.before,
                            r.after,
                            r.delta,
                            r.percent,
                            r.overweight,
                            r.interest
                            );
                    }
                    w.WriteLine("</table>");
                }
            }
        }

        #region private 
        private class Result
        {
            public string name;
            public double before;
            public double after;
            public double delta;
            public double overweight;
            public double percent;
            public int interest;
        }

        private static void ComputeOverweights(
            Dictionary<string, double> d1,
            Dictionary<string, double> d2,
            List<Result> results,
            double total1,
            double total2)
        {

            double totalDelta = total2 - total1;
            double growth = total2 / total1;

            foreach (var key in d1.Keys)
            {
                // skip symbols that are not in both traces
                if (!d2.ContainsKey(key))
                {
                    continue;
                }

                var v1 = d1[key];
                var v2 = d2[key];
                var r = new Result();
                r.name = key;
                r.before = v1;
                r.after = v2;
                r.delta = v2 - v1;
                var expectedDelta = v1 * (growth - 1);
                r.overweight = r.delta / expectedDelta * 100;
                r.percent = r.delta / totalDelta * 100;

                // Calculate interest level
                r.interest += Math.Abs(r.overweight) > 110 ? 1 : 0;
                r.interest += Math.Abs(r.percent) > 5 ? 1 : 0;
                r.interest += Math.Abs(r.percent) > 20 ? 1 : 0;
                r.interest += Math.Abs(r.percent) > 100 ? 1 : 0;
                r.interest += r.after / total2 < 0.95 ? 1 : 0;  // Ignore top of the stack frames
                r.interest += r.after / total2 < 0.75 ? 1 : 0;  // Bonus point for being further down the stack.

                results.Add(r);
            }
            results.Sort((Result r1, Result r2) =>
            {
                if (r1.interest < r2.interest)
                {
                    return 1;
                }

                if (r1.interest > r2.interest)
                {
                    return -1;
                }

                if (r1.overweight < r2.overweight)
                {
                    return 1;
                }

                if (r1.overweight > r2.overweight)
                {
                    return -1;
                }

                if (r1.delta < r2.delta)
                {
                    return -1;
                }

                if (r1.delta > r2.delta)
                {
                    return 1;
                }

                return 0;
            });
        }

        private static double LoadOneTrace(StackSource source, Dictionary<string, double> dict)
        {
            var calltree = new CallTree(ScalingPolicyKind.ScaleToData);
            calltree.StackSource = source;

            double total = 0;
            foreach (var node in calltree.ByID)
            {
                if (node.InclusiveMetric == 0)
                {
                    continue;
                }

                double weight = 0;

                string key = node.Name;
                dict.TryGetValue(key, out weight);
                dict[key] = weight + node.InclusiveMetric;

                total += node.ExclusiveMetric;
            }
            return total;
        }

        #endregion
    }
}
