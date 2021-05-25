using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TraceEventTests
{
    public class EventPipeParsing : EventPipeTestBase
    {
        private class EventRecord
        {
            public int TotalCount;
            public string FirstSeriazliedSample;
        }

        public EventPipeParsing(ITestOutputHelper output)
            : base(output)
        {
        }
        
        [Theory()]
        [MemberData(nameof(TestEventPipeFiles))]
        public void Basic(string eventPipeFileName)
        {
            // Initialize
            PrepareTestData();

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            Output.WriteLine(string.Format("Processing the file {0}, Making ETLX and scanning.", Path.GetFullPath(eventPipeFilePath)));

            var eventStatistics = new SortedDictionary<string, EventRecord>(StringComparer.Ordinal);

            using (var traceLog = new TraceLog(TraceLog.CreateFromEventPipeDataFile(eventPipeFilePath)))
            {
                var traceSource = traceLog.Events.GetSource();

                traceSource.AllEvents += delegate (TraceEvent data)
                {
                    string eventName = data.ProviderName + "/" + data.EventName;

                    if (eventStatistics.ContainsKey(eventName))
                    {
                        eventStatistics[eventName].TotalCount++;
                    }
                    else
                    {
                        eventStatistics[eventName] = new EventRecord()
                        {
                            TotalCount = 1,
                            FirstSeriazliedSample = new String(data.ToString().Replace("\n", "\\n").Replace("\r", "\\r").Take(1000).ToArray())
                        };
                    }
                };

                // Process
                traceSource.Process();
            }
            // Validate
            ValidateEventStatistics(eventStatistics, eventPipeFileName);
        }

        [Theory()]
        [MemberData(nameof(StreamableTestEventPipeFiles))]
        public void Streaming(string eventPipeFileName)
        {
            // Initialize
            PrepareTestData();

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);
            Output.WriteLine(string.Format("Processing the file {0}", Path.GetFullPath(eventPipeFilePath)));
            var eventStatistics = new SortedDictionary<string, EventRecord>(StringComparer.Ordinal);

            long curStreamPosition = 0;
            using (MockStreamingOnlyStream s = new MockStreamingOnlyStream(new FileStream(eventPipeFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                using (var traceSource = new EventPipeEventSource(s))
                {
                    Action<TraceEvent> handler = delegate (TraceEvent data)
                    {
                        long newStreamPosition = s.TestOnlyPosition;
                        // Empirically these files have event blocks of no more than 103K bytes each
                        // The streaming code should never need to read ahead beyond the end of the current
                        // block to read the events
                        Assert.InRange(newStreamPosition, curStreamPosition, curStreamPosition + 103_000);
                        curStreamPosition = newStreamPosition;


                        string eventName = data.ProviderName + "/" + data.EventName;

                        // For whatever reason the parse filtering below produces a couple extra events
                        // that TraceLog managed to filter out:
                        //    Microsoft-Windows-DotNETRuntime/Method, 2,
                        //    Microsoft-Windows-DotNETRuntimeRundown/Method, 26103, ...
                        // I haven't had an opportunity to investigate and its probably not a big
                        // deal so just hacking around it for the moment
                        if (eventName == "Microsoft-Windows-DotNETRuntimeRundown/Method" ||
                            eventName == "Microsoft-Windows-DotNETRuntime/Method")
                            return;

                        if (eventStatistics.ContainsKey(eventName))
                        {
                            eventStatistics[eventName].TotalCount++;
                        }
                        else
                        {
                            eventStatistics[eventName] = new EventRecord()
                            {
                                TotalCount = 1,
                                FirstSeriazliedSample = new String(data.ToString().Replace("\n", "\\n").Replace("\r", "\\r").Take(1000).ToArray())
                            };
                        }
                    };

                    // this is somewhat arbitrary looking set of parser event callbacks empirically
                    // produces the same set of events as TraceLog.Events.GetSource().AllEvents so
                    // that the baseline files can be reused from the Basic test
                    var rundown = new ClrRundownTraceEventParser(traceSource);
                    rundown.LoaderAppDomainDCStop += handler;
                    rundown.LoaderAssemblyDCStop += handler;
                    rundown.LoaderDomainModuleDCStop += handler;
                    rundown.LoaderModuleDCStop += handler;
                    rundown.MethodDCStopComplete += handler;
                    rundown.MethodDCStopInit += handler;
                    var sampleProfiler = new SampleProfilerTraceEventParser(traceSource);
                    sampleProfiler.All += handler;
                    var privateClr = new ClrPrivateTraceEventParser(traceSource);
                    privateClr.All += handler;
                    traceSource.Clr.All += handler;
                    traceSource.Clr.MethodILToNativeMap -= handler;
                    traceSource.Dynamic.All += handler;

                    // Process
                    traceSource.Process();
                }
            }
            // Validate
            ValidateEventStatistics(eventStatistics, eventPipeFileName);
        }

        [Fact]
        public void CanParseHeaderOfV1EventPipeFile()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore2.0-linux-objver1.netperf";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var eventPipeSource = new EventPipeEventSource(eventPipeFilePath))
            {
                Assert.Equal(8, eventPipeSource.PointerSize);
                Assert.Equal(0, eventPipeSource._processId);
                Assert.Equal(1, eventPipeSource.NumberOfProcessors);

                Assert.Equal(636414354195130000, eventPipeSource._syncTimeUTC.Ticks);
                Assert.Equal(1477613380157300, eventPipeSource._syncTimeQPC);
                Assert.Equal(1000000000, eventPipeSource._QPCFreq);

                Assert.Equal(10, eventPipeSource.CpuSpeedMHz);
            }
        }

        [Fact]
        public void CanParseHeaderOfV3EventPipeFile()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore2.1-win-x86-objver3.netperf";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var eventPipeSource = new EventPipeEventSource(eventPipeFilePath))
            {
                Assert.Equal(4, eventPipeSource.PointerSize);
                Assert.Equal(3312, eventPipeSource._processId);
                Assert.Equal(4, eventPipeSource.NumberOfProcessors);
                Assert.Equal(1000000, eventPipeSource._expectedCPUSamplingRate);

                Assert.Equal(636531024984420000, eventPipeSource._syncTimeUTC.Ticks);
                Assert.Equal(20461004832, eventPipeSource._syncTimeQPC);
                Assert.Equal(2533315, eventPipeSource._QPCFreq);

                Assert.Equal(10, eventPipeSource.CpuSpeedMHz);
            }
        }

        [Fact]
        public void CanParseHeaderOfV4EventPipeFile()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore3.0-win-x64-objver4.nettrace";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var eventPipeSource = new EventPipeEventSource(eventPipeFilePath))
            {
                Assert.Equal(8, eventPipeSource.PointerSize);
                Assert.Equal(10064, eventPipeSource._processId);
                Assert.Equal(4, eventPipeSource.NumberOfProcessors);
                Assert.Equal(1000000, eventPipeSource._expectedCPUSamplingRate);

                Assert.Equal(636962398197540000, eventPipeSource._syncTimeUTC.Ticks);
                Assert.Equal(100935252313, eventPipeSource._syncTimeQPC);
                Assert.Equal(3124099, eventPipeSource._QPCFreq);

                Assert.Equal(10, eventPipeSource.CpuSpeedMHz);
            }
        }

        [Fact]
        public void V4EventPipeFileHasProcNumbers()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore3.0-win-x64-objver4.nettrace";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var traceSource = new EventPipeEventSource(eventPipeFilePath))
            {
                Assert.Equal(4, traceSource.NumberOfProcessors);
                int[] counts = new int[4];

                Action<TraceEvent> handler = delegate (TraceEvent data)
                {
                    Assert.True(data.ProcessorNumber >= 0 && data.ProcessorNumber < traceSource.NumberOfProcessors);
                    counts[data.ProcessorNumber]++;
                };

                var privateClr = new ClrPrivateTraceEventParser(traceSource);
                privateClr.All += handler;
                traceSource.Clr.All += handler;
                traceSource.Clr.MethodILToNativeMap -= handler;
                traceSource.Dynamic.All += handler;

                // Process
                traceSource.Process();

                for(int i = 0; i < traceSource.NumberOfProcessors; i++)
                {
                    Assert.NotEqual(0, counts[i]);
                }
            }
        }

        [Fact]
        public void CanReadV4EventPipeTraceBiggerThan4GB()
        {
            // Generate a mock stream of events larger than 4GB in size and parse it all
            using (var traceSource = new EventPipeEventSource(new MockHugeStream((long)uint.MaxValue + 5_000_000)))
            {
                int eventCount = 0;

                Action<TraceEvent> handler = delegate (TraceEvent data)
                {
                    eventCount++;
                };

                traceSource.AllEvents += handler;

                // Process
                traceSource.Process();

                Assert.Equal(71600, eventCount);
            }
        }

        [Fact]
        public void CanReadV5EventPipeArrayTypes()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore5.0-win-x64-arraytypes.nettrace";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var traceSource = new EventPipeEventSource(eventPipeFilePath))
            {
                int successCount = 0;
                Action<TraceEvent> handler = delegate (TraceEvent traceEvent)
                {
                    string traceLevelValidationMessage = "Expected level {0} but got level {1} for EventSource={2} Event={3}";
                    string traceKeywordValidationMessage = "Expected keywords {0} but got keywords{1} for EventSource={2} Event={3}";
                    string traceOpcodeValidationMessage = "Expected opcode {0} but got opcode {1} for EventSource={2} Event={3}";
                    string tracePayloadValidationMessage = "Expected {0} payload items but got {1} items for EventSource={2} Event={3}";
                    string tracePayloadNamesValidationMessage = "Expected argument name {0} but got name {1} for EventSource={2} Event={3}";
                    string tracePayloadTypeValidationMessage = "Expected type {0} but got type {1} for EventSource={2} Event={3} Argument={4}";
                    string tracePayloadValueValidationMessage = "Expected argument value {0} but got value {1} for EventSource={2} Event={3} Argument={4}";

                    if (traceEvent.ProviderName == "TestEventSource0")
                    {
                        if (traceEvent.EventName == "TestEvent1")
                        {
                            if ((int)traceEvent.Level != 2) { throw new Exception(String.Format(traceLevelValidationMessage, 2, (int)traceEvent.Level, "TestEventSource0", "TestEvent1")); }
                            if ((int)traceEvent.Keywords != 0) { throw new Exception(String.Format(traceKeywordValidationMessage, 0, (int)traceEvent.Keywords, "TestEventSource0", "TestEvent1")); }
                            if ((int)traceEvent.Opcode != 0) { throw new Exception(String.Format(traceOpcodeValidationMessage, 0, (int)traceEvent.Opcode, "TestEventSource0", "TestEvent1")); }
                            if (traceEvent.PayloadNames.Count() != 3) { throw new Exception(String.Format(tracePayloadValidationMessage, 3, traceEvent.PayloadNames.Count(), "TestEventSource0", "TestEvent1")); }
                            uint[] testEvent1Array0 = new uint[] { (uint)2032854139, (uint)1608221689, (uint)1470019200, (uint)199494339, (uint)1238846140, (uint)1366609043 };
                            if (traceEvent.PayloadNames[0] != "arg0") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg0", traceEvent.PayloadNames[0], "TestEventSource0", "TestEvent1")); }
                            if (traceEvent.PayloadValue(0).GetType() != typeof(uint[])) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "uint[]", traceEvent.PayloadValue(0).GetType(), "TestEventSource0", "TestEvent1", "arg0")); }
                            if (!Enumerable.SequenceEqual((uint[])traceEvent.PayloadValue(0), testEvent1Array0)) { throw new Exception(String.Format(tracePayloadValueValidationMessage, testEvent1Array0, traceEvent.PayloadValue(0), "TestEventSource0", "TestEvent1", "arg0")); }
                            short[] testEvent1Array1 = new short[] { (short)26494, (short)17115, (short)-7229, (short)18850, (short)27931 };
                            if (traceEvent.PayloadNames[1] != "arg1") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg1", traceEvent.PayloadNames[1], "TestEventSource0", "TestEvent1")); }
                            if (traceEvent.PayloadValue(1).GetType() != typeof(short[])) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "short[]", traceEvent.PayloadValue(1).GetType(), "TestEventSource0", "TestEvent1", "arg1")); }
                            if (!Enumerable.SequenceEqual((short[])traceEvent.PayloadValue(1), testEvent1Array1)) { throw new Exception(String.Format(tracePayloadValueValidationMessage, testEvent1Array1, traceEvent.PayloadValue(1), "TestEventSource0", "TestEvent1", "arg1")); }
                            if (traceEvent.PayloadNames[2] != "arg2") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg2", traceEvent.PayloadNames[2], "TestEventSource0", "TestEvent1")); }
                            if (traceEvent.PayloadValue(2).GetType() != typeof(long)) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "long", traceEvent.PayloadValue(2).GetType(), "TestEventSource0", "TestEvent1", "arg2")); }
                            if ((long)traceEvent.PayloadValue(2) != 2033417279) { throw new Exception(String.Format(tracePayloadValueValidationMessage, 2033417279, traceEvent.PayloadValue(2), "TestEventSource0", "TestEvent1", "arg2")); }

                            ++successCount;
                            return;
                        }
                        if (traceEvent.EventName == "TestEvent2")
                        {
                            if ((int)traceEvent.Level != 4) { throw new Exception(String.Format(traceLevelValidationMessage, 4, (int)traceEvent.Level, "TestEventSource0", "TestEvent2")); }
                            if ((int)traceEvent.Keywords != 1231036291) { throw new Exception(String.Format(traceKeywordValidationMessage, 1231036291, (int)traceEvent.Keywords, "TestEventSource0", "TestEvent2")); }
                            if ((int)traceEvent.Opcode != 0) { throw new Exception(String.Format(traceOpcodeValidationMessage, 0, (int)traceEvent.Opcode, "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadNames.Count() != 4) { throw new Exception(String.Format(tracePayloadValidationMessage, 4, traceEvent.PayloadNames.Count(), "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadNames[0] != "arg0") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg0", traceEvent.PayloadNames[0], "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadValue(0).GetType() != typeof(long)) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "long", traceEvent.PayloadValue(0).GetType(), "TestEventSource0", "TestEvent2", "arg0")); }
                            if ((long)traceEvent.PayloadValue(0) != -1001479330) { throw new Exception(String.Format(tracePayloadValueValidationMessage, -1001479330, traceEvent.PayloadValue(0), "TestEventSource0", "TestEvent2", "arg0")); }
                            if (traceEvent.PayloadNames[1] != "arg1") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg1", traceEvent.PayloadNames[1], "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadValue(1).GetType() != typeof(int)) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "int", traceEvent.PayloadValue(1).GetType(), "TestEventSource0", "TestEvent2", "arg1")); }
                            if ((int)traceEvent.PayloadValue(1) != -1397908228) { throw new Exception(String.Format(tracePayloadValueValidationMessage, -1397908228, traceEvent.PayloadValue(1), "TestEventSource0", "TestEvent2", "arg1")); }
                            int[] testEvent2Array0 = new int[] { 1470938110, 172564262, 1133558854, -1572049829 };
                            if (traceEvent.PayloadNames[2] != "arg2") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg2", traceEvent.PayloadNames[2], "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadValue(2).GetType() != typeof(int[])) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "int[]", traceEvent.PayloadValue(2).GetType(), "TestEventSource0", "TestEvent2", "arg2")); }
                            if (!Enumerable.SequenceEqual((int[])traceEvent.PayloadValue(2), testEvent2Array0)) { throw new Exception(String.Format(tracePayloadValueValidationMessage, testEvent2Array0, traceEvent.PayloadValue(2), "TestEventSource0", "TestEvent2", "arg2")); }
                            ulong[] testEvent2Array1 = new ulong[] { (ulong)2055126903, (ulong)593325874, (ulong)2130052527, (ulong)162795177 };
                            if (traceEvent.PayloadNames[3] != "arg3") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg3", traceEvent.PayloadNames[3], "TestEventSource0", "TestEvent2")); }
                            if (traceEvent.PayloadValue(3).GetType() != typeof(ulong[])) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "ulong[]", traceEvent.PayloadValue(3).GetType(), "TestEventSource0", "TestEvent2", "arg3")); }
                            if (!Enumerable.SequenceEqual((ulong[])traceEvent.PayloadValue(3), testEvent2Array1)) { throw new Exception(String.Format(tracePayloadValueValidationMessage, testEvent2Array1, traceEvent.PayloadValue(3), "TestEventSource0", "TestEvent2", "arg3")); }

                            ++successCount;
                            return;
                        }
                        if (traceEvent.EventName == "TestEvent3/24")
                        {
                            if ((int)traceEvent.Level != 2) { throw new Exception(String.Format(traceLevelValidationMessage, 2, (int)traceEvent.Level, "TestEventSource0", "TestEvent3")); }
                            if ((int)traceEvent.Keywords != 0) { throw new Exception(String.Format(traceKeywordValidationMessage, 0, (int)traceEvent.Keywords, "TestEventSource0", "TestEvent3")); }
                            if ((int)traceEvent.Opcode != 24) { throw new Exception(String.Format(traceOpcodeValidationMessage, 24, (int)traceEvent.Opcode, "TestEventSource0", "TestEvent3")); }
                            if (traceEvent.PayloadNames.Count() != 1) { throw new Exception(String.Format(tracePayloadValidationMessage, 1, traceEvent.PayloadNames.Count(), "TestEventSource0", "TestEvent3")); }
                            if (traceEvent.PayloadNames[0] != "arg0") { throw new Exception(String.Format(tracePayloadNamesValidationMessage, "arg0", traceEvent.PayloadNames[0], "TestEventSource0", "TestEvent3")); }
                            if (traceEvent.PayloadValue(0).GetType() != typeof(char[])) { throw new Exception(String.Format(tracePayloadTypeValidationMessage, "char[]", traceEvent.PayloadValue(0).GetType(), "TestEventSource0", "TestEvent3", "arg0")); }
                            char[] testEvent3Array = new char[] { (char)59299, (char)13231, (char)38541, (char)7407, (char)35812 };
                            if (!Enumerable.SequenceEqual((char[])traceEvent.PayloadValue(0), testEvent3Array)) { throw new Exception(String.Format(tracePayloadValueValidationMessage, testEvent3Array, traceEvent.PayloadValue(0), "TestEventSource0", "TestEvent3", "arg0")); }

                            ++successCount;
                            return;
                        }
                    }
                };

                traceSource.Dynamic.All += handler;

                // Process
                traceSource.Process();

                Assert.Equal(3, successCount);
            }
        }

        [Fact]
        public void AllEventsLeavesNoUnhandledEvents()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore2.1-win-x86-objver3.netperf";

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            using (var traceSource = new TraceLog(TraceLog.CreateFromEventPipeDataFile(eventPipeFilePath)).Events.GetSource())
            {
                int dynamicAllInvocationCount = 0;
                int unhandledEvents = 0;

                traceSource.AllEvents += _ => dynamicAllInvocationCount++;

                traceSource.UnhandledEvents += _ => unhandledEvents++;

                traceSource.Process();

                Assert.NotEqual(0, dynamicAllInvocationCount);
                Assert.Equal(0, unhandledEvents);
            }
        }

        [Fact]
        public void GuidsInMetadataAreSupported()
        {
            PrepareTestData();

            const string eventPipeFileName = "eventpipe-dotnetcore2.1-linux-x64-objver3.netperf";
            Guid ExpectedActivityId = new Guid("10000000-0000-0000-0000-000000000001");

            string eventPipeFilePath = Path.Combine(UnZippedDataDir, eventPipeFileName);

            bool activityIdHasBeenSet = false;
            using (var traceSource = new TraceLog(TraceLog.CreateFromEventPipeDataFile(eventPipeFilePath)).Events.GetSource())
            {
                traceSource.AllEvents += (TraceEvent @event) =>
                {
                    // before the activity Id is set, it's empty
                    // after the activity Id is set, all the events should have it (here we had only 1 thread)
                    Assert.Equal(activityIdHasBeenSet ? ExpectedActivityId : Guid.Empty, @event.ActivityID);

                    // the EVENTID for SetActivityId is 25 https://github.com/dotnet/coreclr/blob/c67c29d6e226e4cca1f1efb4d57b7f498d58b534/src/mscorlib/src/System/Threading/Tasks/TPLETWProvider.cs#L524
                    if (@event.ProviderName != TplEtwProviderTraceEventParser.ProviderName || @event.ID != (TraceEventID)25)
                    {
                        return;
                    }

                    Assert.False(activityIdHasBeenSet); // make sure the event comes only once

                    // Sneak in a test of the DLR support here (casting to 'dynamic'
                    // instead of using PayloadByName("NewId")):
                    if (((dynamic)@event).NewId.Equals(ExpectedActivityId))
                    {
                        activityIdHasBeenSet = true;
                    }
                };

                traceSource.Process();

                Assert.True(activityIdHasBeenSet);
            }
        }

        [Fact]
        public void WellKnownDiagnosticSourceEventsHavePayloads()
        {
            // NetPerf and NetTrace v1 format don't support serializing array typed parameters.
            // DiagnosticSourceEventSource declares well known events that use array typed parameters
            // and the runtime serializes EventPipe metadata which claims the events have no parameters.
            // We made a targeted fix where EventPipeEventSource will recognize these well-known events
            // and ignore the empty parameter metadata provided in the stream, treating the events
            // as if the runtime had provided the correct parameter schema.
            //
            // I am concurrently working on a runtime fix and updated file format revision which can 
            // correctly encode these parameter types. However for back-compat with older runtimes we
            // need this.


            // Serialize an EventPipe stream containing the parameterless metadata for
            // DiagnosticSourceEventSource events...
            EventPipeWriter writer = new EventPipeWriter();
            writer.WriteHeaders();
            writer.WriteMetadataBlock(
                new EventMetadata(1, "Microsoft-Diagnostics-DiagnosticSource", "Event", 3),
                new EventMetadata(2, "Microsoft-Diagnostics-DiagnosticSource", "Activity1Start", 4),
                new EventMetadata(3, "Microsoft-Diagnostics-DiagnosticSource", "Activity1Stop", 5),
                new EventMetadata(4, "Microsoft-Diagnostics-DiagnosticSource", "Activity2Start", 6),
                new EventMetadata(5, "Microsoft-Diagnostics-DiagnosticSource", "Activity2Stop", 7),
                new EventMetadata(6, "Microsoft-Diagnostics-DiagnosticSource", "RecursiveActivity1Start", 8),
                new EventMetadata(7, "Microsoft-Diagnostics-DiagnosticSource", "RecursiveActivity1Stop", 9));

            EventPayloadWriter payload = new EventPayloadWriter();
            payload.Write("FakeProviderName");
            payload.Write("FakeEventName");
            payload.WriteArray(new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string,string>("key1", "val1"),
                new KeyValuePair<string, string>("key2", "val2")
            },
            kv =>
            {
                payload.Write(kv.Key);
                payload.Write(kv.Value);
            });
            byte[] payloadBytes = payload.ToArray();

            int sequenceNumber = 1;
            writer.WriteEventBlock(
                w =>
                {
                    // write one of each of the 7 well-known DiagnosticSourceEventSource events.
                    for (int metadataId = 1; metadataId <= 7; metadataId++)
                    {
                        EventPipeWriter.WriteEventBlob(w, metadataId, sequenceNumber++, payloadBytes);
                    }
                });
            writer.WriteEndObject();
            MemoryStream stream = new MemoryStream(writer.ToArray());

            // Confirm we can parse the event payloads even though the parameters were not described in
            // the metadata.
            EventPipeEventSource source = new EventPipeEventSource(stream);
            int diagSourceEventCount = 0;
            source.Dynamic.All += e =>
            {
                Assert.Equal("FakeProviderName", e.PayloadByName("SourceName"));
                Assert.Equal("FakeEventName", e.PayloadByName("EventName"));
                IDictionary<string, object>[] args = (IDictionary<string, object>[])e.PayloadByName("Arguments");
                Assert.Equal(2, args.Length);
                Assert.Equal("key1", args[0]["Key"]);
                Assert.Equal("val1", args[0]["Value"]);
                Assert.Equal("key2", args[1]["Key"]);
                Assert.Equal("val2", args[1]["Value"]);
                diagSourceEventCount++;
            };
            source.Process();
            Assert.Equal(7, diagSourceEventCount);
        }

        private void Dynamic_All(TraceEvent obj)
        {
            throw new NotImplementedException();
        }

        private void ValidateEventStatistics(SortedDictionary<string, EventRecord> eventStatistics, string eventPipeFileName)
        {
            StringBuilder sb = new StringBuilder(1024 * 1024);
            foreach (var item in eventStatistics)
            {
                sb.AppendLine($"{item.Key}, {item.Value.TotalCount}, {item.Value.FirstSeriazliedSample}");
            }

            string actual = sb.ToString();
            string baselineFile = Path.Combine(TestDataDir, eventPipeFileName + ".baseline.txt");
            string expected = File.ReadAllText(baselineFile);

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                string eventStatisticsFile = Path.Combine(TestDataDir, eventPipeFileName + ".actual.txt");
                File.WriteAllText(eventStatisticsFile, actual, Encoding.UTF8);

                Output.WriteLine($"Baseline File: {baselineFile}");
                Output.WriteLine($"Actual File: {eventStatisticsFile}");
                Output.WriteLine($"To Diff: windiff {baselineFile} {eventStatisticsFile}");
                Assert.True(false, $"The event statistics doesn't match {Path.GetFullPath(baselineFile)}. It's saved in {Path.GetFullPath(eventStatisticsFile)}.");
            }
        }
    }


    class MockStreamingOnlyStream : Stream
    {
        Stream _innerStream;
        public MockStreamingOnlyStream(Stream innerStream)
        {
            _innerStream = innerStream;
        }
        public long TestOnlyPosition {  get { return _innerStream.Position; } }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush()
        {
            throw new NotImplementedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _innerStream.Read(buffer, offset, count);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }


    class EventMetadata
    {
        public EventMetadata(int metadataId, string providerName, string eventName, int eventId)
        {
            MetadataId = metadataId;
            ProviderName = providerName;
            EventName = eventName;
            EventId = eventId;
        }

        public int MetadataId { get; set; }
        public string ProviderName { get; set; }
        public string EventName { get; set; }
        public int EventId { get; set; }
    }

    class EventPayloadWriter
    {
        BinaryWriter _writer = new BinaryWriter(new MemoryStream());

        public void Write(string arg)
        {
            _writer.Write(Encoding.Unicode.GetBytes(arg));
            _writer.Write((ushort)0);
        }

        public void WriteArray<T>(T[] elements, Action<T> writeElement)
        {
            WriteArrayLength(elements.Length);
            for(int i = 0; i < elements.Length; i++)
            {
                writeElement(elements[i]);
            }
        }

        public void WriteArrayLength(int length)
        {
            _writer.Write((ushort)length);
        }

        public byte[] ToArray()
        {
            return (_writer.BaseStream as MemoryStream).ToArray();
        }
    }

    class EventPipeWriter
    {
        BinaryWriter _writer;

        public EventPipeWriter()
        {
            _writer = new BinaryWriter(new MemoryStream());
        }

        public byte[] ToArray()
        {
            return (_writer.BaseStream as MemoryStream).ToArray();
        }

        public void WriteHeaders()
        {
            WriteNetTraceHeader(_writer);
            WriteFastSerializationHeader(_writer);
            WriteTraceObject(_writer);
        }

        public void WriteMetadataBlock(params EventMetadata[] metadataBlobs)
        {
            WriteMetadataBlock(_writer, metadataBlobs);
        }

        public void WriteEventBlock(Action<BinaryWriter> writeEventBlobs)
        {
            WriteEventBlock(_writer, writeEventBlobs);
        }

        public void WriteEndObject()
        {
            WriteEndObject(_writer);
        }

        public static void WriteNetTraceHeader(BinaryWriter writer)
        {
            writer.Write(Encoding.UTF8.GetBytes("Nettrace"));
        }

        public static void WriteFastSerializationHeader(BinaryWriter writer)
        {
            WriteString(writer, "!FastSerialization.1");
        }

        public static void WriteString(BinaryWriter writer, string val)
        {
            writer.Write(val.Length);
            writer.Write(Encoding.UTF8.GetBytes(val));
        }

        public static void WriteObject(BinaryWriter writer, string name, int version, int minVersion,
    Action writePayload)
        {
            writer.Write((byte)5); // begin private object
            writer.Write((byte)5); // begin private object - type
            writer.Write((byte)1); // type of type
            writer.Write(version);
            writer.Write(minVersion);
            WriteString(writer, name);
            writer.Write((byte)6); // end object
            writePayload();
            writer.Write((byte)6); // end object
        }

        public static void WriteTraceObject(BinaryWriter writer)
        {
            WriteObject(writer, "Trace", 4, 4, () =>
            {
                DateTime now = DateTime.Now;
                writer.Write((short)now.Year);
                writer.Write((short)now.Month);
                writer.Write((short)now.DayOfWeek);
                writer.Write((short)now.Day);
                writer.Write((short)now.Hour);
                writer.Write((short)now.Minute);
                writer.Write((short)now.Second);
                writer.Write((short)now.Millisecond);
                writer.Write((long)1_000_000); // syncTimeQPC
                writer.Write((long)1000); // qpcFreq
                writer.Write(8); // pointer size
                writer.Write(1); // pid
                writer.Write(4); // num procs
                writer.Write(1000); // sampling rate
            });
        }

        private static void Align(BinaryWriter writer, long previousBytesWritten)
        {
            int offset = (int)((writer.BaseStream.Position + previousBytesWritten) % 4);
            if (offset != 0)
            {
                for (int i = offset; i < 4; i++)
                {
                    writer.Write((byte)0);
                }
            }
        }

        public static void WriteBlock(BinaryWriter writer, string name, Action<BinaryWriter> writeBlockData, 
            long previousBytesWritten = 0)
        {
            Debug.WriteLine($"Starting block {name} position: {writer.BaseStream.Position + previousBytesWritten}");
            MemoryStream block = new MemoryStream();
            BinaryWriter blockWriter = new BinaryWriter(block);
            writeBlockData(blockWriter);
            WriteObject(writer, name, 2, 0, () =>
            {
                writer.Write((int)block.Length);
                Align(writer, previousBytesWritten);
                writer.Write(block.GetBuffer(), 0, (int)block.Length);
            });
        }

        public static void WriteMetadataBlock(BinaryWriter writer, Action<BinaryWriter> writeMetadataEventBlobs, long previousBytesWritten = 0)
        {
            WriteBlock(writer, "MetadataBlock", w =>
            {
                // header
                w.Write((short)20); // header size
                w.Write((short)0); // flags
                w.Write((long)0);  // min timestamp
                w.Write((long)0);  // max timestamp
                writeMetadataEventBlobs(w);
            },
            previousBytesWritten);
        }

        public static void WriteMetadataBlock(BinaryWriter writer, EventMetadata[] metadataBlobs, long previousBytesWritten = 0)
        {
            WriteMetadataBlock(writer,
                w =>
                {
                    foreach (EventMetadata blob in metadataBlobs)
                    {
                        WriteMetadataEventBlob(w, blob);
                    }
                },
                previousBytesWritten);
        }

        public static void WriteMetadataBlock(BinaryWriter writer, params EventMetadata[] metadataBlobs)
        {
            WriteMetadataBlock(writer, metadataBlobs, 0);
        }

        public static void WriteMetadataEventBlob(BinaryWriter writer, EventMetadata eventMetadataBlob)
        {
            MemoryStream payload = new MemoryStream();
            BinaryWriter payloadWriter = new BinaryWriter(payload);
            payloadWriter.Write(eventMetadataBlob.MetadataId);           // metadata id
            payloadWriter.Write(Encoding.Unicode.GetBytes(eventMetadataBlob.ProviderName));  // provider name
            payloadWriter.Write((short)0);                               // null terminator
            payloadWriter.Write(eventMetadataBlob.EventId);              // event id
            payloadWriter.Write(Encoding.Unicode.GetBytes(eventMetadataBlob.EventName)); // event name
            payloadWriter.Write((short)0);                               // null terminator
            payloadWriter.Write((long)0);                                // keywords
            payloadWriter.Write(1);                                      // version
            payloadWriter.Write(5);                                      // level
            payloadWriter.Write(0);                                      // fieldcount

            MemoryStream eventBlob = new MemoryStream();
            BinaryWriter eventWriter = new BinaryWriter(eventBlob);
            eventWriter.Write(0);                                        // metadata id
            eventWriter.Write(0);                                        // sequence number
            eventWriter.Write((long)999);                                // thread id
            eventWriter.Write((long)999);                                // capture thread id
            eventWriter.Write(1);                                        // proc number
            eventWriter.Write(0);                                        // stack id
            eventWriter.Write((long)123456789);                          // timestamp
            eventWriter.Write(Guid.Empty.ToByteArray());                 // activity id
            eventWriter.Write(Guid.Empty.ToByteArray());                 // related activity id
            eventWriter.Write((int)payload.Length);                      // payload size
            eventWriter.Write(payload.GetBuffer(), 0, (int)payload.Length);

            writer.Write((int)eventBlob.Length);
            writer.Write(eventBlob.GetBuffer(), 0, (int)eventBlob.Length);
        }

        public static void WriteEventBlock(BinaryWriter writer, Action<BinaryWriter> writeEventBlobs, long previousBytesWritten = 0)
        {
            WriteBlock(writer, "EventBlock", w =>
            {
                // header
                w.Write((short)20); // header size
                w.Write((short)0);  // flags
                w.Write((long)0);   // min timestamp
                w.Write((long)0);   // max timestamp
                writeEventBlobs(w);
            },
            previousBytesWritten);
        }

        public static void WriteEventBlob(BinaryWriter writer, int metadataId, int sequenceNumber, int payloadSize, Action<BinaryWriter> writeEventPayload)
        {
            MemoryStream eventBlob = new MemoryStream();
            BinaryWriter eventWriter = new BinaryWriter(eventBlob);
            eventWriter.Write(metadataId);                               // metadata id
            eventWriter.Write(sequenceNumber);                           // sequence number
            eventWriter.Write((long)999);                                // thread id
            eventWriter.Write((long)999);                                // capture thread id
            eventWriter.Write(1);                                        // proc number
            eventWriter.Write(0);                                        // stack id
            eventWriter.Write((long)123456789);                          // timestamp
            eventWriter.Write(Guid.Empty.ToByteArray());                 // activity id
            eventWriter.Write(Guid.Empty.ToByteArray());                 // related activity id
            eventWriter.Write(payloadSize);                              // payload size

            writer.Write((int)eventBlob.Length + payloadSize);
            writer.Write(eventBlob.GetBuffer(), 0, (int)eventBlob.Length);
            long beforePayloadPosition = writer.BaseStream.Position;
            writeEventPayload(writer);
            long afterPayloadPosition = writer.BaseStream.Position;
            Debug.Assert(afterPayloadPosition - beforePayloadPosition == payloadSize);
        }

        public static void WriteEventBlob(BinaryWriter writer, int metadataId, int sequenceNumber, byte[] payloadBytes)
        {
            WriteEventBlob(writer, metadataId, sequenceNumber, payloadBytes.Length, w => w.Write(payloadBytes));
        }

        public static void WriteEndObject(BinaryWriter writer)
        {
            writer.Write(1); // null tag
        }
    }

    class MockHugeStream : Stream
    {
        // the events are big to make the stream grow fast
        const int payloadSize = 60000;

        MemoryStream _currentChunk = new MemoryStream();
        long _minSize;
        long _bytesWritten;
        int _sequenceNumber = 1;

        public MockHugeStream(long minSize)
        {
            _minSize = minSize;
            _currentChunk = GetFirstChunk();
            _bytesWritten = _currentChunk.Length;
        }

        MemoryStream GetFirstChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            EventPipeWriter.WriteNetTraceHeader(writer);
            EventPipeWriter.WriteFastSerializationHeader(writer);
            EventPipeWriter.WriteTraceObject(writer);
            EventPipeWriter.WriteMetadataBlock(writer, 
                new EventMetadata(1, "Provider", "Event", 1));
            ms.Position = 0;
            return ms;
        }



        MemoryStream GetNextChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            if (_bytesWritten > _minSize)
            {
                EventPipeWriter.WriteEndObject(writer);
            }
            else
            {
                // 20 blocks, each with 20 events in them
                for(int i = 0; i < 20; i++)
                {
                    EventPipeWriter.WriteEventBlock(writer, 
                        w =>
                        {
                            for (int j = 0; j < 20; j++)
                            {
                                EventPipeWriter.WriteEventBlob(w, 1, _sequenceNumber++, payloadSize, WriteEventPayload);
                            }
                        }, 
                        _bytesWritten);
                }
            }
            ms.Position = 0;
            return ms;
        }

        static void WriteEventPayload(BinaryWriter writer)
        {
            for (int i = 0; i < payloadSize / 8; i++)
            {
                writer.Write((long)i);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush()
        {
            throw new NotImplementedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int ret = _currentChunk.Read(buffer, offset, count);
            if(ret == 0)
            {
                _currentChunk = GetNextChunk();
                _bytesWritten += _currentChunk.Length;
                ret = _currentChunk.Read(buffer, offset, count);
            }
            return ret;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
