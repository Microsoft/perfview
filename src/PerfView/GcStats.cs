// Copyright (c) Microsoft Corporation.  All rights reserved
using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using Utilities;

namespace Stats
{
    internal static class GcStats
    {
        public static void ToHtml(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, string fileName, bool doServerGCReport = false)
        {
            writer.WriteLine("<H3><A Name=\"Stats_{0}\"><font color=\"blue\">GC Stats for Process {1,5}: {2}</font><A></H3>", stats.ProcessID, stats.ProcessID, stats.Name);
            writer.WriteLine("<UL>");
            if (runtime.GC.Stats().GCVersionInfoMismatch)
            {
                writer.WriteLine("<LI><Font size=3 color=\"red\">Warning: Did not recognize the V4.0 GC Information events.  Falling back to V2.0 behavior.</font></LI>");
            }

            if (!string.IsNullOrEmpty(stats.CommandLine))
            {
                writer.WriteLine("<LI>CommandLine: {0}</LI>", stats.CommandLine);
            }

            var runtimeBuiltTime = "";
            if (runtime.RuntimeBuiltTime != default(DateTime))
            {
                runtimeBuiltTime = string.Format(" (built on {0})", runtime.RuntimeBuiltTime);
            }

            writer.WriteLine("<LI>Runtime Version: {0}{1}</LI>", runtime.RuntimeVersion ?? "&lt;Unknown Runtime Version&gt;", runtimeBuiltTime);
            writer.WriteLine("<LI>CLR Startup Flags: {0}</LI>", runtime.StartupFlags.ToString());
            writer.WriteLine("<LI>Total CPU Time: {0:n0} msec</LI>", stats.CPUMSec);
            writer.WriteLine("<LI>Total GC CPU Time: {0:n0} msec</LI>", runtime.GC.Stats().TotalCpuMSec);
            writer.WriteLine("<LI>Total Allocs  : {0:n3} MB</LI>", runtime.GC.Stats().TotalAllocatedMB);
            writer.WriteLine("<LI>GC CPU MSec/MB Alloc : {0:n3} MSec/MB</LI>", runtime.GC.Stats().TotalCpuMSec / runtime.GC.Stats().TotalAllocatedMB);
            writer.WriteLine("<LI>Total GC Pause: {0:n1} msec</LI>", runtime.GC.Stats().TotalPauseTimeMSec);
            writer.WriteLine("<LI>% Time paused for Garbage Collection: {0:f1}%</LI>", runtime.GC.Stats().GetGCPauseTimePercentage());

            writer.WriteLine("<LI>% CPU Time spent Garbage Collecting: {0:f1}%</LI>", runtime.GC.Stats().TotalCpuMSec * 100.0 / stats.CPUMSec);

            writer.WriteLine("<LI>Max GC Heap Size: {0:n3} MB</LI>", runtime.GC.Stats().MaxSizePeakMB);
            if (stats.PeakWorkingSet != 0)
            {
                writer.WriteLine("<LI>Peak Process Working Set: {0:n3} MB</LI>", stats.PeakWorkingSet / 1000000.0);
            }

            if (stats.PeakWorkingSet != 0)
            {
                writer.WriteLine("<LI>Peak Virtual Memory Usage: {0:n3} MB</LI>", stats.PeakVirtual / 1000000.0);
            }

            var usersGuideFile = ClrStatsUsersGuide.WriteUsersGuide(fileName);
            writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingGCPerf\">GC Perf Users Guide</A></LI>", usersGuideFile);

            writer.WriteLine("<LI><A HREF=\"#Events_Pause_{0}\">GCs that &gt; 200 msec Events</A></LI>", stats.ProcessID);
            writer.WriteLine("<LI><A HREF=\"#LOH_allocation_Pause_{0}\">LOH allocation pause (due to background GC) &gt; 200 msec Events</A></LI>", stats.ProcessID);
            writer.WriteLine("<LI><A HREF=\"#Events_Gen2_{0}\">GCs that were Gen2</A></LI>", stats.ProcessID);

            writer.WriteLine("<LI><A HREF=\"#Events_{0}\">Individual GC Events</A> </LI>", stats.ProcessID);
            writer.WriteLine("<UL><LI> <A HREF=\"command:excel/{0}\">View in Excel</A></LI></UL>", stats.ProcessID);
            writer.WriteLine("<LI> <A HREF=\"command:excel/perGeneration/{0}\">Per Generation GC Events in Excel</A></LI>", stats.ProcessID);
            if (runtime.GC.Stats().HasDetailedGCInfo)
            {
                writer.WriteLine("<LI> <A HREF=\"command:xml/{0}\">Raw Data XML file (for debugging)</A></LI>", stats.ProcessID);
            }

            if (runtime.GC.Stats().FinalizedObjects.Count > 0)
            {
                writer.WriteLine("<LI><A HREF=\"#Finalization_{0}\">Finalized Objects</A> </LI>", stats.ProcessID);
                writer.WriteLine("<UL><LI> <A HREF=\"command:excelFinalization/{0}\">View in Excel</A></LI></UL>", stats.ProcessID);
                writer.WriteLine("<UL><LI> <A HREF=\"{0}#UnderstandingFinalization\">Finalization Perf Users Guide</A></LI></UL>", usersGuideFile);
            }
            else
            {
                writer.WriteLine("<LI><I>No finalized object counts available. No objects were finalized and/or the trace did not include the necessary information.</I></LI>");
            }
            writer.WriteLine("</UL>");
            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH colspan=\"12\" Align=\"Center\">GC Rollup By Generation</TH></TR>");
            writer.WriteLine("<TR><TH colspan=\"12\" Align=\"Center\">All times are in msec.</TH></TR>");
            writer.WriteLine("<TR>" +
                             "<TH>Gen</TH>" +
                             "<TH>Count</TH>" +
                             "<TH>Max<BR/>Pause</TH>" +
                             "<TH>Max<BR/>Peak MB</TH>" +
                             "<TH>Max Alloc<BR/>MB/sec</TH>" +
                             "<TH>Total<BR/>Pause</TH>" +
                             "<TH>Total<BR/>Alloc MB</TH>" +
                             "<TH>Alloc MB/<BR/>MSec GC</TH>" +
                             "<TH>Survived MB/<BR/>MSec GC</TH>" +
                             "<TH>Mean<BR/>Pause</TH>" +
                             "<TH>Induced</TH>" +

                             (ShowPinnedInformation(runtime.GC.Stats()) ?
                             "<TH>Avg Pinned Obj %</TH>"
                             : string.Empty) +

                             "</TR>");
            writer.WriteLine("<TR>" +
                             "<TD Align=\"Center\">{0}</TD>" +
                             "<TD Align=\"Center\">{1}</TD>" +
                             "<TD Align=\"Center\">{2:n1}</TD>" +
                             "<TD Align=\"Center\">{3:n1}</TD>" +
                             "<TD Align=\"Center\">{4:n3}</TD>" +
                             "<TD Align=\"Center\">{5:n1}</TD>" +
                             "<TD Align=\"Center\">{6:n1}</TD>" +
                             "<TD Align=\"Center\">{7:n1}</TD>" +
                             "<TD Align=\"Center\">{8:n3}</TD>" +
                             "<TD Align=\"Center\">{9:n1}</TD>" +
                             "<TD Align=\"Center\">{10}</TD>" +

                             (ShowPinnedInformation(runtime.GC.Stats()) ?
                             "<TD Align=\"Center\">{11}</TD>"
                             : string.Empty) +

                             "</TR>",
                            "ALL",
                            runtime.GC.Stats().Count,
                            runtime.GC.Stats().MaxPauseDurationMSec,
                            runtime.GC.Stats().MaxSizePeakMB,
                            runtime.GC.Stats().MaxAllocRateMBSec,
                            runtime.GC.Stats().TotalPauseTimeMSec,
                            runtime.GC.Stats().TotalAllocatedMB,
                            runtime.GC.Stats().TotalAllocatedMB / runtime.GC.Stats().TotalPauseTimeMSec,
                            runtime.GC.Stats().TotalPromotedMB / runtime.GC.Stats().TotalCpuMSec,
                            runtime.GC.Stats().MeanPauseDurationMSec,
                            runtime.GC.Stats().NumInduced,
                            (((runtime.GC.Stats().NumWithPinEvents != 0) && (runtime.GC.Stats().NumWithPinEvents == runtime.GC.Stats().NumWithPinPlugEvents)) ? (runtime.GC.Stats().PinnedObjectPercentage / runtime.GC.Stats().NumWithPinEvents) : double.NaN));

