using System;


namespace Utilities
{
    // This class is a helper that hides the OS Version tests that you might have done with Evironment.OSVersion
    // but can't because this API was removed in .NET COre. 
    internal static class OperatingSystemVersion
    {
        public const int Win10 = 100;
        public const int Win8 = 62;
        public const int Win7 = 61;
        public const int Vista = 60;
        /// <summary>
        /// requiredOSVersion is a number that is the major version * 10 + minor.  Thus
        ///     Win 10 == 100
        ///     Win 8 == 62
        ///     Win 7 == 61
        ///     Vista == 60
        /// This returns true if true OS version is >= 'requiredOSVersion
        /// </summary>
        public static bool AtLeast(int requiredOSVersion)
        {
            int osVersion = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            return osVersion >= requiredOSVersion;
        }
    }
}