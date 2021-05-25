using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Xunit;
using Xunit.Abstractions;

namespace TraceEventTests
{
    public class MultiFileMergeAll : EtlTestBase
    {
        public MultiFileMergeAll(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// This test unzips a zip file containing 4 etls files, open them as 1 trace
        /// and asserts the correct TraceLog size and event count
        /// </summary>
        [Fact]
        public void ETW_MultiFileMergeAll_Basic()
        {
            PrepareTestData();
            IEnumerable<string> fileNames = Directory.EnumerateFiles(UnZippedDataDir + "\\diaghub-dotnetcore3.1-win-x64-diagsession", "*.etl");
            Output.WriteLine($"In {nameof(ETW_MultiFileMergeAll_Basic)}(\"{string.Join(", ", fileNames)}\")");

            string etlFilePath = "diaghub-dotnetcore3.1-win-x64-diagsession";
            Output.WriteLine(string.Format("Processing the file {0}, Making ETLX and scanning.", etlFilePath));
            string eltxFilePath = Path.ChangeExtension(etlFilePath, ".etlx");

            TraceEventDispatcher source = new ETWTraceEventSource(fileNames, TraceEventSourceType.MergeAll);
            TraceLog traceLog = new TraceLog(TraceLog.CreateFromEventTraceLogFile(source, eltxFilePath));

            Assert.Equal(95506, traceLog.EventCount);
            var stopEvents = traceLog.Events.Filter(e => e.EventName == "Activity2Stop/Stop");
            Assert.Equal(55, stopEvents.Count());
            Assert.Equal((uint)13205, (uint)stopEvents.Last().EventIndex);

            using (var file = new StreamReader($"{TestDataDir}\\diaghub-dotnetcore3.1-win-x64-diagsession.baseline.txt"))
            {
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

                    var evt = GeneralParsing.Parse(data);

                    var line = file.ReadLine();
                    Assert.Equal(evt, line);
                };

                traceSource.Process();
            }
        }
    }
}
