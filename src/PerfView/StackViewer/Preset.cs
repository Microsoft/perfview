﻿using System;
using System.Collections.Generic;
using System.Text;

namespace PerfView
{
    /// <summary>
    /// Stack viewer preset that includes information about grouping and folding patterns,
    /// folding percentage and inclusion/exclusion patterns.
    /// </summary>
    public class Preset
    {
        public string Name { get; set; }
        public string GroupPat { get; set; }
        public string FoldPercentage { get; set; }
        public string FoldPat { get; set; }
        public string IncPat { get; set; }
        public string ExcPat { get; set; }

        /// <summary>
        /// Parses collection of presets kept as a string
        /// </summary>
        public static List<Preset> ParseCollection(string presets)
        {
            var result = new List<Preset>();
            if (presets == null)
            {
                return result;
            }
            var entries = presets.Split(new[] { PresetSeparator }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var preset = new Preset();
                var presetParts = entry.Split(new[] { PartSeparator }, StringSplitOptions.None);
                foreach (var presetPart in presetParts)
                {
                    int separatorIndex = presetPart.IndexOf('=');
                    string partName = presetPart.Substring(0, separatorIndex);
                    string partValue = presetPart.Substring(separatorIndex + 1);
                    switch (partName)
                    {
                        case "Name":
                            preset.Name = partValue;
                            break;
                        case "GroupPat":
                            preset.GroupPat = partValue;
                            break;
                        case "FoldPercentage":
                            preset.FoldPercentage = partValue;
                            break;
                        case "FoldPat":
                            preset.FoldPat = partValue;
                            break;
                        case "IncPat":
                            preset.IncPat = partValue;
                            break;
                        case "ExcPat":
                            preset.ExcPat = partValue;
                            break;
                    }
                }

                result.Add(preset);
            }

            return result;
        }

        /// <summary>
        /// Serializes list of presets to be stored in the string.
        /// </summary>
        public static string Serialize(List<Preset> presets)
        {
            var result = new StringBuilder();
            bool firstPreset = true;
            foreach (var preset in presets)
            {
                if (!firstPreset)
                {
                    result.Append(PresetSeparator);
                }
                firstPreset = false;

                result.Append("Name=" + preset.Name + PartSeparator);
                result.Append("GroupPat=" + preset.GroupPat + PartSeparator);
                result.Append("FoldPercentage=" + preset.FoldPercentage + PartSeparator);
                result.Append("FoldPat=" + preset.FoldPat + PartSeparator);
                result.Append("IncPat=" + preset.IncPat + PartSeparator);
                result.Append("ExcPat=" + preset.ExcPat);
            }

            return result.ToString();
        }

        private const string PresetSeparator = "####";
        private const string PartSeparator = "$$$$";
    }
}