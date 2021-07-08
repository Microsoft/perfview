using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Diagnostics;

using OptimizationTier = Microsoft.Diagnostics.Tracing.Parsers.Clr.OptimizationTier;

namespace Diagnostics.Tracing.StackSources
{
    public class XmlStackSourceWriter
    {
        public static void WriteStackViewAsZippedXml(StackSource source, string fileName, Action<XmlWriter> additionalData = null)
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            using (var archive = ZipFile.Open(fileName, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(Path.GetFileNameWithoutExtension(fileName));
                using (var entryStream = entry.Open())
                {
                    WriteStackViewAsXml(source, entryStream, additionalData);
                }
            }
        }
        public static void WriteStackViewAsXml(StackSource source, string fileName, Action<XmlWriter> additionalData = null)
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            using (var writeStream = File.Create(fileName))
            {
                WriteStackViewAsXml(source, writeStream, additionalData);
            }
        }
        public static void WriteStackViewAsXml(StackSource source, Stream writeStream, Action<XmlWriter> additionalData = null)
        {
            using (var writer = XmlWriter.Create(writeStream, new XmlWriterSettings() { Indent = true, IndentChars = " " }))
            {
                writer.WriteStartElement("StackWindow");
                XmlStackSourceWriter.WriteStacks(source, writer);

                additionalData?.Invoke(writer);
                writer.WriteEndElement();
            }
        }

