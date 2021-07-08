// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    /// <summary>
    /// A Histogram is logically an array of floating point values.  Often they
    /// represent frequency, but it can be some other metric.  The X axis can 
    /// represent different things (time, scenario).  It is the HisogramContoller
    /// which understands what the X axis is.   Histograms know their HistogramController
    /// but not the reverse.  
    /// 
    /// Often Histograms are sparse (most array elements are zero), so the represnetation
    /// is designed to optimzed for this case (an array of non-zero index, value pairs). 
    /// </summary>
    public class Histogram : IEnumerable<double>
    {
        /// <summary>
        /// Create a new histogram.  Every histogram needs a controller but these controllers 
        /// can be shared among many histograms.  
        /// </summary>
        public Histogram(HistogramController controller)
        {
            m_controller = controller;
            m_singleBucketNum = -1;
        }

        /// <summary>
        /// Add a sample to this histogram.
        /// </summary>
        /// <param name="sample">The sample to add.</param>
        public void AddSample(StackSourceSample sample)
        {
            m_controller.AddSample(this, sample);
        }

        /// <summary>
        /// Add an amount to a bucket in this histogram.
        /// </summary>
        /// <param name="metric">The amount to add to the bucket.</param>
        /// <param name="bucket">The bucket to add to.</param>
        public void AddMetric(double metric, int bucket)
        {
            Debug.Assert(0 <= bucket && bucket < Count);

            if (m_singleBucketNum < 0)
            {
                m_singleBucketNum = bucket;
            }

            if (m_singleBucketNum == bucket)
            {
                m_singleBucketValue += metric;
                return;
            }
            if (m_buckets == null)
            {
                m_buckets = new double[Count];
                m_buckets[m_singleBucketNum] = m_singleBucketValue;
            }
            m_buckets[bucket] += metric;
        }

        /// <summary>
        /// Computes this = this + histogram * weight in place (this is updated).  
        /// </summary>
        public void AddScaled(Histogram histogram, double weight = 1)
        {
            var histArray = histogram.m_buckets;
            if (histArray != null)
            {
                for (int i = 0; i < histArray.Length; i++)
                {
                    var val = histArray[i];
                    if (val != 0)
                    {
                        AddMetric(val, i);
                    }
                }
            }
            else if (0 <= histogram.m_singleBucketNum)
            {
                AddMetric(histogram.m_singleBucketValue * weight, histogram.m_singleBucketNum);
            }
        }

        /// <summary>
        /// The number of buckets in this histogram.
        /// </summary>
        public int Count
        {
            get { return m_controller.BucketCount; }
        }

        /// <summary>
        /// The <see cref="HistogramController"/> that controls this histogram.
        /// </summary>
        public HistogramController Controller
        {
            get { return m_controller; }
        }

        /// <summary>
        /// Get the metric contained in a bucket.
        /// </summary>
        /// <param name="index">The bucket to retrieve.</param>
        /// <returns>The metric contained in that bucket.</returns>
        public double this[int index]
        {
            get
            {
                Debug.Assert(0 <= index && index < Count);
                if (m_buckets != null)
                {
                    return m_buckets[index];
                }
                else if (m_singleBucketNum == index)
                {
                    return m_singleBucketValue;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Make a copy of this histogram.
        /// </summary>
        /// <returns>An independent copy of this histogram.</returns>
        public Histogram Clone()
        {
            return new Histogram(this);
        }

        /// <summary>
        /// A string representation (for debugging)
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return Controller.GetDisplayString(this);
        }

        #region private
        /// <summary>
        /// Create a histogram that is a copy of another histogram.
        /// </summary>
        /// <param name="other">The histogram to copy.</param>
        private Histogram(Histogram other)
        {
            m_controller = other.m_controller;

            m_singleBucketNum = other.m_singleBucketNum;
            m_singleBucketValue = other.m_singleBucketValue;
            if (other.m_buckets != null)
            {
                m_buckets = new double[other.m_buckets.Length];
                Array.Copy(other.m_buckets, m_buckets, other.m_buckets.Length);
            }
        }

        /// <summary>
        /// Implementes IEnumerable interface
        /// </summary>
        public IEnumerator<double> GetEnumerator()
        {
            return GetEnumerable().GetEnumerator();
        }
        /// <summary>
        /// Implementes IEnumerable interface
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() { throw new NotImplementedException(); }

        /// <summary>
        /// Get an IEnumerable that can be used to enumerate the metrics stored in the buckets of this Histogram.
        /// </summary>
        private IEnumerable<double> GetEnumerable()
        {
            int end = Count;
            for (int i = 0; i < end; i++)
            {
                yield return this[i];
            }
        }

        /// <summary>
        /// The controller for this histogram.
        /// </summary>
        private readonly HistogramController m_controller;

        private double[] m_buckets;               // If null means is its single value or no values

        // We special case a histogram with a single bucket.  
        private int m_singleBucketNum;          // -1 means no values
        private double m_singleBucketValue;
        #endregion
    }

    /// <summary>
    /// A Histogram is conceputually an array of floating point values.   A Histogram Controller
    /// contains all the information besides the values themselves need to understand the array
    /// of floating point value.   There are a lot of Histograms, however they all tend to share
    /// the same histogram controller.   Thus Histograms know their Histogram controller, but not
    /// the reverse.  
    /// 
    /// Thus HistogramContoller is a abstract class (we have one for time, and one for scenarios).  
    ///
    /// HistogramControllers are responsible for:
    /// 
    /// - Adding a sample to the histogram for a node (see <see cref="AddSample"/>)
    /// - Converting a histogram to its string representation see (<see cref="GetDisplayString"/>)
    /// - Managing the size and scale of histograms and their corresponding display strings
    /// </summary>
    public abstract class HistogramController
    {
        /// <summary>
        /// The scale factor for histograms controlled by this HistogramController.
        /// </summary>
        public double Scale
        {
            get
            {
                lock (this)
                {
                    if (m_scale == 0.0)
                    {
                        m_scale = CalculateScale();
                    }
                }
                return m_scale;
            }
        }
        /// <summary>
        /// The number of buckets in each histogram controlled by this HistogramController.
        /// </summary>
        public int BucketCount { get; protected set; }
        /// <summary>
        /// The number of characters in the display string for histograms controlled by this HistogramController.
        /// Buckets are a logial concept, where CharacterCount is a visual concept (how many you can see on the 
        /// screen right now).  
        /// </summary>
        public int CharacterCount { get; protected set; }
        /// <summary>
        /// The CallTree managed by this HistogramController.
        /// </summary>
        public CallTree Tree { get; protected set; }
        /// <summary>
        /// Force recalculation of the scale parameter.
        /// </summary>
        public void InvalidateScale()
        {
            m_scale = 0.0;
        }

        // Abstract methods
        /// <summary>
        /// Add a sample to the histogram for a node.
        /// </summary>
        /// <param name="histogram">The histogram to add this sample to. Must be controlled by this HistogramController.</param>
        /// <param name="sample">The sample to add.</param>
        /// <remarks>
        /// Overriding classes are responsible for extracting the metric, scaling the metric,
        /// determining the appropriate bucket or buckets, and adding the metric to the histogram using <see cref="Histogram.AddMetric"/>.
        /// </remarks>
        public abstract void AddSample(Histogram histogram, StackSourceSample sample);
        /// <summary>
        /// Gets human-readable information about a range of histogram characters.
        /// </summary>
        /// <param name="start">The start character index (inclusive).</param>
        /// <param name="end">The end character index (exclusive).</param>
        /// <param name="histogram">The histogram.</param>
        /// <returns>A string containing information about the contents of that character range.</returns>
        public abstract string GetInfoForCharacterRange(HistogramCharacterIndex start, HistogramCharacterIndex end, Histogram histogram);
        /// <summary>
        /// Convert a histogram into its display string.
        /// </summary>
        /// <param name="histogram">The histogram to convert to a string.</param>
        /// <returns>A string suitable for GUI display.</returns>
        public abstract string GetDisplayString(Histogram histogram);

        // Static utility functions
        /// <summary>
        /// A utility function that turns an array of floats into a ASCII character graph.  
        /// </summary>
        public static string HistogramString(IEnumerable<double> buckets, int bucketCount, double scale, int maxLegalBucket = 0)
        {
            if (buckets == null)
            {
                return "";
            }

            var chars = new char[bucketCount];
            int i = 0;
            foreach (double metric in buckets)
            {
                char val = '_';
                if (0 < maxLegalBucket && maxLegalBucket <= i)
                {
                    val = '?';
                }

                int valueBucket = (int)(metric / scale * 10);       // TODO should we round?
                if (metric > 0)
                {
                    // Scale the metric according to the wishes of the client
                    if (valueBucket < 10)
                    {
                        val = (char)('0' + valueBucket);
                        if (valueBucket == 0 && (metric / scale < .01))
                        {
                            val = 'o';
                            if (metric / scale < .001)
                            {
                                val = '.';
                            }
                        }
                    }
                    else
                    {
                        valueBucket -= 10;
                        if (valueBucket < 25)
                        {
                            val = (char)('A' + valueBucket);          // We go through the alphabet too.
                        }
                        else
                        {
                            val = '*';                                // Greater than 3.6X CPUs 
                        }
                    }
                }
                else if (metric < 0)
                {
                    valueBucket = -valueBucket;
                    // TODO we are not symetric, we use digits on the positive side but not negative.  
                    if (valueBucket < 25)
                    {
                        val = (char)('a' + valueBucket);          // We go through the alphabet too.
                    }
                    else
                    {
                        val = '@';
                    }
                }
                chars[i] = val;
                i++;
            }
            return new string(chars);
        }
        /// <summary>
        /// A utility function that turns an array of floats into a ASCII character graph.  
        /// </summary>
        public static string HistogramString(double[] buckets, double scale, int maxLegalBucket = 0)
        {
            return (buckets == null) ? "" : HistogramString(buckets, buckets.Length, scale, maxLegalBucket);
        }

        /// <summary>
        /// Initialize a new HistogramController.
        /// </summary>
        /// <param name="tree">The CallTree that this HistogramController controls.</param>
        protected HistogramController(CallTree tree)
        {
            BucketCount = 32;
            CharacterCount = 32;
            Tree = tree;
        }
        /// <summary>
        /// Calculate the scale factor for this histogram.
        /// </summary>
        /// <returns>The scale factor for this histogram.</returns>
        protected abstract double CalculateScale();
        /// <summary>
        /// Calculates an average scale factor for a histogram.
        /// </summary>
        /// <param name="hist">The root histogram to calculate against.</param>
        /// <returns>A scale factor that will normalize the maximum value to 200%.</returns>
        protected double CalculateAverageScale(Histogram hist)
        {
            // Return half the max of the absolute values in the top histogram 
            double max = 0;
            for (int i = 0; i < hist.Count; i++)
            {
                max = Math.Max(Math.Abs(hist[i]), max);
            }

            return max / 2;
        }

        #region private
        /// <summary>
        /// The scale parameter. 0.0 if uncalculated.
        /// </summary>
        private double m_scale;
        #endregion
    }

    /// <summary>
    /// An enum representing a displayed histogram bucket (one character in a histogram string).
    /// </summary>
    public enum HistogramCharacterIndex
    {
        /// <summary>
        /// A HistogramCharacterIndex can be used to represent error conditions 
        /// </summary>
        Invalid = -1
    }

    /// <summary>
    /// A <see cref="HistogramController"/> that groups histograms by scenarios.
    /// </summary>
    public class ScenarioHistogramController : HistogramController
    {
        /// <summary>
        /// Initialize a new ScenarioHistogramController.
        /// </summary>
        /// <param name="tree">The CallTree to manage.</param>
        /// <param name="scenarios">An ordered array of scenario IDs to display.</param>
        /// <param name="totalScenarios">The total number of possible scenarios that can be supplied by the underlying StackSource.
        /// This number might be larger than the highest number in <paramref name="scenarios"/>.</param>
        /// <param name="scenarioNames">The names of the scenarios (for UI use).</param>
        public ScenarioHistogramController(CallTree tree, int[] scenarios, int totalScenarios, string[] scenarioNames = null)
            : base(tree)
        {
            Debug.Assert(totalScenarios > 0);

            BucketCount = totalScenarios;
            CharacterCount = Math.Min(scenarios.Length, CharacterCount);

            m_scenariosFromCharacter = new List<int>[CharacterCount];
            m_characterFromScenario = new HistogramCharacterIndex[BucketCount];

            for (int i = 0; i < CharacterCount; i++)
            {
                m_scenariosFromCharacter[i] = new List<int>();
            }

            for (int i = 0; i < BucketCount; i++)
            {
                m_characterFromScenario[i] = HistogramCharacterIndex.Invalid;
            }

            for (int i = 0; i < scenarios.Length; i++)
            {
                var scenario = scenarios[i];
                var bucket = (i * CharacterCount) / scenarios.Length;

                m_characterFromScenario[scenario] = (HistogramCharacterIndex)bucket;
                m_scenariosFromCharacter[bucket].Add(scenario);
            }

            m_scenarioNames = scenarioNames;
        }

        /// <summary>
        /// Get a list of scenarios contained in a given bucket.
        /// </summary>
        /// <param name="bucket">The bucket to look up.</param>
        /// <returns>The scenarios contained in that bucket.</returns>
        public int[] GetScenariosForCharacterIndex(HistogramCharacterIndex bucket)
        {
            return m_scenariosFromCharacter[(int)bucket].ToArray();
        }

        /// <summary>
        /// Get a list of scenarios contained in a given bucket range.
        /// </summary>
        /// <param name="start">The start of the bucket range (inclusive).</param>
        /// <param name="end">The end of the bucket range (exclusive).</param>
        /// <returns>The scenarios contained in that range of buckets.</returns>
        public int[] GetScenariosForCharacterRange(HistogramCharacterIndex start, HistogramCharacterIndex end)
        {
            var rv = new List<int>();

            for (var bucket = start; bucket < end; bucket++)
            {
                rv.AddRange(m_scenariosFromCharacter[(int)bucket]);
            }

            return rv.ToArray();
        }

        /// <summary>
        /// Add a sample to a histogram controlled by this HistogramController.
        /// </summary>
        /// <param name="histogram">The histogram to add the sample to.</param>
        /// <param name="sample">The sample to add.</param>
        public override void AddSample(Histogram histogram, StackSourceSample sample)
        {
            histogram.AddMetric(sample.Metric, sample.Scenario);
        }

        /// <summary>
        /// Get the human-readable name for a scenario.
        /// </summary>
        /// <param name="scenario">The ID of the scenario to look up.</param>
        /// <returns>The human-readable name for that scenario.</returns>
        public string GetNameForScenario(int scenario)
        {
            if (m_scenarioNames != null)
            {
                return m_scenarioNames[scenario];
            }
            else
            {
                return string.Format("<scenario #{0}>", scenario);
            }
        }

        /// <summary>
        /// Get the human-readable names for all scenarios contained in a range of histogram characters.
        /// </summary>
        /// <param name="start">The (inclusive) start index of the range.</param>
        /// <param name="end">The (exclusive) end index of the range.</param>
        /// <param name="histogram">The histogram.</param>
        /// <returns>A comma-separated list of scenario names contained in that range.</returns>
        public override string GetInfoForCharacterRange(HistogramCharacterIndex start, HistogramCharacterIndex end, Histogram histogram)
        {
            var sb = new StringBuilder();
            for (var bucket = start; bucket < end; bucket++)
            {
                if (bucket == start)
                {
                    sb.Append("Scenarios: ");
                }

                foreach (int scenario in m_scenariosFromCharacter[(int)bucket])
                {
                    sb.AppendFormat("{0}, ", GetNameForScenario(scenario));
                }
            }
            if (2 <= sb.Length)
            {
                sb.Remove(sb.Length - 2, 2);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Convert a histogram into a string suitable for UI display.
        /// </summary>
        /// <param name="histogram">The histogram to convert.</param>
        /// <returns>A string representing the histogram that is suitable for UI display.</returns>
        public override string GetDisplayString(Histogram histogram)
        {
            double[] displayBuckets = new double[CharacterCount];

            // Sort out and add up our metrics from the model buckets.
            // Each display bucket is the average of the scenarios in the corresponding model bucket.
            for (int i = 0; i < histogram.Count; i++)
            {
                if (m_characterFromScenario[i] != HistogramCharacterIndex.Invalid)
                {
                    displayBuckets[(int)m_characterFromScenario[i]] += histogram[i];
                }
            }

            for (int i = 0; i < displayBuckets.Length; i++)
            {
                displayBuckets[i] /= m_scenariosFromCharacter[i].Count;
            }

            return HistogramString(displayBuckets, Scale);
        }

        /// <summary>
        /// Calculate the scale factor for all histograms controlled by this ScenarioHistogramController.
        /// </summary>
        /// <returns>
        /// In the current implementation, returns a scale that normalizes 100% to half of the maximum value at the root.
        /// </returns>
        protected override double CalculateScale()
        {
            return CalculateAverageScale(Tree.Root.InclusiveMetricByScenario);
        }
        #region Private
        /// <summary>
        /// An array mapping each scenario to a bucket.
        /// </summary>
        private readonly HistogramCharacterIndex[] m_characterFromScenario;
        /// <summary>
        /// An array mapping each bucket to a list of scenarios.
        /// </summary>
        private readonly List<int>[] m_scenariosFromCharacter;
        /// <summary>
        /// An array mapping each scenario to its name.
        /// </summary>
        private readonly string[] m_scenarioNames;
        #endregion
    }

    /// <summary>
    /// A HistogramController holds all the information to understand the buckets of a histogram
    /// (basically everything except the array of metrics itself.   For time this is the
    /// start and end time  
    /// </summary>
    public class TimeHistogramController : HistogramController
    {
        /// <summary>
        /// Create a new TimeHistogramController.
        /// </summary>
        /// <param name="tree">The CallTree to control with this controller.</param>
        /// <param name="start">The start time of the histogram.</param>
        /// <param name="end">The end time of the histogram.</param>
        public TimeHistogramController(CallTree tree, double start, double end)
            : base(tree)
        {
            Start = start;
            End = end;
        }

        /// <summary>
        /// The start time of the histogram.
        /// </summary>
        public double Start { get; private set; }

        /// <summary>
        /// The end time of the histogram.
        /// </summary>
        public double End { get; private set; }

        /// <summary>
        /// Gets the start time for the histogram bucket represented by a character.
        /// </summary>
        /// <param name="bucket">The index of the character to look up.</param>
        /// <returns>The start time of the bucket represented by the character.</returns>
        public double GetStartTimeForBucket(HistogramCharacterIndex bucket)
        {
            Debug.Assert(bucket != HistogramCharacterIndex.Invalid);

            return (BucketDuration * (int)bucket) + Start;
        }

        /// <summary>
        /// The duration of time represented by each bucket.
        /// </summary>
        public double BucketDuration
        {
            get { return (End - Start) / BucketCount; }
        }

        #region overrides
        /// <summary>
        /// Implements HistogramController interface
        /// </summary>
        protected override double CalculateScale()
        {
            if (Tree.ScalingPolicy == ScalingPolicyKind.TimeMetric)
            {
                return BucketDuration;
            }
            else
            {
                return CalculateAverageScale(Tree.Root.InclusiveMetricByTime);
            }
        }
        /// <summary>
        /// Implements HistogramController interface
        /// </summary>
        public override void AddSample(Histogram histogram, StackSourceSample sample)
        {
            double bucketDuration = BucketDuration;
            double startSampleInBucket = sample.TimeRelativeMSec;
            int bucketIndex = (int)((sample.TimeRelativeMSec - Start) / bucketDuration);
            Debug.Assert(0 <= bucketIndex && bucketIndex <= BucketCount);

            if (Tree.ScalingPolicy == ScalingPolicyKind.TimeMetric)
            {
                // place the metric in each of the buckets it overlaps with. 
                var nextBucketStart = GetStartTimeForBucket((HistogramCharacterIndex)(bucketIndex + 1));

                // The Math.Abs is a bit of a hack.  The problem is that that sample does not
                // represent time for a DIFF (because we negated it) but I rely on the fact 
                // that we only negate it so I can undo it 
                double endSample = sample.TimeRelativeMSec + Math.Abs(sample.Metric);
                var metricSign = sample.Metric > 0 ? 1 : -1;
                for (; ; )
                {
                    if (BucketCount <= bucketIndex)
                    {
                        break;
                    }

                    var metricInBucket = Math.Min(nextBucketStart, endSample) - startSampleInBucket;
                    histogram.AddMetric(metricInBucket * metricSign, bucketIndex);

                    bucketIndex++;
                    startSampleInBucket = nextBucketStart;
                    nextBucketStart += bucketDuration;
                    if (startSampleInBucket > endSample)
                    {
                        break;
                    }
                }
            }
            else
            {
                // Put the sample in the right bucket.  Note that because we allow inclusive times on the end
                // point we could get bucketIndex == Length, so put that sample in the last bucket.  
                if (bucketIndex >= BucketCount)
                {
                    bucketIndex = BucketCount - 1;
                }

                histogram.AddMetric(sample.Metric, bucketIndex);
            }
        }
        /// <summary>
        /// Implements HistogramController interface
        /// </summary>
        public override string GetInfoForCharacterRange(HistogramCharacterIndex start, HistogramCharacterIndex end, Histogram histogram)
        {
            var rangeStart = GetStartTimeForBucket(start);
            var rangeEnd = GetStartTimeForBucket(end);

            var cumStats = "";
            if (start != HistogramCharacterIndex.Invalid && end != HistogramCharacterIndex.Invalid && start < end)
            {
                double cumStart = 0;
                for (int i = 0; i < (int)start; i++)
                {
                    cumStart += histogram[i];
                }

                double cum = cumStart;
                double cumMax = cumStart;
                HistogramCharacterIndex cumMaxIdx = start;

                for (HistogramCharacterIndex i = start; i < end; i++)
                {
                    var val = histogram[(int)i];
                    cum += val;
                    if (cum > cumMax)
                    {
                        cumMax = cum;
                        cumMaxIdx = i + 1;
                    }
                }
                cumStats = string.Format(" CumStart:{0,9:n3}M Cum:{1,9:n3}M  CumMax:{2,9:n3}M at {3,11:n3}ms",
                    cumStart / 1000000, cum / 1000000, cumMax / 1000000, GetStartTimeForBucket(cumMaxIdx));
            }

            return string.Format("TimeRange = {0,11:n3} - {1,11:n3} Duration {2,9:n3}ms{3}", rangeStart, rangeEnd, rangeEnd - rangeStart, cumStats);
        }
        /// <summary>
        /// Implements HistogramController interface
        /// </summary>
        public override string GetDisplayString(Histogram histogram)
        {
            return HistogramString(histogram, histogram.Count, Scale);
        }
        #endregion
    }

}