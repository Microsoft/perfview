// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
public enum Address : long { };

internal class Program
{
    /// <summary>
    /// This program generates C# code for manipulating ETW events given the event XML schema definition
    /// </summary>
    /// <param name="args"></param>
    public static int Main(string[] args)
    {
        return CommandLineUtilities.RunConsoleMainWithExceptionProcessing(delegate
        {
            // See code:#CommandLineDefinitions for command line definitions
            CommandLine commandLine = new CommandLine();
#if PRIVATE
            if (commandLine.Mof)
            {
                string mofFile = commandLine.ManifestFile;
                commandLine.ManifestFile = Path.ChangeExtension(mofFile, ".man.xml");
                Console.WriteLine("Converting " + mofFile + " to the manifest " + commandLine.ManifestFile);
                CreateManifestFromMof(mofFile, commandLine.ManifestFile, commandLine.Verbose);
            }
#endif

            if (commandLine.EventSource != null)
            {
                string exe = commandLine.ManifestFile;
                var eventSources = EventSourceFinder.GetEventSourcesInFile(exe, true);
                bool foundEventSource = false;

                foreach (var eventSource in eventSources)
                {
                    var name = EventSourceFinder.GetName(eventSource);
                    Console.WriteLine("Found EventSource {0} in {1}", name, exe);
                    if (commandLine.EventSource.Length == 0 || string.Compare(name, commandLine.EventSource, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        commandLine.ManifestFile = Path.ChangeExtension(commandLine.OutputFile, ".manifest.xml");
                        Console.WriteLine("Generated manifest from EventSource {0}", name);
                        File.WriteAllText(commandLine.ManifestFile, EventSourceFinder.GetManifest(eventSource));
                        foundEventSource = true;
                        break;
                    }
                }
                if (!foundEventSource)
                {
                    throw new ApplicationException("Could not find an eventSource named " + commandLine.EventSource + " in " + exe);
                }
            }

            Console.WriteLine("Reading manifest file " + commandLine.ManifestFile);
#if PRIVATE
#if MERGE
            if (commandLine.Merge)
            {
                bool shouldMerge = File.Exists(commandLine.OutputFile) && File.Exists(commandLine.BaseFile);
                string newBaseLine = commandLine.BaseFile + ".new";
                string newOutput = commandLine.OutputFile + ".new";
                schema.CreateParserSource(newBaseLine);
                Console.WriteLine("Writing output to " + newBaseLine);
                ApplyRenames(commandLine.RenameFile, newBaseLine);

                if (shouldMerge)
                {
                    string cmd = "vssmerge -M \"" +
                        commandLine.BaseFile + "\" \"" +
                        newBaseLine + "\" \"" +
                        commandLine.OutputFile + "\" \"" +
                        newOutput + "\"";

                    Console.WriteLine("Running: " + cmd);
                    Command command = Command.Run(cmd, new CommandOptions().AddTimeout(CommandOptions.Infinite).AddNoThrow());
                    if (command.ExitCode == 0 && File.Exists(newOutput))
                    {
                        Console.WriteLine("Success, updating output and baseline file (old files in *.orig)");
                        FileUtilities.ForceCopy(commandLine.OutputFile, commandLine.OutputFile + ".orig");
                        FileUtilities.ForceCopy(commandLine.BaseFile, commandLine.BaseFile + ".orig");
                        FileUtilities.ForceMove(newOutput, commandLine.OutputFile);
                        FileUtilities.ForceMove(newBaseLine, commandLine.BaseFile);
                    }
                    else
                    {
                        Console.WriteLine("Error running merge command, doing nothing.");
                        return 1;
                    }
                }
                else
                {
                    FileUtilities.ForceMove(newBaseLine, commandLine.OutputFile);
                    FileUtilities.ForceMove(newBaseLine, commandLine.BaseFile);
                }
            }
            else
#endif //  MERGE
                {
                    if (File.Exists(commandLine.BaseFile))
                        throw new ApplicationException("Baseline file " + commandLine.BaseFile + " exists.  Did you mean to merge?");

                    Console.WriteLine("Writing output to " + commandLine.OutputFile);
                    schema.CreateParserSource(commandLine.OutputFile);

                }

#endif // PRIVATE
            List<ETWManifest.Provider> providers = ETWManifest.Provider.ParseManifest(commandLine.ManifestFile);
            foreach (var provider in providers)
            {
                var parserGen = new TraceParserGen(provider);
                parserGen.Internal = commandLine.Internal;
                parserGen.NeedsParserState = commandLine.NeedsState;

                Console.WriteLine("Writing output to " + commandLine.OutputFile);
                parserGen.GenerateTraceEventParserFile(commandLine.OutputFile);
                break;  // TODO FIX NOW allow multiple providers in a manifest.
            }
#if PRIVATE
            ApplyRenames(commandLine.RenameFile, commandLine.OutputFile);
#endif
            return 0;
        });
    }

#if PRIVATE
    /// <summary>
    /// Applies the regular expressions in 'renameFileName' to the 'targetFile'
    /// </summary>
    private static void ApplyRenames(string renameFileName, string targetFile)
    {
        if (renameFileName == null)
            return;

        string targetData = File.ReadAllText(targetFile);
        StreamReader renameFile = File.OpenText(renameFileName);
        for (int lineNum = 0; ; )
        {
            string line = renameFile.ReadLine();
            lineNum++;
            if (line == null)
                break;
            if (Regex.IsMatch(line, @"^\s*$"))
                continue;
            if (line.StartsWith("#"))
                continue;
            int spaceIdx = line.IndexOf(' ');
            if (spaceIdx < 0)
                throw new ApplicationException("Error, rename file syntax error at line " + lineNum);
            string pat = line.Substring(0, spaceIdx);
            string replace = line.Substring(spaceIdx + 1);
            Console.WriteLine("Renaming " + pat + " -> " + replace);
            targetData = Regex.Replace(targetData, pat, replace);
        }
        File.WriteAllText(targetFile, targetData);
    }

    class MofClass
    {
        public string[] Opcodes
        {
            get
            {
                Match match = Regex.Match(attributes, @"EventType\s*[{\(]\s*(.*)\s*[\)}]", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;
                return new Regex(@"\s*,\s*").Split(match.Groups[1].Value);
            }
        }
        public string[] OpcodeNames
        {
            get
            {
                Match match = Regex.Match(attributes, "EventTypeName\\s*[{\\(]\\s*\"(.*)\"\\s*[\\)}]", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;
                return new Regex("\"\\s*,\\s*\"").Split(match.Groups[1].Value);
            }
        }
        public string Version
        {
            get
            {
                Match match = Regex.Match(attributes, @"EventVersion\s*\(\s*(\S*)\s*\)", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    if (superClass != null && !IsTask())
                        return superClass.Version;
                    return "0";
                }
                return match.Groups[1].Value;
            }
        }
        public string Guid
        {
            get
            {
                Match match = Regex.Match(attributes, "Guid\\s*\\(\\s*\"{(.*)}\"\\s*\\)", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;
                return match.Groups[1].Value;
            }
        }
        public MofClass() { fields = new List<MofField>(); }
        public bool IsProvider()
        {
            return superClassName == "EventTrace";
        }
        public MofClass Provider()
        {
            if (IsProvider())
                return this;
            Debug.Assert(superClass != null);
            return superClass.Provider();
        }
        public MofClass Task()
        {
            if (IsTask())
            {
                if (cannonVersion != null)
                    return cannonVersion;
                return this;
            }
            Debug.Assert(superClass != null);
            return superClass.Task();
        }
        public bool IsTask()
        {
            return superClass != null && superClass.IsProvider();
        }
        public bool IsTemplate()
        {
            return !IsProvider() && !IsTask();
        }
        public string attributes;
        public string name;
        public string superClassName;
        public MofClass superClass;
        public string body;
        public List<MofField> fields;
        public int id;
        public MofClass prevVersions;
        public MofClass cannonVersion;
    }

    class MofField
    {
        public MofField() { }
        public string attributes;
        public string name;
        public string type;
        public int arrayCount;
    }

    /// <summary>
    /// Given a textual MOf file create a XML file 'manifestFileName' from it
    /// </summary>
    private static void CreateManifestFromMof(string mofFileName, string manifestFileName, bool verbose)
    {
        string mofFileData = File.ReadAllText(mofFileName);
        mofFileData = Regex.Replace(mofFileData, "#pragma[^\r\n]*", "");
        mofFileData = Regex.Replace(mofFileData, "//[^\r\n]*", "");

        using (TextWriter manifestFile = File.CreateText(manifestFileName))
        {
            List<MofClass> mofClasses = new List<MofClass>();
            Dictionary<string, MofClass> classesByName = new Dictionary<string, MofClass>();
            Regex classPat = new Regex(@"\s*\[([^\]]*)\]\s*class\s+(\w+)\s*:\s*(\w+)\s*{\s*(([^}]|({[^}]*}))*)}\s*;\s*");
            int pos = 0;
            while (pos < mofFileData.Length)
            {
                Match classMatch = classPat.Match(mofFileData, pos);
                if (!classMatch.Success || classMatch.Index != pos)
                    throw new ApplicationException("Error parsing class definition starting at line " + (NewLineCount(mofFileData, 0, pos) + 1));

                MofClass mofClass = new MofClass();
                mofClass.attributes = classMatch.Groups[1].Value;
                mofClass.name = classMatch.Groups[2].Value;
                mofClass.superClassName = classMatch.Groups[3].Value;
                mofClass.body = classMatch.Groups[4].Value;
                if (verbose)
                    Console.WriteLine("Got MOF class " + mofClass.name);

                Regex fieldPat = new Regex(@"\s*\[([^\]]*)\]\s*(\w+)\s+(\w+)(\[(\d+)\])?\s*;\s*");
                int bodyStart = classMatch.Groups[4].Index;
                int fieldPos = 0;
                while (fieldPos < mofClass.body.Length)
                {
                    Match fieldMatch = fieldPat.Match(mofClass.body, fieldPos);
                    if (!fieldMatch.Success || fieldMatch.Index != fieldPos)
                        throw new ApplicationException("Error parsing field definition at line " + (NewLineCount(mofFileData, 0, bodyStart + fieldPos) + 1));
                    MofField mofField = new MofField();
                    mofField.attributes = fieldMatch.Groups[1].Value;
                    mofField.type = fieldMatch.Groups[2].Value;
                    mofField.name = fieldMatch.Groups[3].Value;
                    string arrayBoundStr = fieldMatch.Groups[5].Value;
                    if (arrayBoundStr.Length > 0)
                        mofField.arrayCount = int.Parse(arrayBoundStr);
                    mofClass.fields.Add(mofField);
                    if (verbose)
                        Console.WriteLine("   Got field " + mofField.name + " of type " + mofField.type);
                    fieldPos += fieldMatch.Length;
                }
                if (verbose)
                    Console.WriteLine("   Found " + mofClass.fields.Count + " Fields");
                mofClasses.Add(mofClass);
                classesByName.TryGetValue(mofClass.superClassName, out mofClass.superClass);
                classesByName[mofClass.name] = mofClass;
                pos += classMatch.Length;
            }

            foreach (string name in classesByName.Keys)
            {
                Match m = Regex.Match(name, @"(.*)_V(\d+)$");
                MofClass curVersion;
                if (m.Success && classesByName.TryGetValue(m.Groups[1].Value, out curVersion))
                {
                    MofClass prevVersion = classesByName[name];
                    prevVersion.prevVersions = curVersion.prevVersions;
                    curVersion.prevVersions = prevVersion;
                    prevVersion.cannonVersion = curVersion;
                    bool ret = mofClasses.Remove(prevVersion);
                    Debug.Assert(ret);
                }
            }
            classesByName = null;       // we are done with the dictionary.

            // OK at this point we have a mofClasses list intialized, so now we spit it out as
            // and XML manifest.

            manifestFile.WriteLine("<instrumentationManifest xmlns=\"http://schemas.microsoft.com/win/2004/08/events\">");
            manifestFile.WriteLine("  <instrumentation xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:win=\"http://manifests.microsoft.com/win/2004/08/windows/events\">");
            manifestFile.WriteLine("    <events xmlns=\"http://schemas.microsoft.com/win/2004/08/events\">");

            foreach (MofClass provider in mofClasses)
            {
                if (!provider.IsProvider())
                    continue;

                manifestFile.WriteLine("      <provider name=\"" + provider.name + "\" guid=\"{" + provider.Guid + "}\">");

                /***
                // Output all opcodes
                manifestFile.WriteLine("        <opcodes>");
                manifestFile.WriteLine("          <opcode name=\"" + "\" symbol=\""+"\" value=\""+"\"/>");
                manifestFile.WriteLine("        </opcodes>");
                ***/

                // Output all tasks
                manifestFile.WriteLine("        <tasks>");
                int taskId = 0;
                Dictionary<string, MofClass> tasksSoFar = new Dictionary<string, MofClass>();
                foreach (MofClass task in mofClasses)
                {
                    if (!task.IsTask())
                        continue;
                    if (task.Provider() != provider)
                        continue;
                    string taskGuid = task.Guid;

                    MofClass canonTask;
                    if (tasksSoFar.TryGetValue(taskGuid, out canonTask))
                    {
                        task.id = canonTask.id;
                        continue;
                    }
                    tasksSoFar.Add(taskGuid, task);

                    // TODO Debug.Assert(task.id == 0);
                    task.id = taskId++;
                    manifestFile.WriteLine("          <task name=\"" + task.name + "\"  value=\"" + task.id + "\" eventGUID=\"{" + taskGuid + "}\"/>");
                }
                manifestFile.WriteLine("        </tasks>");

                // output all templates
                manifestFile.WriteLine("        <templates>");
                int templateId = 0;
                foreach (MofClass template in mofClasses)
                {
                    if (!template.IsTemplate())
                        continue;
                    if (template.Provider() != provider)
                        continue;

                    // TODO Debug.Assert(template.id == 0);
                    template.id = templateId++;

                    manifestFile.WriteLine("          <template tid=\"" + template.name + "\">");
                    foreach (MofField mofField in template.fields)
                    {
                        string inType = ManifestType(mofField.type, mofField.attributes);
                        manifestFile.Write("            <data name=\"" + mofField.name + "\" inType=\"" + inType + "\"");
                        if (mofField.arrayCount != 0)
                            manifestFile.Write(" count=\"" + mofField.arrayCount + "\"");
                        if (mofField.attributes.Contains("format(\"x\")"))
                        {
                            Match m = Regex.Match(inType, @"int(\d+)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                string outType = "win:hexInt" + m.Groups[1].Value;
                                manifestFile.Write(" outType=\"" + outType + "\"");
                            }
                        }
                        manifestFile.WriteLine("/>");
                    }
                    manifestFile.WriteLine("          </template>");
                }
                manifestFile.WriteLine("        </templates>");

                // output all events
                manifestFile.WriteLine("        <events>");
                int newEventIDs = 10000;
                Dictionary<string, int> EventIDs = new Dictionary<string, int>();
                foreach (MofClass aEvent in mofClasses)
                {
                    if (!aEvent.IsTemplate())
                        continue;
                    if (aEvent.Provider() != provider)
                        continue;

                    string[] opcodeNames = aEvent.OpcodeNames;
                    string[] opcodes = aEvent.Opcodes;
                    if (opcodeNames.Length == 0)
                        Console.WriteLine("Warning template " + aEvent.name + " has no opcodes!");
                    Debug.Assert(opcodes.Length == opcodeNames.Length);
                    for (int j = 0; j < opcodes.Length; j++)
                    {
                        string opcode = opcodes[j];
                        string opcodeName = opcodeNames[j].Replace('-', '_');
                        int EventID;
                        string eventKey = aEvent.Task().name + opcodeName;
                        if (!EventIDs.TryGetValue(eventKey, out EventID))
                        {
                            EventID = newEventIDs++;
                            EventIDs.Add(eventKey, EventID);
                        }

                        // TODO opcode wrong.
                        manifestFile.WriteLine("          <event value=\"" + EventID + "\" version=\"" + aEvent.Version +
                            "\" template=\"" + aEvent.name +
                            "\" opcode=\"" + opcode + "\" task=\"" + aEvent.Task().name + "\" symbol=\"" + aEvent.Task().name + opcodeName + "\"/>");
                    }
                }
                manifestFile.WriteLine("        </events>");
                manifestFile.WriteLine("      </provider>");
            }
            manifestFile.WriteLine("    </events>");
            manifestFile.WriteLine("  </instrumentation>");
            manifestFile.WriteLine("</instrumentationManifest>");

        }
    }

    private static string ManifestType(string typeName, string attributes)
    {
        if (typeName == "object")
        {
            Match match = Regex.Match(attributes, "extension\\(\"(.*)\"\\)", RegexOptions.IgnoreCase);
            Debug.Assert(match.Success);
            string extensionType = match.Groups[1].Value;

            if (extensionType == "Sid")
                return "trace:WBEMSid";
            if (extensionType == "Guid")
                return "win:GUID";
            return "trace:" + extensionType;
        }

        if (typeName == "string")
        {
            Match match = Regex.Match(attributes, "format\\(\"w\"\\)", RegexOptions.IgnoreCase);
            if (match.Success)
                return "win:UnicodeString";
            else
                return "win:AnsiString";
        }

        if (typeName == "char16")
            return "trace:UnicodeChar";

        if (typeName.StartsWith("sint"))
            typeName = typeName.Substring(1);

        if (typeName.StartsWith("uint"))
            typeName = "UI" + typeName.Substring(2);

        if (attributes.Contains("pointer"))
            typeName = "pointer";

        return "win:" + typeName.Substring(0, 1).ToUpper() + typeName.Substring(1);
    }
#endif

    /// <summary>
    /// Counts the number of newlines in 'str' from 'startIndex' of length 'length'
    /// </summary>
    private static int NewLineCount(string str, int startIndex, int length)
    {
        int ret = 0;
        for (; ; )
        {
            int newLineIndex = str.IndexOf('\n', startIndex, length);
            if (newLineIndex < 0)
            {
                return ret;
            }

            length -= newLineIndex - startIndex + 1;
            startIndex = newLineIndex + 1;
            ret++;
        }
    }
}

/// <summary>
/// EventSourceFinder is a class that can find all the EventSources in a file and can
/// </summary>
internal static class EventSourceFinder
{
    // TODO remove and depend on framework for these instead.
    public static Guid GetGuid(Type eventSource)
    {
        foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
        {
            foreach (var arg in attrib.NamedArguments)
            {
                if (arg.MemberInfo.Name == "Guid")
                {
                    var value = (string)arg.TypedValue.Value;
                    return new Guid(value);
                }
            }
        }

        return GenerateGuidFromName(GetName(eventSource).ToUpperInvariant());
    }
    public static string GetName(Type eventSource)
    {
        foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
        {
            foreach (var arg in attrib.NamedArguments)
            {
                if (arg.MemberInfo.Name == "Name")
                {
                    var value = (string)arg.TypedValue.Value;
                    return value;
                }
            }
        }
        return eventSource.Name;
    }
    public static string GetManifest(Type eventSource)
    {
        // Invoke GenerateManifest
        string manifest = (string)eventSource.BaseType.InvokeMember("GenerateManifest",
            BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.Public,
            null, null, new object[] { eventSource, "" });

        return manifest;
    }

    // TODO load it its own appdomain so we can unload them properly.
    public static IEnumerable<Type> GetEventSourcesInFile(string fileName, bool allowInvoke = false)
    {
        System.Reflection.Assembly assembly;
        try
        {
            if (allowInvoke)
            {
                assembly = System.Reflection.Assembly.LoadFrom(fileName);
            }
            else
            {
                assembly = System.Reflection.Assembly.ReflectionOnlyLoadFrom(fileName);
            }
        }
        catch (Exception e)
        {
            // Convert to an application exception TODO is this a good idea?
            throw new ApplicationException(e.Message);
        }

        Dictionary<Assembly, Assembly> soFar = new Dictionary<Assembly, Assembly>();
        GetStaticReferencedAssemblies(assembly, soFar);

        List<Type> eventSources = new List<Type>();
        foreach (Assembly subAssembly in soFar.Keys)
        {
            try
            {
                foreach (Type type in subAssembly.GetTypes())
                {
                    if (type.BaseType != null && type.BaseType.Name == "EventSource")
                    {
                        eventSources.Add(type);
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Problem loading {0} module, skipping.", subAssembly.GetName().Name);
            }
        }
        return eventSources;
    }
    #region private
    private static Guid GenerateGuidFromName(string name)
    {
        // The algorithm below is following the guidance of http://www.ietf.org/rfc/rfc4122.txt
        // Create a blob containing a 16 byte number representing the namespace
        // followed by the unicode bytes in the name.
        var bytes = new byte[name.Length * 2 + 16];
        uint namespace1 = 0x482C2DB2;
        uint namespace2 = 0xC39047c8;
        uint namespace3 = 0x87F81A15;
        uint namespace4 = 0xBFC130FB;
        // Write the bytes most-significant byte first.
        for (int i = 3; 0 <= i; --i)
        {
            bytes[i] = (byte)namespace1;
            namespace1 >>= 8;
            bytes[i + 4] = (byte)namespace2;
            namespace2 >>= 8;
            bytes[i + 8] = (byte)namespace3;
            namespace3 >>= 8;
            bytes[i + 12] = (byte)namespace4;
            namespace4 >>= 8;
        }
        // Write out  the name, most significant byte first
        for (int i = 0; i < name.Length; i++)
        {
            bytes[2 * i + 16 + 1] = (byte)name[i];
            bytes[2 * i + 16] = (byte)(name[i] >> 8);
        }

        // Compute the Sha1 hash
        var sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] hash = sha1.ComputeHash(bytes);

        // Create a GUID out of the first 16 bytes of the hash (SHA-1 create a 20 byte hash)
        int a = (((((hash[3] << 8) + hash[2]) << 8) + hash[1]) << 8) + hash[0];
        short b = (short)((hash[5] << 8) + hash[4]);
        short c = (short)((hash[7] << 8) + hash[6]);

        c = (short)((c & 0x0FFF) | 0x5000);   // Set high 4 bits of octet 7 to 5, as per RFC 4122
        Guid guid = new Guid(a, b, c, hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
        return guid;
    }

    private static void GetStaticReferencedAssemblies(Assembly assembly, Dictionary<Assembly, Assembly> soFar)
    {
        soFar[assembly] = assembly;
        string assemblyDirectory = Path.GetDirectoryName(assembly.ManifestModule.FullyQualifiedName);
        foreach (AssemblyName childAssemblyName in assembly.GetReferencedAssemblies())
        {
            try
            {
                // TODO is this is at best heuristic.
                string childPath = Path.Combine(assemblyDirectory, childAssemblyName.Name + ".dll");
                Assembly childAssembly = null;
                if (File.Exists(childPath))
                {
                    childAssembly = Assembly.ReflectionOnlyLoadFrom(childPath);
                }

                //TODO do we care about things in the GAC?   it expands the search quite a bit.
                //else
                //    childAssembly = Assembly.Load(childAssemblyName);

                if (childAssembly != null && !soFar.ContainsKey(childAssembly))
                {
                    GetStaticReferencedAssemblies(childAssembly, soFar);
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Could not load assembly " + childAssemblyName + " skipping.");
            }
        }
    }
    #endregion
}

/// <summary>
/// The code:CommandLine class holds the parsed form of all the command line arguments.  It is
/// intialized by handing it the 'args' array for main, and it has a public field for each named argument
/// (eg -debug). See code:#CommandLineDefinitions for the code that defines the arguments (and the help
/// strings associated with them).
///
/// See code:CommandLineParser for more on parser itself.
/// </summary>
internal class CommandLine
{
    public CommandLine()
    {
        bool usersGuide = false;
        CommandLineParser.ParseForConsoleApplication(delegate (CommandLineParser parser)
        {
            // #CommandLineDefinitions
            parser.DefineParameterSet("UsersGuide", ref usersGuide, true, "Display the users guide.");
            parser.DefineDefaultParameterSet("Generates a set of C# classes that can be used with TraceEvent to parse ETW events.\r\n" +
                "OS providers can be listed using 'logMan query Providers' and a manifest obtained by doing\r\n" +
                "* PerfView /nogui userCommand DumpRegisteredManifest PROVIDER_NAME\r\n" +
                "EventSource manifests can be obtained from an ETL file containing EventSource data using\r\n" +
                "* PerfView /nogui userCommand DumpEventSourceManifests etlFileName\r\n" +
                "If the DLL containing the EventSource is available, TraceParserGen can use it directly with the /EventSource qualifier.\r\n" +
                "* TraceParserGen /EventSource EVENTSOURCE_DLL\r\n");
            parser.DefineOptionalQualifier("Verbose", ref Verbose, "Print Verbose information.");
            parser.DefineOptionalQualifier("EventSource", ref EventSource, "Assume 'ManifestFile' is a text EXE or DLL, " +
                "This parameter indicates the name of the event source in the EXE or DLL to generate.  " +
                "Passing the empty string will select every eventSource in the file.");
            parser.DefineOptionalQualifier("Internal", ref Internal, "If specified generates a provider that defines the overrides \"internal\".");
            parser.DefineOptionalQualifier("NeedsState", ref NeedsState, "If specified generates a provider that has a state class associated with it.");
            parser.DefineParameter("ManifestFile", ref ManifestFile, "ETW Manifest to generate C# TraceEvent subclasses from.");
            parser.DefineOptionalParameter("OutputFile", ref outputFile, "The output C# file to generate.");
#if PRIVATE
            parser.DefineOptionalQualifier("Internal", ref Internal, "Make the Dispatch and Verify methods internal protected rather than just protected.");
            parser.DefineOptionalQualifier("Mof", ref Mof, "Assume 'ManifestFile' is a text MOF description instead of a manifest file.");
            parser.DefineOptionalQualifier("Old", ref Old, "Use old mechanism for generating a parser gen.");
#endif
#if MERGE
            parser.DefineOptionalQualifier("Merge", ref Merge, "Merge the differences between a /baseFile and the output file.");
            parser.DefineOptionalQualifier("BaseFile", ref baseFile, "If /merge is specified, the differences between this file and output file will be merged in.");
            parser.DefineOptionalQualifier("RenameFile", ref RenameFile, "If /renameFile is a file that has two words per line, specifying how to rename identifiers.  They can be .NET regular expressions");
#endif
        });
        if (usersGuide)
        {
            UsersGuide.DisplayConsoleAppUsersGuide("UsersGuide.htm");
        }
    }
    public string ManifestFile;
    public string OutputFile
    {
        get
        {
            if (outputFile == null)
            {
                outputFile = Path.ChangeExtension(ManifestFile, ".cs");
            }

            return outputFile;
        }
    }
    public string BaseFile
    {
        get
        {
            if (baseFile == null)
            {
                baseFile = Path.ChangeExtension(outputFile, ".base.cs");
            }

            return baseFile;
        }
    }

    public string EventSource;
    public bool Verbose;
    public bool NeedsState;
    public bool Internal;
#if PRIVATE
    public string RenameFile;
    public bool Merge;
    public bool Mof;
#endif
    #region private
    private string outputFile;
    public string baseFile;
    #endregion
};

