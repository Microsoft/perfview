using Microsoft.Diagnostics.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Utilities
{
    /// <summary>
    /// Some applications need to make files that are associated with the application
    /// but also have affinity with other files on the disk.   This class helps manage this
    /// </summary>
    internal static class CacheFiles
    {
        public static double KeepTimeInDays { get; set; }
        public static string CacheDir
        {
            get
            {
                if (s_CacheDir == null)
                {
                    Assembly exeAssembly = Assembly.GetExecutingAssembly();
                    string exePath = exeAssembly.ManifestModule.FullyQualifiedName;
                    string exeName = Path.GetFileNameWithoutExtension(exePath);
                    string tempDir = Environment.GetEnvironmentVariable("TEMP");
                    if (tempDir == null)
                    {
                        tempDir = ".";
                    }

                    s_CacheDir = Path.Combine(tempDir, exeName);

                    string keepTimeEnvVarName = exeName + "_Cache_KeepTimeInDays";
                    string keepTimeEnvVarValueStr = Environment.GetEnvironmentVariable(keepTimeEnvVarName);
                    double keepTimeEnvVarValue;
                    if (keepTimeEnvVarName != null && double.TryParse(keepTimeEnvVarValueStr, out keepTimeEnvVarValue))
                    {
                        // Insure that keep time is at least 10 mins.   This avoids files disappearing while in use. 
                        if (keepTimeEnvVarValue < .007f)
                        {
                            keepTimeEnvVarValue = .007f;
                        }

                        KeepTimeInDays = keepTimeEnvVarValue;
                    }
                    else if (KeepTimeInDays == 0)
                    {
                        KeepTimeInDays = 5;
                    }

                    Directory.CreateDirectory(s_CacheDir);
                }
                return s_CacheDir;
            }
            set { s_CacheDir = value; }
        }

        /// <summary>
        /// Find a path name for the file 'baseFilePath' (which can be a path name to anywhere).
        /// which has the extension 'extension'.  It will always return something in 'CacheDir'
        /// and thus might go away.
        /// </summary>
        /// <remarks>
        /// Note that the file 'baseFilePath' is assumed to exist.
        /// </remarks>
        public static string FindFile(string baseFilePath, string extension = "")
        {
            // We expect the original file to exist
            Debug.Assert(File.Exists(baseFilePath));

            var baseFileName = Path.GetFileNameWithoutExtension(baseFilePath);
            var baseFileInfo = new FileInfo(baseFilePath);

            // The hash is a combination of full path, size and last write timestamp
            var hashData = Tuple.Create(Path.GetFullPath(baseFilePath), baseFileInfo.Length, baseFileInfo.LastWriteTimeUtc);
            int hash = hashData.GetHashCode();

            string ret = Path.Combine(CacheDir, baseFileName + "_" + hash.ToString("x") + extension);
            if (File.Exists(ret))
            {
                // See if it is up to date. 
                if (File.GetLastWriteTimeUtc(ret) < baseFileInfo.LastWriteTimeUtc)
                {
                    FileUtilities.ForceDelete(ret);
                }
                else
                {
                    // Set the last access time so we can clean up files based on their last usage.
                    // However if someone else is actual using the file, we don't want an 'in use' 
                    // exception, so treat this part as optional.  
                    try { File.SetLastAccessTimeUtc(ret, DateTime.UtcNow); }
                    catch (Exception) { }
                }
            }
            if (!s_didCleanup)
            {
                s_didCleanup = true;
                Cleanup();
            }
            return ret;
        }
        public static void Cleanup()
        {
            CleanupDirectory(CacheDir, KeepTimeInDays);
        }

        public static void CleanupDirectory(string directory, double keepTimeInDays)
        {
            var nowUTC = DateTime.UtcNow;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if ((nowUTC - File.GetLastAccessTimeUtc(file)).TotalDays > keepTimeInDays)
                {
                    try
                    {
                        File.Delete(file);      // Don't try to hard. If it is in use let it be.  
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// Returns true if the output file is up to date with respect to the input file (exists and created after it). 
        /// </summary>
        public static bool UpToDate(string outputFile, string inputFile)
        {
            return File.Exists(outputFile) && File.GetLastWriteTimeUtc(inputFile) <= File.GetLastWriteTimeUtc(outputFile);
        }

        #region private 
        private static string s_CacheDir;
        private static bool s_didCleanup;
        #endregion 
    }
}