        public static void WriteStacks(StackSource source, XmlWriter writer)
        {
            writer.WriteStartElement("StackSource");
            writer.WriteStartElement("Frames");
            writer.WriteAttributeString("Count", source.CallFrameIndexLimit.ToString());
            for (int i = 0; i < source.CallFrameIndexLimit; i++)
            {
                writer.WriteStartElement("Frame");
                writer.WriteAttributeString("ID", i.ToString());
                var frameName = source.GetFrameName((StackSourceFrameIndex)i, true);

                // Check for the optimization tier. The frame name would contain the optimization tier in the form:
                //   Module![OptimizationTier]Symbol
                // Extract the optimization tier into an attribute and convert the frame name to this form for storage:
                //   Module!Symbol
                if (frameName != null && frameName.Length >= 4)
                {
                    int openBracketIndex = frameName.IndexOf("![") + 1;
                    if (openBracketIndex > 0)
                    {
                        int closeBracketIndex = frameName.IndexOf(']', openBracketIndex + 1);
                        if (closeBracketIndex - openBracketIndex > 1)
                        {
                            var optimizationTierStr =
                                frameName.Substring(openBracketIndex + 1, closeBracketIndex - openBracketIndex - 1);
                            if (Enum.TryParse<OptimizationTier>(optimizationTierStr, out var optimizationTier))
                            {
                                if (optimizationTier != OptimizationTier.Unknown)
                                {
                                    writer.WriteAttributeString("OptimizationTier", optimizationTierStr);
                                }
                                frameName = frameName.Substring(0, openBracketIndex) + frameName.Substring(closeBracketIndex + 1);
                            }
                        }
                    }
                }

                writer.WriteString(frameName);
                writer.WriteEndElement();   // Frame
            }
            writer.WriteEndElement();   // Frames

            writer.WriteStartElement("Stacks");
            writer.WriteAttributeString("Count", source.CallStackIndexLimit.ToString());
            for (int i = 0; i < source.CallStackIndexLimit; i++)
            {
                writer.WriteStartElement("Stack");
                writer.WriteAttributeString("ID", i.ToString());
                var FrameID = source.GetFrameIndex((StackSourceCallStackIndex)i);
                var callerID = source.GetCallerIndex((StackSourceCallStackIndex)i);
                writer.WriteAttributeString("CallerID", ((int)callerID).ToString());
                writer.WriteAttributeString("FrameID", ((int)FrameID).ToString());
                writer.WriteEndElement();   // Stack
            }
            writer.WriteEndElement();   // Stacks

            writer.WriteStartElement("Samples");
            writer.WriteAttributeString("Count", source.SampleIndexLimit.ToString());
            // We use the invariant culture, otherwise if we encode in France and decode 
            // in English we get parse errors (this happened!);
            var invariantCulture = CultureInfo.InvariantCulture;
            source.ForEach(delegate (StackSourceSample sample)
            {
                // <Sample ID="1" Time="3432.23" StackID="2" Metric="1" EventKind="CPUSample" />
                writer.WriteStartElement("Sample");
                writer.WriteAttributeString("ID", ((int)sample.SampleIndex).ToString());
                writer.WriteAttributeString("Time", sample.TimeRelativeMSec.ToString("f3", invariantCulture));
                writer.WriteAttributeString("StackID", ((int)sample.StackIndex).ToString());
                if (sample.Metric != 1)
                {
                    var asInt = (int)sample.Metric;
                    if (sample.Metric == asInt)
                    {
                        writer.WriteAttributeString("Metric", asInt.ToString());
                    }
                    else
                    {
                        writer.WriteAttributeString("Metric", sample.Metric.ToString("f3", invariantCulture));
                    }
                }
                writer.WriteEndElement();
            });
            writer.WriteEndElement(); // Samples
            writer.WriteEndElement(); // StackSource
        }
    }

    /// <summary>
    /// Reads a very reasonable XML encoding of a stack source. 
    /// 
    /// </summary>
    public class XmlStackSource : StackSource
    {
        /// <summary>
        /// Generates a Stack Source from an XML file created with XmlStackSourceWriter.
        /// If 'readElement' is non-null Any XML Elements that are not recognised to it so 
        /// that that information can be parsed by upper level logic.  When that routine
        /// returns it must have skipped past that element (so reader points at whatever 
        /// is after the End element tag).  
        /// 
        /// If the filename ends in .zip, the file is assumed to be a ZIPPed XML file and
        /// it is first Unziped and then processed.  
        /// 
        /// If the file ends in .json or .json.zip it can also read that (using JsonReaderWriterFactory.CreateJsonReader)
        /// see https://msdn.microsoft.com/en-us/library/bb412170.aspx?f=255&amp;MSPPError=-2147217396 for 
        /// more on this mapping.  
        /// </summary>
        public XmlStackSource(string fileName, Action<XmlReader> readElement = null, bool showOptimizationTiers = false)
        {
            m_showOptimizationTiers = showOptimizationTiers || PerfView.App.CommandLineArgs.ShowOptimizationTiers;

            using (Stream dataStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var xmlStream = dataStream;
                string unzippedName = fileName;
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var zipArchive = new ZipArchive(dataStream);
                    var entries = zipArchive.Entries;
                    if (entries.Count != 1)
                    {
                        throw new ApplicationException("The ZIP file does not have exactly 1 XML file in it,");
                    }

                    xmlStream = entries[0].Open();
                    unzippedName = fileName.Substring(0, fileName.Length - 4);
                }

                XmlReader reader;
                if (unzippedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    reader = GetJsonReader(dataStream);
                }
                else
                {
                    XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
                    reader = XmlTextReader.Create(xmlStream, settings);
                }

                reader.Read();      // Skip the StackWindow element. 
                bool readStackSource = false;
                for (; ; )
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Name == "StackSource")
                        {
                            if (!readStackSource)
                            {
                                Read(reader);
                                readStackSource = true;
                            }
                        }
                        else if (readElement != null)
                        {
                            readElement(reader);
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                    else if (!reader.Read())
                    {
                        break;
                    }
                }
                if (m_interner != null)
                {
                    // Transfer the interned names to the m_frames array.  
                    // Go from high to low so at most one reallocation happens.  
                    for (int i = m_interner.FrameCount - 1; 0 <= i; --i)
                    {
                        StackSourceFrameIndex frameIdx = m_interner.FrameStartIndex + i;
                        m_frames.Set((int)frameIdx, m_interner.GetFrameName(frameIdx, true));
                    }

                    for (int i = m_interner.CallStackCount - 1; 0 <= i; --i)
                    {
                        StackSourceCallStackIndex stackIdx = m_interner.CallStackStartIndex + i;
                        m_stacks.Set((int)stackIdx, new Frame(
                            (int)m_interner.GetFrameIndex(stackIdx),
                            (int)m_interner.GetCallerIndex(stackIdx)));
                    }

                    m_interner = null;  // we are done with it.  
                }
            }
        }

        // TODO intern modules 
        public override void ForEach(Action<StackSourceSample> callback)
        {
            for (int i = 0; i < m_samples.Count; i++)
            {
                callback(m_samples[i]);
            }
        }
        public override bool SamplesImmutable { get { return true; } }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return (StackSourceCallStackIndex)m_stacks[(int)callStackIndex].callerID;
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return (StackSourceFrameIndex)m_stacks[(int)callStackIndex].frameID;
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            string ret = m_frames.Get((int)frameIndex);
            if (!verboseName)
            {
                var shortName = m_shortFrameNames.Get((int)frameIndex);
                if (shortName == null)
                {
                    shortName = ret;
                    // Strip off anything before the last \\ before the !
                    var exclaimIdx = ret.IndexOf('!');
                    if (0 < exclaimIdx)
                    {
                        // Becomes 0 if it fails, which is what we want. 
                        var startIdx = ret.LastIndexOfAny(s_directorySeparators, exclaimIdx - 1, exclaimIdx - 1) + 1;
                        shortName = ret.Substring(startIdx);
                    }
                    m_shortFrameNames.Set((int)frameIndex, shortName);
                }
                ret = shortName;
            }
            return ret;
        }
        public override int CallStackIndexLimit { get { return m_stacks.Count; } }
        public override int CallFrameIndexLimit { get { return m_frames.Count; } }

        public override int SampleIndexLimit
        {
            get { return m_samples.Count; }
        }
        public override double SampleTimeRelativeMSecLimit
        {
            get { return m_maxTime; }
        }
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            return m_samples[(int)sampleIndex];
        }

        #region 
        // To avoid loading the System.Runtime.Serialization Dll which is only needed in the JSON case, we have
        // the actual reference to that in this method that will not be called unless it is needed. 
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static XmlReader GetJsonReader(Stream dataStream)
        {
            return JsonReaderWriterFactory.CreateJsonReader(dataStream, new XmlDictionaryReaderQuotas());
        }

        private void Read(XmlReader reader)
        {
            Stack<string> frameStack = null;
            // We use the invarient culture, otherwise if we encode in france and decode 
            // in english we get parse errors (this happened!);
            var invariantCulture = CultureInfo.InvariantCulture;
            var inputDepth = reader.Depth;
            var depthForSamples = 0;
            while (reader.Read())
            {
                PROCESS_NODE:
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (reader.Name == "Sample")
                        {
                            var sample = new StackSourceSample(this);
                            sample.Metric = 1;
                            if (reader.MoveToFirstAttribute())
                            {
                                do
                                {
                                    if (reader.Name == "Time")
                                    {
                                        sample.TimeRelativeMSec = double.Parse(reader.ReadContentAsString(), invariantCulture);
                                    }
                                    else if (reader.Name == "StackID")
                                    {
                                        sample.StackIndex = (StackSourceCallStackIndex)reader.ReadContentAsInt();
                                    }
                                    else if (reader.Name == "Metric")
                                    {
                                        sample.Metric = double.Parse(reader.ReadContentAsString(), invariantCulture);
                                    }
                                } while (reader.MoveToNextAttribute());
                            }
                            sample.SampleIndex = (StackSourceSampleIndex)m_curSample;
                            m_samples.Set(m_curSample++, sample);
                            if (sample.TimeRelativeMSec > m_maxTime)
                            {
                                m_maxTime = sample.TimeRelativeMSec;
                            }

                            // See if there is a literal stack present as the body of 
                            if (!reader.Read())
                            {
                                break;
                            }

                            if (reader.NodeType != XmlNodeType.Text)
                            {
                                goto PROCESS_NODE;
                            }

                            string rawStack = reader.Value.Trim();
                            if (0 < rawStack.Length)
                            {
                                InitInterner();

                                StackSourceCallStackIndex stackIdx = StackSourceCallStackIndex.Invalid;
                                string[] frames = rawStack.Split('\n');
                                for (int i = frames.Length - 1; 0 <= i; --i)
                                {
                                    var frameIdx = m_interner.FrameIntern(frames[i].Trim());
                                    stackIdx = m_interner.CallStackIntern(frameIdx, stackIdx);
                                }
                                sample.StackIndex = stackIdx;
                            }
                        }
                        else if (reader.Name == "Stack")
                        {
                            int stackID = -1;
                            int callerID = -1;
                            int frameID = -1;
                            if (reader.MoveToFirstAttribute())
                            {
                                do
                                {
                                    if (reader.Name == "ID")
                                    {
                                        stackID = reader.ReadContentAsInt();
                                    }
                                    else if (reader.Name == "FrameID")
                                    {
                                        frameID = reader.ReadContentAsInt();
                                    }
                                    else if (reader.Name == "CallerID")
                                    {
                                        callerID = reader.ReadContentAsInt();
                                    }
                                } while (reader.MoveToNextAttribute());
                                if (0 <= stackID)
                                {
                                    m_stacks.Set(stackID, new Frame(frameID, callerID));
                                }
                            }

                        }
                        else if (reader.Name == "Frame")
                        {
                            var frameID = -1;
                            var optimizationTierStr = string.Empty;
                            if (reader.MoveToFirstAttribute())
                            {
                                do
                                {
                                    if (reader.Name == "ID")
                                    {
                                        frameID = reader.ReadContentAsInt();
                                    }
                                    else if (reader.Name == "OptimizationTier")
                                    {
                                        if (m_showOptimizationTiers)
                                        {
                                            var optimizationTierCandidateStr = reader.ReadContentAsString();
                                            if (Enum.TryParse<OptimizationTier>(optimizationTierCandidateStr, out var optimizationTier) &&
                                                optimizationTier != OptimizationTier.Unknown)
                                            {
                                                optimizationTierStr = optimizationTierCandidateStr;
                                            }
                                        }
                                    }
                                } while (reader.MoveToNextAttribute());
                            }
                            reader.Read();      // Move on to body of the element
                            var frameName = reader.ReadContentAsString();

                            if (optimizationTierStr.Length > 0)
                            {
                                int exclamationIndex = frameName.IndexOf('!');
                                if (exclamationIndex >= 0)
                                {
                                    frameName =
                                        frameName.Substring(0, exclamationIndex + 1) +
                                        $"[{optimizationTierStr}]" +
                                        frameName.Substring(exclamationIndex + 1);
                                }
                            }

                            m_frames.Set(frameID, frameName);
                        }
                        else if (reader.Name == "Frames")
                        {
                            var count = reader.GetAttribute("Count");
                            if (count != null && m_frames.Count == 0)
                            {
                                m_frames = new GrowableArray<string>(int.Parse(count));
                            }
                        }
                        else if (reader.Name == "Stacks")
                        {
                            var count = reader.GetAttribute("Count");
                            if (count != null && m_stacks.Count == 0)
                            {
                                m_stacks = new GrowableArray<Frame>(int.Parse(count));
                            }
#if DEBUG
                            for (int i = 0; i < m_stacks.Count; i++)
                            {
                                m_stacks[i] = new Frame(int.MinValue, int.MinValue);
                            }
#endif
                        }
                        else if (reader.Name == "Samples")
                        {
                            var count = reader.GetAttribute("Count");
                            if (count != null && m_samples.Count == 0)
                            {
                                m_samples = new GrowableArray<StackSourceSample>(int.Parse(count));
                            }

                            depthForSamples = reader.Depth;
                        }
                        // This is the logic for the JSON case.  These are the anonymous object representing a sample.  
                        else if (reader.Name == "item")
                        {
                            // THis is an item which is an element of the 'Samples' array.  
                            if (reader.Depth == depthForSamples + 1)
                            {
                                var sample = new StackSourceSample(this);
                                sample.Metric = 1;

                                InitInterner();
                                int depthForSample = reader.Depth;
                                if (frameStack == null)
                                {
                                    frameStack = new Stack<string>();
                                }

                                frameStack.Clear();

                                while (reader.Read())
                                {
                                    PROCESS_NODE_SAMPLE:
                                    if (reader.Depth <= depthForSample)
                                    {
                                        break;
                                    }

                                    if (reader.NodeType == XmlNodeType.Element)
                                    {
                                        if (reader.Name == "Time")
                                        {
                                            sample.TimeRelativeMSec = reader.ReadElementContentAsDouble();
                                            goto PROCESS_NODE_SAMPLE;
                                        }
                                        else if (reader.Name == "Metric")
                                        {
                                            sample.Metric = reader.ReadElementContentAsDouble();
                                            goto PROCESS_NODE_SAMPLE;
                                        }
                                        else if (reader.Name == "item")
                                        {
                                            // Item is a string under stack under the sample.  
                                            if (reader.Depth == depthForSample + 2)
                                            {
                                                frameStack.Push(reader.ReadElementContentAsString());
                                                goto PROCESS_NODE_SAMPLE;
                                            }
                                        }
                                    }
                                }

                                // Reverse the order of the frames in the stack.  
                                sample.StackIndex = StackSourceCallStackIndex.Invalid;
                                while (0 < frameStack.Count)
                                {
                                    var frameIdx = m_interner.FrameIntern(frameStack.Pop());
                                    sample.StackIndex = m_interner.CallStackIntern(frameIdx, sample.StackIndex);
                                }

                                if (sample.TimeRelativeMSec > m_maxTime)
                                {
                                    m_maxTime = sample.TimeRelativeMSec;
                                }

                                sample.SampleIndex = (StackSourceSampleIndex)m_curSample;
                                m_samples.Set(m_curSample++, sample);
                            }
                        }
                        break;
                    case XmlNodeType.EndElement:
                        if (reader.Depth <= inputDepth)
                        {
                            reader.Read();
                            goto Done;
                        }
                        break;
                    case XmlNodeType.Text:
                    default:
                        break;
                }
            }
            Done:;
