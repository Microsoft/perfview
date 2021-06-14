using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tracing.EventPipe
{
    unsafe delegate void ParseBufferItemFunction(ref EventPipeEventHeader header);

    internal class EventCache
    {
        public event ParseBufferItemFunction OnEvent;
        public event Action<int> OnEventsDropped;

        public unsafe void ProcessEventBlock(int version, byte[] eventBlockData)
        {
            // parse the header
            if(eventBlockData.Length < 20)
            {
                Debug.Assert(false, "Expected EventBlock of at least 20 bytes");
                return;
            }
            ushort headerSize = BitConverter.ToUInt16(eventBlockData, 0);
            if(headerSize < 20 || headerSize > eventBlockData.Length)
            {
                Debug.Assert(false, "Invalid EventBlock header size");
                return;
            }
            ushort flags = BitConverter.ToUInt16(eventBlockData, 2);
            bool useHeaderCompression = (flags & (ushort)EventBlockFlags.HeaderCompression) != 0;
            bool isULZCompressed = (flags & (ushort)EventBlockFlags.EventBlockULZCompression) != 0;

            int eventBlockSize = eventBlockData.Length;
            if (isULZCompressed && headerSize >= 24)
            {
                int decompressedSize = (int)BitConverter.ToUInt32(eventBlockData, 20); // Decompressed Size is at offset 20
                ArraySegment<byte> retVal = ULZCompression.Decompress(new ArraySegment<byte>(eventBlockData, headerSize, eventBlockData.Length - headerSize), decompressedSize);
                eventBlockData = retVal.Array;
                eventBlockSize = decompressedSize;
            }

            // parse the events
            PinnedBuffer buffer = new PinnedBuffer(eventBlockData);
            byte* cursor = (byte*)buffer.PinningHandle.AddrOfPinnedObject();
            byte* end = cursor + eventBlockSize;

            // if we did compression the byte array was replaced and therefore the cursor doesn't need to be advanced
            if (!isULZCompressed)
            {
                cursor += headerSize;
            }
            
            EventMarker eventMarker = new EventMarker(buffer);
            long timestamp = 0;

            EventPipeEventHeader.ReadFromFormat(version, cursor, useHeaderCompression, ref eventMarker.Header);
            if (!_threads.TryGetValue(eventMarker.Header.CaptureThreadId, out EventCacheThread thread))
            {
                thread = new EventCacheThread();
                thread.SequenceNumber = eventMarker.Header.SequenceNumber - 1;
                AddThread(eventMarker.Header.CaptureThreadId, thread);
            }
            eventMarker = new EventMarker(buffer);
            while (cursor < end)
            {
                EventPipeEventHeader.ReadFromFormat(version, cursor, useHeaderCompression, ref eventMarker.Header);
                bool isSortedEvent = eventMarker.Header.IsSorted;
                timestamp = eventMarker.Header.TimeStamp;
                int sequenceNumber = eventMarker.Header.SequenceNumber;
                if (isSortedEvent)
                {
                    thread.LastCachedEventTimestamp = timestamp;

                    // sorted events are the only time the captureThreadId should change
                    long captureThreadId = eventMarker.Header.CaptureThreadId;
                    if (!_threads.TryGetValue(captureThreadId, out thread))
                    {
                        thread = new EventCacheThread();
                        thread.SequenceNumber = sequenceNumber - 1;
                        AddThread(captureThreadId, thread);
                    }
                }

                int droppedEvents = (int)Math.Min(int.MaxValue, sequenceNumber - thread.SequenceNumber - 1);
                if(droppedEvents > 0)
                {
                    OnEventsDropped?.Invoke(droppedEvents);
                }
                else
                {
                    // When a thread id is recycled the sequenceNumber can abruptly reset to 1 which
                    // makes droppedEvents go negative
                    Debug.Assert(droppedEvents == 0 || sequenceNumber == 1);
                }
                thread.SequenceNumber = sequenceNumber;

                if(isSortedEvent)
                {
                    SortAndDispatch(timestamp);
                    OnEvent?.Invoke(ref eventMarker.Header);
                }
                else
                {
                    thread.Events.Enqueue(eventMarker);

                }

                cursor += eventMarker.Header.TotalNonHeaderSize + eventMarker.Header.HeaderSize;
                EventMarker lastEvent = eventMarker;
                eventMarker = new EventMarker(buffer);
                eventMarker.Header = lastEvent.Header;
            }
            thread.LastCachedEventTimestamp = timestamp;
        }

        public unsafe void ProcessSequencePointBlock(byte[] sequencePointBytes)
        {
            const int SizeOfTimestampAndThreadCount = 12;
            const int SizeOfThreadIdAndSequenceNumber = 12;
            if(sequencePointBytes.Length < SizeOfTimestampAndThreadCount)
            {
                Debug.Assert(false, "Bad sequence point block length");
                return;
            }
            long timestamp = BitConverter.ToInt64(sequencePointBytes, 0);
            int threadCount = BitConverter.ToInt32(sequencePointBytes, 8);
            if(sequencePointBytes.Length < SizeOfTimestampAndThreadCount + threadCount*SizeOfThreadIdAndSequenceNumber)
            {
                Debug.Assert(false, "Bad sequence point block length");
                return;
            }
            SortAndDispatch(timestamp);
            foreach(EventCacheThread thread in _threads.Values)
            {
                Debug.Assert(thread.Events.Count == 0, "There shouldn't be any pending events after a sequence point");
                thread.Events.Clear();
                thread.Events.TrimExcess();
            }

            int cursor = SizeOfTimestampAndThreadCount;
            for(int i = 0; i < threadCount; i++)
            {
                long captureThreadId = BitConverter.ToInt64(sequencePointBytes, cursor);
                int sequenceNumber = BitConverter.ToInt32(sequencePointBytes, cursor + 8);
                if (!_threads.TryGetValue(captureThreadId, out EventCacheThread thread))
                {
                    if(sequenceNumber > 0)
                    {
                        OnEventsDropped?.Invoke(sequenceNumber);
                    }
                    thread = new EventCacheThread();
                    thread.SequenceNumber = sequenceNumber;
                    AddThread(captureThreadId, thread);
                }
                else
                {
                    int droppedEvents = unchecked(sequenceNumber - thread.SequenceNumber);
                    if (droppedEvents > 0)
                    {
                        OnEventsDropped?.Invoke(droppedEvents);
                    }
                    else
                    {
                        // When a thread id is recycled the sequenceNumber can abruptly reset to 1 which
                        // makes droppedEvents go negative
                        Debug.Assert(droppedEvents == 0 || sequenceNumber == 1);
                    }
                    thread.SequenceNumber = sequenceNumber;
                }
                cursor += SizeOfThreadIdAndSequenceNumber;
            }
        }

        /// <summary>
        /// After all events have been parsed we could have some straglers that weren't
        /// earlier than any sorted event. Sort and dispatch those now.
        /// </summary>
        public void Flush()
        {
            SortAndDispatch(long.MaxValue);
        }

        private unsafe void SortAndDispatch(long stopTimestamp)
        {
            // This sort could be made faster by using a min-heap but this is a simple place to start
            List<Queue<EventMarker>> threadQueues = new List<Queue<EventMarker>>(_threads.Values.Select(t => t.Events));
            while(true)
            {
                long lowestTimestamp = stopTimestamp;
                Queue<EventMarker> oldestEventQueue = null;
                foreach(Queue<EventMarker> threadQueue in threadQueues)
                {
                    if(threadQueue.Count == 0)
                    {
                        continue;
                    }
                    long eventTimestamp = threadQueue.Peek().Header.TimeStamp;
                    if (eventTimestamp < lowestTimestamp)
                    {
                        oldestEventQueue = threadQueue;
                        lowestTimestamp = eventTimestamp;
                    }
                }
                if(oldestEventQueue == null)
                {
                    break;
                }
                else
                {
                    EventMarker eventMarker = oldestEventQueue.Dequeue();
                    OnEvent?.Invoke(ref eventMarker.Header);
                }
            }

            // If the app creates and destroys threads over time we need to flush old threads
            // from the cache or memory usage will grow unbounded. AddThread handles the
            // the thread objects but the storage for the queue elements also does not shrink
            // below the high water mark unless we free it explicitly.
            foreach(Queue<EventMarker> q in threadQueues)
            {
                if(q.Count == 0)
                {
                    q.TrimExcess();
                }
            }
        }

        private void AddThread(long captureThreadId, EventCacheThread thread)
        {
            // To ensure we don't have unbounded growth we evict old threads to make room
            // for new ones. Evicted threads can always be re-added later if they log again
            // but there are two consequences:
            // a) We won't detect lost events on that thread after eviction
            // b) If the thread still had events pending dispatch they will be lost
            // We pick the thread that has gone the longest since it last logged an event
            // under the presumption that it is probably dead, has no events, and won't
            // log again.
            //
            // In the future if we had explicit thread death notification events we could keep
            // this cache leaner.
            if(_threads.Count >= 5000)
            {
                long oldestThreadCaptureId = -1;
                long smallestTimestamp = long.MaxValue;
                foreach(var kv in _threads)
                {
                    if(kv.Value.LastCachedEventTimestamp < smallestTimestamp)
                    {
                        smallestTimestamp = kv.Value.LastCachedEventTimestamp;
                        oldestThreadCaptureId = kv.Key;
                    }
                }
                Debug.Assert(oldestThreadCaptureId != -1);
                _threads.Remove(oldestThreadCaptureId);
            }
            _threads[captureThreadId] = thread;
        }

        Dictionary<long, EventCacheThread> _threads = new Dictionary<long, EventCacheThread>();
    }

    internal class EventCacheThread
    {
        public Queue<EventMarker> Events = new Queue<EventMarker>();
        public int SequenceNumber;
        public long LastCachedEventTimestamp;
    }

    internal class EventMarker
    {
        public EventMarker(PinnedBuffer buffer)
        {
            Buffer = buffer;
        }
        public EventPipeEventHeader Header;
        public PinnedBuffer Buffer;
    }

    internal class PinnedBuffer
    {
        public PinnedBuffer(byte[] data)
        {
            Data = data;
            PinningHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        }

        ~PinnedBuffer()
        {
            PinningHandle.Free();
        }

        public byte[] Data { get; private set; }
        public GCHandle PinningHandle { get; private set; }
    }
}
