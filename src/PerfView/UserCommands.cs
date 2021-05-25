using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;
using Microsoft.Diagnostics.Utilities;
using PerfView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Utilities;
using Address = System.UInt64;

#if !PERFVIEW_COLLECT
using Graphs;
using EventSources;
using PerfView.Dialogs;
using PerfView.GuiUtilities;
#endif

// This is an example use of the extensibility features.  
namespace PerfViewExtensibility
{
    /// <summary>
    /// Commands is an actual use of the extensibility functionality.   Normally a 'Commands'
    /// class is compiled into a user defined DLL.
    /// </summary>
    public class Commands : CommandEnvironment
    {
        // If you add new build-in commands you need to add lines to src\PerfView\SupportFiles\PerfVIew.xml.
        // This is the file that contains the help for the user commands.   If you don't update this
        // file, your new command will not have help.   
        //
        // This can be as simple as coping the PerfView.xml file from output directory to src\PerfView\SupportFiles.
        // HOwever you can do better than this by removing all 'method' entries that are not user commands
        // That is members of this class.   THis makes the file (and therefore PerfView.exe) smaller.  

        /// <summary>
        /// Save Thread stacks from a NetPerf file into a *.speedscope.json file.
        /// </summary>
        /// <param name="netPerfFileName">The ETL file to convert</param>
        public void NetperfToSpeedScope(string netPerfFileName)
        {
            string outputName = Path.ChangeExtension(netPerfFileName, ".speedscope.json");

            string etlxFileName = TraceLog.CreateFromEventPipeDataFile(netPerfFileName);
            using (var eventLog = new TraceLog(etlxFileName))
            {
                var startStopSource = new MutableTraceEventStackSource(eventLog);
                // EventPipe currently only has managed code stacks.
                startStopSource.OnlyManagedCodeStacks = true;

                var computer = new SampleProfilerThreadTimeComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
                computer.GenerateThreadTimeStacks(startStopSource);

                SpeedScopeStackSourceWriter.WriteStackViewAsJson(startStopSource, outputName);

                LogFile.WriteLine("[Converted {0} to {1}  Use https://www.speedscope.app/ to view.]", netPerfFileName, outputName);
            }
        }
#if false // TODO Ideally you don't need Linux Specific versions, and it should be based
          // on eventPipe.   You can delete after 1/2018
        public void LinuxGCStats(string traceFileName)
        {
            var options = new TraceLogOptions();
            options.ConversionLog = LogFile;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            string etlxFilePath = traceFileName + ".etlx";
            etlxFilePath = TraceLog.CreateFromLttngTextDataFile(traceFileName, etlxFilePath, options);

            TraceLog traceLog = new TraceLog(etlxFilePath);

            List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> processes = new List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
            using (var source = traceLog.Events.GetSource())
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                source.Process();
                foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                {
                    if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null)
                    {
                        processes.Add(proc);
                    }
                }
            }