            for (int genNum = 0; genNum < runtime.GC.Generations().Length; genNum++)
            {
                GCStats gen = (runtime.GC.Generations()[genNum] != null) ? runtime.GC.Generations()[genNum] : new GCStats();
                writer.WriteLine("<TR " + GetGenerationBackgroundColorAttribute(genNum) + ">" +
                                 "<TD Align=\"Center\">{0}</TD>" +
                                 "<TD Align=\"Center\">{1}</TD>" +
                                 "<TD Align=\"Center\">{2:n1}</TD>" +
                                 "<TD Align=\"Center\">{3:n1}</TD>" +
                                 "<TD Align=\"Center\">{4:n3}</TD>" +
                                 "<TD Align=\"Center\">{5:n1}</TD>" +
                                 "<TD Align=\"Center\">{6:n1}</TD>" +
                                 "<TD Align=\"Center\">{7:n1}</TD>" +
                                 "<TD Align=\"Center\">{8:n3}</TD>" +
                                 "<TD Align=\"Center\">{9:n1}</TD>" +
                                 "<TD Align=\"Center\">{10}</TD>" +

                                 (ShowPinnedInformation(runtime.GC.Stats()) ?
                                 "<TD Align=\"Center\">{11}</TD>"
                                 : string.Empty) +

                                 "</TR>",
                                genNum,
                                gen.Count,
                                gen.MaxPauseDurationMSec,
                                gen.MaxSizePeakMB,
                                gen.MaxAllocRateMBSec,
                                gen.TotalPauseTimeMSec,
                                gen.TotalAllocatedMB,
                                gen.TotalPauseTimeMSec / runtime.GC.Stats().TotalAllocatedMB,
                                gen.TotalPromotedMB / gen.TotalCpuMSec,
                                gen.MeanPauseDurationMSec,
                                gen.NumInduced,
                                (((gen.NumWithPinEvents != 0) && (gen.NumWithPinEvents == gen.NumWithPinPlugEvents)) ? (gen.PinnedObjectPercentage / gen.NumWithPinEvents) : double.NaN));
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_Pause_{0}\">Pause &gt; 200 Msec GC Events for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);
            PrintEventTable(writer, stats, runtime, 0, delegate (TraceGC _gc) { return _gc.PauseDurationMSec > 200; });

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"LOH_allocation_Pause_{0}\">LOH Allocation Pause (due to background GC) &gt; 200 Msec for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);
            PrintLOHAllocLargePauseTable(writer, stats, runtime, 200);

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_Gen2_{0}\">Gen 2 for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);
            PrintEventTable(writer, stats, runtime, 0, delegate (TraceGC _gc) { return _gc.Generation > 1; });

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_{0}\">All GC Events for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);
            PrintEventTable(writer, stats, runtime, Math.Max(0, runtime.GC.GCs.Count - 1000));
            PrintEventCondemnedReasonsTable(writer, stats, runtime, Math.Max(0, runtime.GC.GCs.Count - 1000));

            if (PerfView.AppLog.InternalUser)
            {
                RenderServerGcConcurrencyGraphs(writer, stats, runtime, doServerGCReport);
            }

            if (runtime.GC.Stats().FinalizedObjects.Count > 0)
            {
                const int MaxResultsToShow = 20;
                int resultsToShow = Math.Min(runtime.GC.Stats().FinalizedObjects.Count, MaxResultsToShow);
                writer.WriteLine("<HR/>");
                writer.WriteLine("<H4><A Name=\"Finalization_{0}\">Finalized Object Counts for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);
                writer.WriteLine("<Center><Table Border=\"1\">");
                writer.WriteLine("<TR><TH>Type</TH><TH>Count</TH></TR>");
                foreach (var finalized in runtime.GC.Stats().FinalizedObjects.OrderByDescending(f => f.Value).Take(resultsToShow))
                {
                    var encodedTypeName = SecurityElement.Escape(finalized.Key);
                    writer.WriteLine("<TR><TD Align=\"Center\">{0}</TD><TD Align=\"Center\">{1}</TD><TR>", encodedTypeName, finalized.Value);
                }
                writer.WriteLine("</Table></Center>");
                if (resultsToShow < runtime.GC.Stats().FinalizedObjects.Count)
                {
                    writer.WriteLine("<P><I>Only showing {0} of {1} rows.</I></P>", resultsToShow, runtime.GC.Stats().FinalizedObjects.Count);
                }
                writer.WriteLine("<P><A HREF=\"command:excelFinalization/{0}\">View the full list</A> in Excel.<P>", stats.ProcessID);
            }

            writer.WriteLine("<HR/><HR/><BR/><BR/>");
        }

        public static void ToXml(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, string indent)
        {
            writer.Write("{0}<GCProcess", indent);
            writer.Write(" Process={0}", StringUtilities.QuotePadLeft(stats.Name, 10));
            writer.Write(" ProcessID={0}", StringUtilities.QuotePadLeft(stats.ProcessID.ToString(), 5));
            if (stats.CPUMSec != 0)
            {
                writer.Write(" ProcessCpuTimeMsec={0}", StringUtilities.QuotePadLeft(stats.CPUMSec.ToString("f0"), 5));
            }
            ToXmlAttribs(writer, runtime.GC.Stats());
            if (runtime.RuntimeVersion != null)
            {
                writer.Write(" RuntimeVersion={0}", StringUtilities.QuotePadLeft(runtime.RuntimeVersion, 8));
                writer.Write(" StartupFlags={0}", StringUtilities.QuotePadLeft(runtime.StartupFlags.ToString(), 10));
                writer.Write(" CommandLine="); writer.Write(XmlUtilities.XmlQuote(stats.CommandLine));
            }
            if (stats.PeakVirtual != 0)
            {
                writer.Write(" PeakVirtualMB={0}", StringUtilities.QuotePadLeft((stats.PeakVirtual / 1000000.0).ToString(), 8));
            }
            if (stats.PeakWorkingSet != 0)
            {
                writer.Write(" PeakWorkingSetMB={0}", StringUtilities.QuotePadLeft((stats.PeakWorkingSet / 1000000.0).ToString(), 8));
            }
            writer.WriteLine(">");
            writer.WriteLine("{0}  <Generations Count=\"{1}\" TotalGCCount=\"{2}\" TotalAllocatedMB=\"{3:n3}\" TotalGCCpuMSec=\"{4:n3}\" MSecPerMBAllocated=\"{5:f3}\">",
                indent, runtime.GC.Generations().Length, runtime.GC.Stats().Count, runtime.GC.Stats().TotalAllocatedMB, runtime.GC.Stats().TotalCpuMSec, runtime.GC.Stats().TotalCpuMSec / runtime.GC.Stats().TotalAllocatedMB);
            for (int gen = 0; gen < runtime.GC.Generations().Length; gen++)
            {
                writer.Write("{0}   <Generation Gen=\"{1}\"", indent, gen);
                ToXmlAttribs(writer, runtime.GC.Generations()[gen]);
                writer.WriteLine("/>");
            }
            writer.WriteLine("{0}  </Generations>", indent);

            writer.WriteLine("{0}  <GCEvents Count=\"{1}\">", indent, runtime.GC.GCs.Count);
            foreach (TraceGC _event in runtime.GC.GCs)
            {
                ToXmlAttribs(writer, stats, runtime, _event);
            }

            writer.WriteLine("{0}  </GCEvents>", indent);
            writer.WriteLine("{0} </GCProcess>", indent);
        }

        public static void ToCsv(string filePath, TraceLoadedDotNetRuntime runtime)
        {
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filePath))
            {
                //                  0   1           2       3    4             5        6         7            8       9        10                11          12          13        14            15          16       17         18           19        20            21      22                23        24             25                                      27             28
                writer.WriteLine("Num{0}PauseStart{0}Reason{0}Gen{0}PauseMSec{0}Time In GC{0}AllocMB{0}Alloc MB/sec{0}PeakMB{0}AfterMB{0}Peak/After Ratio{0}PromotedMB{0}Gen0 Size{0}Gen0 Surv%{0}Gen0 Frag%{0}Gen1 Size{0}Gen1 Surv%{0}Gen1 Frag%{0}Gen2 Size{0}Gen2 Surv%{0}Gen2 Frag%{0}LOH Size{0}LOH Surv%{0}LOH Frag%{0}FinalizeSurviveMB{0}Pinned Object{0}% Pause Time{0}Suspend Msec", listSeparator);
                for (int i = 0; i < runtime.GC.GCs.Count; i++)
                {
                    var _event = runtime.GC.GCs[i];
                    if (!(_event.IsComplete))
                    {
                        continue;
                    }

                    var allocGen0MB = _event.GenSizeBeforeMB[(int)Gens.Gen0];
                    writer.WriteLine("{0}{26}{1:f3}{26}{2}{26}{3}{26}{4:f3}{26}{5:f1}{26}{6:f3}{26}{7:f2}{26}{8:f3}{26}{9:f3}{26}{10:2}{26}{11:f3}{26}{12:f3}{26}{13:f0}{26}{14:f2}{26}{15:f3}{26}{16:f0}{26}{17:f2}{26}{18:f3}{26}{19:f0}{26}{20:f2}{26}{21:f3}{26}{22:f0}{26}{23:f2}{26}{24:f2}{26}{25:f0}{26}{27:f1}{26}{28:f3}",
                                   _event.Number,
                                   _event.PauseStartRelativeMSec,
                                   _event.Reason,
                                   _event.GCGenerationName,
                                   _event.PauseDurationMSec,
                                   _event.PercentTimeInGC,
                                   allocGen0MB,
                                   (allocGen0MB * 1000.0) / _event.DurationSinceLastRestartMSec,
                                   _event.HeapSizePeakMB,
                                   _event.HeapSizeAfterMB,
                                   _event.HeapSizePeakMB / _event.HeapSizeAfterMB,
                                   _event.PromotedMB,
                                   _event.GenSizeAfterMB(Gens.Gen0),
                                   _event.SurvivalPercent(Gens.Gen0),
                                   _event.GenFragmentationPercent(Gens.Gen0),
                                   _event.GenSizeAfterMB(Gens.Gen1),
                                   _event.SurvivalPercent(Gens.Gen1),
                                   _event.GenFragmentationPercent(Gens.Gen1),
                                   _event.GenSizeAfterMB(Gens.Gen2),
                                   _event.SurvivalPercent(Gens.Gen2),
                                   _event.GenFragmentationPercent(Gens.Gen2),
                                   _event.GenSizeAfterMB(Gens.GenLargeObj),
                                   _event.SurvivalPercent(Gens.GenLargeObj),
                                   _event.GenFragmentationPercent(Gens.GenLargeObj),
                                   _event.HeapStats.FinalizationPromotedSize / 1000000.0,
                                   _event.HeapStats.PinnedObjectCount,
                                   listSeparator,
                                   _event.PauseTimePercentageSinceLastGC,
                                   _event.SuspendDurationMSec);
                }
            }
        }

        public static void ToCsvFinalization(string filePath, TraceLoadedDotNetRuntime runtime)
        {
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filePath))
            {
                writer.WriteLine("Type{0}Count", listSeparator);
                foreach (var finalized in runtime.GC.Stats().FinalizedObjects.OrderByDescending(f => f.Value))
                {
                    writer.WriteLine("{0}{1}{2}", finalized.Key.Replace(listSeparator, ""), listSeparator, finalized.Value);
                }
            }
        }

        public static void PerGenerationCsv(string filePath, TraceLoadedDotNetRuntime runtime)
        {
            // Sadly, streamWriter does not have a way of setting the IFormatProvider property
            // So we have to do it in this ugly, global variable way.
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filePath))
            {
                writer.WriteLine("PauseStart{0}Num{0}Gen{0}GCStop{0}TickMB{0}" +
                    "Before0{0}Before1{0}Before2{0}Before3{0}" +
                    "After0{0}After1{0}After2{0}After3{0}" +

                    "Surv0{0}Surv1{0}Surv2{0}Surv3{0}" +
                    "In0{0}In1{0}In2{0}In3{0}" +
                    "Out0{0}Out1{0}Out2{0}Out3{0}" +
                    "Frag0{0}Frag1{0}Frag2{0}Frag3" +
                    (runtime.GC.Stats().HasDetailedGCInfo ? "{0}Budget0{0}Budget1{0}Budget2{0}Budget3" : ""), listSeparator);

                foreach (TraceGC _event in runtime.GC.GCs)
                {
                    if (!_event.IsComplete)
                    {
                        continue;
                    }

                    writer.WriteLine("{0:f3}{33}{1}{33}{2}{33}{3:f3}{33}{4:f3}{33}" +
                        "{5:f3}{33}{6:f3}{33}{7:f3}{33}{8:f3}{33}" +
                        "{9:f3}{33}{10:f3}{33}{11:f3}{33}{12:f3}{33}" +
                        "{13:f3}{33}{14:f3}{33}{15:f3}{33}{16:f3}{33}" +
                        "{17:f3}{33}{18:f3}{33}{19:f3}{33}{20:f3}{33}" +

                        "{21:f3}{33}{22:f3}{33}{23:f3}{33}{24:f3}{33}" +
                        "{25:f3}{33}{26:f3}{33}{27:f3}{33}{28:f3}" +
                        (runtime.GC.Stats().HasDetailedGCInfo ? "{33}{29:f3}{33}{30:f3}{33}{31:f3}{33}{32:f3}" : ""),
                        _event.PauseStartRelativeMSec,
                        _event.Number,
                        _event.GCGenerationName,
                        _event.DurationMSec + _event.StartRelativeMSec,
                        (_event.AllocedSinceLastGCBasedOnAllocTickMB[0] + _event.AllocedSinceLastGCBasedOnAllocTickMB[1]),

                        _event.GenSizeBeforeMB[(int)Gens.Gen0], _event.GenSizeBeforeMB[(int)Gens.Gen1],
                        _event.GenSizeBeforeMB[(int)Gens.Gen2], _event.GenSizeBeforeMB[(int)Gens.GenLargeObj],

                        _event.GenSizeAfterMB(Gens.Gen0), _event.GenSizeAfterMB(Gens.Gen1),
                        _event.GenSizeAfterMB(Gens.Gen2), _event.GenSizeAfterMB(Gens.GenLargeObj),

                        _event.GenPromotedMB(Gens.Gen0), _event.GenPromotedMB(Gens.Gen1),
                        _event.GenPromotedMB(Gens.Gen2), _event.GenPromotedMB(Gens.GenLargeObj),

                        _event.GenInMB(Gens.Gen0), _event.GenInMB(Gens.Gen1),
                        _event.GenInMB(Gens.Gen2), _event.GenInMB(Gens.GenLargeObj),

                        _event.GenOutMB(Gens.Gen0), _event.GenOutMB(Gens.Gen1),
                        _event.GenOutMB(Gens.Gen2), _event.GenOutMB(Gens.GenLargeObj),

                        _event.GenFragmentationMB(Gens.Gen0), _event.GenFragmentationMB(Gens.Gen1),
                        _event.GenFragmentationMB(Gens.Gen2), _event.GenFragmentationMB(Gens.GenLargeObj),

                        _event.GenBudgetMB(Gens.Gen0), _event.GenBudgetMB(Gens.Gen1),
                        _event.GenBudgetMB(Gens.Gen2), _event.GenBudgetMB(Gens.GenLargeObj),
                        listSeparator
                        );
                }
            }
        }

        public static void ToXmlAttribs(TextWriter writer, GCStats gc)
        {
            if (gc == null)
            {
                gc = new GCStats();
            }

            writer.Write(" GCCount={0}", StringUtilities.QuotePadLeft(gc.Count.ToString(), 6));
            writer.Write(" MaxPauseDurationMSec={0}", StringUtilities.QuotePadLeft(gc.MaxPauseDurationMSec.ToString("n3"), 10));
            writer.Write(" MeanPauseDurationMSec={0}", StringUtilities.QuotePadLeft(gc.MeanPauseDurationMSec.ToString("n3"), 10));
            writer.Write(" MeanSizePeakMB={0}", StringUtilities.QuotePadLeft(gc.MeanSizePeakMB.ToString("f1"), 10));
            writer.Write(" MeanSizeAfterMB={0}", StringUtilities.QuotePadLeft(gc.MeanSizeAfterMB.ToString("f1"), 10));
            writer.Write(" TotalAllocatedMB={0}", StringUtilities.QuotePadLeft(gc.TotalAllocatedMB.ToString("f1"), 10));
            writer.Write(" TotalGCDurationMSec={0}", StringUtilities.QuotePadLeft(gc.TotalCpuMSec.ToString("n3"), 10));
            writer.Write(" MSecPerMBAllocated={0}", StringUtilities.QuotePadLeft((gc.TotalCpuMSec / gc.TotalAllocatedMB).ToString("n3"), 10));
            writer.Write(" TotalPauseTimeMSec={0}", StringUtilities.QuotePadLeft(gc.TotalPauseTimeMSec.ToString("n3"), 10));
            writer.Write(" MaxAllocRateMBSec={0}", StringUtilities.QuotePadLeft(gc.MaxAllocRateMBSec.ToString("n3"), 10));
            writer.Write(" MeanGCCpuMSec={0}", StringUtilities.QuotePadLeft(gc.MeanCpuMSec.ToString("n3"), 10));
            writer.Write(" MaxSuspendDurationMSec={0}", StringUtilities.QuotePadLeft(gc.MaxSuspendDurationMSec.ToString("n3"), 10));
            writer.Write(" MaxSizePeakMB={0}", StringUtilities.QuotePadLeft(gc.MaxSizePeakMB.ToString("n3"), 10));
        }

        public static void ToXmlAttribs(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, TraceGC gc)
        {
            writer.Write("   <GCEvent");
            writer.Write(" GCNumber={0}", StringUtilities.QuotePadLeft(gc.Number.ToString(), 10));
            writer.Write(" GCGeneration={0}", StringUtilities.QuotePadLeft(gc.Generation.ToString(), 3));
            writer.Write(" GCCpuMSec={0}", StringUtilities.QuotePadLeft(gc.GCCpuMSec.ToString("n0").ToString(), 10));
            writer.Write(" ProcessCpuMSec={0}", StringUtilities.QuotePadLeft(gc.ProcessCpuMSec.ToString("n0").ToString(), 10));
            writer.Write(" PercentTimeInGC={0}", StringUtilities.QuotePadLeft(gc.PercentTimeInGC.ToString("n2").ToString(), 10));
            writer.Write(" PauseStartRelativeMSec={0}", StringUtilities.QuotePadLeft(gc.PauseStartRelativeMSec.ToString("n3").ToString(), 10));
            writer.Write(" PauseDurationMSec={0}", StringUtilities.QuotePadLeft(gc.PauseDurationMSec.ToString("n3").ToString(), 10));
            writer.Write(" PercentPauseTime={0}", StringUtilities.QuotePadLeft(gc.PauseTimePercentageSinceLastGC.ToString("n2").ToString(), 10));
            writer.Write(" SizePeakMB={0}", StringUtilities.QuotePadLeft(gc.HeapSizePeakMB.ToString("n3"), 10));
            writer.Write(" SizeAfterMB={0}", StringUtilities.QuotePadLeft(gc.HeapSizeAfterMB.ToString("n3"), 10));
            writer.Write(" RatioPeakAfter={0}", StringUtilities.QuotePadLeft(gc.RatioPeakAfter.ToString("n3"), 5));
            writer.Write(" AllocRateMBSec={0}", StringUtilities.QuotePadLeft(gc.AllocRateMBSec.ToString("n3"), 5));
            writer.Write(" GCDurationMSec={0}", StringUtilities.QuotePadLeft(gc.DurationMSec.ToString("n3").ToString(), 10));
            writer.Write(" SuspendDurationMSec={0}", StringUtilities.QuotePadLeft(gc.SuspendDurationMSec.ToString("n3").ToString(), 10));
            writer.Write(" GCStartRelativeMSec={0}", StringUtilities.QuotePadLeft(gc.StartRelativeMSec.ToString("n3"), 10));
            writer.Write(" DurationSinceLastRestartMSec={0}", StringUtilities.QuotePadLeft(gc.DurationSinceLastRestartMSec.ToString("n3"), 5));
            writer.Write(" AllocedSinceLastGC={0}", StringUtilities.QuotePadLeft(gc.AllocedSinceLastGCMB.ToString("n3"), 5));
            writer.Write(" Type={0}", StringUtilities.QuotePadLeft(gc.Type.ToString(), 18));
            writer.Write(" Reason={0}", StringUtilities.QuotePadLeft(gc.Reason.ToString(), 27));
            writer.WriteLine(">");
            if (gc.HeapStats != null)
            {
                writer.Write("      <HeapStats");
                writer.Write(" GenerationSize0=\"{0:n0}\"", gc.HeapStats.GenerationSize0);
                writer.Write(" TotalPromotedSize0=\"{0:n0}\"", gc.HeapStats.TotalPromotedSize0);
                writer.Write(" GenerationSize1=\"{0:n0}\"", gc.HeapStats.GenerationSize1);
                writer.Write(" TotalPromotedSize1=\"{0:n0}\"", gc.HeapStats.TotalPromotedSize1);
                writer.Write(" GenerationSize2=\"{0:n0}\"", gc.HeapStats.GenerationSize2);
                writer.Write(" TotalPromotedSize2=\"{0:n0}\"", gc.HeapStats.TotalPromotedSize2);
                writer.Write(" GenerationSize3=\"{0:n0}\"", gc.HeapStats.GenerationSize3);
                writer.Write(" TotalPromotedSize3=\"{0:n0}\"", gc.HeapStats.TotalPromotedSize3);
                writer.Write(" GenerationSize4=\"{0:n0}\"", gc.HeapStats.GenerationSize4);
                writer.Write(" TotalPromotedSize4=\"{0:n0}\"", gc.HeapStats.TotalPromotedSize4);
                writer.Write(" FinalizationPromotedSize=\"{0:n0}\"", gc.HeapStats.FinalizationPromotedSize);
                writer.Write(" FinalizationPromotedCount=\"{0:n0}\"", gc.HeapStats.FinalizationPromotedCount);
                writer.Write(" PinnedObjectCount=\"{0:n0}\"", gc.HeapStats.PinnedObjectCount);
                writer.Write(" SinkBlockCount=\"{0:n0}\"", gc.HeapStats.SinkBlockCount);
                writer.Write(" GCHandleCount=\"{0:n0}\"", gc.HeapStats.GCHandleCount);
                writer.WriteLine("/>");
            }
            if (gc.GlobalHeapHistory != null)
            {
                writer.Write("      <GlobalHeapHistory");
                writer.Write(" FinalYoungestDesired=\"{0:n0}\"", gc.GlobalHeapHistory.FinalYoungestDesired);
                writer.Write(" NumHeaps=\"{0}\"", gc.GlobalHeapHistory.NumHeaps);
                writer.Write(" CondemnedGeneration=\"{0}\"", gc.GlobalHeapHistory.CondemnedGeneration);
                writer.Write(" Gen0ReductionCount=\"{0:n0}\"", gc.GlobalHeapHistory.Gen0ReductionCount);
                writer.Write(" Reason=\"{0}\"", gc.GlobalHeapHistory.Reason);
                writer.Write(" GlobalMechanisms=\"{0}\"", gc.GlobalHeapHistory.GlobalMechanisms);
                writer.WriteLine("/>");
            }

            // I am seeing a GC with SuspendEE/RestartEE/HeapStats events yet in the middle we are missing GlobalHistory and some of
            // the PerHeapHistory events so need to compensate for that.
            if (gc.PerHeapHistories != null && gc.GlobalHeapHistory != null && gc.PerHeapHistories.Count > 0)
            {
                writer.WriteLine("      <PerHeapHistories Count=\"{0}\" MemoryLoad=\"{1}\">",
                                 gc.PerHeapHistories.Count,
                                 (gc.GlobalHeapHistory.HasMemoryPressure ? gc.GlobalHeapHistory.MemoryPressure : gc.PerHeapHistories[0].MemoryPressure));
                int HeapNum = 0;
                foreach (var perHeapHistory in gc.PerHeapHistories)
                {
                    writer.Write("      <PerHeapHistory");
#if false // TODO FIX NOW
                    writer.Write(" MemoryPressure=\"{0:n0}\"", gc.perHeapHistory.MemoryPressure);
                    writer.Write(" MechanismHeapExpand=\"{0}\"", gc.perHeapHistory.MechanismHeapExpand);
                    writer.Write(" MechanismHeapCompact=\"{0}\"", gc.perHeapHistory.MechanismHeapCompact);
                    writer.Write(" InitialGenCondemned=\"{0}\"", gc.perHeapHistory.InitialGenCondemned);
                    writer.Write(" FinalGenCondemned=\"{0}\"", gc.perHeapHistory.FinalGenCondemned);
                    writer.Write(" GenWithExceededBudget=\"{0}\"", gc.perHeapHistory.GenWithExceededBudget);
                    writer.Write(" GenWithTimeTuning=\"{0}\"", gc.perHeapHistory.GenWithTimeTuning);
                    writer.Write(" GenCondemnedReasons=\"{0}\"", gc.perHeapHistory.GenCondemnedReasons);
#endif
                    if ((gc.PerHeapMarkTimes != null) && (gc.PerHeapMarkTimes.ContainsKey(HeapNum)))
                    {
                        MarkInfo mt = gc.PerHeapMarkTimes[HeapNum];

                        if (mt != null)
                        {
                            writer.Write(" MarkStack =\"{0:n3}", mt.MarkTimes[(int)MarkRootType.MarkStack]);
                            if (mt.MarkPromoted != null)
                            {
                                writer.Write("({0})", mt.MarkPromoted[(int)MarkRootType.MarkStack]);
                            }

                            writer.Write("\" MarkFQ =\"{0:n3}", mt.MarkTimes[(int)MarkRootType.MarkFQ]);
                            if (mt.MarkPromoted != null)
                            {
                                writer.Write("({0})", mt.MarkPromoted[(int)MarkRootType.MarkFQ]);
                            }

                            writer.Write("\" MarkHandles =\"{0:n3}", mt.MarkTimes[(int)MarkRootType.MarkHandles]);
                            if (mt.MarkPromoted != null)
                            {
                                writer.Write("({0})", mt.MarkPromoted[(int)MarkRootType.MarkHandles]);
                            }

                            writer.Write("\"");
                            if (gc.Generation != 2)
                            {
                                writer.Write(" MarkOldGen =\"{0:n3}", mt.MarkTimes[(int)MarkRootType.MarkOlder]);
                                if (mt.MarkPromoted != null)
                                {
                                    writer.Write("({0})", mt.MarkPromoted[(int)MarkRootType.MarkOlder]);
                                }

                                writer.Write("\"");
                            }
                            if (mt.MarkTimes[(int)MarkRootType.MarkOverflow] != 0.0)
                            {
                                writer.Write(" MarkOverflow =\"{0:n3}", mt.MarkTimes[(int)MarkRootType.MarkOverflow]);
                                if (mt.MarkPromoted != null)
                                {
                                    writer.Write("({0})", mt.MarkPromoted[(int)MarkRootType.MarkOverflow]);
                                }
                            }
                        }
                    }
                    else
                    {
                        writer.Write(" DataUnavailable=\"true\"");
                    }
                    writer.WriteLine(">");

                    var sb = new System.Text.StringBuilder();
                    for (int gens = 0; gens < perHeapHistory.GenData.Length; gens++)
                    {
                        sb.Clear();
                        sb.Append("        ");
                        writer.Write(perHeapHistory.GenData[(int)gens].ToXml((Gens)gens, sb).AppendLine().ToString());
                    }

                    writer.WriteLine("      </PerHeapHistory>");
                    HeapNum++;
                }
                writer.WriteLine("      </PerHeapHistories>");
            }
            writer.WriteLine("   </GCEvent>");
        }

        #region private

        private static bool ShowPinnedInformation(GCStats stats)
        {
            if ((PerfView.AppLog.InternalUser) && (stats.NumWithPinEvents > 0))
            {
                return true;
            }

            return false;
        }

        private static bool HasServerGcThreadingInfo(TraceGC gc)
        {
            foreach (var heap in gc.ServerGcHeapHistories)
            {
                if (heap.SampleSpans.Count > 0 || heap.SwitchSpans.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RenderServerGcConcurrencyGraphs(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, bool doServerGCReport)
        {
            if (runtime.GC.Stats().HeapCount <= 1 || runtime.GC.Stats().IsServerGCUsed != 1)
            {
                return false;
            }

            TextWriter serverGCActivityStatsFile = null;
            int gcGraphsToRender = 10;

            var serverGCs = runtime.GC.GCs
                            .Where(gc => gc.Type != GCType.BackgroundGC && HasServerGcThreadingInfo(gc))
                            .OrderByDescending(gc => gc.DurationMSec + gc.SuspendDurationMSec)
                            .ToArray();

            if (serverGCs.Length == 0)
            {
                return false;
            }

            if (doServerGCReport)
            {
                string name = "SGCStats-" + stats.Name + "-" + stats.ProcessID + ".txt";
                serverGCActivityStatsFile = new StreamWriter(name, false);
            }

            writer.WriteLine("<h3>Longest Server GCs. (CPU time by heap)</h3>");
            RenderServerGcLegend(writer);
            foreach (var gc in serverGCs)
            {
                if (gcGraphsToRender == 0)
                {
                    break;
                }
                if (ServerGcHistoryEx.ServerGcConcurrencyGraphs(writer, serverGCActivityStatsFile, gc))
                {
                    gcGraphsToRender--;
                }
            }

            if (serverGCActivityStatsFile != null)
            {
                serverGCActivityStatsFile.Close();
            }

            return true;
        }

        private static void RenderServerGcLegend(TextWriter writer)
        {
            writer.WriteLine("<svg width='500' height='200' >");
            writer.WriteLine("<rect x='10' y='10' width='5' height='30' style='fill:rgb(0,200,0);' />");
            writer.WriteLine("<text x='20' y='40'> GC thread working.</text>");
            writer.WriteLine("</rect>");

            writer.WriteLine("<rect x='10' y='50' width='5' height='30' style='fill:rgb(250,20,20);' />");
            writer.WriteLine("<text x='20' y='80'>Another thread working, potentially taking CPU time from GC thread.</text>");
            writer.WriteLine("</rect>");

            writer.WriteLine("<rect x='10' y='90' width='5' height='30' style='fill:rgb(0,0,220);' />");
            writer.WriteLine("<text x='20' y='120'>Idle.</text>");
            writer.WriteLine("</rect>");

            writer.WriteLine("<rect x='10' y='130' width='5' height='30' style='fill:rgb(0,100,220);' />");
            writer.WriteLine("<text x='20' y='160'>Low priority thread is working. (Most likely not taking CPU time from GC thread)</text>");
            writer.WriteLine("</rect>");

            writer.WriteLine("<polygon points='10,170 10,190 13,180'  style='fill:rgb(255,215,0);' />");
            writer.WriteLine("<text x='20' y='185'>GC Join reset event.</text>");

            writer.WriteLine("<rect x='10' y='200' width='15' height='4' style='fill:rgb(255,215,0);' />");
            writer.WriteLine("<text x='30' y='205'>GC Join - start to end.</text>");

            writer.WriteLine("</svg>");
        }

        private static void PrintEventTable(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, int start = 0, Predicate<TraceGC> filter = null)
        {
            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH colspan=\"" + (28 + (ShowPinnedInformation(runtime.GC.Stats()) ? 4 : 0)) + "\" Align=\"Center\">GC Events by Time</TH></TR>");
            writer.WriteLine("<TR><TH colspan=\"" + (28 + (ShowPinnedInformation(runtime.GC.Stats()) ? 4 : 0)) + "\" Align=\"Center\">All times are in msec.  Hover over columns for help.</TH></TR>");
            writer.WriteLine("<TR>" +
                 "<TH>GC<BR/>Index</TH>" +
                 "<TH>Pause Start</TH>" +
                 "<TH Title=\"How this GC was triggered\">Trigger<BR/>Reason</TH>" +
                 "<TH Title=\"N=NonConcurrent, B=Background, F=Foreground (while background is running) I=Induced i=InducedNotForced\">Gen</TH>" +
                 "<TH Title=\"The time in milliseconds that it took to suspend all threads to start this GC.  For background GCs, we pause multiple times, so this value may be higher than for foreground GCs.\">Suspend<BR/>Msec</TH>" +
                 "<TH Title=\"The amount of time that execution in managed code is blocked because the GC needs exclusive use to the heap.  For background GCs this is small.\">Pause<BR/>MSec</TH>" +
                 "<TH Title=\"Since the last GC, GC pause time expressed as a percentage of total process time.  For background GC, this includes the pause time for foreground GCs that occur during the background GC.\">%<BR/>Pause<BR/>Time</TH>" +
                 "<TH Title=\"Since the last GC, the GC CPU time divided by the total Process CPU time expressed as a percentage.\">% GC</TH>" +
                 "<TH Title=\"Amount allocated since the last GC occured\">Gen0<BR/>Alloc<BR/>MB</TH>" +
                 "<TH Title=\"The average allocation rate since the last GC.\">Gen0<BR/>Alloc<BR/>Rate<BR/>MB/sec</TH>" +
                 "<TH Title=\"The peak size of the GC during GC. (includes fragmentation)\">Peak<BR/>MB</TH>" +
                 "<TH Title=\"The size after GC (includes fragmentation)\">After<BR/>MB</TH>" +
                 "<TH>Ratio<BR/>Peak/After</TH>" +
                 "<TH Title=\"Memory this GC promoted\">Promoted<BR/>MB</TH>" +
                 "<TH Title=\"Size of gen0 at the end of this GC.\">Gen0<BR/>MB</TH>" +
                 "<TH Title=\"The % of objects in Gen0 that survived this GC.\">Gen0<BR/>Survival<BR/>Rate %</TH>" +
                 "<TH Title=\"The % of free space on gen0.\">Gen0<BR/>Frag<BR/>%</TH>" +
                 "<TH Title=\"Size of gen1 at the end of this GC.\">Gen1<BR/>MB</TH>" +
                 "<TH Title=\"The % of objects in Gen1 that survived this GC. Only available if we are doing a gen1 GC.\">Gen1<BR/>Survival<BR/>Rate %</TH>" +
                 "<TH Title=\"The % of free space on Gen1 that is betweeen live objects\">Gen1<BR/>Frag<BR/>%</TH>" +
                 "<TH Title=\"Size of Gen2 in MB at the end of this GC.\">Gen2<BR/>MB</TH>" +
                 "<TH Title=\"The % of objects in Gen2 that survived this GC. Only available if we are doing a gen2 GC.\">Gen2<BR/>Survival<BR/>Rate %</TH>" +
                 "<TH Title=\"The % of free space on gen2.\">Gen2<BR/>Frag<BR/>%</TH>" +
                 "<TH Title=\"Size of Large object heap (LOH) in MB at the end of this GC.\">LOH<BR/>MB</TH>" +
                 "<TH Title=\"The % of objects in the large object heap (LOH) that survived the GC. Only available if we are doing a gen2 GC.\">LOH<BR/>Survival<BR/>Rate %</TH>" +
                 "<TH Title=\"The % of free space that is between live objects on the large object heap (LOH).\">LOH<BR/>Frag<BR/>%</TH>" +
                 "<TH Title=\"The number of MB of objects that have finalizers (destructors) that survived this GC. \">Finalizable<BR/>Surv MB</TH>" +
                 "<TH Title=\"Number of pinned objects this GC promoted.\">Pinned<BR/>Obj</TH>" +

                 (ShowPinnedInformation(runtime.GC.Stats()) ?
                 "<TH Title=\"Size of pinned objects this GC promoted.\">Pinned<BR/>Obj<BR/>Size</TH>" +
                 "<TH Title=\"Percentage of pinned plugs occupied by pinned objects.\">Pinned<BR/>Obj<BR/>%</TH>" +
                 "<TH Title=\"Size of pinned plugs\">Pinned<BR/>Size</TH>" +
                 "<TH Title=\"Size of pinned plugs by GC\">GC<BR/>Pinned<BR/>Size</TH>"
                 : string.Empty) +

                 "</TR>");

            if (start != 0)
            {
                writer.WriteLine("<TR><TD colspan=\"26\" Align=\"Center\"> {0} Beginning entries truncated, use <A HREF=\"command:excel/{1}\">View in Excel</A> to view all...</TD></TR>", start, stats.ProcessID);
            }

            for (int i = start; i < runtime.GC.GCs.Count; i++)
            {
                var _event = runtime.GC.GCs[i];
                if (filter == null || filter(_event))
                {
                    if (!_event.IsComplete)
                    {
                        continue;
                    }

                    var allocGen0MB = _event.UserAllocated[(int)Gens.Gen0];

                    writer.WriteLine("<TR " + GetGenerationBackgroundColorAttribute(_event.Generation) + ">" +
                                    "<TD Align=\"right\">{0}</TD>" +      // GC index
                                    "<TD Align=\"right\">{1:n3}</TD>" +   // Pause start
                                    "<TD Align=\"right\">{2}</TD>" +      // Reason
                                    "<TD Align=\"right\">{3}</TD>" +      // Gen
                                    "<TD Align=\"right\">{4:n3}</TD>" +   // Suspension time
                                    "<TD Align=\"right\">{5:n3}</TD>" +   // Pause duration
                                    "<TD Align=\"right\">{6:n1}</TD>" +   // % pause time since last GC
                                    "<TD Align=\"right\">{7:n1}</TD>" +   // % time in GC
                                    "<TD Align=\"right\">{8:n3}</TD>" +   // Amount Allocated in gen0
                                    "<TD Align=\"right\">{9:n2}</TD>" +   // Gen0 AllocRate
                                    "<TD Align=\"right\">{10:n3}</TD>" +   // Size at the beginning of this GC
                                    "<TD Align=\"right\">{11:n3}</TD>" +   // Size at the end of this GC
                                    "<TD Align=\"right\">{12:n2}</TD>" +  // Ratio of end/beginning
                                    "<TD Align=\"right\">{13:n3}</TD>" +  // Memory this GC promoted
                                    "<TD Align=\"right\">{14:n3}</TD>" +  // Gen0 size at the end of this GC
                                    "<TD Align=\"right\">{15:n0}</TD>" +  // Gen0 survival rate
                                    "<TD Align=\"right\">{16:n2}</TD>" +  // Gen0 frag ratio
                                    "<TD Align=\"right\">{17:n3}</TD>" +  // Gen1 size at the end of this GC
                                    "<TD Align=\"right\">{18:n0}</TD>" +  // Gen1 survival rate
                                    "<TD Align=\"right\">{19:n2}</TD>" +  // Gen1 frag ratio
                                    "<TD Align=\"right\">{20:n3}</TD>" +  // Gen2 size at the end of this GC
                                    "<TD Align=\"right\">{21:n0}</TD>" +  // Gen2 survivl rate
                                    "<TD Align=\"right\">{22:n2}</TD>" +  // Gen2 frag ratio
                                    "<TD Align=\"right\">{23:n3}</TD>" +  // LOH size at the end of this GC
                                    "<TD Align=\"right\">{24:n0}</TD>" +  // LOH survival rate
                                    "<TD Align=\"right\">{25:n2}</TD>" +  // LOH frag ratio
                                    "<TD Align=\"right\">{26:n2}</TD>" +  // Finalize promoted for this GC
                                    "<TD Align=\"right\">{27:n0}</TD>" +  // # of pinned object this GC saw

                                    (ShowPinnedInformation(runtime.GC.Stats()) ?
                                    "<TD Align=\"right\">{28:n0}</TD>" +  // size of pinned object this GC saw
                                    "<TD Align=\"right\">{29:n0}</TD>" + // percent of pinned object this GC saw
                                    "<TD Align=\"right\">{30:n0}</TD>" + // size of pinned plugs
                                    "<TD Align=\"right\">{31:n0}</TD>"  // size of pinned plugs by GC
                                    : string.Empty) +

                                    "</TR>",
                       _event.Number,
                       _event.PauseStartRelativeMSec,
                       _event.Reason,
                       _event.GCGenerationName,
                       _event.SuspendDurationMSec,
                       _event.PauseDurationMSec,
                       _event.PauseTimePercentageSinceLastGC,
                       _event.PercentTimeInGC,
                       allocGen0MB,
                       (allocGen0MB * 1000.0) / _event.DurationSinceLastRestartMSec,
                       _event.HeapSizePeakMB,
                       _event.HeapSizeAfterMB,
                       _event.HeapSizePeakMB / _event.HeapSizeAfterMB,
                       _event.PromotedMB,
                       _event.GenSizeAfterMB(Gens.Gen0),
                       _event.SurvivalPercent(Gens.Gen0),
                       _event.GenFragmentationPercent(Gens.Gen0),
                       _event.GenSizeAfterMB(Gens.Gen1),
                       _event.SurvivalPercent(Gens.Gen1),
                       _event.GenFragmentationPercent(Gens.Gen1),
                       _event.GenSizeAfterMB(Gens.Gen2),
                       _event.SurvivalPercent(Gens.Gen2),
                       _event.GenFragmentationPercent(Gens.Gen2),
                       _event.GenSizeAfterMB(Gens.GenLargeObj),
                       _event.SurvivalPercent(Gens.GenLargeObj),
                       _event.GenFragmentationPercent(Gens.GenLargeObj),
                       _event.HeapStats.FinalizationPromotedSize / 1000000.0,
                       _event.HeapStats.PinnedObjectCount,
                       ((_event.GetPinnedObjectSizes() != 0) ? _event.GetPinnedObjectSizes() : double.NaN),
                       ((_event.GetPinnedObjectPercentage() != -1) ? _event.GetPinnedObjectPercentage() : double.NaN),
                       ((_event.GetPinnedObjectPercentage() != -1) ? _event.TotalPinnedPlugSize : double.NaN),
                       ((_event.GetPinnedObjectPercentage() != -1) ? (_event.TotalPinnedPlugSize - _event.TotalUserPinnedPlugSize) : double.NaN)
                       );
                }
            }

            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
        }

        private static void PrintEventCondemnedReasonsTable(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, int start = 0, Predicate<TraceGC> filter = null)
        {
            // Validate that we actually have condemned reasons information.
            int missingPerHeapHistories = 0;
            bool perHeapHistoryPresent = false;
            for (int i = 0; i < runtime.GC.GCs.Count; i++)
            {
                if (runtime.GC.GCs[i].IsComplete)
                {
                    if (runtime.GC.GCs[i].PerHeapHistories == null)
                    {
                        missingPerHeapHistories++;

                        // Allow up to 5 complete events without per-heap histories
                        // before we assume that we don't have any per-heap history information.
                        if (missingPerHeapHistories >= 5)
                        {
                            return;
                        }

                        continue;
                    }

                    perHeapHistoryPresent = true;
                    break;
                }
            }

            // Ensure that we have per-heap history data before continuing.
            if (!perHeapHistoryPresent)
            {
                return;
            }

            bool isServerGC = (runtime.GC.Stats().IsServerGCUsed == 1);

            List<TraceGC> events = new List<TraceGC>();
            List<byte[]> condemnedReasonRows = new List<byte[]>();
            List<int> heapIndexes = isServerGC ? new List<int>() : null;
            for (int i = start; i < runtime.GC.GCs.Count; i++)
            {
                var _event = runtime.GC.GCs[i];
                if (filter == null || filter(_event))
                {
                    if (!_event.IsComplete)
                    {
                        continue;
                    }
                }
                events.Add(_event);
                int heapIndexHighestGen;
                condemnedReasonRows.Add(GetCondemnedReasonRow(_event, out heapIndexHighestGen));
                if (isServerGC)
                {
                    heapIndexes.Add(heapIndexHighestGen);
                }
            }

            bool hasAnyContent = false;
            bool[] columnHasContent = new bool[CondemnedReasonsHtmlHeader.Length];
            for (int j = 0; j < CondemnedReasonsHtmlHeader.Length; j++)
            {
                foreach (byte[] condemnedReasonRow in condemnedReasonRows)
                {
                    if (condemnedReasonRow[j] != 0)
                    {
                        hasAnyContent = true;
                        columnHasContent[j] = true;
                        break;
                    }
                }
            }

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4>Condemned reasons for GCs</H4>");
            if (hasAnyContent)
            {
                writer.WriteLine("<P>This table gives a more detailed account of exactly why a GC decided to collect that generation.  ");
                writer.WriteLine("Hover over the column headings for more info.</P>");
            }
            else
            {
                writer.WriteLine("<P>The trace contains events for the condemned reason but there is none.</P>");
                return;
            }
            if (start != 0)
            {
                writer.WriteLine("<TR><TD colspan=\"26\" Align=\"Center\"> {0} Beginning entries truncated</TD></TR>", start);
            }

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH>GC Index</TH>");
            if (isServerGC)
            {
                writer.WriteLine("<TH>Heap<BR/>Index</TH>");
            }

            for (int i = 0; i < CondemnedReasonsHtmlHeader.Length; i++)
            {
                if (columnHasContent[i])
                {
                    writer.WriteLine("<TH Title=\"{0}\">{1}</TH>",
                                     CondemnedReasonsHtmlHeader[i][1],
                                     CondemnedReasonsHtmlHeader[i][0]);
                }
            }
            writer.WriteLine("</TR>");

            for (int i = 0; i < events.Count; i++)
            {
                TraceGC _event = events[i];
                byte[] condemnedReasons = condemnedReasonRows[i];
                writer.WriteLine("<TR " + GetGenerationBackgroundColorAttribute(_event.Generation) + ">" +
                                 "<TD Align=\"center\">{0}</TD>{1}</TR>",
                                 _event.Number,
                                 PrintCondemnedReasonsToHtml(((heapIndexes == null) ? null : (int?)heapIndexes[i]), condemnedReasons, columnHasContent));
            }

            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
        }

        private static void PrintLOHAllocLargePauseTable(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, int minPauseMSec)
        {
            // Find the first event that has the LOH alloc pause info.
            int index;
            for (index = 0; index < runtime.GC.GCs.Count; index++)
            {
                if ((runtime.GC.GCs[index].LOHWaitThreads != null) && (runtime.GC.GCs[index].LOHWaitThreads.Count != 0))
                {
                    break;
                }
            }

            if (index == runtime.GC.GCs.Count)
            {
                return;
            }

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR>" +
                 "<TH>BGC<BR/>Index</TH>" +
                 "<TH>Thread<BR/>ID</TH>" +
                 "<TH>Pause<BR/>Start</TH>" +
                 "<TH>Pause MSec</TH>" +
                 "<TH Title=\"LOH allocation has to wait when BGC is threading its free list; or if the app has already allocated enough on LOH\">Pause<BR/>Reason</TH>" +
                 "</TR>");

            while (index < runtime.GC.GCs.Count)
            {
                TraceGC _event = runtime.GC.GCs[index];
                if (_event.LOHWaitThreads != null)
                {
                    int longPauseCount = 0;

                    Dictionary<int, BGCAllocWaitInfo>.ValueCollection infoCollection = _event.LOHWaitThreads.Values;

                    foreach (BGCAllocWaitInfo info in infoCollection)
                    {
                        // First pass to know how many rows we'll need to print.
                        if (info.IsLOHWaitLong(minPauseMSec))
                        {
                            longPauseCount++;
                        }
                    }

                    if (longPauseCount > 0)
                    {
                        writer.WriteLine("<TR><TD Align=\"right\" rowspan=\"{0}\">{1}</TD>", longPauseCount, _event.Number);

                        bool isFirstRow = true;

                        foreach (KeyValuePair<int, BGCAllocWaitInfo> kvp in _event.LOHWaitThreads)
                        {
                            BGCAllocWaitInfo info = kvp.Value;
                            // Second pass to actually print.
                            if (info.IsLOHWaitLong(minPauseMSec))
                            {
                                if (isFirstRow)
                                {
                                    isFirstRow = false;
                                }
                                else
                                {
                                    writer.WriteLine("<TR>");
                                }

                                writer.WriteLine("<TD>{0}</TD><TD>{1:n3}</TD><TD>{2:n3}</TD><TD>{3}</TD>",
                                                    kvp.Key,
                                                    kvp.Value.WaitStartRelativeMSec,
                                                    (kvp.Value.WaitStopRelativeMSec - kvp.Value.WaitStartRelativeMSec),
                                                    kvp.Value.ToString());
                            }
                        }
                    }
                }

                index++;
            }

            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
        }

        private static byte[] GetCondemnedReasonRow(TraceGC gc, out int HeapIndexHighestGen)
        {
            HeapIndexHighestGen = 0;
            if (gc.PerHeapCondemnedReasons == null && gc.GlobalCondemnedReasons == null)
            {
                return null;
            }
            byte[] result = new byte[(int)CondemnedReasonGroup.Max];

            if (gc.PerHeapCondemnedReasons != null)
            {
                if (gc.PerHeapCondemnedReasons.Length != 1)
                {
                    // Only need to print out the heap index for server GC - when we are displaying this
                    // in the GCStats Html page we only display the first heap we find that caused us to
                    // collect the generation we collect.
                    HeapIndexHighestGen = gc.FindFirstHighestCondemnedHeap();

                    // We also need to consider the factors that cause blocking GCs.
                    if (((int)gc.Generation == 2) && (gc.Type != GCType.BackgroundGC))
                    {
                        int GenToCheckBlockingIndex = HeapIndexHighestGen;
                        int BlockingFactorsHighest = 0;

                        for (int HeapIndex = GenToCheckBlockingIndex; HeapIndex < gc.PerHeapCondemnedReasons.Length; HeapIndex++)
                        {
                            byte[] ReasonGroups = gc.PerHeapCondemnedReasons[HeapIndex].CondemnedReasonGroups;
                            int BlockingFactors = ReasonGroups[(int)CondemnedReasonGroup.Expand_Heap] +
                                                  ReasonGroups[(int)CondemnedReasonGroup.GC_Before_OOM] +
                                                  ReasonGroups[(int)CondemnedReasonGroup.Fragmented_Gen2] +
                                                  ReasonGroups[(int)CondemnedReasonGroup.Fragmented_Gen2_High_Mem];

                            if (BlockingFactors > BlockingFactorsHighest)
                            {
                                HeapIndexHighestGen = HeapIndex;
                            }
                        }
                    }

                }
                if (HeapIndexHighestGen < gc.PerHeapCondemnedReasons.Length)
                {
                    FillCondemnedReason(result, gc.PerHeapCondemnedReasons[HeapIndexHighestGen]);
                }
            }

            if (gc.GlobalCondemnedReasons != null)
            {
                FillCondemnedReason(result, gc.GlobalCondemnedReasons);
            }

            return result;
        }

        private static void FillCondemnedReason(byte[] result, GCCondemnedReasons reasons)
        {
            for (CondemnedReasonGroup i = 0; i < CondemnedReasonGroup.Max; i++)
            {
                result[(int)i] |= reasons.CondemnedReasonGroups[(int)i];
            }
        }

        private static string PrintCondemnedReasonsToHtml(int? heapIndex, byte[] condemnedReasons, bool[] hasContent)
        {
            StringBuilder sb = new StringBuilder(100);
            if (heapIndex != null)
            {
                sb.Append("<TD Align=\"center\">");
                sb.Append(heapIndex);
                sb.Append("</TD>");
            }
            for (CondemnedReasonGroup i = 0; i < CondemnedReasonGroup.Max; i++)
            {
                int j = (int)i;
                if (hasContent[j])
                {
                    sb.Append("<TD Align=\"center\">");
                    if (i == CondemnedReasonGroup.Induced)
                    {
                        var val = (InducedType)condemnedReasons[j];
                        if (val != 0)
                        {
                            sb.Append(val);
                        }
                    }
                    else
                    {
                        var val = condemnedReasons[j];
                        if (val != 0)
                        {
                            sb.Append(val);
                        }
                    }
                    sb.Append("</TD>");
                }
                sb.Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        // This is what we use for the html header and the help text.
        private static string[][] CondemnedReasonsHtmlHeader = new string[(int)CondemnedReasonGroup.Max][]
        {
            new string[] {"Initial<BR/>Requested<BR/>Generation", "This is the generation when this GC was triggered"},
            new string[] {"Final<BR/>Generation", "The final generation to be collected"},
            new string[] {"Generation<BR/>Budget<BR/>Exceeded", "This is the highest generation whose budget is exceeded"},
            new string[] {"Time<BR/>Tuning", "Time exceeded between GCs so we need to collect this generation"},
            new string[] {"Induced", "Blocking means this was induced as a blocking GC; NotForced means it's up to GC to decide whether it should be a blocking GC or a background GC"},
            new string[] {"Ephemeral<BR/>Low", "We are running low on the ephemeral segment, GC needs to do at least a gen1 GC"},
            new string[] {"Expand<BR/>Heap", "We are running low in an ephemeral GC, GC needs to do a full GC"},
            new string[] {"Fragmented<BR/>Ephemeral", "Ephemeral generations are fragmented"},
            new string[] {"Low Ephemeral<BR/>Fragmented Gen2", "We are running low on the ephemeral segment but gen2 is fragmented enough so a full GC would avoid expanding the heap"},
            new string[] {"Fragmented<BR/>Gen2", "Gen2 is too fragmented, doing a full blocking GC"},
            new string[] {"High<BR/>Memory", "We are in high memory load situation and doing a full blocking GC"},
            new string[] {"Compacting<BR/>Full<BR/>GC", "Last GC we trigger before we throw OOM"},
            new string[] {"Small<BR/>Heap", "Heap is too small for doing a background GC and we do a blocking one instead"},
            new string[] {"Ephemeral<BR/>Before<BR/>BGC", "Ephemeral GC before a background GC starts"},
            new string[] {"Internal<BR/>Tuning", "Internal tuning"},
            new string[] {"Max Generation Budget", "We are in high memory load situation and have consumed enough max generation budget, so we decided to do a full GC"},
            new string[] {"Avoid Unproductive<BR>Full GC", "This happens when the GC detects previous attempts to do a full compacting GC is not making progress and therefore reduce to gen1"},
            new string[] {"Provisional Mode<BR>Induced", "Provisional mode was triggered, and last gen1 GC increased gen2 size, and therefore induced a full GC"},
            new string[] {"Provisional Mode<BR>LOH alloc", "Provisional mode was triggered but this GC was triggered due to LOH allocation" },
            new string[] {"Provision Mode", "Provisional mode was triggered and we do a gen1 GC normally until it increases gen2 size" },
            new string[] {"Compacting Full<BR>under HardLimit", "Last GC we trigger before we throw OOM under hard limit"},
            new string[] {"LOH Frag<BR> HardLimit", "This happens when we had a heap limit and the LOH fragmentation is > 1/8 of the hard limit"},
            new string[] {"LOH Reclaim<BR>HardLimit", "This happens when we had a heap limit and we could potentially reclaim from LOH is > 1/8 of the hard limit"},
            new string[] {"Servo<BR>Initial", "This happens when the servo tuning is trying to get some initial data by triggering BGC"},
            new string[] {"Servo<BR>Blocking gc", "This happens when the servo tuning decides a blocking gc is appropriate"},
            new string[] {"Servo<BR>BGC", "This happens when the servo tuning decides a BGC is appropriate"},
            new string[] {"Servo<BR>Gen0", "This happens when the servo tuning decides a gen1 gc should be postponed and doing a gen0 GC instead"},
            new string[] {"Stress<BR>Mix", "This happens in GCStress mix mode, every 10th GC is gen2"},
            new string[] {"Stress", "This happens in GCStress, every GC is gen2"},
        };

        // Change background color according to generation
        //          gen0 = robin egg blue
        //          gen1 = light sky blue
        //          gen2 = iceberg
        private static string GetGenerationBackgroundColorAttribute(int gen)
        {
            switch (gen)
            {
                case 2:
                    return "bgcolor=#56A5EC";
                case 1:
                    return "bgcolor=#82CAFF";
                default:
                    return "bgcolor=#BDEDFF";
            }
        }
        #endregion
    }

    // Server history per heap. This is for CSwitch/CPU sample/Join events.
    // Each server GC thread goes through this flow during each GC
    // 1) runs server GC code
    // 2) joins with other GC threads
    // 3) restarts
    // 4) goes back to 1).
    // We call 1 through 3 an activity. There are as many activities as there are joins.
    internal class ServerGcHistoryEx
    {
        //returns true if server GC graph has data
        public static bool ServerGcConcurrencyGraphs(TextWriter writer, TextWriter serverGCActivityStatsFile, TraceGC gc)
        {
            bool hasData = false;
            writer.WriteLine("<div>");
            writer.WriteLine("<h4>" + gc.Number + "</h4>");

            int scale;
            if (gc.PauseDurationMSec < 100)
            {
                scale = 3;
            }
            else if (gc.PauseDurationMSec < 600)
            {
                scale = 2;
            }
            else
            {
                scale = 1;
            }

            writer.WriteLine("Gen" + gc.Generation + " Pause:" + (int)gc.PauseDurationMSec + "ms");
            writer.WriteLine("1ms = " + scale + "px");
            foreach (var heap in gc.ServerGcHeapHistories)
            {
                if (heap.SwitchSpans.Count > 0 || heap.SampleSpans.Count > 0)
                {
                    writer.WriteLine("<table><tr>");
                    writer.WriteLine("<td style='min-width:200px'>Heap #" + heap.HeapId + " Gc Thread Id: " + heap.GcWorkingThreadId + "</td>");
                    writer.WriteLine("<td>");
                    LogServerGCAnalysis(serverGCActivityStatsFile, "--------------[HEAP {0}]--------------", heap.HeapId);
                    ServerGcHistoryEx hist = new ServerGcHistoryEx();
                    hist.RenderGraph(writer, serverGCActivityStatsFile, gc, heap, scale);
                    writer.WriteLine("</td></tr></table>");
                    hasData = true;
                }
            }
            writer.WriteLine("</div>");
            return hasData;
        }

        #region private
        private enum ServerGCThreadState
        {
            // This is when GC thread needs to run to do GC work. We care the most about
            // other threads running during this state.
            State_Ready = 0,
            // GC thread doesn't need the CPU so other threads can run and don't count as
            // interference to the GC thread.
            State_WaitInJoin = 1,
            // This is when GC needs to do work on a single thread. Other threads running
            // in this state is also important.
            State_SingleThreaded = 2,
            // For the last joined thread, this is how long it took between restart start and end.
            // For other threads, this is when restart start is fired and when this join actually
            // ended. This usually should be really short and interference is also important.
            State_WaitingInRestart = 3,
            State_Max = 4,
        }

        private class ServerGCThreadStateInfo
        {
            public double gcThreadRunningTime;
            // Process ID and running time in that process.
            // The process ID could be the current process, but not the GC thread.
            public Dictionary<int, OtherThreadInfo> otherThreadsRunningTime;
        }

        private class OtherThreadInfo
        {
            public string processName;
            public double runningTime;

            public OtherThreadInfo(string name, double time)
            {
                processName = name;
                runningTime = time;
            }
        }

        private Dictionary<WorkSpanType, string> Type2Color = new Dictionary<WorkSpanType, string>(4)
                {
                    {WorkSpanType.GcThread, "rgb(0,200,0)"},
                    {WorkSpanType.RivalThread, "rgb(250,20,20)"},
                    {WorkSpanType.Idle, "rgb(0,0,220)"},
                    {WorkSpanType.LowPriThread, "rgb(0,100,220)"},
                };

        private string[] SGCThreadStateDesc = new string[(int)ServerGCThreadState.State_Max]
        {
                "GC thread needs to run - non GC threads running on this CPU means GC runs slower",
                "GC thread is waiting to synchronize with other threads - non GC threads running does not affect GC",
                "GC thread needs to run single threaded work - non GC threads running on this CPU means GC runs slower",
                "GC thread is waiting to restart - non GC threads running on this CPU means GC runs slower",
        };

        internal void RenderGraph(TextWriter writer, TextWriter serverGCActivityStatsFile, TraceGC parent, ServerGcHistory heap, int scale)
        {
            double puaseTime = parent.PauseDurationMSec;
            writer.WriteLine(string.Format("<svg width='{0}' height='37' >", scale * puaseTime));
            //draw ruler
            writer.WriteLine(string.Format("<rect x='0' y='35' width='{0}' height='1' style='fill:black;' />", scale * puaseTime));
            for (int i = 0; i < puaseTime; i += 10)
            {
                writer.WriteLine(string.Format("<rect x='{0}' y='32' width='1' height='4' style='fill:black;' />", scale * i));
            }

            // Server GC report isn't implemented for CSwitch yet.
            if ((heap.SwitchSpans.Count > 0) && serverGCActivityStatsFile == null)
            {
                RenderSwitches(writer, parent, heap, scale);
            }
            else
            {
                RenderSamples(writer, serverGCActivityStatsFile, parent, heap, scale);
            }

            //draw GC start time marker
            {
                writer.WriteLine(string.Format("<rect x='{0}' y='0' width='2' height='37' style='fill:black;' />", scale * parent.SuspendDurationMSec));
            }

            //draw GC joins, if any
            GcJoin lastStartJoin = null;
            foreach (var join in heap.GcJoins)
            {

                if (join.Type == GcJoinType.Restart)
                {
                    if (join.Time == GcJoinTime.End)
                    {
                        int x = scale * (int)join.RelativeTimestampMsc;
                        string color = "rgb(255,215,0)";
                        writer.WriteLine(string.Format("<polygon points='{0},5 {0},25 {1},15'  style='fill:{2};' >", x, x + 3, color));
                        writer.WriteLine(string.Format("<title>GC Join Restart. Timestamp:{0:0.00} Type: {1} Called from heap #:{2} (Waking up other threads)</title>", join.AbsoluteTimestampMsc, join.Type, join.Heap));
                        writer.WriteLine("</polygon>");
                    }
                }
                else
                {
                    if (join.Time == GcJoinTime.Start)
                    {
                        lastStartJoin = join;
                    }
                    else
                    {
                        if (lastStartJoin != null)
                        {
                            if (lastStartJoin.Type == join.Type)
                            {
                                int x = scale * (int)lastStartJoin.RelativeTimestampMsc;
                                int width = scale * (int)(join.RelativeTimestampMsc - lastStartJoin.RelativeTimestampMsc);

                                string color = "rgb(255,215,0)";
                                writer.WriteLine(string.Format("<rect x='{0}' y='13' width='{1}' height='4' style='fill:{2};'  >", x, Math.Max(width, 2), color));
                                writer.WriteLine(string.Format("<title>GC Join. Timestamp:{0:0.00}ms Duration: {1:0.00}ms Type: {2} (Waiting for other threads)</title>",
                                    lastStartJoin.AbsoluteTimestampMsc, join.AbsoluteTimestampMsc - lastStartJoin.AbsoluteTimestampMsc, join.Type));
                                writer.WriteLine("</rect>");
                            }
                            lastStartJoin = null;
                        }

                    }
                }
            }
            writer.WriteLine("</svg>");
        }

        private void UpdateActivityThreadTime(TextWriter serverGCActivityStatsFile, ServerGcHistory heap, GcWorkSpan span, ServerGCThreadStateInfo info, double threadTime, ServerGCThreadState currentThreadState)
        {
            LogServerGCAnalysis(serverGCActivityStatsFile, "TIME: {0, 20} - {1}: {2:n3}ms (span: {3:n3} ms -> {4:n3} ms({5:n3}))",
                currentThreadState, span.ProcessName,
                threadTime,
                span.AbsoluteTimestampMsc, (span.AbsoluteTimestampMsc + span.DurationMsc), span.DurationMsc);

            if (span.Type == WorkSpanType.GcThread)
            {
                info.gcThreadRunningTime += threadTime;
            }
            else
            {
                if (info.otherThreadsRunningTime.ContainsKey(span.ProcessId))
                {
                    OtherThreadInfo other = info.otherThreadsRunningTime[span.ProcessId];
                    if (!other.processName.Contains(span.ProcessName))
                    {
                        other.processName += ";" + span.ProcessName;
                    }

                    other.runningTime += threadTime;
                }
                else
                {
                    info.otherThreadsRunningTime.Add(span.ProcessId, new OtherThreadInfo(span.ProcessName, threadTime));
                }
            }

            if ((currentThreadState != ServerGCThreadState.State_WaitInJoin) &&
                (span.ThreadId != heap.GcWorkingThreadId) &&
                (threadTime > 5))
            {
                LogServerGCAnalysis(serverGCActivityStatsFile, "Long interference of {0:n3} ms detected on thread {1}({2}:{3}) ({4:n3} ms -> {5:n3} ms)",
                    threadTime, span.ThreadId, span.ProcessName, span.ProcessId, span.AbsoluteTimestampMsc, (span.AbsoluteTimestampMsc + span.DurationMsc));
            }
            if ((heap.ProcessId == 9140) &&
                (currentThreadState != ServerGCThreadState.State_WaitInJoin) &&
                (span.ThreadId == heap.GcWorkingThreadId) &&
                // If the reason is not one of UserRequest, QuantumEnd or YieldExecution, we need to pay attention.
                ((span.WaitReason != 6) || (span.WaitReason != 30) || (span.WaitReason != 33)))
            {
                LogServerGCAnalysis(serverGCActivityStatsFile, "S: {0, 30} - {1:n3} ms -> {2:n3} ms({3:n3}) (WR: {4}), pri: {5}",
                    currentThreadState, span.AbsoluteTimestampMsc, (span.AbsoluteTimestampMsc + span.DurationMsc), threadTime,
                    span.WaitReason, span.Priority);
                LogServerGCAnalysis(serverGCActivityStatsFile, "S: {8} - {0:n3} ms from thread {1}({2}:{3})(WR: {4}), pri: {5} ({6:n3} ms -> {7:n3} ms)",
                    threadTime, span.ThreadId, span.ProcessName, span.ProcessId, span.WaitReason, span.Priority, span.AbsoluteTimestampMsc, (span.AbsoluteTimestampMsc + span.DurationMsc),
                    currentThreadState);
            }
        }

        private ServerGCThreadState UpdateCurrentThreadState(TextWriter serverGCActivityStatsFile, ServerGcHistory heap, GcJoin join, ServerGCThreadState oldState)
        {
            ServerGCThreadState newThreadState = oldState;
            switch (join.Time)
            {
                case GcJoinTime.Start:
                    if ((join.Type == GcJoinType.LastJoin) || (join.Type == GcJoinType.FirstJoin))
                    {
                        newThreadState = ServerGCThreadState.State_SingleThreaded;
                    }
                    else if (join.Type == GcJoinType.Restart)
                    {
                        newThreadState = ServerGCThreadState.State_WaitingInRestart;
                    }
                    else
                    {
                        newThreadState = ServerGCThreadState.State_WaitInJoin;
                    }

                    break;
                case GcJoinTime.End:
                    if (join.Heap == heap.HeapId)
                    {
                        newThreadState = ServerGCThreadState.State_Ready;
                    }

                    break;
                default:
                    break;
            }

            LogServerGCAnalysis(serverGCActivityStatsFile, "S: {0}->{1} {2:n3} - heap: {3}, time: {4}, type: {5}, id: {6}",
                oldState, newThreadState,
                join.AbsoluteTimestampMsc,
                join.Heap, join.Time, join.Type, join.JoinID);

            return newThreadState;
        }

        // This is for verbose logging within a span (CSwitch or CPU sample).
        private void LogJoinInSpan(TextWriter serverGCActivityStatsFile, ServerGcHistory heap, int currentJoinEventIndex, ServerGCThreadState state)
        {
            if ((heap.GcJoins.Count > 0) && (currentJoinEventIndex < heap.GcJoins.Count))
            {
                LogServerGCAnalysis(serverGCActivityStatsFile, "{0:n3}: Heap{1}: Join {2}: type: {3}, time: {4} [S={5}]",
                    heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc,
                    heap.GcJoins[currentJoinEventIndex].Heap,
                    currentJoinEventIndex,
                    heap.GcJoins[currentJoinEventIndex].Type,
                    heap.GcJoins[currentJoinEventIndex].Time,
                    state);
            }
        }

        private void RenderSwitches(TextWriter writer, TraceGC parent, ServerGcHistory heap, int scale)
        {
            double lastTimestamp = 0;
            foreach (var span in heap.SwitchSpans)
            {
                //filtering out workspans that ended before GC actually started
                if (span.AbsoluteTimestampMsc + span.DurationMsc >= parent.PauseStartRelativeMSec)
                {
                    if (span.DurationMsc >= 1.0 || span.Type == WorkSpanType.GcThread || (span.RelativeTimestampMsc - lastTimestamp) >= 1.0)
                    {
                        string color = Type2Color[span.Type];
                        lastTimestamp = (int)(span.RelativeTimestampMsc);
                        int width = scale * (int)(span.DurationMsc + 1);
                        int x = scale * (int)(span.RelativeTimestampMsc);
                        if (x < 0)
                        {
                            width += x;
                            x = 0;
                        }

                        writer.WriteLine(string.Format("<rect x='{0}' y='2' width='{1}' height='30' style='fill:{2};' >", x, width, color));
                        writer.WriteLine(string.Format("<title>{0} (PID: {1} TIP: {2} Priority: {3} Timestamp:{4:0.00} Duration: {5}ms WR:{6})</title>",
                            span.ProcessName, span.ProcessId, span.ThreadId, span.Priority, span.AbsoluteTimestampMsc, (int)span.DurationMsc, span.WaitReason));
                        writer.WriteLine("</rect>");
                        //border
                        if (span.DurationMsc > 3)
                        {
                            writer.WriteLine(string.Format("<rect x='{0}' y='2' width='1' height='30' style='fill:rgb(0,0,0);' />", x + width - 1));
                        }
                    }
                }
            }
        }

        private void RenderSamples(TextWriter writer, TextWriter serverGCActivityStatsFile, TraceGC parent, ServerGcHistory heap, int scale)
        {
            if (heap.GcJoins.Count > 0)
            {
                activityStats = new ServerGCThreadStateInfo[(int)ServerGCThreadState.State_Max];
                for (int i = 0; i < activityStats.Length; i++)
                {
                    activityStats[i] = new ServerGCThreadStateInfo();
                    activityStats[i].otherThreadsRunningTime = new Dictionary<int, OtherThreadInfo>();
                }
            }

            int currentJoinEventIndex = 0;
            ServerGCThreadState currentThreadState = ServerGCThreadState.State_Ready;
            ServerGCThreadState lastThreadState = currentThreadState;
            gcReadyTime = parent.PauseStartRelativeMSec;
            lastGCSpanEndTime = gcReadyTime;

            LogServerGCAnalysis(serverGCActivityStatsFile, "GC#{0}, gen{1}, {2:n3} ms -> {3:n3} ms",
                parent.Number, parent.Generation, parent.PauseStartRelativeMSec, (parent.PauseStartRelativeMSec + parent.PauseDurationMSec));
            LogServerGCAnalysis(serverGCActivityStatsFile, "GC thread ready to run at {0:n3}ms", gcReadyTime);

            foreach (var span in heap.SampleSpans)
            {
                //filtering out workspans that ended before GC actually started
                if (span.AbsoluteTimestampMsc + span.DurationMsc >= parent.PauseStartRelativeMSec)
                {
                    //Parent.Parent.LogServerGCAnalysis("CPU: {0:n1}->{1:n1}({2:n1}ms) from Process: {3}, thread {4}",
                    //    span.AbsoluteTimestampMsc, (span.AbsoluteTimestampMsc + span.DurationMsc),
                    //    span.DurationMsc,
                    //    span.ProcessName, span.ThreadId);

                    if ((heap.GcJoins.Count > 0) && (currentJoinEventIndex < heap.GcJoins.Count))
                    {
                        if (span.AbsoluteTimestampMsc > heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc)
                        {
                            while ((currentJoinEventIndex < heap.GcJoins.Count) &&
                                   (heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc < span.AbsoluteTimestampMsc))
                            {
                                currentThreadState = UpdateCurrentThreadState(serverGCActivityStatsFile, heap, heap.GcJoins[currentJoinEventIndex], currentThreadState);
                                //LogJoinInSpan(currentJoinEventIndex, currentThreadState);
                                currentJoinEventIndex++;
                            }
                        }

                        double spanEndTime = span.AbsoluteTimestampMsc + span.DurationMsc;

                        // We straddle a join event, update state and attribute the thread time. Note there can be multiple joins
                        // in this sample.
                        if ((currentJoinEventIndex < heap.GcJoins.Count) && (spanEndTime > heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc))
                        {
                            double lastStateEndTime = ((span.AbsoluteTimestampMsc < parent.PauseStartRelativeMSec) ?
                                                       parent.PauseStartRelativeMSec : span.AbsoluteTimestampMsc);

                            while ((currentJoinEventIndex < heap.GcJoins.Count) &&
                                   (heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc < spanEndTime))
                            {
                                double currentStateDuration = heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc - lastStateEndTime;
                                UpdateActivityThreadTime(serverGCActivityStatsFile, heap, span, activityStats[(int)currentThreadState], currentStateDuration, currentThreadState);

                                currentThreadState = UpdateCurrentThreadState(serverGCActivityStatsFile, heap, heap.GcJoins[currentJoinEventIndex], currentThreadState);
                                //LogJoinInSpan(currentJoinEventIndex, currentThreadState);
                                lastStateEndTime = heap.GcJoins[currentJoinEventIndex].AbsoluteTimestampMsc;
                                currentJoinEventIndex++;
                            }

                            // Attribute the last part of the sample.
                            UpdateActivityThreadTime(serverGCActivityStatsFile, heap, span, activityStats[(int)currentThreadState], (spanEndTime - lastStateEndTime), currentThreadState);
                        }
                        else
                        {
                            double duration = ((span.AbsoluteTimestampMsc < parent.PauseStartRelativeMSec) ?
                                               (span.AbsoluteTimestampMsc + span.DurationMsc - parent.PauseStartRelativeMSec) :
                                               span.DurationMsc);

                            UpdateActivityThreadTime(serverGCActivityStatsFile, heap, span, activityStats[(int)currentThreadState], duration, currentThreadState);
                        }
                    }

                    if (currentThreadState != lastThreadState)
                    {
                        if (lastThreadState == ServerGCThreadState.State_WaitInJoin)
                        {
                            //Parent.Parent.LogServerGCAnalysis("last S: {0}, this S: {1}, GC thread ready to run at {2:n3}ms",
                            //    lastThreadState, currentThreadState, GcJoins[currentJoinEventIndex - 1].AbsoluteTimestampMsc);
                            gcReadyTime = heap.GcJoins[currentJoinEventIndex - 1].AbsoluteTimestampMsc;
                        }
                        lastThreadState = currentThreadState;
                    }

                    if (span.ThreadId == heap.GcWorkingThreadId)
                    {
                        lastGCSpanEndTime = span.AbsoluteTimestampMsc + span.DurationMsc;
                        //Parent.Parent.LogServerGCAnalysis("Updating last GC span end time to {0:n3}ms", lastGCSpanEndTime);
                    }

                    string color = Type2Color[span.Type];
                    int width = scale * (int)span.DurationMsc;
                    int x = scale * (int)(span.RelativeTimestampMsc);
                    if (x < 0)
                    {
                        width += x;
                        x = 0;
                    }

                    writer.WriteLine(string.Format("<rect x='{0}' y='2' width='{1}' height='30' style='fill:{2};' >", x, width, color));
                    writer.WriteLine(string.Format("<title>{0} (PID: {1} TIP: {2} Priority: {3} Timestamp:{4:0.00} Duration: {5}ms WR:{6})</title>",
                        span.ProcessName, span.ProcessId, span.ThreadId, span.Priority, span.AbsoluteTimestampMsc, (int)span.DurationMsc, span.WaitReason));
                    writer.WriteLine("</rect>");
                }
            }

            if (heap.GcJoins.Count > 0)
            {
                for (int i = 0; i < (int)ServerGCThreadState.State_Max; i++)
                {
                    ServerGCThreadStateInfo info = activityStats[i];
                    LogServerGCAnalysis(serverGCActivityStatsFile, "---------[State - {0}]", SGCThreadStateDesc[i]);
                    LogServerGCAnalysis(serverGCActivityStatsFile, "[S{0}] GC: {1:n3} ms", i, info.gcThreadRunningTime);
                    var otherThreads = from pair in info.otherThreadsRunningTime
                                       orderby pair.Value.runningTime descending
                                       select pair;

                    // This is the time from non GC threads.
                    double interferenceTime = 0;

                    foreach (KeyValuePair<int, OtherThreadInfo> item in otherThreads)
                    {
                        // If it's less than 1ms we don't bother to print it.
                        //if (item.Value.runningTime > 1)
                        LogServerGCAnalysis(serverGCActivityStatsFile, "Process {0,8}({1,10}): {2:n3} ms", item.Key, item.Value.processName, item.Value.runningTime);
                        interferenceTime += item.Value.runningTime;
                    }

                    if ((i != (int)ServerGCThreadState.State_WaitInJoin) && ((interferenceTime + info.gcThreadRunningTime) > 0.0))
                    {
                        LogServerGCAnalysis(serverGCActivityStatsFile, "[S{0}] Other threads took away {1:n2}% CPU from GC running time {2:n3}",
                            (ServerGCThreadState)i, (int)((interferenceTime * 100.0) / (interferenceTime + info.gcThreadRunningTime)), info.gcThreadRunningTime);
                    }
                }
            }
        }

        internal static void LogServerGCAnalysis(TextWriter serverGCActivityStatsFile, string format, params Object[] args)
        {
            if (serverGCActivityStatsFile != null)
            {
                serverGCActivityStatsFile.WriteLine(format, args);
            }
        }

        internal static void LogServerGCAnalysis(TextWriter serverGCActivityStatsFile, string msg)
        {
            if (serverGCActivityStatsFile != null)
            {
                serverGCActivityStatsFile.WriteLine(msg);
            }
        }

        private ServerGCThreadStateInfo[] activityStats;
        private double lastGCSpanEndTime;
        private double gcReadyTime; // When GC thread is ready to run.
        #endregion
    }

    public static class ClrStatsUsersGuide
    {
        public static string WriteUsersGuide(string inputFileName)
        {
            var usersGuideName = Path.ChangeExtension(Path.ChangeExtension(inputFileName, null), "usersGuide.html");
            if (!File.Exists(usersGuideName) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(usersGuideName)).TotalHours > 1)
            {
                File.Copy(Path.Combine(SupportFiles.SupportFileDir, "HtmlReportUsersGuide.htm"), usersGuideName, true);
            }

            return Path.GetFileName(usersGuideName);        // return the relative path
        }
    }
}
