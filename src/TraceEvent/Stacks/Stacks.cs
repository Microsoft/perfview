// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)

using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;                        // For TextWriter.  
using System.Text;
using System.Threading;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    /// <summary>
    /// A stack source is a logically a list of StackSourceSamples.  Each sample has a metric and stack (hence the name StackSource)
    /// The stacks are represented as indexes that the  StackSourceStacks base class can resolve into frame names and stack chains.  
    /// The result is very efficient (no string processing) way of processing the conceptual list of stack samples.  
    /// </summary>    
    public abstract class StackSource : StackSourceStacks
    {
        /// <summary>
        /// Call 'callback' on every sample in the StackSource.   Will be done linearly and only
        /// one callback will be active simultaneously.  
        /// </summary>
        [Obsolete("Use ForEach")]
        public void ProduceSamples(Action<StackSourceSample> callback) { ForEach(callback); }
        /// <summary>
        /// Call 'callback' on every sample in the StackSource.   Will be done linearly and only
        /// one callback will be active simultaneously.  
        /// </summary>
        public abstract void ForEach(Action<StackSourceSample> callback);
        /// <summary>
        /// If this is overridden to return true, then during the 'Foeach' callback you can save references
        /// to the samples you are given because they will not be overridden by the stack source.  If this is
        /// false you must make a copy of the sample if you with to remember it.  
        /// </summary>
        public virtual bool SamplesImmutable { get { return false; } }
        /// <summary>
        /// Also called 'callback' on every sample in the StackSource however there may be more than
        /// one callback running simultaneously.    Thus 'callback' must be thread-safe and the order
        /// of the samples should not matter.   If desiredParallelism == 0 (the default) then the 
        /// implementation will choose a good value of parallelism. 
        /// </summary>
        public virtual void ParallelForEach(Action<StackSourceSample> callback, int desiredParallelism = 0)
        {
            if (desiredParallelism == 0)
            {
                desiredParallelism = Environment.ProcessorCount * 5 / 4 + 1;
            }

            var freeBlocks = new ConcurrentBag<StackSourceSample[]>();
            bool sampleImmutable = SamplesImmutable;

            // Create a set of workers waiting for work from the dispatcher.  
            var workerQueues = new BlockingCollection<StackSourceSample[]>[desiredParallelism];
            var workers = new Thread[desiredParallelism];
            for (int i = 0; i < workerQueues.Length; i++)
            {
                workerQueues[i] = new BlockingCollection<StackSourceSample[]>(3);
                var worker = workers[i] = new Thread(delegate (object workQueueObj)
                {
                    // Set me priority lower so that the producer can always outrun the consumer.  
                    // The cure may be worse than the disease.   
                    // Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                    var workQueue = (BlockingCollection<StackSourceSample[]>)workQueueObj;
                    for (; ; )
                    {
                        StackSourceSample[] readerSampleBlock;
                        // Trace.WriteLine("Task " + Task.CurrentId + " fetching work");
                        // DebugEventSource.Log.Message(Task.CurrentId ?? 0, workQueue.GetHashCode(), 0, 0, "Waiting");
                        if (!workQueue.TryTake(out readerSampleBlock, -1))
                        {
                            break;
                        }
                        // Trace.WriteLine("Task " + Task.CurrentId + " Consuming Sample " + sample.SampleIndex);
                        // DebugEventSource.Log.Message(Task.CurrentId ?? 0, workQueue.GetHashCode(), workQueue.Count, (int)readerSampleBlock[0].SampleIndex, "Calling callback");

                        for (int j = 0; j < readerSampleBlock.Length; j++)
                        {
                            callback(readerSampleBlock[j]);
                        }

                        freeBlocks.Add(readerSampleBlock);       // Recycle sample object. 
                    }
                });
                worker.Start(workerQueues[i]);
            }

            var curIdx = 0;
            StackSourceSample[] writerSampleBlock = null;
            ForEach(delegate (StackSourceSample sample)
            {
                if (writerSampleBlock == null)
                {
                    freeBlocks.TryTake(out writerSampleBlock);
                    if (writerSampleBlock == null)
                    {
                        writerSampleBlock = new StackSourceSample[1000];
                        if (!sampleImmutable)
                        {
                            for (int i = 0; i < writerSampleBlock.Length; i++)
                            {
                                writerSampleBlock[i] = new StackSourceSample(this);
                            }
                        }
                    }
                }

                if (sampleImmutable)
                {
                    writerSampleBlock[curIdx] = sample;
                }
                else
                {
                    var sampleCopy = writerSampleBlock[curIdx];
                    sampleCopy.Count = sample.Count;
                    sampleCopy.Metric = sample.Metric;
                    sampleCopy.StackIndex = sample.StackIndex;
                    sampleCopy.Scenario = sample.Scenario;
                    sampleCopy.TimeRelativeMSec = sample.TimeRelativeMSec;
                }

                // We have a full block, give it to a worker.  
                curIdx++;
                if (curIdx >= writerSampleBlock.Length)
                {
                    // Add it to someone's work queue
                    int workerNum = BlockingCollection<StackSourceSample[]>.AddToAny(workerQueues, writerSampleBlock);
                    // DebugEventSource.Log.Message(Task.CurrentId ?? 0, workerQueues[workerNum].GetHashCode(), workerQueues[workerNum].Count, (int)writerSampleBlock[0].SampleIndex, "Added to worker");
                    curIdx = 0;
                    writerSampleBlock = null;
                }
            });

            // Indicate to the workers they are done.   This will cause them to exit.  
            for (int i = 0; i < workerQueues.Length; i++)
            {
                workerQueues[i].CompleteAdding();
            }

            // Wait for all my workers to die before returning.  
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i].Join();
            }

            // Write out any stragglers.  (do it after waiting since it keeps them in order (roughly).  
            for (int i = 0; i < curIdx; i++)
            {
                callback(writerSampleBlock[i]);
            }
        }

        // These are optional
        /// <summary>
        /// If this stack source is a source that simply groups another source, get the base source.  It will return
        /// itself if there is no base source.  
        /// </summary>
        public virtual StackSource BaseStackSource { get { return this; } }
        /// <summary>
        /// If this source supports fetching the samples by index, this is how you get it.  Like ForEach the sample that
        /// is returned is not allowed to be modified.   Also the returned sample will become invalid the next time GetSampleIndex
        /// is called (we reuse the StackSourceSample on each call)
        /// </summary>
        public virtual StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex) { return null; }
        /// <summary>
        /// Returns the limit on stack samples indexes (all index are strictly less than this).  Returns 0 if unknown.  
        /// </summary>
        public virtual int SampleIndexLimit { get { return 0; } }
        /// <summary>
        /// Returns a time which is greater than or equal the timestamp of any sample in the StackSource.   Returns 0 if unknown.  
        /// </summary>
        public virtual double SampleTimeRelativeMSecLimit { get { return 0; } }
        /// <summary>
        /// In addition to Time and Metric a sample can have a Scneario number associated with it.   ScenarioCount 
        /// returns the number of such scnearios.   Returning 0 implies no scenario support.  
        /// </summary>
        public virtual int ScenarioCount { get { return 0; } }
        /// <summary>
        /// StackSources can optionally support a sampling rate.   If the source supports it it will return
        /// non-null for the current sampling rate (1 if it is doing nothing).    Sampling is a way of speeding
        /// things up.  If you sample at a rate of 10, it means that only one out of every 10 samples is actually
        /// produced by 'ForEach'.   Note that it is expected that when the sampling rate is set the 
        /// source will correspondingly adjust the CountMultiplier, so that the total will look like no sampling
        /// is occurring 
        /// </summary>
        public virtual float? SamplingRate { get { return null; } set { } }

        // GraphSource Support (optional)
        /// <summary>
        /// If each 'callstack' is really a node in a graph (like MemoryGraphStackSource)
        /// Then return true.  If this returns true 'GetRefs' works. 
        /// </summary>
        public virtual bool IsGraphSource { get { return false; } }
        /// <summary>
        /// Only used if IsGraphSource==true.   If 'direction' is 'From' Calls 'callback' for node that is referred to FROM nodeIndex.
        /// If 'direction' is 'To' then it calls 'callback' for every node that refers TO nodeIndex.  This API returns references 
        /// that are not necessarily a tree (they can for DAGs or have cycles).  
        /// </summary>
        public virtual void GetReferences(StackSourceSampleIndex nodeIndex, RefDirection direction, Action<StackSourceSampleIndex> callback) { }

        /// <summary>
        /// Dump the stack source to a file as XML.   Used for debugging.  
        /// </summary>
        public void Dump(string fileName)
        {
            using (var writer = File.CreateText(fileName))
            {
                Dump(writer);
            }
        }
        /// <summary>
        /// Dump the stack source to a TextWriter as XML.   Used for debugging.  
        /// </summary>
        public void Dump(TextWriter writer)
        {
            writer.WriteLine("<StackSource>");
            writer.WriteLine(" <Samples>");
            ForEach(delegate (StackSourceSample sample)
            {
                writer.Write("  ");
                writer.WriteLine(ToString(sample));
            });
            writer.WriteLine(" </Samples>");
            writer.WriteLine("</StackSource>");
        }
    }

    /// <summary>
    /// RefDirection represents the direction of the references in a heap graph.  
    /// </summary>
    public enum RefDirection
    {
        /// <summary>
        /// Indicates that you are interested in referneces FROM the node of interest
        /// </summary>
        From,
        /// <summary>
        /// Indicates that you are interested in referneces TO the node of interest
        /// </summary>
        To
    };

    /// <summary>
    /// Samples have stacks (lists of frames, each frame contains a name) associated with them.  This interface allows you to get 
    /// at this information.  We don't use normal objects to represent these but rather give each stack (and frame) a unique
    /// (dense) index.   This has a number of advantages over using objects to represent the stack.
    /// 
    ///     * Indexes are very serialization friendly, and this data will be presisted.  Thus indexes are the natural form for data on disk. 
    ///     * It allows the data to be read from the serialized format (disk) lazily in a very straightfoward fashion, keeping only the
    ///         hottest elements in memory.  
    ///     * Users of this API can associate additional data with the call stacks or frames trivially and efficiently simply by
    ///         having an array indexed by the stack or frame index.   
    ///         
    /// So effectively a StackSourceStacks is simply a set of 'Get' methods that allow you to look up information given a Stack or
    /// frame index.  
    /// </summary>
    public abstract class StackSourceStacks
    {
        /// <summary>
        /// Given a call stack, return the call stack of the caller.   This function can return StackSourceCallStackIndex.Discard
        /// which means that this sample should be discarded.  
        /// </summary>
        public abstract StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex);
        /// <summary>
        /// For efficiency, m_frames are assumed have a integer ID instead of a string name that
        /// is unique to the frame.  Note that it is expected that GetFrameIndex(x) == GetFrameId(y) 
        /// then GetFrameName(x) == GetFrameName(y).   The converse does NOT have to be true (you 
        /// can reused the same name for distinct m_frames, however this can be confusing to your
        /// users, so be careful.  
        /// </summary>
        public abstract StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex);
        /// <summary>
        /// FilterStackSources can combine more than one frame into a given frame.  It is useful to know
        /// how many times this happened.   Returning 0 means no combining happened.  This metric does
        /// not include grouping, but only folding.  
        /// </summary>
        public virtual int GetNumberOfFoldedFrames(StackSourceCallStackIndex callStackIndex)
        {
            return 0;
        }
        /// <summary>
        /// Get the frame name from the FrameIndex.   If 'verboseName' is true then full module path is included.
        /// </summary>
        public abstract string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName);
        /// <summary>
        /// all StackSourceCallStackIndex are guaranteed to be less than this.  Allocate an array of this size to associate side information
        /// </summary>
        public abstract int CallStackIndexLimit { get; }
        /// <summary>
        /// all StackSourceFrameIndex are guaranteed to be less than this.  Allocate an array of this size to associate side information
        /// </summary>
        public abstract int CallFrameIndexLimit { get; }
        /// <summary>
        /// True if it only has managed code stacks. Otherwise false.
        /// </summary>
        public virtual bool OnlyManagedCodeStacks { get; set; } = false;

        /// <summary>
        /// Computes the depth (number of callers), associated with callStackIndex.  This routine is O(n) and mostly useful for debugging.  
        /// </summary>
        public int StackDepth(StackSourceCallStackIndex callStackIndex)
        {
            int ret = 0;
            while (callStackIndex != StackSourceCallStackIndex.Invalid)
            {
                callStackIndex = GetCallerIndex(callStackIndex);
                ret++;
            }
            return ret;
        }

        /// <summary>
        /// Returns an XML string representation of a 'sample'.  For debugging. 
        /// </summary>
        public string ToString(StackSourceSample sample, StringBuilder sb = null)
        {
            if (sb == null)
            {
                sb = new StringBuilder();
            }

            sb.Append("<StackSourceSample");
            sb.Append(" Metric=\"").Append(sample.Metric.ToString("f1")).Append('"');
            sb.Append(" TimeRelativeMSec=\"").Append(sample.TimeRelativeMSec.ToString("n3")).Append('"');
            sb.Append(" SampleIndex=\"").Append(sample.SampleIndex.ToString()).Append('"');
            sb.Append(">").AppendLine();
            sb.AppendLine(ToString(sample.StackIndex));
            sb.Append("</StackSourceSample>");
            return sb.ToString();
        }
        /// <summary>
        /// Returns an XML string representation of a 'callStackIndex'.  For debugging. 
        /// </summary>
        public string ToString(StackSourceCallStackIndex callStackIndex)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(" <CallStack Index =\"").Append((int)callStackIndex).Append("\">").AppendLine();
            for (int i = 0; callStackIndex != StackSourceCallStackIndex.Invalid; i++)
            {
                if (i >= 300)
                {
                    sb.AppendLine("  <Truncated/>");
                    break;
                }
                sb.Append(ToString(GetFrameIndex(callStackIndex), callStackIndex)).AppendLine();
                callStackIndex = GetCallerIndex(callStackIndex);
            }
            sb.Append(" </CallStack>");
            return sb.ToString();
        }
        private string ToString(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex stackIndex = StackSourceCallStackIndex.Invalid)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("  <Frame");
            if (stackIndex != StackSourceCallStackIndex.Invalid)
            {
                sb.Append(" StackID=\"").Append(((int)stackIndex).ToString()).Append("\"");
            }

            sb.Append(" FrameID=\"").Append(((int)frameIndex).ToString()).Append("\"");
            sb.Append(" Name = \"").Append(XmlUtilities.XmlEscape(GetFrameName(frameIndex, false))).Append("\"");
            sb.Append("/>");
            return sb.ToString();
        }
    }

    /// <summary>
    /// StackSourceSample represents a single sample that has a stack.   It has a number of predefined data items associate with it
    /// including a stack, a metric and a time as well as other optional fields.  Note that all its properties are read-write.  
    /// It is basically a named tuple. 
    /// 
    /// StackSource.ProductSamples push these.  
    /// 
    /// In general StackSourceSample are NOT immutable but expected to be overwritted frequently.  Thus you need to copy 
    /// the sample if you want to keep a reference to it.      
    /// </summary>
    public class StackSourceSample
    {
        /// <summary>
        /// The Stack associated with the sample 
        /// </summary>
        public StackSourceCallStackIndex StackIndex { get; set; }
        /// <summary>
        /// The metric (cost) associated with the sample 
        /// </summary>
        public float Metric { get; set; }

        // The rest of these are optional. 
        /// <summary>
        /// If the source supports fetching samples by some ID, then SampleIndex returns this ID for the sample and 
        /// GetSampleByIndex is the API that converts this index into a sample again.  
        /// </summary>
        public StackSourceSampleIndex SampleIndex { get; set; }
        /// <summary>
        /// The time associated with the sample.  (can be left 0)
        /// </summary>
        public double TimeRelativeMSec { get; set; }

        /// <summary>
        /// Normally the count of a sample is 1, however when you take a statistical sample, and you also have 
        /// other constraints (like you do when you are going a sample of heap memory),  you may need to have the
        /// count adjusted to something else.
        /// </summary>
        public float Count { get; set; }
        /// <summary>
        /// A scenario is simply a integer that represents some group the sample belongs to. 
        /// </summary>
        public int Scenario { get; set; }

        /// <summary>
        /// Returns an XML string representing the sample
        /// </summary>
        public override string ToString()
        {
            return String.Format("<Sample Metric=\"{0:f1}\" TimeRelativeMSec=\"{1:f3}\" StackIndex=\"{2}\" SampleIndex=\"{3}\">",
                Metric, TimeRelativeMSec, StackIndex, SampleIndex);
        }
        /// <summary>
        /// Returns an XML string representing the sample, howevever this one can actually expand the stack because it is given the source
        /// </summary>
        public string ToString(StackSourceStacks source)
        {
            return source.ToString(this);
        }

        /// <summary>
        /// Create a StackSourceSample which is associated with 'source'.  
        /// </summary>
        public StackSourceSample(StackSource source) { SampleIndex = StackSourceSampleIndex.Invalid; Count = 1; }
        /// <summary>
        /// Copy a StackSourceSample from 'template'
        /// </summary>
        /// <param name="template"></param>
        public StackSourceSample(StackSourceSample template)
        {
            StackIndex = template.StackIndex;
            Metric = template.Metric;
            TimeRelativeMSec = template.TimeRelativeMSec;
            SampleIndex = template.SampleIndex;
            Scenario = template.Scenario;
            Count = template.Count;
        }
    }

    /// <summary>
    /// Identifies a particular sample from the sample source, it allows 3rd parties to attach additional
    /// information to the sample by creating an array indexed by sampleIndex.  
    /// </summary>
    public enum StackSourceSampleIndex
    {
        /// <summary>
        /// Returned when no appropriate Sample exists.  
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// An opaque handle that are 1-1 with a complete call stack
    /// 
    /// </summary>
    public enum StackSourceCallStackIndex
    {
        /// <summary>
        /// The first real call stack index (after the pseudo-ones before this)
        /// </summary>
        Start = 0,
        /// <summary>
        /// Returned when no appropriate CallStack exists.   (Top of stack)
        /// </summary>
        Invalid = -1,
    };

    /// <summary>
    /// Identifies a particular frame within a stack   It represents a particular instruction pointer (IP) location 
    /// in the code or a group of such locations.  
    /// </summary>
    public enum StackSourceFrameIndex
    {
        /// <summary>
        /// Pseduo-node representing the root of all stacks
        /// </summary>
        Root = 0,
        /// <summary>
        /// Pseduo-frame that represents the caller of all broken stacks. 
        /// </summary>
        Broken = 1,
        /// <summary>
        /// Unknown what to do (Must be before the 'special ones below')  // Non negative represents normal m_frames (e.g. names of methods)
        /// </summary>
        Unknown = 2,
        /// <summary>
        ///  Profiling overhead (rundown)
        /// </summary>
        Overhead = 3,
        /// <summary>
        /// The first real call stack index (after the pseudo-ones before this)
        /// </summary>
        Start = 4,
        /// <summary>
        /// Should not happen (uninitialized) (also means completely folded away)
        /// </summary>
        Invalid = -1,
        /// <summary>
        /// Sample has been filtered out (useful for filtering stack sources)
        /// </summary>
        Discard = -2,
    };

    /// <summary>
    /// A StackSourceModuleIndex uniquely identifies a module to the stack source.  
    /// </summary>
    public enum StackSourceModuleIndex
    {
        /// <summary>
        /// Start is where 'ordinary' module indexes start. 
        /// </summary>
        Start = 0,
        /// <summary>
        /// Invalid is a module index that is never used and can be used to signal error conditions. 
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// This stack source takes another and copies out all its events.   This allows you to 'replay' the source 
    /// efficiently when the original source only does this inefficiently.  
    /// </summary>
    public class CopyStackSource : StackSource
    {
        /// <summary>
        /// Create a CopyStackSource that has no samples in it.  It can never have samples so it is only useful as a placeholder.  
        /// </summary>
        public CopyStackSource() { }
        /// <summary>
        /// Create a CopyStackSource that you can add samples which use indexes that 'sourceStacks' can decode.   All samples
        /// added to the stack source must only refer to this StackSourceStacks
        /// </summary>
        public CopyStackSource(StackSourceStacks sourceStacks)
        {
            m_sourceStacks = sourceStacks;
        }
        /// <summary>
        /// Add a sample to stack source.  it will clone 'sample' so sample can be overwritten after this method returns.  
        /// It is an error if 'sample' does not used the StackSourceStacks passed to the CopyStackSource at construction. 
        /// </summary>
        public StackSourceSample AddSample(StackSourceSample sample)
        {
            // TODO assert that the samples are associated with this source.  
            var sampleCopy = new StackSourceSample(sample);
            sampleCopy.SampleIndex = (StackSourceSampleIndex)m_samples.Count;
            m_samples.Add(sampleCopy);
            var endTime = sampleCopy.TimeRelativeMSec + sampleCopy.Metric;   // Add in the metric for the metric as time case it is OK to overestimate slightly 
            if (endTime > m_sampleTimeRelativeMSecLimit)
            {
                m_sampleTimeRelativeMSecLimit = endTime;
            }

            return sampleCopy;
        }
        /// <summary>
        /// Create a clone of the given stack soruce.  
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static CopyStackSource Clone(StackSource source)
        {
            var ret = new CopyStackSource(source);
            source.ForEach(delegate (StackSourceSample sample)
            {
                ret.AddSample(sample);
            });
            return ret;
        }

        /// <summary>
        /// Returns the StackSourceStacks that can interpret indexes for this stack source.  
        /// </summary>
        public StackSourceStacks SourceStacks { get { return m_sourceStacks; } }

        #region overrides
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            return m_samples[(int)sampleIndex];
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override int SampleIndexLimit
        {
            get { return m_samples.Count; }
        }

        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override double SampleTimeRelativeMSecLimit { get { return m_sampleTimeRelativeMSecLimit; } }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override void ForEach(Action<StackSourceSample> callback)
        {
            for (int i = 0; i < m_samples.Count; i++)
            {
                callback(m_samples[i]);
            }
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override bool SamplesImmutable { get { return true; } }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_sourceStacks.GetCallerIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_sourceStacks.GetFrameIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            return m_sourceStacks.GetFrameName(frameIndex, verboseName);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override int CallStackIndexLimit
        {
            get { if (m_sourceStacks == null) { return 0; } return m_sourceStacks.CallStackIndexLimit; }
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override int CallFrameIndexLimit
        {
            get { if (m_sourceStacks == null) { return 0; } return m_sourceStacks.CallFrameIndexLimit; }
        }
        #endregion
        #region private
        internal /*protected*/ GrowableArray<StackSourceSample> m_samples;
        internal /*protected*/ double m_sampleTimeRelativeMSecLimit;
        internal /*protected*/ StackSourceStacks m_sourceStacks;
        #endregion
    }

    /// <summary>
    /// Like CopyStackSource InternStackSource copies the samples. however unlike CopyStackSource
    /// InternStackSource copies all the information in the stacks too (mapping stack indexes to names)
    /// Thus it never refers to the original source again).   It also interns the stacks making for 
    /// an efficient representation of the data.   This is useful when the original source is expensive 
    /// to iterate over.   
    /// </summary>
    public class InternStackSource : CopyStackSource
    {
        /// <summary>
        /// Compute the difference between two sources of stacks.
        /// </summary>
        public static InternStackSource Diff(StackSource source, StackSource baselineSource)
        {
            return Diff(source, source, baselineSource, baselineSource);
        }
        /// <summary>
        /// Compute only the delta of source from the baseline.  This variation allows you to specify
        /// the unfiltered names (the sourceStacks and baselineStacks) but otherwise keep the filtering.  
        /// </summary>
        public static InternStackSource Diff(StackSource source, StackSourceStacks sourceStacks,
                                            StackSource baselineSource, StackSourceStacks baselineStacks)
        {
            // The ability to pass the StackSourceStacks is really just there to bypass grouping
            Debug.Assert(source == sourceStacks || source.BaseStackSource == sourceStacks);
            Debug.Assert(baselineSource == baselineStacks || baselineSource.BaseStackSource == baselineStacks);

            var ret = new InternStackSource();

            ret.ReadAllSamples(source, sourceStacks, 1.0F);
            ret.ReadAllSamples(baselineSource, baselineStacks, -1.0F);

#if false
            // Turn on for diffing between XPERF and PerfView 

            // This code will throw away any samples that are not 'close' to a 'negative' sample.
            // It was designed SPECIFICALLY to allow comparisons between PerfView decoded ETL files
            // and XPERF created CSVZ files (we consider XPERF the gold standard). 
            
            // The basic problem is that we KNOW that XPERF also has problem decoding stacks all the time
            // and we get some stacks XPERF does not (confirmed by hand analysis).   Moreover, the CSVZ
            // processor will NOT show any samples that don't have stacks.  Thus the easiest way to deal
            // with this is to simply look at the samples that both have.  That is what this code does

            // You can confirm that XperfView does get exactly what PerfView does for total and exclusive counts.
            // Doing a diff more than that is time consuming.  

            // Sort by time assending, then by metric decending (positive before baseline)
            ret.m_samples.Sort(delegate(StackSourceSample x, StackSourceSample y)
            {
                return x.TimeRelativeMSec.CompareTo(y.TimeRelativeMSec);
            });

            var toPtr = 0;
            for (int i = 0; i < ret.m_samples.Count - 1; )
            {
                if (Math.Abs(ret.m_samples[i].TimeRelativeMSec - ret.m_samples[i + 1].TimeRelativeMSec) < 0.1)
                {
                    Debug.Assert(ret.m_samples[i].Metric + ret.m_samples[i + 1].Metric == 0);
                    ret.m_samples[toPtr++] = ret.m_samples[i++];
                    ret.m_samples[toPtr++] = ret.m_samples[i++];
                }
                else
                    i++;
            }
            ret.m_samples.Count = toPtr;
#endif
            ret.Interner.DoneInterning();
            return ret;
        }

        /// <summary>
        /// Create a new stack source that can create things out of nothing.  
        /// </summary>
        public InternStackSource(StackSource source, StackSourceStacks sourceStacks)
            : this()
        {
            ReadAllSamples(source, sourceStacks, 1.0F);
            Interner.DoneInterning();
        }

        /// <summary>
        /// Create a new InternStackSource
        /// </summary>
        public InternStackSource()
        {
            Interner = new StackSourceInterner();
        }
        /// <summary>
        /// Returns the Interner, which is the class that holds the name->index mappings that that every
        /// name has a unique index.  
        /// </summary>
        public StackSourceInterner Interner { get; private set; }

        #region overrides
        // TODO this should be added to the stack source interface and overriden here 
#if false 
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public StackSourceModuleIndex GetModuleIndex(StackSourceFrameIndex frameIndex)
        {
            return Interner.GetModuleIndex(frameIndex);
        }
#endif

        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return Interner.GetCallerIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return Interner.GetFrameIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            // TODO does this belong in the interner?
            if (frameIndex < StackSourceFrameIndex.Start)
            {
                if (frameIndex == StackSourceFrameIndex.Broken)
                {
                    return "BROKEN";
                }
                else if (frameIndex == StackSourceFrameIndex.Overhead)
                {
                    return "OVERHEAD";
                }
                else if (frameIndex == StackSourceFrameIndex.Root)
                {
                    return "ROOT";
                }
                else
                {
                    return "?!?";
                }
            }
            return Interner.GetFrameName(frameIndex, verboseName);
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override int CallStackIndexLimit
        {
            get { return (int)StackSourceCallStackIndex.Start + Interner.CallStackCount; }
        }
        /// <summary>
        /// Implementation of the StackSource interface
        /// </summary>
        public override int CallFrameIndexLimit
        {
            get { return (int)(StackSourceFrameIndex.Start + Interner.FrameCount); }
        }
        #endregion

        #region private

        internal void ReadAllSamples(StackSource source, StackSourceStacks stackLookup, float scaleFactor)
        {
            var ctr = 0;
            source.ForEach(delegate (StackSourceSample sample)
            {
                var sampleCopy = new StackSourceSample(sample);
                if (scaleFactor != 1.0F)
                {
                    sampleCopy.Metric *= scaleFactor;
                    if (scaleFactor < 0)
                    {
                        sampleCopy.Count = -sampleCopy.Count;
                    }
                }
                sampleCopy.SampleIndex = (StackSourceSampleIndex)m_samples.Count;
                sampleCopy.StackIndex = InternFullStackFromSource(sampleCopy.StackIndex, stackLookup);
                m_samples.Add(sampleCopy);
                var endTime = sampleCopy.TimeRelativeMSec + Math.Abs(sampleCopy.Metric);  // Add in metric in case this is a time metric.  
                if (endTime > m_sampleTimeRelativeMSecLimit)
                {
                    m_sampleTimeRelativeMSecLimit = endTime;
                }

                if (ctr > 8192)
                {
                    System.Threading.Thread.Sleep(0);       // allow interruption
                    ctr = 0;
                }
            });
        }

        /// <summary>
        /// InternFullStackFromSource will take a call stack 'baseCallStackIndex' from the source 'source' and completely copy it into
        /// the intern stack source (interning along the way of course).   Logically baseCallStackIndex has NOTHING to do with any of the
        /// call stack indexes in the intern stack source.  
        /// </summary>
        private StackSourceCallStackIndex InternFullStackFromSource(StackSourceCallStackIndex baseCallStackIndex, StackSourceStacks source, int maxDepth = 1000)
        {
            // To avoid stack overflows.  
            if (maxDepth < 0)
            {
                return StackSourceCallStackIndex.Invalid;
            }

            if (baseCallStackIndex == StackSourceCallStackIndex.Invalid)
            {
                return StackSourceCallStackIndex.Invalid;
            }

            var baseCaller = source.GetCallerIndex(baseCallStackIndex);
            var baseFrame = source.GetFrameIndex(baseCallStackIndex);

            var baseFullFrameName = source.GetFrameName(baseFrame, true);
            var moduleName = "";
            var frameName = baseFullFrameName;
            var index = baseFullFrameName.IndexOf('!');
            if (index >= 0)
            {
                moduleName = baseFullFrameName.Substring(0, index);
                frameName = baseFullFrameName.Substring(index + 1);
            }

            var myModuleIndex = Interner.ModuleIntern(moduleName);
            var myFrameIndex = Interner.FrameIntern(frameName, myModuleIndex);
            var ret = Interner.CallStackIntern(myFrameIndex, InternFullStackFromSource(baseCaller, source, maxDepth - 1));
            return ret;
        }
        #endregion
    }

    /// <summary>
    /// StackSourceInterner is a helper class that knows how to intern module, frame and call stacks. 
    /// </summary>
    public class StackSourceInterner
    {
        /// <summary>
        /// Create a new StackSourceInterner.  Optionally supply estimates on how many items you need and where the frame, callstack and module indexes start.    
        /// </summary>
        public StackSourceInterner(
            int estNumCallStacks = 5000, int estNumFrames = 1000, int estNumModules = 100,
            StackSourceFrameIndex frameStartIndex = StackSourceFrameIndex.Start,
            StackSourceCallStackIndex callStackStartIndex = StackSourceCallStackIndex.Start,
            StackSourceModuleIndex moduleStackStartIndex = StackSourceModuleIndex.Start)
        {
            m_moduleIntern = new InternTable<string>(estNumModules);
            m_frameIntern = new InternTable<FrameInfo>(estNumFrames);
            m_callStackIntern = new InternTable<CallStackInfo>(estNumCallStacks);

            if (frameStartIndex < StackSourceFrameIndex.Start)
            {
                frameStartIndex = StackSourceFrameIndex.Start;
            }

            if (callStackStartIndex < StackSourceCallStackIndex.Start)
            {
                callStackStartIndex = StackSourceCallStackIndex.Start;
            }

            if (moduleStackStartIndex < StackSourceModuleIndex.Start)
            {
                moduleStackStartIndex = StackSourceModuleIndex.Start;
            }

            m_frameStartIndex = frameStartIndex;
            m_callStackStartIndex = callStackStartIndex;
            m_moduleStackStartIndex = moduleStackStartIndex;
            m_emptyModuleIdx = ModuleIntern("");
        }
        /// <summary>
        /// As an optimization, if you are done adding new nodes, then you can call this routine can abandon
        /// some tables only needed during the interning phase.
        /// </summary>
        public void DoneInterning()
        {
            m_moduleIntern.DoneInterning();
            m_frameIntern.DoneInterning();
            m_callStackIntern.DoneInterning();
        }

        /// <summary>
        /// The CallStackStartIndex value passed to the constructor
        /// </summary>
        public StackSourceCallStackIndex CallStackStartIndex { get { return m_callStackStartIndex; } }

        /// <summary>
        /// The FrameStartIndex value passed to the constructor
        /// </summary>
        public StackSourceFrameIndex FrameStartIndex { get { return m_frameStartIndex; } }

        // used to get info about previously interned data. 
        /// <summary>
        /// Given a StackSourceCallStackIndex return the StackSourceCallStackIndex of the caller
        /// </summary>
        public StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_callStackIntern[callStackIndex - m_callStackStartIndex].callerIndex;
        }
        /// <summary>
        /// Given a StackSourceCallStackIndex return the StackSourceFrameIndex for the Frame associated
        /// with the top call stack
        /// </summary>
        public StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_callStackIntern[callStackIndex - m_callStackStartIndex].frameIndex;
        }
        /// <summary>
        /// Get a name from a frame index.  If the frame index is a 
        /// </summary>
        public string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
        {
            var frameIndexOffset = (int)(frameIndex - m_frameStartIndex);
            Debug.Assert(0 <= frameIndexOffset && frameIndexOffset < m_frameIntern.Count);
            var frameName = m_frameIntern[frameIndexOffset].FrameName;
            var baseFrameIndex = m_frameIntern[frameIndexOffset].BaseFrameIndex;
            if (baseFrameIndex != StackSourceFrameIndex.Invalid)
            {
                string baseName;
                if (FrameNameLookup != null)
                {
                    baseName = FrameNameLookup(baseFrameIndex, fullModulePath);
                }
                else
                {
                    baseName = "Frame " + ((int)baseFrameIndex).ToString();
                }

                return baseName + " " + frameName;
            }
            var moduleName = m_moduleIntern[m_frameIntern[frameIndexOffset].ModuleIndex - m_moduleStackStartIndex];
            if (moduleName.Length == 0)
            {
                return frameName;
            }

            if (!fullModulePath)
            {
                var lastDirectorySep = moduleName.LastIndexOfAny(s_directorySeparators);
                if (0 <= lastDirectorySep)
                {
                    moduleName = moduleName.Substring(lastDirectorySep + 1);
                }
            }
            // Remove a .dll or .exe extension 
            // TODO should we be doing this here?  This feels like a presentation transformation
            // and we are at the semantic model layer.  
            int lastDot = moduleName.LastIndexOf('.');
            if (lastDot == moduleName.Length - 4 &&
                (moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                moduleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                moduleName = moduleName.Substring(0, lastDot);
            }

            return moduleName + "!" + frameName;
        }
        /// <summary>
        /// Given a StackSourceFrameIndex return the StackSourceModuleIndex associated with the frame 
        /// </summary>
        /// <param name="frameIndex"></param>
        /// <returns></returns>
        public StackSourceModuleIndex GetModuleIndex(StackSourceFrameIndex frameIndex)
        {
            var framesIndex = frameIndex - m_frameStartIndex;
            Debug.Assert(frameIndex >= 0);
            return m_frameIntern[framesIndex].ModuleIndex;
        }

        /// <summary>
        /// If you intern frames as derived frames, when GetFrameName is called the interner needs to know
        /// how to look up the derived frame from its index.  This is the function that is called.  
        /// 
        /// It is called with the frame index and a boolean which indicates whether the full path of the module 
        /// should be specified, and returns the frame string. 
        /// </summary>
        public Func<StackSourceFrameIndex, bool, string> FrameNameLookup { get; set; }

        // Used to create new nodes 
        /// <summary>
        /// Lookup or create a StackSourceModuleIndex for moduleName
        /// </summary>
        public StackSourceModuleIndex ModuleIntern(string moduleName)
        {
            return m_moduleIntern.Intern(moduleName) + m_moduleStackStartIndex;
        }
        /// <summary>
        /// Lookup or create a StackSourceFrameIndex for frame with the name frameName and the module identified by moduleIndex
        /// </summary>
        public StackSourceFrameIndex FrameIntern(string frameName, StackSourceModuleIndex moduleIndex = StackSourceModuleIndex.Invalid)
        {
            // An invalid module index will be treated as empty.  
            if (moduleIndex == StackSourceModuleIndex.Invalid)
            {
                moduleIndex = m_emptyModuleIdx;
            }

            Debug.Assert(frameName != null);
            return m_frameIntern.Intern(new FrameInfo(frameName, moduleIndex)) + m_frameStartIndex;
        }

        /// <summary>
        /// You can also create frames out of other frames using this method.  Given an existing frame, and
        /// a suffix 'frameSuffix' 
        /// </summary>
        public StackSourceFrameIndex FrameIntern(StackSourceFrameIndex frameIndex, string frameSuffix)
        {
            // In order to use this, you must 
            Debug.Assert(FrameNameLookup != null);
            Debug.Assert(frameSuffix != null);
            return m_frameIntern.Intern(new FrameInfo(frameSuffix, frameIndex)) + m_frameStartIndex;
        }
        /// <summary>
        /// Lookup or create a StackSourceCallStackIndex for a call stack with the frame identified frameIndex and caller identified by callerIndex
        /// </summary>
        public StackSourceCallStackIndex CallStackIntern(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex callerIndex)
        {
            return m_callStackIntern.Intern(new CallStackInfo(frameIndex, callerIndex)) + m_callStackStartIndex;
        }

        /// <summary>
        /// The current number of unique frames that have been interned so far
        /// </summary>
        public int FrameCount { get { return m_frameIntern.Count; } }
        /// <summary>
        /// The current number of unique call stacks that have been interned so far
        /// </summary>
        public int CallStackCount { get { return m_callStackIntern.Count; } }

        #region private
        private struct FrameInfo : IEquatable<FrameInfo>
        {
            public FrameInfo(string frameName, StackSourceModuleIndex moduleIndex)
            {
                ModuleIndex = moduleIndex;
                FrameName = frameName;
                BaseFrameIndex = StackSourceFrameIndex.Invalid;
            }
            public FrameInfo(string frameSuffix, StackSourceFrameIndex baseFrame)
            {
                ModuleIndex = StackSourceModuleIndex.Invalid;
                BaseFrameIndex = baseFrame;
                FrameName = frameSuffix;
            }
            // TODO we could make this smaller if we care since BaseFrame and ModuleIndex are never used together.  
            public readonly StackSourceFrameIndex BaseFrameIndex;
            public readonly StackSourceModuleIndex ModuleIndex;
            public readonly string FrameName;       // This is the suffix if this is a derived frame

            public override int GetHashCode()
            {
                return (int)ModuleIndex + (int)BaseFrameIndex + FrameName.GetHashCode();
            }
            public override bool Equals(object obj) { throw new NotImplementedException(); }
            public bool Equals(FrameInfo other)
            {
                return ModuleIndex == other.ModuleIndex && BaseFrameIndex == other.BaseFrameIndex && FrameName == other.FrameName;
            }
        }
        private struct CallStackInfo : IEquatable<CallStackInfo>
        {
            public CallStackInfo(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex callerIndex)
            {
                this.frameIndex = frameIndex;
                this.callerIndex = callerIndex;
            }
            public readonly StackSourceFrameIndex frameIndex;
            public readonly StackSourceCallStackIndex callerIndex;

            public override int GetHashCode()
            {
                return (int)callerIndex + (int)frameIndex * 0x10000;
            }
            public override bool Equals(object obj) { throw new NotImplementedException(); }
            public bool Equals(CallStackInfo other)
            {
                return frameIndex == other.frameIndex && callerIndex == other.callerIndex;
            }
        };

        /// <summary>
        /// A specialized hash table for interning.
        /// It loosely follows the implementation of <see cref="Dictionary{TKey, TValue}"/> but with
        /// several key allowances for known usage patterns:
        /// 1. We don't store the hashcode on each entry on the assumption that values can be compared
        ///    as quickly as recomputing hash codes. The downside to that is that the hash codes must
        ///    be recomputed whenever the map is resized, but that is very cheap.
        /// 2. We supply a single <see cref="Intern(T)"/> method (instead of a TryGetValue
        ///    followed by an Add) so that a hashcode computation is saved in the case of a "miss".
        /// 3. We don't support removal. This means we don't need to keep track of a free list and neither
        ///    do we need sentinel values. This also allows us to use all 32 bits of the hash-code (where
        ///    <see cref="Dictionary{TKey, TValue}"/> uses only 31 bits, reserving -1 to indicate a freed
        ///    entry. The only sentinel value is in the <see cref="_buckets"/> array to indicate a free
        ///    bucket.
        /// 4. We return an index (of the interned item) to the caller which can be used for constant-time
        ///    look-up in the table via <see cref="this[int]"/>.
        /// 5. To free up memory, the caller can call <see cref="DoneInterning"/>. The entries themselves
        ///    are stored separately from the indexing parts of the table so that the latter can be dropped
        ///    easily.
        /// </summary>
        private class InternTable<T> where T : IEquatable<T>
        {
            /// <summary>
            /// Construct the intern map
            /// </summary>
            /// <param name="initialCapacity">The estimated capacity of the map.</param>
            public InternTable(int initialCapacity = 0)
            {
                Resize(desiredSize: initialCapacity);
            }

            /// <summary>
            /// Count of interned values.
            /// </summary>
            public int Count
            {
                get { return _count; }
            }

            /// <summary>
            /// Access an element by index.
            /// </summary>
            /// <param name="index">The zero-based index of the desired entry.</param>
            /// <returns>The entry at the requested index.</returns>
            /// <remarks>For performance, in Release mode we do no range checking on <paramref name="index"/>, so it is possible to
            /// access an entry beyond <see cref="Count"/> but prior to the maximum capacity of the array.</remarks>
            /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> was less than zero or greater than the capacity.</exception>
            public T this[int index]
            {
                get
                {
                    Debug.Assert(index < _count);
                    return _entries[index];
                }
            }

            /// <summary>
            /// Intern a value. If the same value has been seen before
            /// then this returns the index of the previously seen entry. If not, a new entry
            /// is added and this returns the index of the newly added entry.
            /// </summary>
            /// <param name="value">The candidate value.</param>
            /// <returns>The index of the interned entry.</returns>
            /// <exception cref="NullReferenceException">This routine was called after calling <see cref="DoneInterning"/>.</exception>
            public int Intern(T value)
            {
                int targetBucket = BucketNumberFromValue(value);
                int index;
                for (index = _buckets[targetBucket]._head; index >= 0; index = _buckets[index]._next)
                {
                    if (_entries[index].Equals(value))
                    {
                        // Found
                        return index;
                    }
                }

                // Grow if necessary
                if (_count == _entries.Length)
                {
                    int newsize = _count * 2 + 1;
                    if (newsize <= _count)
                    {
                        newsize = int.MaxValue;
                    }

                    if (newsize <= _count)          // _count == int.MaxValue;
                    {
                        throw new OutOfMemoryException();
                    }

                    Resize(newsize); // Simple doubling (geometric growth)
                    targetBucket = BucketNumberFromValue(value);
                }

                index = _count++;
                _entries[index] = value;
                _buckets[index]._next = _buckets[targetBucket]._head;
                _buckets[targetBucket]._head = index;
                return index;
            }

            /// <summary>
            /// As an optimization, if you are done calling <see cref="Intern(T)"/>, then you can call this
            /// to free up some memory.
            /// </summary>
            /// <remarks>After calling this, you can still call <see cref="this[int]"/>. However, if you try to
            /// call <see cref="Intern(T)"/> you will get a <see cref="NullReferenceException"/>.</remarks>
            public void DoneInterning()
            {
                _buckets = null;

                // Trim _entries if it's less than 75% full (watching out for overflow) 
                if (_count < (_entries.Length / 4 * 3))
                {
                    Array.Resize(ref _entries, _count);
                }
            }

            private int BucketNumberFromValue(T value)
            {
                return BucketNumberFromValue(value, _buckets.Length);
            }

            private static int BucketNumberFromValue(T value, int bucketCount)
            {
                int hashCode = value.GetHashCode();
                uint targetBucket = (uint)hashCode % (uint)bucketCount;
                return (int)targetBucket;
            }

            private void Resize(int desiredSize)
            {
                var newBuckets = new Bucket[desiredSize];
                for (int i = 0; i < newBuckets.Length; i++)
                {
                    newBuckets[i]._head = -1;
                }

                var newEntries = new T[desiredSize];
                if (_entries != null)
                {
                    Array.Copy(_entries, 0, newEntries, 0, _count);
                    // Regenerate the index
                    for (int i = 0; i < _count; i++)
                    {
                        int bucket = BucketNumberFromValue(newEntries[i], desiredSize);
                        newBuckets[i]._next = newBuckets[bucket]._head;
                        newBuckets[bucket]._head = i;
                    }
                }

                _buckets = newBuckets;
                _entries = newEntries;
            }

            /// <summary>
            /// Elements representing the structure of the hash table. The structure is
            /// a collection of singly linked lists, one list per 'bucket' where a
            /// bucket number is selected by taking the hash code of an incoming item
            /// and mapping it onto the <see cref="_buckets"/> array (see <see cref="BucketNumberFromValue(T)"/>).
            /// </summary>
            /// <remarks>
            /// Caution: For a given <see cref="Bucket"/>, <see cref="_head"/> and
            /// <see cref="_next"/> are UNRELATED to each other. Logically, you can
            /// think of <see cref="_next"/> as being part of a value in the
            /// <see cref="_entries"/> table. (We don't actually do that in order to
            /// support <see cref="DoneInterning"/> efficiently.)
            /// To find the next element in the linked list, you should NOT simply
            /// look at <see cref="_next"/>. Instead, you should first look up the
            /// <see cref="Bucket"/> in the <see cref="_buckets"/> array indexed by
            /// <see cref="_head"/> and look at the <see cref="_next"/> field of that.
            /// </remarks>
            private struct Bucket
            {
                /// <summary>
                /// Index into the <see cref="_entries"/> array of the head item in the linked list or
                /// -1 to indicate an empty bucket.
                /// </summary>
                public int _head;

                /// <summary>
                /// Index into the <see cref="_buckets"/> array of the next item in the linked list or
                /// -1 to indicate that this is the last item.
                /// </summary>
                public int _next;
            }

            private Bucket[] _buckets;
            private T[] _entries;
            private int _count;
        }
        private static char[] s_directorySeparators = { '\\', '/' };

        private readonly InternTable<string> m_moduleIntern;
        private readonly StackSourceModuleIndex m_emptyModuleIdx;

        // maps (frameIndex - m_frameStartIndex) to frame information
        private readonly InternTable<FrameInfo> m_frameIntern;

        // Given a Call Stack index, return the list of call stack indexes that that routine calls.  
        // Also maps (callStackIndex - m_callStackStartIndex) to call stack information (frame and caller)  
        private readonly InternTable<CallStackInfo> m_callStackIntern;

        // To allow the interner to 'open' an existing stackSource, we make it flexible about where indexes start.
        // The typical case these are all 0.  
        private readonly StackSourceFrameIndex m_frameStartIndex;
        private readonly StackSourceCallStackIndex m_callStackStartIndex;
        private readonly StackSourceModuleIndex m_moduleStackStartIndex;
        #endregion
    }
}