            string outputFileName = traceFileName + ".gcStats.html";
            using (StreamWriter output = File.CreateText(outputFileName))
            {
                Stats.ClrStats.ToHtml(output, processes, outputFileName, "GCStats", Stats.ClrStats.ReportType.GC);
            }
        }

        public void LinuxJITStats(string traceFileName)
        {
            var options = new TraceLogOptions();
            options.ConversionLog = LogFile;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            string outputFileName = traceFileName + ".jitStats.html";
            string etlxFilePath = traceFileName + ".etlx";
            etlxFilePath = TraceLog.CreateFromLttngTextDataFile(traceFileName, etlxFilePath, options);

            TraceLog traceLog = new TraceLog(etlxFilePath);
            var source = traceLog.Events.GetSource();

            Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> jitStats = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
            Dictionary<int, List<object>> bgJitEvents = new Dictionary<int, List<object>>();

            // attach callbacks to grab background JIT events
            var clrPrivate = new ClrPrivateTraceEventParser(source);
            clrPrivate.ClrMulticoreJitCommon += delegate (Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate.MulticoreJitPrivateTraceData data)
            {
                if (!bgJitEvents.ContainsKey(data.ProcessID))
                {
                    bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                bgJitEvents[data.ProcessID].Add(data.Clone());
            };
            source.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
            {
                if (!bgJitEvents.ContainsKey(data.ProcessID))
                {
                    bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                bgJitEvents[data.ProcessID].Add(data.Clone());
            };

            // process the model
            Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
            source.Process();
            foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
            {
                if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null && !jitStats.ContainsKey(proc.ProcessID))
                {
                    jitStats.Add(proc.ProcessID, proc);
                }
            }

            using (TextWriter output = File.CreateText(outputFileName))
            {
                Stats.ClrStats.ToHtml(output, jitStats.Values.ToList(), outputFileName, "JITStats", Stats.ClrStats.ReportType.JIT, true);
            }
        }

#endif
#if !PERFVIEW_COLLECT
        /// <summary>
        /// Dump every event in 'etlFileName' (which can be a ETL file or an ETL.ZIP file), as an XML file 'xmlOutputFileName'
        /// If the output file name is not given, the input filename's extension is changed to '.etl.xml' and that is used. 
        /// 
        /// This command is particularly useful for EventSources, where you want to post-process the data in some other tool.  
        /// </summary>
        public void DumpEventsAsXml(string etlFileName, string xmlOutputFileName = null)
        {
            if (xmlOutputFileName == null)
                xmlOutputFileName = PerfViewFile.ChangeExtension(etlFileName, ".etl.xml");

            var eventCount = 0;
            using (var outputFile = File.CreateText(xmlOutputFileName))
            {
                using (var etlFile = OpenETLFile(etlFileName))
                {
                    var events = GetTraceEventsWithProcessFilter(etlFile);
                    var sb = new StringBuilder();

                    outputFile.WriteLine("<Events>");
                    foreach (TraceEvent _event in events)
                    {
                        sb.Clear();
                        _event.ToXml(sb);
                        outputFile.WriteLine(sb.ToString());
                        eventCount++;
                    }
                    outputFile.WriteLine("</Events>");
                }
            }
            LogFile.WriteLine("[Wrote {0} events to {1}]", eventCount, xmlOutputFileName);
        }

        /// <summary>
        /// Save the CPU stacks from 'etlFileName'.  If the /process qualifier is present use it to narrow what
        /// is put into the file to a single process.  
        /// </summary>
        public void SaveCPUStacks(string etlFileName, string processName = null)
        {
            using (var etlFile = OpenETLFile(etlFileName))
            {
                TraceProcess process = null;
                if (processName != null)
                {
                    process = etlFile.Processes.LastProcessWithName(processName);
                    if (process == null)
                        throw new ApplicationException("Could not find process named " + processName);
                }
                SaveCPUStacksForProcess(etlFile, process);
            }
        }

        /// <summary>
        /// Save the CPU stacks for a set of traces.
        /// 
        /// If 'scenario' is an XML file, it will be used as a configuration file.
        /// 
        /// Otherwise, 'scenario' must refer to a directory. All ETL files in that directory and
        /// any subdirectories will be processed according to the default rules.
        /// 
        /// Summary of config XML:      ([] used instead of brackets)
        ///     [ScenarioConfig]
        ///         [Scenarios files="*.etl" process="$1.exe" name="scenario $1" /]
        ///     [/ScenarioConfig]
        /// </summary>
        public void SaveScenarioCPUStacks(string scenario)
        {
            var startTime = DateTime.Now;
            int skipped = 0, updated = 0;

            Dictionary<string, ScenarioConfig> configs;
            var outputBuilder = new StringBuilder();
            string outputName = null;
            DateTime scenarioUpdateTime = DateTime.MinValue;
            var writerSettings = new XmlWriterSettings()
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = true
            };

            using (var outputWriter = XmlWriter.Create(outputBuilder, writerSettings))
            {
                if (scenario.EndsWith(".xml"))
                {
                    using (var reader = XmlReader.Create(scenario))
                    {
                        configs = DeserializeScenarioConfig(reader, outputWriter, LogFile, Path.GetDirectoryName(scenario));
                    }
                    outputName = Path.ChangeExtension(scenario, ".scenarioSet.xml");
                    scenarioUpdateTime = File.GetLastWriteTimeUtc(scenario);
                }
                else
                {
                    configs = new Dictionary<string, ScenarioConfig>();
                    var dirent = new DirectoryInfo(scenario);
                    foreach (var etl in dirent.EnumerateFiles("*.etl").Concat(dirent.EnumerateFiles("*.etl.zip")))
                    {
                        configs[PerfViewFile.ChangeExtension(etl.FullName, ".perfView.xml.zip")] = new ScenarioConfig(etl.FullName);
                    }

                    // Write default ScenarioSet.
                    outputWriter.WriteStartDocument();
                    outputWriter.WriteStartElement("ScenarioSet");
                    outputWriter.WriteStartElement("Scenarios");
                    outputWriter.WriteAttributeString("files", "*.perfView.xml.zip");
                    outputWriter.WriteEndElement();
                    outputWriter.WriteEndElement();

                    outputName = Path.Combine(scenario, "Default.scenarioSet.xml");
                }
            }

            if (configs.Count == 0)
            {
                throw new ApplicationException("No ETL files specified");
            }

            foreach (var configPair in configs)
            {
                var destFile = configPair.Key;
                var config = configPair.Value;
                var filename = config.InputFile;

                // Update if we've been written to since updateTime (max of file and scenario config write time).
                var updateTime = File.GetLastWriteTimeUtc(filename);
                if (scenarioUpdateTime > updateTime)
                    updateTime = scenarioUpdateTime;

                if (File.Exists(destFile) &&
                    File.GetLastWriteTimeUtc(destFile) >= scenarioUpdateTime)
                {
                    LogFile.WriteLine("[Skipping file {0}: up to date]", filename);
                    skipped++;
                    continue;
                }

                var etl = OpenETLFile(filename);
                TraceProcess processOfInterest;

                bool wildCard = false;
                if (config.ProcessFilter == null)
                {
                    processOfInterest = FindProcessOfInterest(etl);
                }
                else if (config.ProcessFilter == "*")
                {
                    processOfInterest = null;
                    wildCard = true;
                }
                else
                {
                    processOfInterest = null;
                    foreach (var process in etl.Processes)
                    {
                        if (config.StartTime <= process.StartTimeRelativeMsec &&
                            string.Compare(process.Name, config.ProcessFilter, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            processOfInterest = process;
                            break;
                        }
                    }
                }

                if (processOfInterest == null & !wildCard)
                    throw new ApplicationException("Process of interest could not be located for " + filename);


                FilterParams filter = new FilterParams();
                filter.StartTimeRelativeMSec = config.StartTime.ToString("R");
                filter.EndTimeRelativeMSec = config.EndTime.ToString("R");
                SaveCPUStacksForProcess(etl, processOfInterest, filter, destFile);
                LogFile.WriteLine("[File {0} updated]", filename);
                updated++;
            }

            // Regenerate scenario set if out-of-date.
            if (!scenario.EndsWith(".xml") || !File.Exists(outputName) ||
                File.GetLastWriteTimeUtc(outputName) < File.GetLastWriteTimeUtc(scenario))
            {
                LogFile.WriteLine("[Writing ScenarioSet file {0}]", outputName);
                File.WriteAllText(outputName, outputBuilder.ToString(), Encoding.UTF8);
            }
            var endTime = DateTime.Now;

            LogFile.WriteLine("[Scenario {3}: {0} generated, {1} up-to-date [{2:F3} s]]",
                updated, skipped, (endTime - startTime).TotalSeconds,
                Path.GetFileName(PerfViewFile.ChangeExtension(outputName, "")));
        }

        /// <summary>
        /// If there are System.Diagnostics.Tracing.EventSources that are logging data to the ETL file
        /// then there are manifests for each of these EventSources in event stream.  This method 
        /// dumps these to 'outputDirectory' (each manifest file is 'ProviderName'.manifest.xml)
        /// 
        /// If outputDirectory is not present, then the directory 'EtwManifests' in the same directory
        /// as the 'etlFileName' is used as the output directory.  
        /// If 'pattern' is present this is a .NET regular expression and only EventSources that match 
        /// the pattern will be output. 
        /// </summary>
        public void DumpEventSourceManifests(string etlFileName, string outputDirectory = null, string pattern = null)
        {
            if (outputDirectory == null)
                outputDirectory = Path.Combine(Path.GetDirectoryName(etlFileName), "ETWManifests");

            var etlFile = OpenETLFile(etlFileName);
            Directory.CreateDirectory(outputDirectory);
            int manifestCount = 0;
            foreach (var parser in etlFile.TraceLog.Parsers)
            {
                var asDynamic = parser as DynamicTraceEventParser;
                if (asDynamic != null)
                {
                    foreach (var provider in asDynamic.DynamicProviders)
                    {
                        if (pattern == null || Regex.IsMatch(provider.Name, pattern))
                        {
                            var filePath = Path.Combine(outputDirectory, provider.Name + ".manifest.xml");
                            LogFile.WriteLine("Creating manifest file {0}", filePath);
                            File.WriteAllText(filePath, provider.Manifest);
                            manifestCount++;
                        }
                    }
                }
            }
            LogFile.WriteLine("[Created {0} manifest files in {1}]", manifestCount, outputDirectory);
        }

        /// <summary>
        /// Generate a GCDumpFile of a JavaScript heap from ETW data in 'etlFileName'
        /// </summary>
        public void JSGCDumpFromETLFile(string etlFileName, string gcDumpOutputFileName = null)
        {
            if (gcDumpOutputFileName == null)
                gcDumpOutputFileName = Path.ChangeExtension(etlFileName, ".gcdump");

            // TODO FIX NOW retrieve the process name, ID etc.  
            var reader = new JavaScriptDumpGraphReader(LogFile);
            var memoryGraph = reader.Read(etlFileName);
            GCHeapDump.WriteMemoryGraph(memoryGraph, gcDumpOutputFileName);
            LogFile.WriteLine("[Wrote gcDump file {0}]", gcDumpOutputFileName);
        }

        /// <summary>
        /// Generate a GCDumpFile of a DotNet heap from ETW data in 'etlFileName', 
        /// need to have a V4.5.1 runtime (preferably V4.5.2) to have the proper events.    
        /// </summary>
        public void DotNetGCDumpFromETLFile(string etlFileName, string processNameOrId = null, string gcDumpOutputFileName = null)
        {
            if (gcDumpOutputFileName == null)
                gcDumpOutputFileName = PerfViewFile.ChangeExtension(etlFileName, ".gcdump");

            CommandProcessor.UnZipIfNecessary(ref etlFileName, LogFile);

            // TODO FIX NOW retrieve the process name, ID etc.  
            var reader = new DotNetHeapDumpGraphReader(LogFile);
            var memoryGraph = reader.Read(etlFileName, processNameOrId);
            GCHeapDump.WriteMemoryGraph(memoryGraph, gcDumpOutputFileName);
            LogFile.WriteLine("[Wrote gcDump file {0}]", gcDumpOutputFileName);
        }

        /// <summary>
        /// Pretty prints the raw .NET GC dump events (GCBulk*) with minimal processing as XML.   This is mostly
        /// useful for debugging, to see if the raw data sane if there is a question on why something is not showing
        /// up properly in a more user-friendly view.  
        /// </summary>
        /// <param name="etlFileName">The input ETW file containing the GC dump events</param>
        /// <param name="processId">The process to focus on.  0 (the default) says to pick the first process with Bulk GC events.</param>
        /// <param name="outputFileName">The output XML file.</param>
        public void DumpRawDotNetGCHeapEvents(string etlFileName, string processId = null, string outputFileName = null)
        {
            if (outputFileName == null)
                outputFileName = Path.ChangeExtension(etlFileName, ".rawEtwGCDump.xml");

            int proccessIdInt = 0;
            if (processId != null)
                proccessIdInt = int.Parse(processId);

            CommandProcessor.UnZipIfNecessary(ref etlFileName, LogFile);
            var typeLookup = new Dictionary<Address, string>(500);
            var events = new List<TraceEvent>();
            var edges = new List<GCBulkEdgeTraceData>();

            using (var source = new ETWTraceEventSource(etlFileName, TraceEventSourceType.MergeAll))
            using (TextWriter output = File.CreateText(outputFileName))
            {
                source.Clr.TypeBulkType += delegate (GCBulkTypeTraceData data)
                {
                    if (proccessIdInt == 0)
                        proccessIdInt = data.ProcessID;
                    if (proccessIdInt != data.ProcessID)
                        return;

                    output.WriteLine(" <TypeBulkType Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\"/>",
                        data.ProcessID, data.TimeStampRelativeMSec, data.Count);
                    for (int i = 0; i < data.Count; i++)
                    {
                        var typeData = data.Values(i);
                        typeLookup[typeData.TypeID] = typeData.TypeName;
                    }
                };
                source.Clr.GCBulkEdge += delegate (GCBulkEdgeTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    output.WriteLine(" <GCBulkEdge Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\"/>",
                        data.ProcessID, data.TimeStampRelativeMSec, data.Count);
                    edges.Add((GCBulkEdgeTraceData)data.Clone());
                };
                source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                source.Clr.GCBulkRootStaticVar += delegate (GCBulkRootStaticVarTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                source.Clr.GCBulkRootEdge += delegate (GCBulkRootEdgeTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                source.Clr.GCBulkRootConditionalWeakTableElementEdge += delegate (GCBulkRootConditionalWeakTableElementEdgeTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                source.Clr.GCBulkRootCCW += delegate (GCBulkRootCCWTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                source.Clr.GCBulkRCW += delegate (GCBulkRCWTraceData data)
                {
                    if (proccessIdInt != data.ProcessID)
                        return;
                    events.Add(data.Clone());
                };
                output.WriteLine("<HeapDumpEvents>");
                // Pass one process types and gather up interesting events.  
                source.Process();

                // Need to do these things after all the type events are processed. 
                foreach (var data in events)
                {
                    var node = data as GCBulkNodeTraceData;
                    if (node != null)
                    {
                        output.WriteLine(" <GCBulkNode Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                        data.ProcessID, data.TimeStampRelativeMSec, node.Count);
                        for (int i = 0; i < node.Count; i++)
                        {
                            var value = node.Values(i);
                            output.WriteLine("  <Node Type=\"{0}\" ObjectID=\"0x{1:x}\" Size=\"{2}\" EdgeCount=\"{3}\"/>",
                                typeName(typeLookup, value.TypeID), value.Address, value.Size, value.EdgeCount);

                            // TODO can show edges.   
                        }
                        output.WriteLine(" </GCBulkNode>");
                        continue;
                    }
                    var rootEdge = data as GCBulkRootEdgeTraceData;
                    if (rootEdge != null)
                    {
                        output.WriteLine(" <GCBulkRootEdge Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                        rootEdge.ProcessID, rootEdge.TimeStampRelativeMSec, rootEdge.Count);

                        for (int i = 0; i < rootEdge.Count; i++)
                        {
                            var value = rootEdge.Values(i);
                            output.WriteLine("  <RootEdge GCRootID=\"0x{0:x}\" ObjectID=\"0x{1:x}\" GCRootKind=\"{2}\" GCRootFlag=\"{3}\"/>",
                                 value.GCRootID, value.RootedNodeAddress, value.GCRootKind, value.GCRootFlag);
                        }
                        output.WriteLine(" </GCBulkRootEdge>");
                        continue;
                    }
                    var staticVar = data as GCBulkRootStaticVarTraceData;
                    if (staticVar != null)
                    {
                        output.WriteLine(" <GCBulkRootStaticVar Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                            staticVar.ProcessID, staticVar.TimeStampRelativeMSec, staticVar.Count);

                        for (int i = 0; i < staticVar.Count; i++)
                        {
                            var value = staticVar.Values(i);
                            output.WriteLine("  <StaticVar Type=\"{0}\" Name=\"{1}\" GCRootID=\"0x{2:x}\" ObjectID=\"0x{3:x}\"/>",
                                typeName(typeLookup, value.TypeID), XmlUtilities.XmlEscape(value.FieldName), value.GCRootID, value.ObjectID);

                        }
                        output.WriteLine(" </GCBulkRootStaticVar>");
                        continue;
                    }
                    var rcw = data as GCBulkRCWTraceData;
                    if (rcw != null)
                    {
                        output.WriteLine(" <GCBulkRCW Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                            rcw.ProcessID, rcw.TimeStampRelativeMSec, rcw.Count);
                        for (int i = 0; i < rcw.Count; i++)
                        {
                            var value = rcw.Values(i);
                            output.WriteLine("  <RCW Type=\"{0}\" ObjectID=\"0x{1:x}\" IUnknown=\"0x{2:x}\"/>",
                                 typeName(typeLookup, value.TypeID), value.ObjectID, value.IUnknown);
                        }
                        output.WriteLine(" </GCBulkRCW>");
                        continue;
                    }
                    var ccw = data as GCBulkRootCCWTraceData;
                    if (ccw != null)
                    {
                        output.WriteLine(" <GCBulkRootCCW Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                            ccw.ProcessID, ccw.TimeStampRelativeMSec, ccw.Count);
                        for (int i = 0; i < ccw.Count; i++)
                        {
                            var value = ccw.Values(i);
                            output.WriteLine("  <RootCCW Type=\"{0}\" ObjectID=\"0x{1:x}\" IUnknown=\"0x{2:x}\"/>",
                                 typeName(typeLookup, value.TypeID), value.ObjectID, value.IUnknown);
                        }
                        output.WriteLine(" </GCBulkRootCCW>");
                        continue;
                    }
                    var condWeakTable = data as GCBulkRootConditionalWeakTableElementEdgeTraceData;
                    if (condWeakTable != null)
                    {
                        output.WriteLine(" <GCBulkRootConditionalWeakTableElementEdge Proc=\"{0}\" TimeMSec=\"{1:f3}\" Count=\"{2}\">",
                            condWeakTable.ProcessID, condWeakTable.TimeStampRelativeMSec, condWeakTable.Count);

                        for (int i = 0; i < condWeakTable.Count; i++)
                        {
                            var value = condWeakTable.Values(i);
                            output.WriteLine("  <ConditionalWeakTableElementEdge GCRootID=\"0x{0:x}\" GCKeyID=\"0x{1:x}\" GCValueID=\"0x{2:x}\"/>",
                                 value.GCRootID, value.GCKeyNodeID, value.GCValueNodeID);
                        }
                        output.WriteLine(" </GCBulkRootConditionalWeakTableElementEdge>");
                        continue;
                    }
                }
                output.WriteLine("</HeapDumpEvents>");
            }
            LogFile.WriteLine("[Wrote XML output for process {0} to file {1}]", processId, outputFileName);
        }

        private static string typeName(Dictionary<Address, string> types, Address typeId)
        {
            string ret;
            if (types.TryGetValue(typeId, out ret))
                return XmlUtilities.XmlEscape(ret);
            return "TypeID(0x" + typeId.ToString("x") + ")";
        }

        /// <summary>
        /// Dumps a GCDump file as XML.  Useful for debugging heap dumping issues.   It is easier to read than 
        /// what is produced by 'WriteGCDumpAsXml' but can't be read in with as a '.gcdump.xml' file.  
        /// </summary>
        /// <param name="gcDumpFileName"></param>
        public void DumpGCDumpFile(string gcDumpFileName)
        {
            var log = LogFile;
            var gcDump = new GCHeapDump(gcDumpFileName);

            Graph graph = gcDump.MemoryGraph;

            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   gcDumpFileName, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);

            var outputFileName = Path.ChangeExtension(gcDumpFileName, ".heapDump.xml");
            using (StreamWriter writer = File.CreateText(outputFileName))
                ((MemoryGraph)graph).DumpNormalized(writer);

            log.WriteLine("[File {0} dumped as {1}.]", gcDumpFileName, outputFileName);
        }

        /// <summary>
        /// Dumps a GCDump file as gcdump.xml file.  THese files can be read back by PerfView.   
        /// </summary>
        /// <param name="gcDumpFileName">The input file (.gcdump)</param>
        /// <param name="outputFileName">The output file name (defaults to input file with .gcdump.xml suffix)</param>
        public void WriteGCDumpAsXml(string gcDumpFileName, string outputFileName = null)
        {
            var log = LogFile;
            var gcDump = new GCHeapDump(gcDumpFileName);
            Graph graph = gcDump.MemoryGraph;
            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   gcDumpFileName, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);

            if (outputFileName == null)
                outputFileName = Path.ChangeExtension(gcDumpFileName, ".gcDump.xml");

            using (StreamWriter writer = File.CreateText(outputFileName))
                XmlGcHeapDump.WriteGCDumpToXml(gcDump, writer);

            log.WriteLine("[File {0} written as {1}.]", gcDumpFileName, outputFileName);
        }

        /// <summary>
        /// Given a name (or guid) of a provider registered on the system, generate a '.manifest.xml' file that 
        /// represents the manifest for that provider.   
        /// </summary>  
        public void DumpRegisteredManifest(string providerName, string outputFileName = null)
        {
            if (outputFileName == null)
                outputFileName = providerName + ".manifest.xml";

            var str = RegisteredTraceEventParser.GetManifestForRegisteredProvider(providerName);
            LogFile.WriteLine("[Output written to {0}]", outputFileName);
            File.WriteAllText(outputFileName, str);
        }

        /// <summary>
        /// Opens a text window that displays events from the given set of event source names
        /// By default the output goes to a GUI window but you can use the /LogFile option to 
        /// redirect it elsewhere.  
        /// </summary>
        /// <param name="etwProviderNames"> a comma separated list of providers specs (just like /Providers value)</param>
        public void Listen(string etwProviderNames)
        {
            var sessionName = "PerfViewListen";
            LogFile.WriteLine("Creating Session {0}", sessionName);
            using (var session = new TraceEventSession(sessionName))
            {
                TextWriter listenTextEditorWriter = null;
                if (!App.CommandLineArgs.NoGui)
                {
                    GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        var logTextWindow = new Controls.TextEditorWindow(GuiApp.MainWindow);
                        // Destroy the session when the widow is closed.  
                        logTextWindow.Closed += delegate (object sender, EventArgs e) { session.Dispose(); };

                        listenTextEditorWriter = new Controls.TextEditorWriter(logTextWindow.m_TextEditor);
                        logTextWindow.TextEditor.IsReadOnly = true;
                        logTextWindow.Title = "Listening to " + etwProviderNames;
                        logTextWindow.Show();
                    });
                }

                // Add callbacks for any EventSource Events to print them to the Text window
                Action<TraceEvent> onAnyEvent = delegate (TraceEvent data)
                {
                    try
                    {
                        String str = data.TimeStamp.ToString("HH:mm:ss.fff ");
                        str += data.EventName;
                        str += "\\" + data.ProviderName + " ";
                        for (int i = 0; i < data.PayloadNames.Length; i++)
                        {
                            var payload = data.PayloadNames[i];
                            if (i != 0)
                                str += ",";
                            str += String.Format("{0}=\"{1}\"", payload, data.PayloadByName(payload));
                        }

                        if (App.CommandLineArgs.NoGui)
                            App.CommandProcessor.LogFile.WriteLine("{0}", str);
                        else
                        {
                            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                            {
                                // This should be null because the BeginInvoke above came before this 
                                // and both are constrained to run in the same thread, so this has to
                                // be after it (and thus it is initialized).  
                                Debug.Assert(listenTextEditorWriter != null);
                                listenTextEditorWriter.WriteLine("{0}", str);
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        App.CommandProcessor.LogFile.WriteLine("Error: Exception during event processing of event {0}: {1}", data.EventName, e.Message);
                    }
                };

                session.Source.Dynamic.All += onAnyEvent;
                // Add support for EventWriteStrings (which are not otherwise parsable).  
                session.Source.UnhandledEvents += delegate (TraceEvent data)
                {
                    string formattedMessage = data.FormattedMessage;
                    if (formattedMessage != null)
                        listenTextEditorWriter.WriteLine("{0} {1} Message=\"{2}\"",
                            data.TimeStamp.ToString("HH:mm:ss.fff"), data.EventName, formattedMessage);
                };

                // Enable all the providers the users asked for

                var parsedProviders = ProviderParser.ParseProviderSpecs(etwProviderNames.Split(','), null, LogFile);
                foreach (var parsedProvider in parsedProviders)
                {
                    LogFile.WriteLine("Enabling provider {0}:{1:x}:{2}", parsedProvider.Name, (ulong)parsedProvider.MatchAnyKeywords, parsedProvider.Level);
                    session.EnableProvider(parsedProvider.Name, parsedProvider.Level, (ulong)parsedProvider.MatchAnyKeywords, parsedProvider.Options);
                }

                // Start listening for events.  
                session.Source.Process();
            }
        }

        /// <summary>
        /// Creates perfView.xml file that represents the directory size of 'directoryPath' and places
        /// it in 'outputFileName'.  
        /// </summary>
        /// <param name="directoryPath">The directory whose size is being computed (default to the current dir)</param>
        /// <param name="outputFileName">The output fileName (defaults to NAME.dirSize.PerfView.xml.zip) where NAME is
        /// the simple name of the directory.</param>
        public void DirectorySize(string directoryPath = null, string outputFileName = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                // Hop to the GUI thread and get the arguments from a dialog box and then call myself again.  
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    var dialog = new FileInputAndOutput(GuiApp.MainWindow, delegate (string dirPath, string outFileName)
                    {
                        App.CommandLineArgs.CommandAndArgs = new string[] { "DirectorySize", dirPath, outFileName };
                        App.CommandLineArgs.DoCommand = App.CommandProcessor.UserCommand;
                        GuiApp.MainWindow.ExecuteCommand("Computing directory size", App.CommandLineArgs.DoCommand);
                    });
                    dialog.SelectingDirectories = true;
                    dialog.OutputExtension = ".dirSize.perfView.xml.zip";
                    dialog.CurrentDirectory = GuiApp.MainWindow.CurrentDirectory.FilePath;
                    dialog.HelpAnchor = "DirectorySize";
                    dialog.Instructions = "Please enter the name of the directory on which to do a disk size analysis " +
                                          "and optionally the output file where place the resulting data.";
                    dialog.Title = "Disk Size Analysis";
                    dialog.Show();
                });
                return;
            }
            if (string.IsNullOrWhiteSpace(outputFileName))
            {
                if (char.IsLetterOrDigit(directoryPath[0]))
                    outputFileName = Path.GetFileNameWithoutExtension(Path.GetFullPath(directoryPath)) + ".dirSize.PerfView.xml.zip";
                else
                    outputFileName = "dirSize.PerfView.xml.zip";
            }

            LogFile.WriteLine("[Computing the file size of the directory {0}...]", directoryPath);
            // Open and close the output file to make sure we can write to it, that way we fail early if we can't
            File.OpenWrite(outputFileName).Close();
            File.Delete(outputFileName);

            FileSizeStackSource fileSizeStackSource = new FileSizeStackSource(directoryPath, LogFile);
            XmlStackSourceWriter.WriteStackViewAsZippedXml(fileSizeStackSource, outputFileName);
            LogFile.WriteLine("[Wrote file {0}]", outputFileName);

            if (!App.CommandLineArgs.NoGui && App.CommandLineArgs.LogFile == null)
            {
                if (outputFileName.EndsWith(".perfView.xml.zip", StringComparison.OrdinalIgnoreCase) && File.Exists(outputFileName))
                    GuiApp.MainWindow.OpenNext(outputFileName);
            }
        }

        /// <summary>
        /// Creates a .perfView.xml.zip that represents the profiling data from a perf script output dump. Adding a
        /// --threadtime tag enables blocked time investigations on the perf script dump.
        /// </summary>
        /// <param name="path">The path to the perf script dump, right now, either a file with suffix perf.data.dump,
        /// .trace.zip or .data.txt will be accepted.</param>
        /// <param name="threadTime">Option to turn on thread time on the perf script dump.</param>
        public void PerfScript(string path, string threadTime = null)
        {
            bool doThreadTime = threadTime != null && threadTime == "--threadtime";

            var perfScriptStackSource = new ParallelLinuxPerfScriptStackSource(path, doThreadTime);
            string outputFileName = Path.ChangeExtension(path, ".perfView.xml.zip");

            XmlStackSourceWriter.WriteStackViewAsZippedXml(perfScriptStackSource, outputFileName);

            if (!App.CommandLineArgs.NoGui && App.CommandLineArgs.LogFile == null)
            {
                if (outputFileName.EndsWith(".perfView.xml.zip", StringComparison.OrdinalIgnoreCase) && File.Exists(outputFileName))
                {
                    GuiApp.MainWindow.OpenNext(outputFileName);
                }
            }
        }

        /// <summary>
        /// Creates a stack source out of the textFileName where each line is a frame (which is directly rooted)
        /// and every such line has a metric of 1.  Thus it allows you to form histograms for these lines nicely
        /// in perfView.  
        /// </summary>
        /// <param name="textFilePath"></param>
        public void TextHistogram(string textFilePath)
        {
            LogFile.WriteLine("[Opening {0} as a Histogram]");
            var stackSource = new PerfView.OtherSources.TextStackSource();
            stackSource.Read(textFilePath);
            var stacks = new Stacks(stackSource);
            OpenStackViewer(stacks);
        }

        /// <summary>
        /// Reads a project N metaData.csv file (From ILC.exe)  and converts it to a .GCDump file (a heap)
        /// </summary>
        public void ProjectNMetaData(string projectNMetadataDataCsv)
        {
            var metaDataReader = new ProjectNMetaDataLogReader();
            var memoryGraph = metaDataReader.Read(projectNMetadataDataCsv);

            var outputName = Path.ChangeExtension(projectNMetadataDataCsv, ".gcdump");
            GCHeapDump.WriteMemoryGraph(memoryGraph, outputName);
            LogFile.WriteLine("[Writing the GCDump to {0}]", outputName);
        }

        /// <summary>
        /// This is used to visualize the Project N ILTransformed\*.reflectionlog.csv file so it can viewed 
        /// in PerfVIew.   
        /// </summary>
        /// <param name="reflectionLogFile">The name of the file to view</param>
        public void ReflectionUse(string reflectionLogFile)
        {
            LogFile.WriteLine("[Opening {0} as a Histogram]");
            var stackSource = new PerfView.OtherSources.TextStackSource();
            var lineNum = 0;

            stackSource.StackForLine = delegate (StackSourceInterner interner, string line)
            {
                lineNum++;
                StackSourceCallStackIndex ret = StackSourceCallStackIndex.Invalid;
                Match m = Regex.Match(line, "^(.*?),(.*?),\"(.*)\"");
                if (m.Success)
                {
                    string reflectionType = m.Groups[1].Value;
                    string entityKind = m.Groups[2].Value;
                    string symbol = m.Groups[3].Value;

                    if (entityKind == "Method" || entityKind == "Field")
                        symbol = Regex.Replace(symbol, "^.*?[^,] +", "");
                    ret = interner.CallStackIntern(interner.FrameIntern("REFLECTION " + reflectionType), ret);
                    ret = interner.CallStackIntern(interner.FrameIntern("KIND " + entityKind), ret);
                    ret = interner.CallStackIntern(interner.FrameIntern("SYM " + symbol), ret);
                }
                else
                    LogFile.WriteLine("Warning {0}: Could not parse {1}", lineNum, line);

                return ret;
            };
            stackSource.Read(reflectionLogFile);
            var stacks = new Stacks(stackSource);
            OpenStackViewer(stacks);
        }

        /// <summary>
        /// ImageSize generates a XML report (by default inputExeName.imageSize.xml) that 
        /// breaks down the executable file 'inputExeName' by the symbols in it (fetched from
        /// its PDB.  The PDB needs to be locatable (either on the _NT_SYMBOL_PATH, or next to 
        /// the file, or in its original build location).   This report can be viewed with
        /// PerfView (it looks like a GC heap).  
        /// </summary>
        /// <param name="inputExeName">The name of the EXE (or DLL) that you wish to analyze.  If blank it will prompt for one.</param>
        /// <param name="outputFileName">The name of the report file.  Defaults to the inputExeName
        /// with a .imageSize.xml suffix.</param>
        public void ImageSize(string inputExeName = null, string outputFileName = null)
        {
            if (outputFileName == null)
                outputFileName = Path.ChangeExtension(inputExeName, ".imageSize.xml");

            if (string.IsNullOrWhiteSpace(inputExeName))
            {
                if (App.CommandLineArgs.NoGui)
                    throw new ApplicationException("Must specify an input EXE name");
                // Hop to the GUI thread and get the arguments from a dialog box and then call myself again.  
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    var dialog = new FileInputAndOutput(GuiApp.MainWindow, delegate (string inExeName, string outFileName)
                    {
                        App.CommandLineArgs.CommandAndArgs = new string[] { "ImageSize", inExeName, outFileName };
                        App.CommandLineArgs.DoCommand = App.CommandProcessor.UserCommand;
                        GuiApp.MainWindow.ExecuteCommand("Computing directory size", App.CommandLineArgs.DoCommand);
                    });
                    dialog.InputExtentions = new string[] { ".dll", ".exe" };
                    dialog.OutputExtension = ".imageSize.xml";
                    dialog.CurrentDirectory = GuiApp.MainWindow.CurrentDirectory.FilePath;
                    dialog.HelpAnchor = "ImageSize";
                    dialog.Instructions = "Please enter the name of the EXE or DLL on which you wish to do a size analysis " +
                                          "and optionally the output file where place the resulting data.";
                    dialog.Title = "Image Size Analysis";
                    dialog.Show();
                });
                return;
            }

            string pdbScopeExe = Path.Combine(ExtensionsDirectory, "PdbScope.exe");
            if (!File.Exists(pdbScopeExe))
                throw new ApplicationException(@"The PerfViewExtensions\PdbScope.exe file does not exit.   ImageSize report not possible");

            // Currently we need to find the DLL again to unmangle names completely, and this DLL name is emedded in the output file.
            // Remove relative paths and try to make it universal so that you stand the best chance of finding this DLL.   
            inputExeName = App.MakeUniversalIfPossible(Path.GetFullPath(inputExeName));

            string commandLine = string.Format("{0} /x /f /s {1}", pdbScopeExe, Command.Quote(inputExeName));
            LogFile.WriteLine("Running command {0}", commandLine);

            FileUtilities.ForceDelete(outputFileName);
            Command.Run(commandLine, new CommandOptions().AddOutputStream(LogFile).AddTimeout(3600000));

            if (!File.Exists(outputFileName) || File.GetLastWriteTimeUtc(outputFileName) <= File.GetLastWriteTimeUtc(inputExeName))
            {
                // TODO can remove after pdbScope gets a proper outputFileName parameter
                string pdbScopeOutputFile = Path.ChangeExtension(Path.GetFullPath(Path.GetFileName(inputExeName)), ".pdb.xml");
                if (!File.Exists(pdbScopeOutputFile))
                    throw new ApplicationException("Error PdbScope did not create a file " + pdbScopeOutputFile);
                LogFile.WriteLine("Moving {0} to {1}", pdbScopeOutputFile, outputFileName);
                FileUtilities.ForceMove(pdbScopeOutputFile, outputFileName);
            }

            // TODO This is pretty ugly.  If the main window is working we can't launch it.   
            if (!App.CommandLineArgs.NoGui && App.CommandLineArgs.LogFile == null)
            {
                if (outputFileName.EndsWith(".imageSize.xml", StringComparison.OrdinalIgnoreCase) && File.Exists(outputFileName))
                    GuiApp.MainWindow.OpenNext(outputFileName);
            }
        }

        /// <summary>
        /// Dumps the PDB signature associated with pdb 'pdbName'
        /// </summary>
        public void PdbSignature(string pdbFileName)
        {
            var reader = new SymbolReader(LogFile);
            var module = reader.OpenSymbolFile(pdbFileName);
            LogFile.WriteLine("[{0} has Signature {1}]", pdbFileName, module.PdbGuid);
            OpenLog();
        }

#if false
        /// <summary>
        /// Mainly here for testing
        /// </summary>
        /// <param name="dllName"></param>
        /// <param name="ILPdb"></param>
        public void LookupSymbolsFor(string dllName, string ILPdb = null)
        {
            var symbolReader = App.GetSymbolReader();
            string ret = symbolReader.FindSymbolFilePathForModule(dllName, (ILPdb ?? "false") == "true");
            if (ret != null)
                LogFile.WriteLine("[Returned PDB {0}]", ret);
            else
                LogFile.WriteLine("[Could not find PDB for {0}]", dllName);
        }
#endif

        public void LookupSymbols(string pdbFileName, string pdbGuid, string pdbAge)
        {
            var symbolReader = App.GetSymbolReader();
            string ret = symbolReader.FindSymbolFilePath(pdbFileName, Guid.Parse(pdbGuid), int.Parse(pdbAge));
            if (ret != null)
                LogFile.WriteLine("[Returned PDB {0}]", ret);
            else
                LogFile.WriteLine("[Could not find PDB for {0}/{1}/{2}]", pdbFileName, pdbGuid, pdbAge);
        }

        class CodeSymbolListener
        {
            public CodeSymbolListener(TraceEventDispatcher source, string targetSymbolCachePath)
            {
                m_symbolFiles = new Dictionary<long, CodeSymbolState>();
                m_targetSymbolCachePath = targetSymbolCachePath;

                source.Clr.AddCallbackForEvents<ModuleLoadUnloadTraceData>(OnModuleLoad);
                source.Clr.AddCallbackForEvents<CodeSymbolsTraceData>(OnCodeSymbols);
            }

        #region private
            private void OnModuleLoad(ModuleLoadUnloadTraceData data)
            {
                Put(data.ProcessID, data.ModuleID, new CodeSymbolState(data, m_targetSymbolCachePath));
            }

            private void OnCodeSymbols(CodeSymbolsTraceData data)
            {
                CodeSymbolState state = Get(data.ProcessID, data.ModuleId);
                if (state != null)
                    state.OnCodeSymbols(data);
            }

            class CodeSymbolState
            {
                string m_pdbIndexPath;
                MemoryStream m_stream;
                private ModuleLoadUnloadTraceData m_moduleData;
                string m_symbolCachePath;

                public CodeSymbolState(ModuleLoadUnloadTraceData data, string path)
                {
                    // See Symbols/Symbolreader.cs for details on making Symbols server paths.   Here is the jist
                    // pdbIndexPath = pdbSimpleName + @"\" + pdbIndexGuid.ToString("N") + pdbIndexAge.ToString() + @"\" + pdbSimpleName;

                    // TODO COMPLETE
                    m_moduleData = data;
                    m_stream = new MemoryStream();
                    m_symbolCachePath = path;

                    string pdbSimpleName = data.ModuleILFileName.Replace(".exe", ".pdb").Replace(".dll", ".pdb");
                    if (!pdbSimpleName.EndsWith(".pdb"))
                    {
                        pdbSimpleName += ".pdb";
                    }
                    m_pdbIndexPath = pdbSimpleName + @"\" +
                        data.ManagedPdbSignature.ToString("N") + data.ManagedPdbAge.ToString() + @"\" + pdbSimpleName;
                }

                public void OnCodeSymbols(CodeSymbolsTraceData data)
                {
                    // TODO read in a chunk if it is out of order fail, when complete close the file.
                    //using (StreamWriter writer = File.WriteAllBytes(m_pdbIndexPath, m_bytes))
                    //    ((MemoryGraph)graph).DumpNormalized(writer);

                    //Assumes the length of the stream does not exceed 2^32
                    m_stream.Write(data.Chunk, 0, data.ChunkLength);
                    if ((data.ChunkNumber + 1) == data.TotalChunks)
                    {
                        byte[] bytes = new byte[m_stream.Length];
                        m_stream.Seek(0, SeekOrigin.Begin);
                        m_stream.Read(bytes, 0, (int)m_stream.Length);
                        string fullPath = m_symbolCachePath + @"\" + m_pdbIndexPath;
                        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                        }
                        File.WriteAllBytes(fullPath, bytes);
                    }
                }
            }

            // hides details of how process/module IDs are looked up.  
            CodeSymbolState Get(int processID, long moduleID)
            {
                CodeSymbolState ret = null;
                m_symbolFiles.TryGetValue((((long)processID) << 48) + moduleID, out ret);
                return ret;
            }
            void Put(int processID, long moduleID, CodeSymbolState value)
            {
                m_symbolFiles[(((long)processID) << 48) + moduleID] = value;
            }

            // Indexed by key;
            Dictionary<long, CodeSymbolState> m_symbolFiles;
            string m_targetSymbolCachePath;
        #endregion
        }

        /// <summary>
        /// Listen for the CLR CodeSymbols events and when you find them write them 
        /// to the directory targetSymbolCachePath using standard symbol server conventions
        /// (Name.Pdb\GUID-AGE\Name.Pdb)
        /// 
        /// Usage 
        /// </summary>
        /// <param name="targetSymbolCachePath"></param>
        public void GetDynamicAssemblySymbols(string targetSymbolCachePath)
        {
            var sessionName = "PerfViewSymbolListener";
            LogFile.WriteLine("Creating Session {0}", sessionName);
            using (var session = new TraceEventSession(sessionName))
            {
                var codeSymbolListener = new CodeSymbolListener(session.Source, targetSymbolCachePath);
                LogFile.WriteLine("Enabling CLR Loader and CodeSymbols events");
                session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                    (long)(ClrTraceEventParser.Keywords.Codesymbols | ClrTraceEventParser.Keywords.Loader));
                session.Source.Process();
            }
        }

        /// <summary>
        /// Given an NGEN image 'ngenImagePath' create a 'heap' description of what is
        /// in the NGEN image (where the metric is size).  
        /// </summary>
        /// <param name="ngenImagePath"></param>
        public void NGenImageSize(string ngenImagePath)
        {
            SymbolReader symReader = App.GetSymbolReader();
            MemoryGraph imageGraph = ImageFileMemoryGraph.Create(ngenImagePath, symReader);

            var fileName = Path.GetFileNameWithoutExtension(ngenImagePath);
            var outputFileName = fileName + ".gcdump";
            GCHeapDump.WriteMemoryGraph(imageGraph, outputFileName);
            LogFile.WriteLine("[Wrote file " + outputFileName + "]");

            if (!App.CommandLineArgs.NoGui && App.CommandLineArgs.LogFile == null)
                GuiApp.MainWindow.OpenNext(outputFileName);
        }

        /// <summary>
        /// Computes the GCStats HTML report for etlFile.  
        /// </summary>
        public void GCStats(string etlFile)
        {
            CommandProcessor.UnZipIfNecessary(ref etlFile, LogFile);

            List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> processes = new List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
            using (var source = new ETWTraceEventSource(etlFile))
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                source.Process();
                foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                    if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null) processes.Add(proc);
            }

            var outputFileName = Path.ChangeExtension(etlFile, ".gcStats.html");
            using (var output = File.CreateText(outputFileName))
            {
                LogFile.WriteLine("Wrote GCStats to {0}", outputFileName);
                Stats.ClrStats.ToHtml(output, processes, outputFileName, "GCStats", Stats.ClrStats.ReportType.GC);
                foreach (Microsoft.Diagnostics.Tracing.Analysis.TraceProcess proc in processes)
                {
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc);
                    if (mang != null)
                    {
                        var csvName = Path.ChangeExtension(etlFile, ".gcStats." + proc.ProcessID.ToString() + ".csv");
                        LogFile.WriteLine("  Wrote CsvFile {0}", csvName);
                        Stats.GcStats.ToCsv(csvName, mang);
                    }
                }
            }
            if (!App.CommandLineArgs.NoGui)
                OpenHtmlReport(outputFileName, "GCStats report");
        }

        /// <summary>
        /// Outputs some detailed Server GC analysis to a file.
        /// </summary>
        public void ServerGCReport(string etlFile)
        {
            if (PerfView.AppLog.InternalUser)
            {
                CommandProcessor.UnZipIfNecessary(ref etlFile, LogFile);

                List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> gcStats = new List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
                using (TraceLog tracelog = TraceLog.OpenOrConvert(etlFile))
                {
                    using (var source = tracelog.Events.GetSource())
                    {
                        Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                        Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(source, proc => { proc.Log = tracelog; });
                        source.Process();
                        foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                            if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null) gcStats.Add(proc);
                    }
                }

                var outputFileName = Path.ChangeExtension(etlFile, ".gcStats.html");
                using (var output = File.CreateText(outputFileName))
                {
                    LogFile.WriteLine("Wrote GCStats to {0}", outputFileName);
                    Stats.ClrStats.ToHtml(output, gcStats, outputFileName, "GCStats", Stats.ClrStats.ReportType.GC, false, true /* do server report */);
                }
                if (!App.CommandLineArgs.NoGui)
                    OpenHtmlReport(outputFileName, "GCStats report");
            }
        }

        /// <summary>
        /// Computes the JITStats HTML report for etlFile.  
        /// </summary>
        public void JITStats(string etlFile)
        {
            CommandProcessor.UnZipIfNecessary(ref etlFile, LogFile);

            List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> jitStats = new List<Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>(); ;
            using (var source = new ETWTraceEventSource(etlFile))
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                source.Process();
                foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                    if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null) jitStats.Add(proc);
            }

            var outputFileName = Path.ChangeExtension(etlFile, ".jitStats.html");
            using (var output = File.CreateText(outputFileName))
            {
                LogFile.WriteLine("Wrote JITStats to {0}", outputFileName);
                Stats.ClrStats.ToHtml(output, jitStats, outputFileName, "JitStats", Stats.ClrStats.ReportType.JIT);
                foreach (var proc in jitStats)
                {
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc);

                    if (mang != null && mang.JIT.Stats().Interesting)
                    {
                        var csvName = Path.ChangeExtension(etlFile, ".jitStats." + proc.ProcessID.ToString() + ".csv");
                        LogFile.WriteLine("  Wrote CsvFile {0}", csvName);
                        Stats.JitStats.ToCsv(csvName, mang);
                    }
                }
            }
            if (!App.CommandLineArgs.NoGui)
                OpenHtmlReport(outputFileName, "JITStats report");
        }

        ///// <summary>
        ///// Given a PDB file, dump the source server information in the PDB file.
        ///// </summary>
        //public void DumpSourceServerStream(string pdbFile)
        //{
        //    SymbolReader reader = new SymbolReader(LogFile);
        //    SymbolModule module = reader.OpenSymbolFile(pdbFile);

        //    string srcsrvData = module.GetSrcSrvStream();
        //    if (srcsrvData == null)
        //        LogFile.WriteLine("[No source server information on {0}]", pdbFile);
        //    else
        //    {
        //        LogFile.WriteLine("Source Server Data for {0}", pdbFile);
        //        LogFile.Write(srcsrvData);
        //        LogFile.WriteLine();
        //        LogFile.WriteLine("[Source Server Data Dumped into log for {0}]", pdbFile);
        //    }
        //}

