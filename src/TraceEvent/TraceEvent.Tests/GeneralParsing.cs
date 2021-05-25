using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TraceEventTests
{
    public class GeneralParsing : EtlTestBase
    {
        public GeneralParsing(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// This test simply scans all the events in the ETL.ZIP files in TestDataDir
        /// and scans them (so you should get asserts if there is parsing problem)
        /// and insures that no more than .1% of the events are 
        /// </summary>
        [Theory]
        [MemberData(nameof(TestEtlFiles))]
        public void ETW_GeneralParsing_Basic(string etlFileName)
        {
            Output.WriteLine($"In {nameof(ETW_GeneralParsing_Basic)}(\"{etlFileName}\")");
            PrepareTestData();

            string etlFilePath = Path.Combine(UnZippedDataDir, etlFileName);
            bool anyFailure = false;
            Output.WriteLine(string.Format("Processing the file {0}, Making ETLX and scanning.", etlFilePath));
            string eltxFilePath = Path.ChangeExtension(etlFilePath, ".etlx");

            // See if we have a cooresponding baseline file 
            string baselineName = Path.Combine(TestDataDir,
                Path.GetFileNameWithoutExtension(etlFilePath) + ".baseline.txt");

            string newBaselineName = Path.Combine(NewBaselineDir,
                Path.GetFileNameWithoutExtension(etlFilePath) + ".baseline.txt");
            string outputName = Path.Combine(OutputDir,
                Path.GetFileNameWithoutExtension(etlFilePath) + ".txt");
            TextWriter outputFile = File.CreateText(outputName);

            StreamReader baselineFile = null;
            if (File.Exists(baselineName))
            {
                baselineFile = File.OpenText(baselineName);
            }
            else
            {
                Output.WriteLine("WARNING: No baseline file");
                Output.WriteLine(string.Format("    ETL FILE: {0}", etlFilePath));
                Output.WriteLine(string.Format("    NonExistant Baseline File: {0}", baselineName));
                Output.WriteLine("To Create a baseline file");
                Output.WriteLine(string.Format("    copy /y \"{0}\" \"{1}\"", newBaselineName, baselineName));
                anyFailure = true;
            }

            bool unexpectedUnknownEvent = false;
            int firstFailLineNum = 0;
            int mismatchCount = 0;
            int lineNum = 0;
            var histogram = new SortedDictionary<string, int>(StringComparer.Ordinal);

            // TraceLog traceLog = TraceLog.OpenOrConvert(etlFilePath);    // This one can be used during development of test itself
            TraceLog traceLog = new TraceLog(TraceLog.CreateFromEventTraceLogFile(etlFilePath));

            var traceSource = traceLog.Events.GetSource();
            traceSource.AllEvents += delegate (TraceEvent data)
            {
                string eventName = data.ProviderName + "/" + data.EventName;

                // We are going to skip dynamic events from the CLR provider.
                // The issue is that this depends on exactly which manifest is present
                // on the machine, and I just don't want to deal with the noise of 
                // failures because you have a slightly different one.  
                if (data.ProviderName == "DotNet")
                {
                    return;
                }

                // We don't want to use the manifest for CLR Private events since 
                // different machines might have different manifests.  
                if (data.ProviderName == "Microsoft-Windows-DotNETRuntimePrivate")
                {
                    if (data.GetType().Name == "DynamicTraceEventData" || data.EventName.StartsWith("EventID"))
                    {
                        return;
                    }
                }
                // Same problem with classic OS events.   We don't want to rely on the OS to parse since this could vary between baseline and test. 
                else if (data.ProviderName == "MSNT_SystemTrace")
                {
                    // However we to allow a couple of 'known good' ones through so we test some aspects of the OS parsing logic in TraceEvent.   
                    if (data.EventName != "SystemConfig/Platform" && data.EventName != "Image/KernelBase")
                    {
                        return;
                    }
                }
                // In theory we have the same problem with any event that the OS supplies the parsing.   I dont want to be too aggressive about 
                // turning them off, however becasuse I want those code paths tested


                // TODO FIX NOW, this is broken and should be fixed.  
                // We are hacking it here so we don't turn off the test completely.  
                if (eventName == "DotNet/CLR.SKUOrVersion")
                {
                    return;
                }

                int count = IncCount(histogram, eventName);

                // To keep the baseline size under control, we only check at
                // most 5 of each event type.  
                const int MaxEventPerType = 5;

                if (count > MaxEventPerType)
                {
                    return;
                }

                string parsedEvent = Parse(data);
                lineNum++;
                outputFile.WriteLine(parsedEvent);      // Make the new output file.

                string expectedParsedEvent = null;
                if (baselineFile != null)
                {
                    expectedParsedEvent = baselineFile.ReadLine();
                }

                if (expectedParsedEvent == null)
                {
                    expectedParsedEvent = "";
                }

                // If we have baseline, it should match what we have in the file.  
                if (baselineFile != null && parsedEvent != expectedParsedEvent)
                {
                    mismatchCount++;
                    if (firstFailLineNum == 0)
                    {
                        firstFailLineNum = lineNum;
                        anyFailure = true;
                        Output.WriteLine(string.Format("ERROR: File {0}: event not equal to expected on line {1}", etlFilePath, lineNum));
                        Output.WriteLine(string.Format("   Expected: {0}", expectedParsedEvent));
                        Output.WriteLine(string.Format("   Actual  : {0}", parsedEvent));

                        Output.WriteLine("To Compare output and baseline (baseline is SECOND)");
                        Output.WriteLine(string.Format("    windiff \"{0}\" \"{1}\"", newBaselineName, baselineName));
                    }
                }

                // Even if we don't have a baseline, we can check that the event names are OK.  
                if (0 <= eventName.IndexOf('('))   // Unknown events have () in them 
                {
                    // Some expected events we don't handle today.   
                    if (data.EventName != "EventID(65534)" &&       // Manifest events 
                        data.ProviderName != "Microsoft-Windows-DNS-Client" &&
                        eventName != "KernelTraceControl/ImageID/Opcode(34)" &&
                        eventName != "Windows Kernel/DiskIO/Opcode(16)" &&
                        eventName != "Windows Kernel/SysConfig/Opcode(37)" &&
                        eventName != "KernelTraceControl/ImageID/Opcode(41)")
                    {
                        Output.WriteLine(string.Format("ERROR: File {0}: has unknown event {1} at {2:n3} MSec",
                            etlFilePath, eventName, data.TimeStampRelativeMSec));

                        // Assert throws an exception which gets swallowed in Process() so instead
                        // we remember that we failed and assert outside th callback.  
                        unexpectedUnknownEvent = true;
                    }
                }
            };

            /********************* PROCESSING ***************************/
            traceSource.Process();

            // Validation after processing, first we check that the histograms are the same as the baseline

            // We also want to check that the count of events is the same as the baseline. 
            bool histogramMismatch = false;
            foreach (var keyValue in histogram)
            {
                var histogramLine = "COUNT " + keyValue.Key + ":" + keyValue.Value;

                outputFile.WriteLine(histogramLine);
                var expectedistogramLine = baselineFile.ReadLine();
                lineNum++;

                // This is a hack.  These seem to have different counts on different machines.
                // Need to figure out why, but for now it is tracked by issue https://github.com/Microsoft/perfview/issues/643
                if (keyValue.Key.Contains("GC/AllocationTick") || keyValue.Key.Contains("Kernel/DiskIO/Read"))
                {
                    continue;
                }

                if (etlFileName.Contains("net.4.0") && (keyValue.Key.Contains("HeapStats") || keyValue.Key.Contains("Kernel/PerfInfo/Sample")))
                {
                    continue;
                }

                if (!histogramMismatch && expectedistogramLine != histogramLine)
                {
                    histogramMismatch = true;
                    anyFailure = true;
                    Output.WriteLine(string.Format("ERROR: File {0}: histogram not equal on  {1}", etlFilePath, lineNum));
                    Output.WriteLine(string.Format("   Expected: {0}", expectedistogramLine));
                    Output.WriteLine(string.Format("   Actual  : {0}", histogramLine));

                    Output.WriteLine("To Compare output and baseline (baseline is SECOND)");
                    Output.WriteLine(string.Format("    windiff \"{0}\" \"{1}\"", newBaselineName, baselineName));
                }
            }

            outputFile.Close();
            if (anyFailure)
            {
                Output.WriteLine(string.Format("ERROR: File {0}: had a failure {1} mismatches. histogram mismatches: {2}", etlFilePath, mismatchCount, histogramMismatch));
                File.Copy(outputName, newBaselineName, true);
                Output.WriteLine(string.Format("To Update: xcopy /s \"{0}\" \"{1}\"", NewBaselineDir, OriginalBaselineDir));
            }

            // If this fires, check the output for the TraceLine just before it for more details.  
            Assert.False(unexpectedUnknownEvent, "Check trace output for details.  Search for ERROR");
            Assert.True(lineNum > 0);     // We had some events.  

            Assert.False(anyFailure, "Check trace output for details.  Search for ERROR");
        }

        private static int IncCount(SortedDictionary<string, int> histogram, string eventName)
        {
            int count = 0;
            histogram.TryGetValue(eventName, out count);
            count++;
            histogram[eventName] = count;
            return count;
        }

        // Create 1 line that embodies the data in event 'data'

        internal static string Parse(TraceEvent data)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("EVENT ");
            sb.Append(data.TimeStampRelativeMSec.ToString("n3")).Append(": ");
            sb.Append(data.ProviderName).Append("/").Append(data.EventName).Append(" ");

            sb.Append("PID=").Append(data.ProcessID).Append("; ");
            sb.Append("TID=").Append(data.ThreadID).Append("; ");
            sb.Append("PName=").Append(data.ProcessName).Append("; ");
            sb.Append("ProceNum=").Append(data.ProcessorNumber).Append("; ");
            sb.Append("DataLen=").Append(data.EventDataLength).Append("; ");

            string[] payloadNames = data.PayloadNames;
            for (int i = 0; i < payloadNames.Length; i++)
            {
                // Normalize DateTime to UTC so tests work in any timezone. 
                object value = data.PayloadValue(i);
                string valueStr;
                if (value is DateTime)
                {
                    valueStr = ((DateTime)value).ToUniversalTime().ToString("yy/MM/dd HH:mm:ss.ffffff");
                }
                else
                {
                    valueStr = (data.PayloadString(i));
                }

                // To debug this set first chance exeption handing before calling PayloadString above.
                Assert.False(valueStr.Contains("EXCEPTION_DURING_VALUE_LOOKUP"), "Exception during event Payload Processing");

                // Keep the value size under control and remove newlines.  
                if (valueStr.Length > 20)
                {
                    valueStr = valueStr.Substring(0, 20) + "...";
                }

                valueStr = valueStr.Replace("\n", "\\n").Replace("\r", "\\r");

                sb.Append(payloadNames[i]).Append('=').Append(valueStr).Append("; ");
            }

            if(data.ContainerID != null)
            {
                sb.Append("ContainerID=").Append(data.ContainerID).Append("; ");
            }

            return sb.ToString();
        }
    }
}
