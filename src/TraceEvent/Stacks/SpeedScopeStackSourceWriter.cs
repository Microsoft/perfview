using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using static Microsoft.Diagnostics.Tracing.Stacks.StackSourceWriterHelper;

namespace Microsoft.Diagnostics.Tracing.Stacks.Formats
{
    public static class SpeedScopeStackSourceWriter
    {
        /// <summary>
        /// exports provided StackSource to a https://www.speedscope.app/ format 
        /// schema: https://www.speedscope.app/file-format-schema.json
        /// </summary>
        public static void WriteStackViewAsJson(StackSource source, string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            using (var writeStream = File.CreateText(filePath))
                Export(source, writeStream, Path.GetFileNameWithoutExtension(filePath));
        }

        #region private
        private static void Export(StackSource source, TextWriter writer, string name)
        {
            var samplesPerThread = GetSortedSamplesPerThread(source);

            var exportedFrameNameToExportedFrameId = new Dictionary<string, int>();
            var exportedFrameIdToFrameTuple = new Dictionary<int, FrameInfo>();
            var profileEventsPerThread = new Dictionary<string, IReadOnlyList<ProfileEvent>>(samplesPerThread.Count);

            foreach (var pair in samplesPerThread)
            {
                var sortedProfileEvents = GetProfileEvents(source, pair.Value, exportedFrameNameToExportedFrameId, exportedFrameIdToFrameTuple);

                Debug.Assert(Validate(sortedProfileEvents), "The output should be always valid");

                profileEventsPerThread.Add(pair.Key.Name, sortedProfileEvents);
            };

            var orderedFrameNames = exportedFrameNameToExportedFrameId.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();

            WriteToFile(profileEventsPerThread, orderedFrameNames, writer, name);
        }

        /// <summary>
        /// writes pre-calculated data to SpeedScope format
        /// </summary>
        private static void WriteToFile(IReadOnlyDictionary<string, IReadOnlyList<ProfileEvent>> sortedProfileEventsPerThread,
            IReadOnlyList<string> orderedFrameNames, TextWriter writer, string name)
        {
            writer.Write("{");
            writer.Write($"\"exporter\": \"{GetExporterInfo()}\", ");
            writer.Write($"\"name\": \"{name}\", ");
            writer.Write("\"activeProfileIndex\": 0, ");
            writer.Write("\"$schema\": \"https://www.speedscope.app/file-format-schema.json\", ");

            writer.Write("\"shared\": { \"frames\": [ ");
            for (int i = 0; i < orderedFrameNames.Count; i++)
            {
                writer.Write($"{{ \"name\": \"{orderedFrameNames[i].Replace("\\", "\\\\").Replace("\"", "\\\"")}\" }}");

                if (i != orderedFrameNames.Count - 1)
                    writer.Write(", ");
            }
            writer.Write("] }, ");

            writer.Write("\"profiles\": [ ");

            bool isFirst = true;
            foreach (var perThread in sortedProfileEventsPerThread.OrderBy(pair => pair.Value.First().RelativeTime))
            {
                if (!isFirst)
                    writer.Write(", ");
                else
                    isFirst = false;

                var sortedProfileEvents = perThread.Value;

                writer.Write("{ ");
                writer.Write("\"type\": \"evented\", ");
                writer.Write($"\"name\": \"{perThread.Key}\", ");
                writer.Write("\"unit\": \"milliseconds\", ");
                writer.Write($"\"startValue\": {sortedProfileEvents.First().RelativeTime.ToString("R", CultureInfo.InvariantCulture)}, ");
                writer.Write($"\"endValue\": {sortedProfileEvents.Last().RelativeTime.ToString("R", CultureInfo.InvariantCulture)}, ");
                writer.Write("\"events\": [ ");
                for (int i = 0; i < sortedProfileEvents.Count; i++)
                {
                    var frameEvent = sortedProfileEvents[i];

                    writer.Write($"{{ \"type\": \"{(frameEvent.Type == ProfileEventType.Open ? "O" : "C")}\", ");
                    writer.Write($"\"frame\": {frameEvent.FrameId}, ");
                    // "R" is crucial here!!! we can't lose precision because it can affect the sort order!!!!
                    writer.Write($"\"at\": {frameEvent.RelativeTime.ToString("R", CultureInfo.InvariantCulture)} }}");

                    if (i != sortedProfileEvents.Count - 1)
                        writer.Write(", ");
                }
                writer.Write("]");
                writer.Write("}");
            }

            writer.Write("] }");
        }
        #endregion private
    }
}