#if CROSS_GENERATION_LIVENESS
        public void CollectCrossGenerationLiveness(string processId, string generationToTrigger, string promotedBytes, string dumpFilePath)
        {
            // Convert the process id.
            int pid = Convert.ToInt32(processId);

            // Convert the generation to trigger.
            int gen = Convert.ToInt32(generationToTrigger);

            // Convert promoted bytes threshold.
            ulong promotedBytesThreshold = Convert.ToUInt64(promotedBytes);

            // Validate the input file name.
            string fileName = Path.GetFileName(dumpFilePath);
            if (string.IsNullOrEmpty(fileName) || !dumpFilePath.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Invalid GCDump file path.  The specified path must contain a file name that ends in .gcdump.");
            }

            HeapDumper.DumpGCHeapForCrossGenerationLiveness(pid, gen, promotedBytesThreshold, dumpFilePath, LogFile);
        }
#endif

        /// <summary>
        /// Gets the ETW keywords (bitset definitions of what can be turned on in the provider) for a given provider. 
        /// Currently does not work for EventSources 
        /// </summary>
        /// <param name="providerNameOrGuid">The name or GUID of the provider to look up</param>
        public void ListProviderKeywords(string providerNameOrGuid)
        {
            Guid providerGuid;
            if (Regex.IsMatch(providerNameOrGuid, "........-....-....-....-............"))
            {
                if (!Guid.TryParse(providerNameOrGuid, out providerGuid))
                    throw new ApplicationException("Could not parse Guid '" + providerNameOrGuid + "'");
            }
            else if (providerNameOrGuid.StartsWith("*"))
                providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerNameOrGuid.Substring(1));
            else
            {
                providerGuid = TraceEventProviders.GetProviderGuidByName(providerNameOrGuid);
                if (providerGuid == Guid.Empty)
                    throw new ApplicationException("Could not find provider name '" + providerNameOrGuid + "'");
            }

            // TODO support eventSources
            LogFile.WriteLine("Keywords for the Provider {0} ({1})", providerNameOrGuid, providerGuid);
            List<ProviderDataItem> keywords = TraceEventProviders.GetProviderKeywords(providerGuid);
            foreach (ProviderDataItem keyword in keywords)
                LogFile.WriteLine("  {0,-30} {1,16:x} {2}", keyword.Name, keyword.Value, keyword.Description);
            OpenLog();
        }

        /// <summary>
        /// returns a list of providers that exist (can be enabled) in a particular process.  Currently EventSouces 
        /// are returned as GUIDs.  
        /// </summary>
        /// <param name="processNameOrId">The process name (exe without extension) or process ID of the process of interest.</param>
        public void ListProvidersInProcess(string processNameOrId)
        {
            var processID = HeapDumper.GetProcessID(processNameOrId);
            if (processID < 0)
                throw new ApplicationException("Could not find a process with a name or ID of '" + processNameOrId + "'");

            LogFile.WriteLine("Providers that can be enabled in process {0} ({1})", processNameOrId, processID);
            LogFile.WriteLine("EventSources are shown as GUIDs, GUIDS that are likely to be EventSources are marked with *.");
            var providersInProcess = TraceEventProviders.GetRegisteredProvidersInProcess(processID);
            SortAndPrintProviders(providersInProcess, true);
        }

        /// <summary>
        /// returns a list of all providers that have published their meta-data to the Operating system.  This does NOT
        /// include EventSources and is a long list.  Some of these are not actually active and thus will have no effect
        /// if they are enabled (see ListProvidersInProcess). 
        /// </summary>
        public void ListPublishedProviders()
        {
            var publishedProviders = new List<Guid>(TraceEventProviders.GetPublishedProviders());
            LogFile.WriteLine("All Providers Published to the Operating System");
            SortAndPrintProviders(publishedProviders);
        }

        private void SortAndPrintProviders(List<Guid> providers, bool markEventSources = false)
        {
            // Sort them by name
            providers.Sort(delegate (Guid x, Guid y)
            {
                return string.Compare(TraceEventProviders.GetProviderName(x), TraceEventProviders.GetProviderName(y), StringComparison.OrdinalIgnoreCase);
            });

            string mark = "";
            foreach (var providerGuid in providers)
            {
                if (markEventSources)
                    mark = TraceEventProviders.MaybeAnEventSource(providerGuid) ? "*" : " ";
                LogFile.WriteLine("  {0}{1,-39} {2}", mark, TraceEventProviders.GetProviderName(providerGuid), providerGuid);
            }
            OpenLog();
        }

        /// <summary>
        /// Fetch all the PDBs files needed for viewing 'etlFileName' locally.   If 'processName'
        /// is present we only fetch PDBs needed for that process.  This can be either a process
        /// name (exe without extension or path) or a decimal numeric ID.  
        /// </summary>
        public void FetchSymbolsForProcess(string etlFileName, string processName = null)
        {
            // Create a local symbols directory, and the normal logic will fill it.  
            var symbolsDir = Path.Combine(Path.GetDirectoryName(etlFileName), "symbols");
            if (!Directory.Exists(symbolsDir))
                Directory.CreateDirectory(symbolsDir);

            var etlFile = PerfViewFile.Get(etlFileName) as ETLPerfViewData;
            if (etlFile == null)
                throw new ApplicationException("FetchSymbolsForProcess only works on etl files.");

            TraceLog traceLog = etlFile.GetTraceLog(LogFile);
            TraceProcess focusProcess = null;
            foreach (var process in traceLog.Processes)
            {
                if (processName == null)
                {
                    if (process.StartTimeRelativeMsec > 0)
                    {
                        LogFile.WriteLine("Focusing on first process {0} ID {1}", process.Name, process.ProcessID);
                        focusProcess = process;
                        break;
                    }
                }
                else if (string.Compare(process.Name, processName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    LogFile.WriteLine("Focusing on named process {0} ID {1}", process.Name, process.ProcessID);
                    focusProcess = process;
                    break;
                }
            }
            if (focusProcess == null)
            {
                if (processName == null)
                    throw new ApplicationException("No process started in the trace.  Nothing to focus on.");
                else
                    throw new ApplicationException("Could not find a process named " + processName + ".");
            }

            // Lookup all the pdbs for all modules.  
            using (var symReader = etlFile.GetSymbolReader(LogFile))
            {
                foreach (var module in focusProcess.LoadedModules)
                    traceLog.CodeAddresses.LookupSymbolsForModule(symReader, module.ModuleFile);
            }
        }

#if false
        public void Test()
        {
            Cache<string, string> myCache = new Cache<string, string>(8);

            string prev = null;
            for(int i = 0; ;  i++)
            {
                var str = i.ToString() + (i*i).ToString(); 

                Trace.WriteLine(string.Format("**** BEFORE ITERATION {0} CACHE\r\n{1}********", i,  myCache.ToString()));
                if (i == 48)
                    break;

                Trace.WriteLine(string.Format("ADD {0}", str));
                myCache.Add(str, "Out" + str);

                if (i % 2 == 0) myCache.Get("00");

                Trace.WriteLine(string.Format("FETCH {0} = {1}", str, myCache.Get(str)));
                if (prev != null)
                    Trace.WriteLine(string.Format("FETCH {0} = {1}", prev, myCache.Get(prev)));
                prev = str;
            }
        }
#endif
        #region private
        /// <summary>
        /// Strips the file extension for files and if extension is .etl.zip removes both.
        /// </summary>
        private String StripFileExt(String inputFile)
        {
            string outputFileName = Path.ChangeExtension(inputFile, null);
            if (Path.GetExtension(outputFileName).CompareTo(".etl") == 0)
            {
                // In case extension was .etl.zip, remove both
                outputFileName = Path.ChangeExtension(outputFileName, null);
            }
            return outputFileName;
        }

        /// <summary>
        /// Gets the TraceEvents list of events from etlFile, applying a process filter if the /process argument is given. 
        /// </summary>
        private TraceEvents GetTraceEventsWithProcessFilter(ETLDataFile etlFile)
        {
            // If the user asked to focus on one process, do so.  
            TraceEvents events;
            if (CommandLineArgs.Process != null)
            {
                var process = etlFile.Processes.LastProcessWithName(CommandLineArgs.Process);
                if (process == null)
                    throw new ApplicationException("Could not find process named " + CommandLineArgs.Process);
                events = process.EventsInProcess;
            }
            else
                events = etlFile.TraceLog.Events;           // All events in the process.
            return events;
        }

        /// <summary>
        /// Save the CPU stacks for an ETL file into a perfView.xml.zip file.
        /// </summary>
        /// <param name="etlFile">The ETL file to save.</param>
        /// <param name="process">The process to save. If null, save all processes.</param>
        /// <param name="filter">The filter to apply to the stacks. If null, apply no filter.</param>
        /// <param name="outputName">The name of the file to output data to. If null, use the default.</param>
        private static void SaveCPUStacksForProcess(ETLDataFile etlFile, TraceProcess process = null, FilterParams filter = null, string outputName = null)
        {
            // Focus on a particular process if the user asked for it via command line args.
            if (process != null)
            {
                etlFile.SetFilterProcess(process);
            }

            if (filter == null)
                filter = new FilterParams();

            var stacks = etlFile.CPUStacks();
            stacks.Filter = filter;
            stacks.LookupWarmSymbols(10);           // Look up symbols (even on the symbol server) for modules with more than 50 inclusive samples
            stacks.GuiState.Notes = string.Format("Created by SaveCPUStacks from {0} on {1}", etlFile.FilePath, DateTime.Now);

            // Derive the output file name from the input file name.  
            var stackSourceFileName = PerfViewFile.ChangeExtension(etlFile.FilePath, ".perfView.xml.zip");
            stacks.SaveAsXml(outputName ?? stackSourceFileName);
            LogFile.WriteLine("[Saved {0} to {1}]", etlFile.FilePath, stackSourceFileName);
        }

        /// <summary>
        /// The configuration data for an ETL file dumped by SaveCPUStacksForProcess.
        /// </summary>
        private class ScenarioConfig
        {
            /// <summary>
            /// The file to read in.
            /// </summary>
            public readonly string InputFile;

            /// <summary>
            /// The name of the process of interest for the scenario.
            /// 
            /// If null, use heuristic detection.
            /// </summary>
            public readonly string ProcessFilter;
            /// <summary>
            /// The relative time to start taking samples from the ETL file.
            /// 
            /// Set to double.NegativeInfinity to take samples from the beginning.
            /// </summary>
            public readonly double StartTime;
            /// <summary>
            /// The time to stop taking samples from the ETL file.
            /// 
            /// Set to double.PositiveInfinity to take samples until the end.
            /// </summary>
            public readonly double EndTime;


            public ScenarioConfig(string inputFile, string processFilter, double startTime, double endTime)
            {
                ProcessFilter = processFilter;
                StartTime = startTime;
                EndTime = endTime;
                InputFile = inputFile;
            }

            public ScenarioConfig(string inputFile) : this(inputFile, null, double.NegativeInfinity, double.PositiveInfinity) { }
        }

        /// <summary>
        /// Parse a scenario config XML file, creating a mapping of filenames to file configurations.
        /// </summary>
        /// <param name="reader">The XmlReader to read config data from.</param>
        /// <param name="output">The XmlWriter to write a corresponding scenario-set definition to.</param>
        /// <param name="log">The log to write progress to.</param>
        /// <param name="baseDir">The base directory for relative path lookups.</param>
        /// <returns>A Dictionary mapping output filenames to ScenarioConfig objects holding configuration information.</returns>
        /// <remarks>
        /// Example config file:
        /// <ScenarioConfig>
        /// <Scenarios files="*.etl" />
        /// <Scenarios files="foo.etl.zip" name="scenario $1" process="bar" start="500" end="1000" />
        /// </ScenarioConfig>
        /// 
        /// Attributes on Scenarios element:
        /// - files (required)
        ///   The wildcard file pattern of ETL/ETL.ZIP files to include.
        /// - name (optional)
        ///   A pattern by which to name these scenarios. Passed through to scenario-set definition.
        /// - process (optional)
        ///   The name of the process of interest for this trace. If unset, the process of interest will be auto detected
        /// - start, end (optional)
        ///   The start and end times of the region of interest in the trace.
        /// - output (optional)
        ///   Specify name of output perfview.xml.zip.  This is needed to allow outputting different scenarios with the same etl file name to different file. 
        /// </remarks>
        private Dictionary<string, ScenarioConfig> DeserializeScenarioConfig(XmlReader reader, XmlWriter output, TextWriter log, string baseDir)
        {
            var config = new Dictionary<string, ScenarioConfig>();

            if (!reader.ReadToDescendant("ScenarioConfig"))
                throw new ApplicationException("The file does not have a Scenario element");

            if (!reader.ReadToDescendant("Scenarios"))
                throw new ApplicationException("No scenarios specified");

            output.WriteStartDocument();
            output.WriteStartElement("ScenarioSet");

            var defaultRegex = new Regex(@"(.*)", RegexOptions.IgnoreCase);

            do
            {
                string filePattern = reader["files"];
                string namePattern = reader["name"];
                string processPattern = reader["process"];
                string startTimeText = reader["start"];
                string endTimeText = reader["end"];
                string outputPattern = reader["output"];

                if (filePattern == null)
                    throw new ApplicationException("File pattern is required.");

                if (!filePattern.EndsWith(".etl") && !filePattern.EndsWith(".etl.zip"))
                    throw new ApplicationException("Files must be ETL files.");

                string replacePattern = Regex.Escape(filePattern)
                                        .Replace(@"\*", @"([^\\]*)")
                                        .Replace(@"\?", @"[^\\]");

                double startTime =
                    (startTimeText != null) ?
                        double.Parse(startTimeText) :
                        double.NegativeInfinity;

                double endTime =
                    (endTimeText != null) ?
                        double.Parse(endTimeText) :
                        double.PositiveInfinity;

                string pattern = Path.GetFileName(filePattern);
                string dir = Path.GetDirectoryName(filePattern);

                // Tack on the base directory if we're not already an absolute path.
                if (!Path.IsPathRooted(dir))
                    dir = Path.Combine(baseDir, dir);

                var replaceRegex = new Regex(replacePattern, RegexOptions.IgnoreCase);

                foreach (string file in Directory.GetFiles(dir, pattern))
                {
                    string process = null;
                    string outputName = null;
                    if (processPattern != null)
                    {
                        var match = replaceRegex.Match(file);

                        // We won't have a group to match if there were no wildcards in the pattern.
                        if (match.Groups.Count < 1)
                            match = defaultRegex.Match(PerfViewFile.GetFileNameWithoutExtension(file));

                        process = match.Result(processPattern);
                    }

                    if (outputPattern != null)
                    {
                        var match = replaceRegex.Match(file);

                        // We won't have a group to match if there were no wildcards in the pattern.
                        if (match.Groups.Count < 1)
                            match = defaultRegex.Match(PerfViewFile.GetFileNameWithoutExtension(file));

                        outputName = Path.Combine(Path.GetDirectoryName(file), match.Result(outputPattern));
                    }
                    else
                    {
                        outputName = PerfViewFile.ChangeExtension(file, "");
                    }

                    config[outputName + ".perfView.xml.zip"] = new ScenarioConfig(file, process, startTime, endTime);

                    log.WriteLine("Added {0}", file);
                }

                string outputWildcard;
                if (outputPattern != null)
                    outputWildcard = Regex.Replace(outputPattern, @"\$([0-9]+|[&`'+_$])", m => (m.Value == "$$" ? "$" : "*"));
                else
                    outputWildcard = PerfViewFile.ChangeExtension(filePattern, "");

                // Dump out scenario set data from the config input.
                output.WriteStartElement("Scenarios");
                output.WriteAttributeString("files", outputWildcard + ".perfView.xml.zip");
                if (namePattern != null)
                {
                    output.WriteAttributeString("namePattern", namePattern);
                }
                output.WriteEndElement();
            }
            while (reader.ReadToNextSibling("Scenarios"));

            output.WriteEndElement();
            output.WriteEndDocument();

            return config;
        }

        /// <summary>
        /// Heuristically find the process of interest for a given ETL trace.
        /// </summary>
        /// <param name="etlFile">The ETL file to search.</param>
        /// <returns>The "most interesting" process in the trace.</returns>
        private TraceProcess FindProcessOfInterest(ETLDataFile etlFile)
        {
            // THE HEURISTIC:
            // - First Win8 Store app to start.
            // *else*
            // - First app to start that runs for more than half the trace length.
            // *else*
            // - First app to start after the trace starts.

            double threshold = etlFile.TraceLog.SessionDuration.TotalMilliseconds * 0.5;

            // Find first Win8 store app if available.
            var isWin8StoreApp = etlFile.Processes.FirstOrDefault(IsStoreApp);

            if (isWin8StoreApp != null)
            {
                LogFile.WriteLine("Found Win 8 store app {0}", isWin8StoreApp.Name);
                return isWin8StoreApp;
            }


            // Find processes that started after tracing began.
            // This will exclude system processes, services, and many test harnesses.
            var startedInTrace = etlFile.Processes.Where(x => x.StartTimeRelativeMsec > 0.0);

            var retval = startedInTrace.FirstOrDefault(
                x => x.EndTimeRelativeMsec - x.StartTimeRelativeMsec >= threshold
            );

            if (retval != null)
            {
                LogFile.WriteLine("Found over-threshold app {0}", retval.Name);
            }
            else
            {
                retval = null;
                LogFile.WriteLine("Process of interest could not be determined automatically.");
            }
            return retval;
        }

        private static bool IsStoreApp(TraceProcess proc)
        {
            if (proc.StartTimeRelativeMsec == 0.0) return false;

            var startEvent = proc.EventsInProcess.ByEventType<ProcessTraceData>().First();

            return (startEvent.Flags & ProcessFlags.PackageFullName) != 0;
        }
        #endregion
#endif
    }
}