#if DEBUG
            for (int i = 0; i < m_samples.Count; i++)
            {
                Debug.Assert(m_samples[i] != null);
            }

            for (int i = 0; i < m_frames.Count; i++)
            {
                Debug.Assert(m_frames[i] != null);
            }

            for (int i = 0; i < m_stacks.Count; i++)
            {
                Debug.Assert(m_stacks[i].frameID >= 0);
                Debug.Assert(m_stacks[i].callerID >= -1);
            }
#endif
        }

        private void InitInterner()
        {
            if (m_interner == null)
            {
                m_interner = new StackSourceInterner(5000, 1000, 5,
                    (StackSourceFrameIndex)m_frames.Count,
                    (StackSourceCallStackIndex)m_stacks.Count);
            }
        }

        private struct Frame
        {
            public Frame(int frameID, int callerID) { this.frameID = frameID; this.callerID = callerID; }
            public int frameID;
            public int callerID;
        }

        private static char[] s_directorySeparators = { '\\', '/' };
        private GrowableArray<string> m_shortFrameNames;
        private GrowableArray<string> m_frames;
        private GrowableArray<Frame> m_stacks;
        private GrowableArray<StackSourceSample> m_samples;
        private StackSourceInterner m_interner;     // If the XML has samples with explicit stacks, then this is non-null and used to intern them. 
        private int m_curSample;
        private double m_maxTime;
        private bool m_showOptimizationTiers;
        #endregion
    }
}