using System;
using System.Globalization;

namespace Microsoft.Diagnostics.Utilities
{
    /// <summary>
    /// Provides utility operations for <see cref="double"/> ranges.
    /// </summary>
    internal static class RangeUtilities
    {
        /// <summary>
        /// Array of range separators built from the current culture.
        /// </summary>
        /// <remarks>The user can set any character as a separator and that character can turn a regular expression invalid.</remarks>
        private static readonly string[] rangeSeparators = ResolveRangeSeparators();

        /// <summary>
        /// Resolves the range separators for the current culture.
        /// </summary>
        /// <returns>The range separators for the current culture.</returns>
        private static string[] ResolveRangeSeparators()
        {
            var listSeparator = " ";

            if (string.IsNullOrWhiteSpace(CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator)
                || string.IsNullOrWhiteSpace(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator))
            {
                listSeparator = "|";
            }

            return new string[] { listSeparator };
        }

        /// <summary>
        /// Converts the <paramref name="text"/> representing a range of <see cref="double"/> as as <see cref="string"/> to its <paramref name="start"/> and <paramref name="end"/> using the <see cref="CultureInfo.CurrentCulture"/>.
        /// </summary>
        /// <param name="text">A <see cref="string"/> containing a number to convert.</param>
        /// <param name="start">When this method returns, contains a <see cref="double"/> value representing the start of the range if the conversion succeeds, or zero if the conversion fails.</param>
        /// <param name="end">When this method returns, contains a <see cref="double"/> value representing the end of the range if the conversion succeeds, or zero if the conversion fails.</param>
        /// <returns><see langword="true"/> if s was converted successfully; otherwise, <see langword="false"/>.</returns>
        public static bool TryParse(string text, out double start, out double end)
        {
            // The user can set any character as a separator and that character can turn a regular expression invalid.
            var parts = text.Split(rangeSeparators, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || !double.TryParse(parts[0], out start) || !double.TryParse(parts[1], out end))
            {
                start = default;
                end = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts the <paramref name="start"/> and <paramref name="end"/> of a range to a <see cref="string"/> representation using the  <see cref="CultureInfo.CurrentCulture"/>.
        /// </summary>
        /// <param name="start">The start of the range.</param>
        /// <param name="end">The end of the range.</param>
        /// <returns>The <see cref="string"/> representation of the range.</returns>
        public static string ToString(double start, double end) => ToString(start.ToString("n3", CultureInfo.CurrentCulture), end.ToString("n3", CultureInfo.CurrentCulture));

        /// <summary>
        /// Converts the <paramref name="startText"/> and <paramref name="endText"/> of a range to a <see cref="string"/> representation using the  <see cref="CultureInfo.CurrentCulture"/>.
        /// </summary>
        /// <param name="startText">The start of the range.</param>
        /// <param name="endText">The end of the range.</param>
        /// <returns>The <see cref="string"/> representation of the range.</returns>
        public static string ToString(string startText, string endText) => startText + rangeSeparators[0] + endText;
    }
}