// TODO FIX NOW decide where to put these.
public static class TraceEventStackSourceExtensions
{
    public static StackSource CPUStacks(this TraceLog eventLog, TraceProcess process = null, CommandLineArgs commandLineArgs = null, bool showOptimizationTiers = false, Predicate<TraceEvent> predicate = null)
    {
        TraceEvents events;
        if (process == null)
        {
            events = eventLog.Events.Filter((x) => ((predicate == null) || predicate(x)) && x is SampledProfileTraceData && x.ProcessID != 0);
        }
        else
        {
            events = process.EventsInProcess.Filter((x) => ((predicate == null) || predicate(x)) && x is SampledProfileTraceData);
        }

        var traceStackSource = new TraceEventStackSource(events);
        if (commandLineArgs != null)
        {
            traceStackSource.ShowUnknownAddresses = commandLineArgs.ShowUnknownAddresses;
            traceStackSource.ShowOptimizationTiers = showOptimizationTiers || commandLineArgs.ShowOptimizationTiers;
        }
        // We clone the samples so that we don't have to go back to the ETL file from here on.  
        return CopyStackSource.Clone(traceStackSource);
    }
    public static MutableTraceEventStackSource ThreadTimeStacks(this TraceLog eventLog, TraceProcess process = null)
    {
        var stackSource = new MutableTraceEventStackSource(eventLog);
        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

        var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
        computer.ExcludeReadyThread = true;
        computer.GenerateThreadTimeStacks(stackSource);

        return stackSource;
    }

