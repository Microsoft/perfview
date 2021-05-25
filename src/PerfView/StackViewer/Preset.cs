using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace PerfView
{
    /// <summary>
    /// Stack viewer preset that includes information about grouping and folding patterns,
    /// folding percentage.
    /// </summary>
    public class Preset : IEquatable<Preset>
    {
        public string Name { get; set; }
        public string GroupPat { get; set; }
        public string FoldPercentage { get; set; }
        public string FoldPat { get; set; }
        public string Comment { get; set; }

        /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
        /// <returns>true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.</returns>
        /// <param name="other">An object to compare with this object.</param>
        public bool Equals(Preset other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Name, other.Name) &&
                   string.Equals(GroupPat, other.GroupPat) &&
                   string.Equals(FoldPercentage, other.FoldPercentage) &&
                   string.Equals(FoldPat, other.FoldPat) &&
                   string.Equals(Comment, other.Comment);
        }

        /// <summary>
        /// Parses collection of presets kept as a string
        /// </summary>
        public static List<Preset> ParseCollection(string presets)
        {
            if (presets == null)
            {
                return new List<Preset>(0);
            }
            var entries = presets.Split(new[] { PresetSeparator }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<Preset>(entries.Length);
            foreach (var entry in entries)
            {
                result.Add(ParsePreset(entry));
            }

            return result;
        }

        /// <summary>
        /// Parses single preset.
        /// </summary>
        public static Preset ParsePreset(string presetString)
        {
            var preset = new Preset();
            var presetParts = presetString.Split(new[] { PartSeparator }, StringSplitOptions.None);
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
                    case "Comment":
                        preset.Comment = XmlConvert.DecodeName(partValue);
                        break;
                }
            }

            return preset;
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

                result.Append(Serialize(preset));
            }

            return result.ToString();
        }

        /// <summary>
        /// Serializes single preset to string.
        /// </summary>
        public static string Serialize(Preset preset)
        {
            var result = new StringBuilder();
            result.Append("Name=" + preset.Name + PartSeparator);
            result.Append("GroupPat=" + preset.GroupPat + PartSeparator);
            result.Append("FoldPercentage=" + preset.FoldPercentage + PartSeparator);
            result.Append("FoldPat=" + preset.FoldPat + PartSeparator);
            result.Append("Comment=" + XmlConvert.EncodeName(preset.Comment));
            return result.ToString();
        }

        private const string PresetSeparator = "####";
        private const string PartSeparator = "$$$$";
    }
}
