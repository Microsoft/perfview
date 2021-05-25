//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
// 
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// #define DEBUG_SERIALIZE
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Symbols
{
    /// <summary>
    /// SymPath is a class that knows how to parse _NT_SYMBOL_PATH syntax.  
    /// </summary>
    public class SymbolPath
    {
        /// <summary>
        /// This allows you to set the _NT_SYMBOL_PATH as a from the windows environment.    
        /// </summary>
        public static string SymbolPathFromEnvironment
        {
            get
            {
                var ret = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
                if (ret == null)
                {
                    ret = "";
                }

                return ret;
            }
            set
            {
                Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", value);
            }
        }

        /// <summary>
        /// This 'cleans up' a symbol path.  In particular
        /// Empty ones are replaced with good defaults (symweb or msdl)
        /// All symbol server specs have local caches (%Temp%\SymbolCache if nothing else is specified).  
        /// 
        /// Note that this routine does NOT update _NT_SYMBOL_PATH.  
        /// </summary>
        internal static SymbolPath CleanSymbolPath()
        {
            string symPathStr = SymbolPathFromEnvironment;
            if (symPathStr.Length == 0)
            {
                symPathStr = MicrosoftSymbolServerPath;
            }

            var symPath = new SymbolPath(symPathStr);
            return symPath.InsureHasCache(symPath.DefaultSymbolCache()).CacheFirst();
        }

        /// <summary>
        /// Returns the string representing a symbol path for the 'standard' Microsoft symbol servers.   
        /// This returns the public msdl.microsoft.com server if outside Microsoft.  
        /// </summary>
        public static string MicrosoftSymbolServerPath
        {
            get
            {
                if (s_MicrosoftSymbolServerPath == null)
                {
                    s_MicrosoftSymbolServerPath = s_MicrosoftSymbolServerPath +
                        ";" + @"SRV*https://msdl.microsoft.com/download/symbols" +     // Operatig system Symbols
                        ";" + @"SRV*https://nuget.smbsrc.net" +                        // Nuget symbols
                        ";" + @"SRV*https://referencesource.microsoft.com/symbols";    // .NET Runtime desktop symbols 
                }
                return s_MicrosoftSymbolServerPath;
            }
        }

        /// <summary>
        /// Create an empty symbol path
        /// </summary>
        public SymbolPath()
        {
            m_elements = new List<SymbolPathElement>();
        }
        /// <summary>
        /// Create a symbol that represents 'path' (the standard semicolon separated list of locations)
        /// </summary>
        public SymbolPath(string path)
            : this()
        {
            Add(path);
        }
        /// <summary>
        /// Returns the List of elements in the symbol path. 
        /// </summary>
        public ICollection<SymbolPathElement> Elements
        {
            get { return m_elements; }
        }

        /// <summary>
        /// Append all the elements in the semicolon separated list, 'path', to the symbol path represented by 'this'. 
        /// returns the 'this' pointer
        /// </summary>
        public SymbolPath Add(string path)
        {
            if (path == null)
            {
                return this;
            }

            path = path.Trim();
            if (path.Length == 0)
            {
                return this;
            }

            var strElems = path.Split(';');
            foreach (var strElem in strElems)
            {
                Add(new SymbolPathElement(strElem));
            }

            return this;
        }
        /// <summary>
        /// append a new symbol path element to the beginning of the symbol path represented by 'this'.
        /// returns the 'this' pointer
        /// </summary>
        public SymbolPath Add(SymbolPathElement elem)
        {
            if (elem != null && !m_elements.Contains(elem))
            {
                m_elements.Add(elem);
            }

            return this;
        }

        /// <summary>
        /// insert all the elements in the semicolon separated list, 'path' to the beginning of the symbol path represented by 'this'.
        /// returns the 'this' pointer
        /// </summary>
        public SymbolPath Insert(string path)
        {
            var strElems = path.Split(';');
            foreach (var strElem in strElems)
            {
                Insert(new SymbolPathElement(strElem));
            }

            return this;
        }
        /// <summary>
        /// insert a new symbol path element to the beginning of the symbol path represented by 'this'.
        /// returns the 'this' pointer
        /// </summary>
        public SymbolPath Insert(SymbolPathElement elem)
        {
            if (elem != null)
            {
                var existing = m_elements.IndexOf(elem);
                if (existing >= 0)
                {
                    m_elements.RemoveAt(existing);
                }

                m_elements.Insert(0, elem);
            }
            return this;
        }

        /// <summary>
        /// If you need to cache files locally, put them here.  It is defined
        /// to be the first local path of a SRV* qualification or %TEMP%\SymbolCache
        /// if not is present.
        /// </summary>
        public string DefaultSymbolCache(bool localOnly = true)
        {
            foreach (var elem in Elements)
            {
                if (elem.IsSymServer)
                {
                    if (elem.Cache != null)
                    {
                        return elem.Cache;
                    }
                    else if (!localOnly || !elem.IsRemote)
                    {
                        return elem.Target;
                    }
                }
            }
            string temp = Environment.GetEnvironmentVariable("TEMP");
            if (temp == null)
            {
                temp = ".";
            }

            return Path.Combine(temp, "SymbolCache");
        }
        /// <summary>
        /// People can use symbol servers without a local cache.  This is bad, add one if necessary. 
        /// </summary>
        public SymbolPath InsureHasCache(string defaultCachePath)
        {
            var ret = new SymbolPath();
            foreach (var elem in Elements)
            {
                ret.Add(elem.InsureHasCache(defaultCachePath));
            }

            return ret;
        }
        /// <summary>
        /// Removes all references to remote paths.  This insures that network issues don't cause grief.  
        /// </summary>
        public SymbolPath LocalOnly()
        {
            var ret = new SymbolPath();
            foreach (var elem in Elements)
            {
                ret.Add(elem.LocalOnly());
            }

            return ret;
        }
        /// <summary>
        /// Create a new symbol path which first search all machine local locations (either explicit location or symbol server cache locations)
        /// followed by all non-local symbol server.   This produces better behavior (If you can find it locally it will be fast)
        /// </summary>
        public SymbolPath CacheFirst()
        {
            var ret = new SymbolPath();
            foreach (var elem in Elements)
            {
                if (elem.IsSymServer && elem.IsRemote)
                {
                    continue;
                }

                ret.Add(elem);
            }
            foreach (var elem in Elements)
            {
                if (elem.IsSymServer && elem.IsRemote)
                {
                    ret.Add(elem);
                }
            }
            return ret;
        }

        /// <summary>
        /// Returns the string representation (semicolon separated) for the symbol path.  
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (var elem in Elements)
            {
                if (!first)
                {
                    sb.Append(";");
                }

                first = false;
                sb.Append(elem.ToString());
            }
            return sb.ToString();
        }
        /// <summary>
        /// Writes an XML representation of the symbol path to 'writer'
        /// </summary>
        public void ToXml(TextWriter writer, string indent)
        {
            writer.WriteLine("{0}<SymbolPaths>", indent);
            foreach (var elem in Elements)
            {
                writer.WriteLine("  <SymbolPath Value=\"{0}\"/>", XmlUtilities.XmlEscape(elem.ToString()));
            }

            writer.WriteLine("{0}</SymbolPaths>", indent);
        }
        /// <summary>
        /// Checks to see 'computerName' exists (there is a Domain Names Service (DNS) reply to it)
        /// This routine times out relative quickly (after 700 msec) if there is a problem reaching 
        /// the computer, and returns false.  
        /// </summary>
        public static bool ComputerNameExists(string computerName, int timeoutMSec = 700)
        {
            try
            {
                System.Net.IPHostEntry ipEntry = null;
                var result = System.Net.Dns.GetHostEntryAsync(computerName);
                if (Task.WaitAny(result, Task.Delay(timeoutMSec)) == 0)
                {
                    ipEntry = result.Result;
                }

                if (ipEntry != null)
                {
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        #region private
        private List<SymbolPathElement> m_elements;
        /// <summary>
        /// This is the backing field for the lazily-computed <see cref="MicrosoftSymbolServerPath"/> property.
        /// </summary>
        private static string s_MicrosoftSymbolServerPath;
        #endregion
    }

    /// <summary>
    /// SymPathElement represents the text between the semicolons in a symbol path.  It can be a symbol server specification or a simple directory path. 
    /// 
    /// SymPathElement follows functional conventions.  After construction everything is read-only. 
    /// </summary>
    public class SymbolPathElement
    {
        /// <summary>
        /// Returns true if this element of the symbol server path a symbol server specification
        /// </summary>
        public bool IsSymServer { get; private set; }
        /// <summary>
        /// Returns the local cache for a symbol server specification.  returns null if not specified
        /// </summary>
        public string Cache { get; private set; }
        /// <summary>
        /// Returns location to look for symbols.  This is either a directory specification or an URL (for symbol servers)
        /// This can be null if it is not specified (for cache-only paths).  
        /// </summary>
        public string Target { get; private set; }

        /// <summary>
        /// IsRemote returns true if it looks like the target is not on the local machine.
        /// </summary>
        public bool IsRemote
        {
            get
            {
                if (Target != null)
                {
                    if (Target.StartsWith(@"\\"))
                    {
                        return true;
                    }
                    // We assume drive letters from the back of the alphabet are remote.  
                    if (2 <= Target.Length && Target[1] == ':')
                    {
                        char driveLetter = Char.ToUpperInvariant(Target[0]);
                        if ('T' <= driveLetter && driveLetter <= 'Z')
                        {
                            return true;
                        }
                    }
                }

                if (!IsSymServer)
                {
                    return false;
                }

                if (Cache != null)
                {
                    return true;
                }

                if (Target == null)
                {
                    return false;
                }

                if (Target.StartsWith("http:/", StringComparison.OrdinalIgnoreCase)
                    || Target.StartsWith("https:/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
        }
        /// <summary>
        /// Returns the string repsentation for the symbol server path element (e.g. SRV*c:\temp*\\symbols\symbols)
        /// </summary>
        public override string ToString()
        {
            if (IsSymServer)
            {
                var ret = "SRV";
                if (Cache != null)
                {
                    ret += "*" + Cache;
                }

                if (Target != null)
                {
                    ret += "*" + Target;
                }

                return ret;
            }
            else
            {
                return Target;
            }
        }
        #region overrides

        /// <summary>
        /// Implements object interface
        /// </summary>
        public override bool Equals(object obj)
        {
            var asSymPathElem = obj as SymbolPathElement;
            if (asSymPathElem == null)
            {
                return false;
            }

            return
                Target == asSymPathElem.Target &&
                Cache == asSymPathElem.Cache &&
                IsSymServer == asSymPathElem.IsSymServer;
        }
        /// <summary>
        /// Implements object interface
        /// </summary>
        public override int GetHashCode()
        {
            return Target.GetHashCode();
        }
        #endregion
        #region private
        internal SymbolPathElement InsureHasCache(string defaultCachePath)
        {
            if (!IsSymServer)
            {
                return this;
            }

            if (Cache != null)
            {
                return this;
            }

            if (Target == defaultCachePath)
            {
                return this;
            }

            return new SymbolPathElement(true, defaultCachePath, Target);
        }
        internal SymbolPathElement LocalOnly()
        {
            if (!IsRemote)
            {
                return this;
            }

            if (Cache != null)
            {
                return new SymbolPathElement(true, null, Cache);
            }

            return null;
        }

        internal SymbolPathElement(bool isSymServer, string cache, string target)
        {
            IsSymServer = isSymServer;
            Cache = cache;
            Target = target;
        }
        internal SymbolPathElement(string strElem)
        {
            var m = Regex.Match(strElem, @"^\s*SRV\*((\s*.*?\s*)\*)?\s*(.*?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                IsSymServer = true;
                Cache = m.Groups[2].Value;
                Target = m.Groups[3].Value;
                if (Cache.Length == 0)
                {
                    Cache = null;
                }

                if (Target.Length == 0)
                {
                    Target = null;
                }

                return;
            }
            m = Regex.Match(strElem, @"^\s*CACHE\*(.*?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                IsSymServer = true;
                Cache = m.Groups[1].Value;
            }
            else
            {
                Target = strElem.Trim();
            }
        }
        #endregion
    }

}