    public static MutableTraceEventStackSource ThreadTimeWithReadyThreadStacks(this TraceLog eventLog, TraceProcess process = null)
    {
        var stackSource = new MutableTraceEventStackSource(eventLog);
        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

        var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
        computer.GenerateThreadTimeStacks(stackSource);

        return stackSource;
    }
    public static MutableTraceEventStackSource ThreadTimeWithTasksStacks(this TraceLog eventLog, TraceProcess process = null)
    {
        // Use MutableTraceEventStackSource to disable activity tracing support
        var stackSource = new MutableTraceEventStackSource(eventLog);
        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;
        var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
        computer.UseTasks = true;
        computer.ExcludeReadyThread = true;
        computer.GenerateThreadTimeStacks(stackSource);

        return stackSource;
    }
    public static MutableTraceEventStackSource ThreadTimeWithTasksAspNetStacks(this TraceLog eventLog, TraceProcess process = null)
    {
        var stackSource = new MutableTraceEventStackSource(eventLog);
        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

        var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
        computer.UseTasks = true;
        computer.GroupByAspNetRequest = true;
        computer.ExcludeReadyThread = true;
        computer.GenerateThreadTimeStacks(stackSource);

        return stackSource;
    }
    public static MutableTraceEventStackSource ThreadTimeAspNetStacks(this TraceLog eventLog, TraceProcess process = null)
    {
        var stackSource = new MutableTraceEventStackSource(eventLog);
        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

        var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
        computer.ExcludeReadyThread = true;
        computer.GroupByAspNetRequest = true;
        computer.GenerateThreadTimeStacks(stackSource);

        return stackSource;
    }
}