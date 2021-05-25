// Copyright (c) Microsoft Corporation.  All rights reserved

using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Analysis.JIT;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Stats
{
    internal static class JitStats
    {
        public static void ToHtml(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, string fileName)
        {
            JITStatsEx statsEx = JITStatsEx.Create(runtime);

            var usersGuideFile = ClrStatsUsersGuide.WriteUsersGuide(fileName);
            bool hasInliningEvents = runtime.JIT.Stats().InliningSuccesses.Count > 0 || runtime.JIT.Stats().InliningFailures.Count > 0;

            writer.WriteLine("<H3><A Name=\"Stats_{0}\"><font color=\"blue\">JIT Stats for for Process {1,5}: {2}</font><A></H3>", stats.ProcessID, stats.ProcessID, stats.Name);
            writer.WriteLine("<UL>");
            {
                if (!string.IsNullOrEmpty(stats.CommandLine))
                {
                    writer.WriteLine("<LI>CommandLine: {0}</LI>", stats.CommandLine);
                }

                writer.WriteLine("<LI>Process CPU Time: {0:n0} msec</LI>", stats.CPUMSec);
                writer.WriteLine("<LI>Guidance on JIT data:");
                writer.WriteLine("<UL>");
                {
                    writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingJITPerf\">JIT Perf Users Guide</A></LI>", usersGuideFile);

                    if (runtime.JIT.Stats().BackgroundJitThread != 0 || runtime.JIT.Stats().BackgroundJitAbortedAtMSec != 0)
                    {
                        writer.WriteLine("<LI>Background JIT compilation (System.Runtime.ProfileOptimize) in use - <A HREF=\"{0}#UnderstandingBackgroundJIT\">Guide</A></LI>", usersGuideFile);
                        writer.WriteLine(" <UL>");

                        if (runtime.JIT.Stats().RecordedModules.Count == 0)
                        {
                            writer.WriteLine(" <LI><font color=\"red\">This trace is missing some background JIT events, which could result in incorrect information in the background JIT blocking reason column.</font></LI>");
                            writer.WriteLine(" <LI><font color=\"red\">Re-collect the trace enabling \"Background JIT\" events on the collection menu to fix this.</font></LI>");
                        }

                        if (runtime.JIT.Stats().BackgroundJitAbortedAtMSec != 0)
                        {
                            writer.WriteLine("  <LI><font color=\"red\">WARNING: Background JIT aborted at {0:n3} Msec</font></LI>", runtime.JIT.Stats().BackgroundJitAbortedAtMSec);
                            writer.WriteLine("  <LI>The last assembly before the abort was '{0}' loaded {1} at {2:n3}</LI>",
                                runtime.JIT.Stats().LastAssemblyLoadNameBeforeAbort, runtime.JIT.Stats().LastAssemblyLoadBeforeAbortSuccessful ? "successfully" : "unsuccessfully",
                                runtime.JIT.Stats().LastAssemblyLoadBeforeAbortMSec);
                        }

                        writer.WriteLine(" </UL>");
                    }
                    else if (runtime.JIT.Stats().BackgroundJitThread == 0)
                    {
                        if (runtime.JIT.Stats().BackgroundJITEventsOn)
                        {
                            writer.WriteLine("<LI>Background JIT compilation (System.Runtime.ProfileOptimize) not in use - <A HREF=\"{0}#UnderstandingBackgroundJIT\">Guide</A></LI>", usersGuideFile);
                            writer.WriteLine("<UL><LI>If there is a lot of JIT time enabling this may improve startup performance</LI></UL>");
                        }
                        else
                        {
                            writer.WriteLine("<LI>Background JIT compilation (System.Runtime.ProfileOptimize) events are not being collected - <A HREF=\"{0}#UnderstandingBackgroundJIT\">Guide</A></LI>", usersGuideFile);
                            writer.WriteLine("<UL><LI>If you are interested in seeing them enable the 'Background JIT' checkbox in the 'Advanced' section of the collection dialog when collecting the data.</LI></UL>");
                        }
                    }

                    if (!runtime.IsTieredCompilationEnabled)
                    {
                        writer.WriteLine("<LI>Tiered compilation not in use - <A HREF=\"{0}#UnderstandingTieredCompilation\">Guide</A></LI>", usersGuideFile);
                        writer.WriteLine("<UL><LI>On .Net Core, enabling this may improve application performance</LI></UL>");
                    }
                    else
                    {
                        writer.WriteLine("<LI>Tiered compilation in use - <A HREF=\"{0}#UnderstandingTieredCompilation\">Guide</A></LI>", usersGuideFile);
                    }
                }
                writer.WriteLine("</UL>");
                writer.WriteLine("</LI>");

                writer.WriteLine("<LI>Raw data:");
                writer.WriteLine("<UL>");
                {
                    writer.WriteLine("<LI>Individual JIT Events <A HREF=\"#Events_{0}\">Html</A> | <A HREF=\"command:excel/{0}\">Excel</A></LI>", stats.ProcessID);
                    if (hasInliningEvents)
                    {
                        writer.WriteLine("<LI>Inlining Decisions <A HREF=\"#Inlining_{0}\">Html</A> | <A HREF=\"command:excelInlining/{0}\">Excel</A></LI>", stats.ProcessID);
                    }
                    else
                    {
                        writer.WriteLine("<LI><I>No JIT Inlining data available.  Consider enabling the JITInlining option.</I></LI>");
                    }

                    if (runtime.JIT.Stats().BackgroundJitThread != 0 || runtime.JIT.Stats().BackgroundJitAbortedAtMSec != 0)
                    {
                        writer.WriteLine("<LI>Background Jit Diagnostics <A HREF=\"command:excelBackgroundDiag/{0}\">Excel</A></LI>", stats.ProcessID);
                    }
                }
                writer.WriteLine("</UL>");
                writer.WriteLine("</LI>");


                //
                // Summary table by trigger
                //

                writer.WriteLine("<LI> Summary of jitting time by trigger:");

                writer.WriteLine("<Center>");
                writer.WriteLine("<Table Border=\"1\">");
                writer.WriteLine("<TR>" +
                    "<TH Title=\"The reason why the JIT was invoked.\">Jitting Trigger</TH>" +
                    "<TH Title=\"The number of times the JIT was invoked\">Num Compilations</TH>" +
                    "<TH Title=\"The % of all compilations that triggered by this trigger\">% of total jitted compilations</TH>" +
                    "<TH Title=\"The total time used by all compilations with the given trigger\">Jit Time msec</TH>" +
                    "<TH Title=\"The total time used by all compilations with the given trigger as a % of total CPU time used for the process\">Jit Time (% of total process CPU)</TH>" +
                    "</TR>");
                writer.WriteLine(FormatThreadingModelTableRow("TOTAL", runtime.JIT.Stats().Count, runtime.JIT.Stats().TotalCpuTimeMSec, stats, runtime));
                writer.WriteLine(FormatThreadingModelTableRow(CompilationThreadKind.Foreground, runtime.JIT.Stats().CountForeground, runtime.JIT.Stats().TotalForegroundCpuTimeMSec, stats, runtime));
                writer.WriteLine(FormatThreadingModelTableRow(CompilationThreadKind.MulticoreJitBackground, runtime.JIT.Stats().CountBackgroundMultiCoreJit, runtime.JIT.Stats().TotalBackgroundMultiCoreJitCpuTimeMSec, stats, runtime));
                writer.WriteLine(FormatThreadingModelTableRow(CompilationThreadKind.TieredCompilationBackground, runtime.JIT.Stats().CountBackgroundTieredCompilation, runtime.JIT.Stats().TotalBackgroundTieredCompilationCpuTimeMSec, stats, runtime));

                writer.WriteLine("</Table>");
                writer.WriteLine("</Center>");
                writer.WriteLine("</LI>");

                //
                // Module table
                //

                // Sort the module list by Jit Time;
                List<string> moduleNames = new List<string>(statsEx.TotalModuleStats.Keys);
                moduleNames.Sort(delegate (string x, string y)
                {
                    double diff = statsEx.TotalModuleStats[y].TotalCpuTimeMSec - statsEx.TotalModuleStats[x].TotalCpuTimeMSec;
                    if (diff > 0)
                    {
                        return 1;
                    }
                    else if (diff < 0)
                    {
                        return -1;
                    }

                    return 0;
                });


                writer.WriteLine("<LI> Summary of jitting time by module:</P>");
                writer.WriteLine("<Center>");
                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>" +
                    "<TH Title=\"The name of the module\">Name</TH>" +
                    "<TH Title=\"The total CPU time spent jitting for all methods in this module\">JitTime<BR/>msec</TH>" +
                    "<TH Title=\"The number of times the JIT was invoked for methods in this module\">Num Compilations</TH>" +
                    "<TH Title=\"The total amount of IL processed by the JIT for all methods in this module\">IL Size</TH>" +
                    "<TH Title=\"The total amount of native code produced by the JIT for all methods in this module\">Native Size</TH>" +
                    "<TH Title=\"Time spent jitting synchronously to produce code for methods that were just invoked. These compilations often consume time at startup.\">" + GetLongNameForThreadClassification(CompilationThreadKind.Foreground) + "<BR/>msec</TH>" +
                    "<TH Title=\"Time spent jitting asynchronously to produce code for methods the runtime speculates will be invoked in the future.\">" + GetLongNameForThreadClassification(CompilationThreadKind.MulticoreJitBackground) + "<BR/>msec</TH>" +
                    "<TH Title=\"Time spent jitting asynchronously to produce code for methods that is more optimized than their initial code.\">" + GetLongNameForThreadClassification(CompilationThreadKind.TieredCompilationBackground) + "<BR/>msec</TH>");

                if (runtime.JIT.Stats().IsJitAllocSizePresent)
                {
                    writer.Write("<TH Title=\"The total amount of heap memory requested for the code produced by the JIT for all methods in this module.  \">JIT Hotcode request size</TH>");
                    writer.Write("<TH Title=\"The total amount of heap memory requested for the read-only data of code produced by the JIT for all methods in this module.  \">JIT RO-data request size</TH>");
                    writer.Write("<TH Title=\"The total amount of heap memory allocated for the code produced by the JIT for all methods in this module. \">Allocated size for JIT code</TH>");
                }

                writer.WriteLine("</TR>");

                string moduleTableRow = "<TR>" +
                    "<TD Align=\"Left\">{0}</TD>" +
                    "<TD Align=\"Center\">{1:n1}</TD>" +
                    "<TD Align=\"Center\">{2:n0}</TD>" +
                    "<TD Align=\"Center\">{3:n0}</TD>" +
                    "<TD Align=\"Center\">{4:n0}</TD>" +
                    "<TD Align=\"Center\">{5:n1}</TD>" +
                    "<TD Align=\"Center\">{6:n1}</TD>" +
                    "<TD Align=\"Center\">{7:n1}</TD>";
                
                writer.Write(moduleTableRow,
                    "TOTAL",
                    runtime.JIT.Stats().TotalCpuTimeMSec,
                    runtime.JIT.Stats().Count,
                    runtime.JIT.Stats().TotalILSize,
                    runtime.JIT.Stats().TotalNativeSize,
                    runtime.JIT.Stats().TotalForegroundCpuTimeMSec,
                    runtime.JIT.Stats().TotalBackgroundMultiCoreJitCpuTimeMSec,
                    runtime.JIT.Stats().TotalBackgroundTieredCompilationCpuTimeMSec);

                string allocSizeColumns = "";
                if (runtime.JIT.Stats().IsJitAllocSizePresent)
                {
                    allocSizeColumns +=
                       "<TD Align=\"Center\">{0:n0}</TD>" +
                       "<TD Align=\"Center\">{1:n0}</TD>" +
                       "<TD Align=\"Center\">{2:n0}</TD>";

                    writer.Write(allocSizeColumns,
                        runtime.JIT.Stats().TotalHotCodeAllocSize,
                        runtime.JIT.Stats().TotalRODataAllocSize,
                        runtime.JIT.Stats().TotalAllocSizeForJitCode);
                }
                writer.WriteLine();

                foreach (string moduleName in moduleNames)
                {
                    JITStats info = statsEx.TotalModuleStats[moduleName];
                    writer.Write(moduleTableRow,
                        moduleName.Length == 0 ? "&lt;UNKNOWN&gt;" : moduleName,
                        info.TotalCpuTimeMSec,
                        info.Count,
                        info.TotalILSize,
                        info.TotalNativeSize,
                        info.TotalForegroundCpuTimeMSec,
                        info.TotalBackgroundMultiCoreJitCpuTimeMSec,
                        info.TotalBackgroundTieredCompilationCpuTimeMSec);

                    if (runtime.JIT.Stats().IsJitAllocSizePresent)
                    {
                        writer.Write(allocSizeColumns,
                            info.TotalHotCodeAllocSize,
                            info.TotalRODataAllocSize,
                            info.TotalAllocSizeForJitCode);
                    }

                    writer.WriteLine("</TR>");

                }
                writer.WriteLine("</Table>");
                writer.WriteLine("</Center>");
                writer.WriteLine("</LI>");

            }
            writer.WriteLine("</UL>");

            bool backgroundJitEnabled = runtime.JIT.Stats().BackgroundJitThread != 0;

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_{0}\">Individual JIT Events for Process {1,5}: {2}<A></H4>", stats.ProcessID, stats.ProcessID, stats.Name);

            // We limit the number of JIT events we ut on the page because it makes the user exerience really bad (browsers crash)
            const int maxEvents = 1000;
            if (runtime.JIT.Methods.Count >= maxEvents)
            {
                writer.WriteLine("<p><Font color=\"red\">Warning: Truncating JIT events to " + maxEvents + ".  <A HREF=\"command:excel/{0}\">View in excel</A> to look all of them.</font></p>", stats.ProcessID);
            }

            bool showOptimizationTiers = ShouldShowOptimizationTiers(runtime);
            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.Write(
                "<TR>" +
                "<TH>Start<BR/>(msec)</TH>" +
                "<TH>Jit Time<BR/>(msec)</TH>" +
                "<TH>IL<BR/>Size</TH>" +
                "<TH>Native<BR/>Size</TH>");

            if (runtime.JIT.Stats().IsJitAllocSizePresent)
            {
                writer.Write(
                    "<TH>JIT Hotcode<BR/>request size</TH>" +
                    "<TH>JIT RO-data<BR/>request size</TH>" +
                    "<TH>Allocated size<BR/>for JIT code</TH>" +
                    "<TH>JIT Allocation<BR/>Flags</TH>"
                    );
            }

            if (showOptimizationTiers)
            {
                writer.Write("<TH Title=\"The optimization tier at which the method was jitted.\">Optimization<BR/>Tier</TH>");
            }
            writer.Write(
                "<TH>Method Name</TH>" +
                "<TH Title=\"Is Jit compilation occurring in the background for Multicore JIT (MC), in the background for tiered compilation (TC), or in the foreground on first execution of a method (FG).\">Trigger</TH>" +
                "<TH>Module</TH>");
            if (backgroundJitEnabled)
            {
                writer.Write(
                    "<TH Title=\"How far ahead of the method usage was relative to the background JIT operation.\">Distance Ahead</TH>" +
                    "<TH Title=\"Why the method was not JITTed in the background.\">Background JIT Blocking Reason</TH>");
            }
            writer.WriteLine("</TR>");
            int eventCount = 0;
            foreach (TraceJittedMethod _event in runtime.JIT.Methods)
            {
                writer.Write(
                    "<TR>" +
                    "<TD Align=\"Center\">{0:n3}</TD>" +
                    "<TD Align=\"Center\">{1:n1}</TD>" +
                    "<TD Align=\"Center\">{2:n0}</TD>" +
                    "<TD Align=\"Center\">{3:n0}</TD>",
                    _event.StartTimeMSec,
                    _event.CompileCpuTimeMSec,
                    _event.ILSize,
                    _event.NativeSize);

                if (_event.IsJitAllocSizePresent)
                {
                    writer.Write(
                        "<TD Align=\"Center\">{0}</TD>" +
                        "<TD Align=\"Center\">{1}</TD>" +
                        "<TD Align=\"Center\">{2}</TD>" +
                        "<TD Align=\"Center\">{3}</TD>",
                        _event.JitHotCodeRequestSize,
                        _event.JitRODataRequestSize,
                        _event.AllocatedSizeForJitCode,
                        _event.JitAllocFlag
                        );
                }

                if (showOptimizationTiers)
                {
                    writer.Write(
                        "<TD Align=\"Center\">{0}</TD>",
                        _event.OptimizationTier == OptimizationTier.Unknown ? string.Empty : _event.OptimizationTier.ToString());
                }
                writer.Write(
                    "<TD Align=Left>{0}</TD>" +
                    "<TD Align=\"Center\">{1}</TD>" +
                    "<TD Align=\"Center\">{2}</TD>",
                    _event.MethodName ?? "&nbsp;",
                    GetShortNameForThreadClassification(_event.CompilationThreadKind),
                    _event.ModuleILPath.Length != 0 ? Path.GetFileName(_event.ModuleILPath) : "&lt;UNKNOWN&gt;");
                if (backgroundJitEnabled)
                {
                    writer.Write(
                        "<TD Align=\"Center\">{0:n3}</TD>" +
                        "<TD Align=\"Left\">{1}</TD>",
                        _event.DistanceAhead,
                        _event.CompilationThreadKind == CompilationThreadKind.MulticoreJitBackground ? "Not blocked" : _event.BlockedReason);
                }
                writer.WriteLine("</TR>");
                eventCount++;
                if (eventCount >= maxEvents)
                {
                    break;
                }
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");

            if (hasInliningEvents)
            {
                writer.WriteLine("<HR/>");
                writer.WriteLine("<A Name=\"Inlining_{0}\">", stats.ProcessID);
                writer.WriteLine("<H4>Successful Inlinings for Process {0,5}: {1}<A></H4>", stats.ProcessID, stats.Name);
                writer.WriteLine("<Center>");
                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR><TH>Method Begin Compiled</TH><TH>Inliner</TH><TH>Inlinee</TH></TR>");
                foreach (InliningSuccessResult success in runtime.JIT.Stats().InliningSuccesses)
                {
                    writer.Write("<TR><TD>{0}</TD><TD>{1}</TD><TD>{2}</TD></TR>", success.MethodBeingCompiled, success.Inliner, success.Inlinee);
                }
                writer.WriteLine("</Table>");
                writer.WriteLine("</Center>");
                writer.WriteLine("<H4>Failed Inlinings for Process {0,5}: {1}<A></H4>", stats.ProcessID, stats.Name);
                writer.WriteLine("<Center>");
                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR><TH>Method Begin Compiled</TH><TH>Inliner</TH><TH>Inlinee</TH><TH>Failure Reason</TH></TR>");
                foreach (InliningFailureResult failure in runtime.JIT.Stats().InliningFailures)
                {
                    writer.Write("<TR><TD>{0}</TD><TD>{1}</TD><TD>{2}</TD><TD>{3}</TD></TR>", failure.MethodBeingCompiled, failure.Inliner, failure.Inlinee, failure.Reason);
                }
                writer.WriteLine("</Table>");
                writer.WriteLine("</Center>");
            }

            writer.WriteLine("<HR/><HR/><BR/><BR/>");
        }

        private static string FormatThreadingModelTableRow(CompilationThreadKind kind, long count, double jitTimeMsec, TraceProcess stats, TraceLoadedDotNetRuntime runtime)
        {
            return FormatThreadingModelTableRow(GetLongNameForThreadClassification(kind), count, jitTimeMsec, stats, runtime);
        }

        private static string FormatThreadingModelTableRow(string name, long count, double jitTimeMsec, TraceProcess stats, TraceLoadedDotNetRuntime runtime)
        {
            var countPercent = runtime.JIT.Stats().Count == 0 ? "-" : (count * 100.0 / runtime.JIT.Stats().Count).ToString("N1");
            var cpuPercent = stats.CPUMSec == 0 ? "-" : (jitTimeMsec * 100.0 / stats.CPUMSec).ToString("N1");
            return string.Format("<TR><TD Align =\"Left\">{0}</TD><TD Align=\"Center\">{1}</TD><TD Align=\"Center\">{2}</TD><TD Align=\"Center\">{3:F1}</TD><TD Align=\"Center\">{4}</TD></TR>",
                name, count, countPercent, jitTimeMsec, cpuPercent);
        }

        private static string GetShortNameForThreadClassification(CompilationThreadKind kind)
        {
            if (kind == CompilationThreadKind.Foreground)
            {
                return "FG";
            }
            else if (kind == CompilationThreadKind.MulticoreJitBackground)
            {
                return "MC";
            }
            else if (kind == CompilationThreadKind.TieredCompilationBackground)
            {
                return "TC";
            }
            else
            {
                throw new ArgumentException("Unknown CompilationThreadKind: " + kind);
            }
        }

        private static string GetLongNameForThreadClassification(CompilationThreadKind kind)
        {
            if (kind == CompilationThreadKind.Foreground)
            {
                return "Foreground";
            }
            else if (kind == CompilationThreadKind.MulticoreJitBackground)
            {
                return "Multicore JIT Background";
            }
            else if (kind == CompilationThreadKind.TieredCompilationBackground)
            {
                return "Tiered Compilation Background";
            }
            else
            {
                throw new ArgumentException("Unknown CompilationThreadKind: " + kind);
            }
        }

        private static bool ShouldShowOptimizationTiers(TraceLoadedDotNetRuntime runtime)
        {
            return PerfView.App.CommandLineArgs.ShowOptimizationTiers && runtime.HasAnyKnownOptimizationTier;
        }

        public static void ToCsv(string filePath, TraceLoadedDotNetRuntime runtime)
        {
            var listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            bool showOptimizationTiers = ShouldShowOptimizationTiers(runtime);
            using (var writer = File.CreateText(filePath))
            {
                writer.Write("Start MSec{0}JitTime MSec{0}ThreadID{0}IL Size{0}Native Size", listSeparator);
                if (runtime.JIT.Stats().IsJitAllocSizePresent)
                {
                    writer.Write("{0}JIT HotCode request size{0}JIT RO-data request size{0}Allocated size for JIT code{0}JIT Allocation Flag", listSeparator);
                }
                if (showOptimizationTiers)
                {
                    writer.Write("{0}OptimizationTier", listSeparator);
                }
                writer.WriteLine("{0}MethodName{0}Trigger{0}Module{0}DistanceAhead{0}BlockedReason", listSeparator);

                for (int i = 0; i < runtime.JIT.Methods.Count; i++)
                {
                    var _event = runtime.JIT.Methods[i];
                    var csvMethodName = _event.MethodName.Replace(",", " ");    // Insure there are no , in the name 

                    writer.Write(
                        "{1:f3}{0}{2:f3}{0}{3}{0}{4}{0}{5}",
                        listSeparator,
                        _event.StartTimeMSec,
                        _event.CompileCpuTimeMSec,
                        _event.ThreadID,
                        _event.ILSize,
                        _event.NativeSize);

                    if (_event.IsJitAllocSizePresent)
                    {
                        writer.Write("{0}{1}{0}{2}{0}{3}{0}{4}",
                            listSeparator,
                            _event.JitHotCodeRequestSize,
                            _event.JitRODataRequestSize,
                            _event.AllocatedSizeForJitCode,
                            _event.JitAllocFlag);
                    }

                    if (showOptimizationTiers)
                    {
                        writer.Write(
                            "{0}{1}",
                            listSeparator,
                            _event.OptimizationTier == OptimizationTier.Unknown
                                ? string.Empty
                                : _event.OptimizationTier.ToString());
                    }
                    writer.WriteLine(
                        "{0}{1}{0}{2}{0}{3}{0}{4}{0}{5}",
                        listSeparator,
                        csvMethodName,
                        GetShortNameForThreadClassification(_event.CompilationThreadKind),
                        _event.ModuleILPath,
                        _event.DistanceAhead,
                        _event.BlockedReason);
                }
            }
        }

        public static void ToInliningCsv(string filePath, TraceLoadedDotNetRuntime runtime)
        {
            var listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filePath))
            {
                writer.WriteLine("MethodBeginCompiled{0}Inliner{0}Inlinee{0}FailureReason", listSeparator);
                foreach (var ev in runtime.JIT.Stats().InliningSuccesses)
                {
                    writer.WriteLine("{1}{0}{2}{0}{3}{0}{4}", listSeparator,
                        ev.MethodBeingCompiled.Replace(listSeparator, ""),
                        ev.Inliner.Replace(listSeparator, ""),
                        ev.Inlinee.Replace(listSeparator, ""),
                        "<success>");
                }
                foreach (var ev in runtime.JIT.Stats().InliningFailures)
                {
                    writer.WriteLine("{1}{0}{2}{0}{3}{0}{4}", listSeparator,
                        ev.MethodBeingCompiled.Replace(listSeparator, ""),
                        ev.Inliner.Replace(listSeparator, ""),
                        ev.Inlinee.Replace(listSeparator, ""),
                        ev.Reason.Replace(listSeparator, ""));
                }
            }
        }

        /// <summary>
        /// Write data about background JIT activities to 'outputCsvFilePath'
        /// </summary>
        public static void BackgroundDiagCsv(string outputCsvFilePath, TraceLoadedDotNetRuntime stats, List<object> events)
        {
            var listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(outputCsvFilePath))
            {
                writer.WriteLine("MSec{0}Command{0}Arg1{0}Arg2{0}Arg3{0}Arg4{0}", listSeparator);
                for (int i = 0; i < events.Count; i++)
                {
                    var event_ = events[i];
                    var bgEvent = event_ as MulticoreJitPrivateTraceData;
                    if (bgEvent != null)
                    {
                        writer.WriteLine("{1:f3}{0}{2}{0}\"{3}\"{0}{4}{0}{5}{0}{6}", listSeparator,
                            bgEvent.TimeStampRelativeMSec, bgEvent.String1, bgEvent.String2, bgEvent.Int1, bgEvent.Int2, bgEvent.Int3);
                        continue;
                    }
                    var loadEvent = event_ as ModuleLoadUnloadTraceData;
                    if (loadEvent != null)
                    {
                        writer.WriteLine("{1:f3}{0}{2}{0}\"{3}\"{0}{4}{0}{5}{0}{6}", listSeparator,
                            loadEvent.TimeStampRelativeMSec, "MODULE_LOAD", Path.GetFileName(loadEvent.ModuleILPath), 0, 0, 0);
                        continue;
                    }
                }
            }
        }

        public static void ToXml(TextWriter writer, TraceProcess stats, TraceLoadedDotNetRuntime runtime, string indent)
        {
            JITStatsEx statsEx = JITStatsEx.Create(runtime);

            // TODO pay attention to indent;
            writer.Write(" <JitProcess Process=\"{0}\" ProcessID=\"{1}\" JitTimeMSec=\"{2:n3}\" Count=\"{3}\" ILSize=\"{4}\" NativeSize=\"{5}\"", stats.Name, stats.ProcessID, runtime.JIT.Stats().TotalCpuTimeMSec, runtime.JIT.Stats().Count, runtime.JIT.Stats().TotalILSize, runtime.JIT.Stats().TotalNativeSize);
            if (runtime.JIT.Stats().IsJitAllocSizePresent)
            {
                writer.Write("HotCodeAllocSize=\"{0}\" RODataAllocSize=\"{1}\" AllocSizeForJitCode=\"{2}\"", runtime.JIT.Stats().TotalHotCodeAllocSize, runtime.JIT.Stats().TotalRODataAllocSize, runtime.JIT.Stats().TotalAllocSizeForJitCode);
            }
            if (stats.CPUMSec != 0)
            {
                writer.Write(" ProcessCpuTimeMsec=\"{0}\"", stats.CPUMSec);
            }

            if (!string.IsNullOrEmpty(stats.CommandLine))
            {
                writer.Write(" CommandLine=\"{0}\"", XmlUtilities.XmlEscape(stats.CommandLine, false));
            }

            writer.WriteLine(">");
            writer.WriteLine("  <JitEvents>");
            bool showOptimizationTiers = ShouldShowOptimizationTiers(runtime);
            foreach (TraceJittedMethod _event in runtime.JIT.Methods)
            {
                ToXml(writer, _event, showOptimizationTiers);
            }

            writer.WriteLine("  </JitEvents>");

            writer.WriteLine(" <ModuleStats Count=\"{0}\" TotalCount=\"{1}\" TotalJitTimeMSec=\"{2:n3}\" TotalILSize=\"{3}\" TotalNativeSize=\"{4}\"  HotCodeAllocSize=\"{5}\" RODataAllocSize=\"{6}\" AllocSizeForJitCode=\"{7}\"",
                statsEx.TotalModuleStats.Count, runtime.JIT.Stats().Count, runtime.JIT.Stats().TotalCpuTimeMSec, runtime.JIT.Stats().TotalILSize, runtime.JIT.Stats().TotalNativeSize, runtime.JIT.Stats().TotalHotCodeAllocSize, runtime.JIT.Stats().TotalRODataAllocSize, runtime.JIT.Stats().TotalAllocSizeForJitCode);

            // Sort the module list by Jit Time;
            List<string> moduleNames = new List<string>(statsEx.TotalModuleStats.Keys);
            moduleNames.Sort(delegate (string x, string y)
            {
                double diff = statsEx.TotalModuleStats[y].TotalCpuTimeMSec - statsEx.TotalModuleStats[x].TotalCpuTimeMSec;
                if (diff > 0)
                {
                    return 1;
                }
                else if (diff < 0)
                {
                    return -1;
                }

                return 0;
            });

            foreach (string moduleName in moduleNames)
            {
                JITStats info = statsEx.TotalModuleStats[moduleName];
                writer.Write("<Module");
                writer.Write(" JitTimeMSec={0}", StringUtilities.QuotePadLeft(info.TotalCpuTimeMSec.ToString("n3"), 11));
                writer.Write(" Count={0}", StringUtilities.QuotePadLeft(info.Count.ToString(), 7));
                writer.Write(" ILSize={0}", StringUtilities.QuotePadLeft(info.TotalILSize.ToString(), 9));
                writer.Write(" NativeSize={0}", StringUtilities.QuotePadLeft(info.TotalNativeSize.ToString(), 9));

                if (info.IsJitAllocSizePresent)
                {
                    writer.Write(" HotCodeAllocSize={0}", StringUtilities.QuotePadLeft(info.TotalHotCodeAllocSize.ToString(), 9));
                    writer.Write(" RODataAllocSize={0}", StringUtilities.QuotePadLeft(info.TotalRODataAllocSize.ToString(), 9));
                    writer.Write(" RequestedAllocSizeForJitCode={0}", StringUtilities.QuotePadLeft(info.TotalAllocSizeForJitCode.ToString(), 9));
                }
                writer.Write(" Name=\"{0}\"", moduleName);
                writer.WriteLine("/>");
            }
            writer.WriteLine("  </ModuleStats>");

            writer.WriteLine(" </JitProcess>");
        }

        private static void ToXml(TextWriter writer, TraceJittedMethod info, bool showOptimizationTiers)
        {
            writer.Write("   <JitEvent");
            writer.Write(" StartMSec={0}", StringUtilities.QuotePadLeft(info.StartTimeMSec.ToString("n3"), 10));
            writer.Write(" JitTimeMSec={0}", StringUtilities.QuotePadLeft(info.CompileCpuTimeMSec.ToString("n3"), 8));
            writer.Write(" ILSize={0}", StringUtilities.QuotePadLeft(info.ILSize.ToString(), 10));
            writer.Write(" NativeSize={0}", StringUtilities.QuotePadLeft(info.NativeSize.ToString(), 10));

            if (info.IsJitAllocSizePresent)
            {
                writer.Write(" JITHotCodeRequestSize={0}", StringUtilities.QuotePadLeft(info.JitHotCodeRequestSize.ToString(), 10));
                writer.Write(" JITRODataRequestSize={0}", StringUtilities.QuotePadLeft(info.JitRODataRequestSize.ToString(), 10));
                writer.Write(" AllocSizeForJitCode={0}", StringUtilities.QuotePadLeft(info.AllocatedSizeForJitCode.ToString(), 10));
                writer.Write(" JitAllocFlag={0}", StringUtilities.QuotePadLeft(info.JitAllocFlag.ToString(), 10));
            }
            if (showOptimizationTiers)
            {
                writer.Write(
                    " OptimizationTier={0}",
                    XmlUtilities.XmlQuote(
                        info.OptimizationTier == OptimizationTier.Unknown ? string.Empty : info.OptimizationTier.ToString()));
            }
            if (info.MethodName != null)
            {
                writer.Write(" MethodName="); writer.Write(XmlUtilities.XmlQuote(info.MethodName));
            }
            writer.Write(
                " Trigger={0}",
                XmlUtilities.XmlQuote(GetShortNameForThreadClassification(info.CompilationThreadKind)));
            if (info.ModuleILPath != null)
            {
                writer.Write(" ModuleILPath="); writer.Write(XmlUtilities.XmlQuote(info.ModuleILPath));
            }
            writer.Write(" DistanceAhead={0}", StringUtilities.QuotePadLeft(info.DistanceAhead.ToString("n3"), 10));
            writer.Write(" BlockedReason="); writer.Write(XmlUtilities.XmlQuote(info.BlockedReason));
            writer.WriteLine("/>");
        }
    }

    internal class JITStatsEx
    {
        public JITStats TotalBGJITStats = new JITStats();
        public SortedDictionary<string, JITStats> TotalModuleStats = new SortedDictionary<string, JITStats>();

        public static JITStatsEx Create(TraceLoadedDotNetRuntime mang)
        {
            JITStatsEx stats = new JITStatsEx();

            if (mang.JIT.Stats().BackgroundJitThread != 0)
            {
                stats.TotalBGJITStats = new JITStats();
                foreach (TraceJittedMethod _method in mang.JIT.Methods)
                {
                    stats.TotalBGJITStats.AddMethodToStatistics(_method);
                }
            }

            // Compute module level stats, we do it here because we may not have the IL method name until late. 
            stats.TotalModuleStats = new SortedDictionary<string, JITStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var _method in mang.JIT.Methods)
            {
                if (_method.ModuleILPath != null)
                {
                    if (!stats.TotalModuleStats.ContainsKey(_method.ModuleILPath))
                    {
                        stats.TotalModuleStats.Add(_method.ModuleILPath, new JITStats());
                    }
                    JITStats moduleStats = stats.TotalModuleStats[_method.ModuleILPath];
                    moduleStats.AddMethodToStatistics(_method);
                }
            }

            return stats;
        }

    }
}
