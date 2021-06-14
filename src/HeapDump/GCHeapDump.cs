using FastSerialization;
using Graphs;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Address = System.UInt64;


/// <summary>
/// Represents a .GCDump file.  You can open it for reading with the construtor
/// and you can write one with WriteMemoryGraph 
/// </summary>
public class GCHeapDump : IFastSerializable, IFastSerializableVersion
{
    public GCHeapDump(string inputFileName) :
        this(new Deserializer(inputFileName))
    { }

    public GCHeapDump(Stream inputStream, string streamName) :
        this(new Deserializer(inputStream, streamName))
    { }

    /// <summary>
    /// Writes the memory graph 'graph' as a .gcump file to 'outputFileName'
    /// 'toolName' is the name of the tool generating the data.  It is persisted in the GCDump file
    /// and can be used by the viewer to customize the view.  
    /// 
    /// TODO can't set the rest of the meta-data associated with the graph this way.  
    /// </summary>
    public static void WriteMemoryGraph(MemoryGraph graph, string outputFileName, string toolName = null)
    {
        var dumper = new GCHeapDump(graph);
        dumper.CreationTool = toolName;
        dumper.Write(outputFileName);
    }

    /// <summary>
    /// The 
    /// </summary>
    public MemoryGraph MemoryGraph { get { return m_graph; } internal set { m_graph = value; } }

    /// <summary>
    /// Information about COM objects that is not contained in the MemoryGraph.  
    /// </summary>
    public InteropInfo InteropInfo { get { return m_interop; } internal set { m_interop = value; } }
    /// <summary>
    /// TODO FIX NOW REMOVE DO NOT USE  Use MemoryGraph.Is64Bit instead.    
    /// Was this dump taken from a 64 bit process
    /// </summary>
    public bool Is64Bit { get { return MemoryGraph.Is64Bit; } }

    // sampling support.  
    /// <summary>
    /// If we have sampled, sampleCount * ThisMultiplier = originalCount.   If sampling not done then == 1
    /// </summary>
    public float AverageCountMultiplier { get; internal set; }
    /// <summary>
    /// If we have sampled sampledSize * thisMultiplier = originalSize.  If sampling not done then == 1
    /// </summary>
    public float AverageSizeMultiplier { get; internal set; }
    /// <summary>
    /// This can be null.  If non-null it indicates that only a sample of the GC graph was persisted in 
    /// the MemoryGraph filed.  To get an approximation of the original heap, each type's count should be 
    /// scaled CountMultipliersByType[T] to get the unsampled count of the original heap.
    /// 
    /// We can't use a uniform number for all types because we want to see all large objects, and we 
    /// want to include paths to root for all objects, which means we can only approximate a uniform scaling.  
    /// </summary>
    public float[] CountMultipliersByType { get; internal set; }

    public DotNetHeapInfo DotNetHeapInfo { get; internal set; }
    public JSHeapInfo JSHeapInfo { get; internal set; }

    /// <summary>
    /// This is the log file that was generated at the time of collection 
    /// </summary>
    public string CollectionLog { get; internal set; }
    public DateTime TimeCollected { get; internal set; }
    public string MachineName { get; internal set; }
    public string ProcessName { get; internal set; }
    public int ProcessID { get; internal set; }
    public long TotalProcessCommit { get; internal set; }
    public long TotalProcessWorkingSet { get; internal set; }

    /// <summary>
    /// Returns a string that represents the tool that created this GCDump file.  May be null if not known/supported.  
    /// </summary>
    public string CreationTool { get; set; }

    public struct ProcessInfo
    {
        public int ID;
        public bool UsesDotNet;
        public bool UsesJavaScript;
    }
    /// <summary>
    /// returns a list of ProcessInfos that indicate which processes
    /// have use a runtime .NET or JavaScript that we can potentially dump
    /// 
    /// Note that for 64 bit systems this will ONLY return processes that
    /// have the same bitness as the current process (for PerfView it is 32 bit)
    /// </summary>
    public static Dictionary<int, ProcessInfo> GetProcessesWithGCHeaps()
    {
        var ret = new Dictionary<int, ProcessInfo>();

        // Do the 64 bit processes first, then do us   
        if (EnvironmentUtilities.Is64BitOperatingSystem && !EnvironmentUtilities.Is64BitProcess)
        {
            GetProcessesWithGCHeapsFromHeapDump(ret);
        }

        var info = new ProcessInfo();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process == null)
                {
                    continue;
                }

                info.ID = process.Id;
                if (info.ID == 0 || info.ID == 4)       // these are special and cause failures otherwise 
                {
                    continue;
                }

                info.UsesDotNet = false;
                info.UsesJavaScript = false;
                foreach (ProcessModule module in process.Modules)
                {
                    if (module == null)
                    {
                        continue;
                    }

                    var fileName = module.FileName;
                    if (fileName.EndsWith("clr.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesDotNet = true;
                    }
                    else if (fileName.EndsWith("coreclr.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesDotNet = true;
                    }
                    else if (fileName.EndsWith("mscorwks.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesDotNet = true;
                    }
                    else if (0 <= fileName.IndexOf(@"\mrt", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesDotNet = true;
                    }
                    else if (fileName.EndsWith("jscript9.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesJavaScript = true;
                    }
                    else if (fileName.EndsWith("chakra.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        info.UsesJavaScript = true;
                    }
                }
            }
            catch (Exception)
            {
            }
            if (info.UsesJavaScript || info.UsesDotNet)
            {
                // Merge with previous values.  
                ProcessInfo prev;
                if (ret.TryGetValue(info.ID, out prev))
                {
                    info.UsesDotNet |= prev.UsesDotNet;
                    info.UsesJavaScript |= prev.UsesJavaScript;
                }
                ret[info.ID] = info;
            }
        }
        return ret;
    }

    #region private

    /// <summary>
    /// Writes the data to 'outputFileName'   
    /// </summary>
    private void Write(string outputFileName)
    {
        Debug.Assert(MemoryGraph != null);
        var serializer = new Serializer(outputFileName, this);
        serializer.Close();
    }

    // Creation APIs
    internal GCHeapDump(MemoryGraph graph)
    {
        m_graph = graph;
        AverageCountMultiplier = 1;
        AverageSizeMultiplier = 1;
    }

    // For serialization
    private GCHeapDump() { }

    private GCHeapDump(Deserializer deserializer)
    {
        deserializer.RegisterFactory(typeof(MemoryGraph), delegate () { return new MemoryGraph(1); });
        deserializer.RegisterFactory(typeof(Graphs.Module), delegate () { return new Graphs.Module(0); });
        deserializer.RegisterFactory(typeof(InteropInfo), delegate () { return new InteropInfo(); });
        deserializer.RegisterFactory(typeof(GCHeapDump), delegate () { return this; });
        deserializer.RegisterFactory(typeof(GCHeapDumpSegment), delegate () { return new GCHeapDumpSegment(); });
        deserializer.RegisterFactory(typeof(JSHeapInfo), delegate () { return new JSHeapInfo(); });
        deserializer.RegisterFactory(typeof(DotNetHeapInfo), delegate () { return new DotNetHeapInfo(); });

        try
        {
            var entryObj = (GCHeapDump)deserializer.GetEntryObject();
            Debug.Assert(entryObj == this);
        }
        catch (Exception e)
        {
            throw new ApplicationException("Error opening file " + deserializer.Name + " Message: " + e.Message);
        }
    }

    private static void GetProcessesWithGCHeapsFromHeapDump(Dictionary<int, ProcessInfo> ret)
    {
#if PERFVIEW
        // TODO FIX NOW, need to work for PerfView64
        var heapDumpExe = Path.Combine(Utilities.SupportFiles.SupportFileDir, @"amd64\HeapDump.exe");
        var cmd = Utilities.Command.Run(heapDumpExe + " /GetProcessesWithGCHeaps");
        var info = new ProcessInfo();

        int idx = 0;
        var output = cmd.Output;
        for (; ; )
        {
            var newLineIdx = output.IndexOf('\n', idx);
            if (newLineIdx < 0)
                break;
            if (idx + 5 <= newLineIdx && output[idx] != '#')
            {
                info.UsesDotNet = (output[idx] == 'N');
                info.UsesJavaScript = (output[idx + 1] == 'J');
                var idStr = output.Substring(idx + 3, newLineIdx - idx - 4);
                if (int.TryParse(idStr, out info.ID))
                    ret[info.ID] = info;
            }
            idx = newLineIdx + 1;
        }
#endif
    }

    void IFastSerializable.ToStream(Serializer serializer)
    {
        serializer.Write(m_graph);
        serializer.Write(m_graph.Is64Bit);  // This is redundant but graph did not used to hold this value 
        // we write the bit here to preserve compatibility. 
        serializer.Write(AverageCountMultiplier);
        serializer.Write(AverageSizeMultiplier);

        serializer.Write(JSHeapInfo);
        serializer.Write(DotNetHeapInfo);

        serializer.Write(CollectionLog);
        serializer.Write(TimeCollected.Ticks);
        serializer.Write(MachineName);
        serializer.Write(ProcessName);
        serializer.Write(ProcessID);
        serializer.Write(TotalProcessCommit);
        serializer.Write(TotalProcessWorkingSet);

        if (CountMultipliersByType == null)
        {
            serializer.Write(0);
        }
        else
        {
            serializer.Write(CountMultipliersByType.Length);
            for (int i = 0; i < CountMultipliersByType.Length; i++)
            {
                serializer.Write(CountMultipliersByType[i]);
            }
        }

        // All fields after version 8 should go here and should be in
        // the version order (thus always add at the end).  Also use the 
        // WriteTagged variation to write. 
        serializer.WriteTagged(m_interop);
        serializer.WriteTagged(CreationTool);
    }

    void IFastSerializable.FromStream(Deserializer deserializer)
    {
        // This is the old crufy way of reading things in.  We can abandon this eventually 
        if (deserializer.VersionBeingRead < 8)
        {
            PreVersion8FromStream(deserializer);
            return;
        }
        if (deserializer.VersionBeingRead == 8)
        {
            throw new SerializationException("Unsupported version GCDump version: 8");
        }

        deserializer.ReadObject(out m_graph);
        deserializer.ReadBool();                    // Used to be Is64Bit but that is now on m_graph and we want to keep compatibility. 

        AverageCountMultiplier = deserializer.ReadSingle();
        AverageSizeMultiplier = deserializer.ReadSingle();

        JSHeapInfo = (JSHeapInfo)deserializer.ReadObject();
        DotNetHeapInfo = (DotNetHeapInfo)deserializer.ReadObject();

        CollectionLog = deserializer.ReadString();
        TimeCollected = new DateTime(deserializer.ReadInt64());
        MachineName = deserializer.ReadString();
        ProcessName = deserializer.ReadString();
        ProcessID = deserializer.ReadInt32();
        TotalProcessCommit = deserializer.ReadInt64();
        TotalProcessWorkingSet = deserializer.ReadInt64();

        int count;
        deserializer.ReadInt32(out count);
        if (count != 0)
        {
            var a = new float[count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = deserializer.ReadSingle();
            }

            CountMultipliersByType = a;
        }

        // Things after version 8 go here. Always add the the end, and it should always work
        // and use the tagged variation.  
        deserializer.TryReadTaggedObject<InteropInfo>(ref m_interop);
        string creationTool = null;
        deserializer.TryReadTaggedString(ref creationTool);
        CreationTool = creationTool;
    }

    /// <summary>
    /// Deals with legacy formats.  We should be able to get rid of eventually.  
    /// </summary>
    private void PreVersion8FromStream(Deserializer deserializer)
    {
        DotNetHeapInfo = new DotNetHeapInfo();

        deserializer.ReadObject(out m_graph);
        DotNetHeapInfo.SizeOfAllSegments = deserializer.ReadInt64();
        deserializer.ReadInt64(); // Size of dumped objects 
        deserializer.ReadInt64(); // Number of dumped objects 
        deserializer.ReadBool();  // All objects dumped

        if (deserializer.VersionBeingRead >= 5)
        {
            CollectionLog = deserializer.ReadString();
            TimeCollected = new DateTime(deserializer.ReadInt64());
            MachineName = deserializer.ReadString();
            ProcessName = deserializer.ReadString();
            ProcessID = deserializer.ReadInt32();
            TotalProcessCommit = deserializer.ReadInt64();
            TotalProcessWorkingSet = deserializer.ReadInt64();

            if (deserializer.VersionBeingRead >= 6)
            {
                // Skip the segments
                var count = deserializer.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    deserializer.ReadObject();
                }

                if (deserializer.VersionBeingRead >= 7)
                {
                    deserializer.ReadBool();    // Is64bit
                }
            }
        }
    }

    int IFastSerializableVersion.Version
    {
        // As long as we are on a tagged plan, we don't really have to increment this because
        // the tagged values we put in the stream do this for us, but it does not hurt and
        // acts as good documentation so we do increment it when we change things.   
        get { return 10; }
    }

    int IFastSerializableVersion.MinimumVersionCanRead
    {
        // We support back to version 4
        get { return 4; }
    }

    int IFastSerializableVersion.MinimumReaderVersion
    {
        // Since version 8 we are on a Tagged plan
        get { return 8; }
    }

    private MemoryGraph m_graph;
    private InteropInfo m_interop;
    #endregion
}

public class JSHeapInfo : IFastSerializable
{
    #region private
    void IFastSerializable.ToStream(Serializer serializer)
    {
    }
    void IFastSerializable.FromStream(Deserializer deserializer)
    {
    }
    #endregion
}

public class InteropInfo : IFastSerializable
{
    public class RCWInfo
    {
        internal NodeIndex node;
        internal int refCount;
        internal Address addrIUnknown;
        internal Address addrJupiter;
        internal Address addrVTable;
        internal int firstComInf;
        internal int countComInf;
    }

    public class CCWInfo
    {
        internal NodeIndex node;
        internal int refCount;
        internal Address addrIUnknown;
        internal Address addrHandle;
        internal int firstComInf;
        internal int countComInf;
    }

    public class ComInterfaceInfo
    {
        internal bool fRCW;
        internal int owner;
        internal NodeTypeIndex typeID;
        internal Address addrInterface;
        internal Address addrFirstVTable;
        internal Address addrFirstFunc;
    }


    public class InteropModuleInfo
    {
        public Address baseAddress;
        public uint fileSize;
        public uint timeStamp;
        public string fileName;

        public int loadOrder;   // unused when serializing
        private string _moduleName;     // unused when serializing

        public string moduleName
        {
            get
            {
                if (_moduleName == null)
                {
                    int pos = fileName.LastIndexOf('\\');

                    if (pos < 0)
                    {
                        pos = fileName.LastIndexOf(':');
                    }

                    if (pos > 0)
                    {
                        _moduleName = fileName.Substring(pos + 1);
                    }
                    else
                    {
                        _moduleName = fileName;
                    }
                }

                return _moduleName;
            }
        }

        public static int CompareBase(InteropModuleInfo one, InteropModuleInfo two)
        {
            return (int)(one.baseAddress - two.baseAddress);
        }
    }

    internal int m_countRCWs;
    internal int m_countCCWs;
    internal int m_countInterfaces;
    internal int m_countRCWInterfaces; // only used in the deserializing case.
    internal int m_countModules;

    internal List<RCWInfo> m_listRCWInfo;
    internal List<CCWInfo> m_listCCWInfo;
    internal List<ComInterfaceInfo> m_listComInterfaceInfo;
    internal List<InteropModuleInfo> m_listModules;

    public InteropInfo(bool fInitLater = false)
    {
        if (!fInitLater)
        {
            m_listRCWInfo = new List<RCWInfo>();
            m_listCCWInfo = new List<CCWInfo>();
            m_listComInterfaceInfo = new List<ComInterfaceInfo>();
            m_listModules = new List<InteropModuleInfo>();
        }
    }

    public int currentRCWCount { get { return m_listRCWInfo.Count; } }
    public int currentCCWCount { get { return m_listCCWInfo.Count; } }
    public int currentInterfaceCount { get { return m_listComInterfaceInfo.Count; } }
    public int currentModuleCount { get { return m_listModules.Count; } }

    public void AddRCW(RCWInfo rcwInfo)
    {
        m_listRCWInfo.Add(rcwInfo);
    }

    public void AddCCW(CCWInfo ccwInfo)
    {
        m_listCCWInfo.Add(ccwInfo);
    }

    public void AddComInterface(ComInterfaceInfo interfaceInfo)
    {
        m_listComInterfaceInfo.Add(interfaceInfo);
    }

    public void AddModule(InteropModuleInfo moduleInfo)
    {
        m_listModules.Add(moduleInfo);
    }

    public bool InteropInfoExists()
    {
        return ((currentRCWCount != 0) || (currentCCWCount != 0));
    }

    // The format we are writing out is:
    // total # of RCWs/CCWs. If this is 0, it means there's no interop info.
    // # of RCWs
    // # of CCWs
    // # of interfaces
    // # of modules
    // RCWs
    // CCWs
    // Interfaces
    // Modules
    void IFastSerializable.ToStream(Serializer serializer)
    {
        int countRCWCCW = m_listRCWInfo.Count + m_listCCWInfo.Count;

        serializer.Write(countRCWCCW);
        if (countRCWCCW == 0)
        {
            return;
        }

        serializer.Write(m_listRCWInfo.Count);
        serializer.Write(m_listCCWInfo.Count);
        serializer.Write(m_listComInterfaceInfo.Count);
        serializer.Write(m_listModules.Count);

        for (int i = 0; i < m_listRCWInfo.Count; i++)
        {
            serializer.Write((int)m_listRCWInfo[i].node);
            serializer.Write(m_listRCWInfo[i].refCount);
            serializer.Write((long)m_listRCWInfo[i].addrIUnknown);
            serializer.Write((long)m_listRCWInfo[i].addrJupiter);
            serializer.Write((long)m_listRCWInfo[i].addrVTable);
            serializer.Write(m_listRCWInfo[i].firstComInf);
            serializer.Write(m_listRCWInfo[i].countComInf);
        }

        for (int i = 0; i < m_listCCWInfo.Count; i++)
        {
            serializer.Write((int)m_listCCWInfo[i].node);
            serializer.Write(m_listCCWInfo[i].refCount);
            serializer.Write((long)m_listCCWInfo[i].addrIUnknown);
            serializer.Write((long)m_listCCWInfo[i].addrHandle);
            serializer.Write(m_listCCWInfo[i].firstComInf);
            serializer.Write(m_listCCWInfo[i].countComInf);
        }

        for (int i = 0; i < m_listComInterfaceInfo.Count; i++)
        {
            serializer.Write(m_listComInterfaceInfo[i].fRCW ? (byte)1 : (byte)0);
            serializer.Write(m_listComInterfaceInfo[i].owner);
            serializer.Write((int)m_listComInterfaceInfo[i].typeID);
            serializer.Write((long)m_listComInterfaceInfo[i].addrInterface);
            serializer.Write((long)m_listComInterfaceInfo[i].addrFirstVTable);
            serializer.Write((long)m_listComInterfaceInfo[i].addrFirstFunc);
        }

        for (int i = 0; i < m_listModules.Count; i++)
        {
            serializer.Write((long)m_listModules[i].baseAddress);
            serializer.Write((int)m_listModules[i].fileSize);
            serializer.Write((int)m_listModules[i].timeStamp);
            serializer.Write(m_listModules[i].fileName);
        }
    }

    void IFastSerializable.FromStream(Deserializer deserializer)
    {
        int countRCWCCW = deserializer.ReadInt32();
        if (countRCWCCW == 0)
        {
            return;
        }

        m_countRCWs = deserializer.ReadInt32();
        m_countCCWs = deserializer.ReadInt32();
        m_countInterfaces = deserializer.ReadInt32();
        m_countModules = deserializer.ReadInt32();

        m_listRCWInfo = new List<RCWInfo>(m_countRCWs);
        m_listCCWInfo = new List<CCWInfo>(m_countCCWs);
        m_listComInterfaceInfo = new List<ComInterfaceInfo>(m_countInterfaces);
        m_listModules = new List<InteropModuleInfo>(m_countModules);

        m_countRCWInterfaces = 0;

        for (int i = 0; i < m_countRCWs; i++)
        {
            RCWInfo infoRCW = new RCWInfo();
            infoRCW.node = (NodeIndex)deserializer.ReadInt32();
            infoRCW.refCount = deserializer.ReadInt32();
            infoRCW.addrIUnknown = (Address)deserializer.ReadInt64();
            infoRCW.addrJupiter = (Address)deserializer.ReadInt64();
            infoRCW.addrVTable = (Address)deserializer.ReadInt64();
            infoRCW.firstComInf = deserializer.ReadInt32();
            infoRCW.countComInf = deserializer.ReadInt32();
            m_listRCWInfo.Add(infoRCW);
            m_countRCWInterfaces += infoRCW.countComInf;
        }

        for (int i = 0; i < m_countCCWs; i++)
        {
            CCWInfo infoCCW = new CCWInfo();
            infoCCW.node = (NodeIndex)deserializer.ReadInt32();
            infoCCW.refCount = deserializer.ReadInt32();
            infoCCW.addrIUnknown = (Address)deserializer.ReadInt64();
            infoCCW.addrHandle = (Address)deserializer.ReadInt64();
            infoCCW.firstComInf = deserializer.ReadInt32();
            infoCCW.countComInf = deserializer.ReadInt32();
            m_listCCWInfo.Add(infoCCW);
        }

        for (int i = 0; i < m_countInterfaces; i++)
        {
            ComInterfaceInfo infoInterface = new ComInterfaceInfo();
            infoInterface.fRCW = ((deserializer.ReadByte() == 1) ? true : false);
            infoInterface.owner = deserializer.ReadInt32();
            infoInterface.typeID = (NodeTypeIndex)deserializer.ReadInt32();
            infoInterface.addrInterface = (Address)deserializer.ReadInt64();
            infoInterface.addrFirstVTable = (Address)deserializer.ReadInt64();
            infoInterface.addrFirstFunc = (Address)deserializer.ReadInt64();
            m_listComInterfaceInfo.Add(infoInterface);
        }

        for (int i = 0; i < m_countModules; i++)
        {
            InteropModuleInfo infoModule = new InteropModuleInfo();
            infoModule.baseAddress = (Address)deserializer.ReadInt64();
            infoModule.fileSize = (uint)deserializer.ReadInt32();
            infoModule.timeStamp = (uint)deserializer.ReadInt32();
            deserializer.ReadString(out infoModule.fileName);
            infoModule.loadOrder = i;
            m_listModules.Add(infoModule);
        }

        m_listModules.Sort(InteropModuleInfo.CompareBase);
    }
}

/// <summary>
/// Reads the format as an XML file.   It it is a very simple format  Here is an example. 
/// <graph>
///   <rootIndex>3</rootIndex>
///   <nodeTypes>
///     <nodeType Index="1" Size="10" Name="Type1" Module="MyModule" />
///     <nodeType Index="2" Size="1" Name="Type2" />
///     <nodeType Index="3" Size="20" Name="Type3" />
///     <nodeType Index="4" Size="10" Name="Type4" Module="MyModule" />
///   </nodeTypes>
///   <nodes>
///     <node Index="1" Size="100" TypeIndex="1" >2</node>
///     <node Index="2" TypeIndex="1" >3</node>
///     <node Index="3" TypeIndex="1" >1 4</node>
///     <node Index="4" TypeIndex="4" >2</node>
///   </nodes>
/// </graph>
/// 
/// </summary>
internal class XmlGcHeapDump
{
    public static GCHeapDump ReadGCHeapDumpFromXml(string fileName)
    {
        XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
        using (XmlReader reader = XmlReader.Create(fileName, settings))
        {
            reader.ReadToDescendant("GCHeapDump");
            return ReadGCHeapDumpFromXml(reader);
        }
    }

    public static GCHeapDump ReadGCHeapDumpFromXml(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new InvalidOperationException("Must advance to GCHeapDump element (e.g. call ReadToDescendant)");
        }

        var elementName = reader.Name;
        var inputDepth = reader.Depth;
        reader.Read();      // Advance to children 

        GCHeapDump ret = new GCHeapDump((MemoryGraph)null);
        while (inputDepth < reader.Depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "MemoryGraph":
                        ret.MemoryGraph = ReadMemoryGraphFromXml(reader);
                        break;
                    case "CollectionLog":
                        ret.CollectionLog = reader.ReadElementContentAsString();
                        break;
                    case "TimeCollected":
                        ret.TimeCollected = DateTime.Parse(reader.ReadElementContentAsString());
                        break;
                    case "MachineName":
                        ret.MachineName = reader.ReadElementContentAsString();
                        break;
                    case "ProcessName":
                        ret.ProcessName = reader.ReadElementContentAsString();
                        break;
                    case "ProcessID":
                        ret.ProcessID = reader.ReadElementContentAsInt();
                        break;
                    case "CountMultipliersByType":
                        var multipliers = new List<float>();
                        ReadCountMultipliersByTypeFromXml(reader, multipliers);
                        ret.CountMultipliersByType = multipliers.ToArray();
                        break;
                    case "TotalProcessCommit":
                        ret.TotalProcessCommit = reader.ReadElementContentAsLong();
                        break;
                    case "TotalProcessWorkingSet":
                        ret.TotalProcessWorkingSet = reader.ReadElementContentAsLong();
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                        reader.Skip();
                        break;
                }
            }
            else if (!reader.Read())
            {
                break;
            }
        }
        if (ret.MemoryGraph == null)
        {
            throw new ApplicationException(elementName + " does not have MemoryGraph field.");
        }

        return ret;
    }

    public static MemoryGraph ReadMemoryGraphFromXml(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new InvalidOperationException("Must advance to MemoryGraph element (e.g. call ReadToDescendant)");
        }

        var expectedSize = 1000;
        var nodeCount = reader.GetAttribute("NodeCount");
        if (nodeCount != null)
        {
            expectedSize = int.Parse(nodeCount) + 1;        // 1 for undefined 
        }

        MemoryGraph graph = new MemoryGraph(10);
        Debug.Assert((int)graph.NodeTypeIndexLimit == 1);
        var firstNode = graph.CreateNode();                             // Use one up
        Debug.Assert(firstNode == 0);
        Debug.Assert((int)graph.NodeIndexLimit == 1);

        var inputDepth = reader.Depth;
        reader.Read();      // Advance to children 
        while (inputDepth < reader.Depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "NodeTypes":
                        ReadNodeTypesFromXml(reader, graph);
                        break;
                    case "Nodes":
                        ReadNodesFromXml(reader, graph);
                        break;
                    case "RootIndex":
                        graph.RootIndex = (NodeIndex)reader.ReadElementContentAsInt();
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                        reader.Skip();
                        break;
                }
            }
            else if (!reader.Read())
            {
                break;
            }
        }

        graph.AllowReading();
        return graph;
    }

    internal static void WriteGCDumpToXml(GCHeapDump gcDump, StreamWriter writer)
    {
        writer.WriteLine("<GCHeapDump>");

        writer.WriteLine("<TimeCollected>{0}</TimeCollected>", gcDump.TimeCollected);
        if (!string.IsNullOrWhiteSpace(gcDump.CollectionLog))
        {
            writer.WriteLine("<CollectionLog>{0}</CollectionLog>", XmlUtilities.XmlEscape(gcDump.CollectionLog));
        }

        if (!string.IsNullOrWhiteSpace(gcDump.MachineName))
        {
            writer.WriteLine("<MachineName>{0}</MachineName>", gcDump.MachineName);
        }

        if (!string.IsNullOrWhiteSpace(gcDump.ProcessName))
        {
            writer.WriteLine("<ProcessName>{0}</ProcessName>", XmlUtilities.XmlEscape(gcDump.ProcessName));
        }

        if (gcDump.ProcessID != 0)
        {
            writer.WriteLine("<ProcessName>{0}</ProcessName>", gcDump.ProcessID);
        }

        if (gcDump.TotalProcessCommit != 0)
        {
            writer.WriteLine("<TotalProcessCommit>{0}</TotalProcessCommit>", gcDump.TotalProcessCommit);
        }

        if (gcDump.TotalProcessWorkingSet != 0)
        {
            writer.WriteLine("<TotalProcessWorkingSet>{0}</TotalProcessWorkingSet>", gcDump.TotalProcessWorkingSet);
        }

        if (gcDump.CountMultipliersByType != null)
        {
            NodeType typeStorage = gcDump.MemoryGraph.AllocTypeNodeStorage();
            writer.WriteLine("<CountMultipliersByType>");
            for (int i = 0; i < gcDump.CountMultipliersByType.Length; i++)
            {
                writer.WriteLine("<CountMultipliers TypeIndex=\"{0}\" TypeName=\"{1}\" Value=\"{2:f4}\"/>", i,
                    XmlUtilities.XmlEscape(gcDump.MemoryGraph.GetType((NodeTypeIndex)i, typeStorage).Name),
                    gcDump.CountMultipliersByType[i]);
            }

            writer.WriteLine("</CountMultipliersByType>");
        }

        // TODO this is not complete.  See the ToStream for more.    Does not include interop etc. 

        // Write the memory graph, which is the main event.  
        gcDump.MemoryGraph.WriteXml(writer);
        writer.WriteLine("</GCHeapDump>");
    }

    #region private
    private static void ReadCountMultipliersByTypeFromXml(XmlReader reader, List<float> countMultipliers)
    {
        Debug.Assert(reader.NodeType == XmlNodeType.Element);
        var inputDepth = reader.Depth;
        reader.Read();      // Advance to children 
        while (inputDepth < reader.Depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "CountMultiplier":
                        countMultipliers.Add(FetchFloat(reader, "Value", 1));
                        reader.Skip();
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                        reader.Skip();
                        break;
                }
            }
            else if (!reader.Read())
            {
                break;
            }
        }
    }

    /// <summary>
    /// Reads the NodeTypes element
    /// </summary>
    private static void ReadNodeTypesFromXml(XmlReader reader, MemoryGraph graph)
    {
        Debug.Assert(reader.NodeType == XmlNodeType.Element);
        var inputDepth = reader.Depth;
        reader.Read();      // Advance to children 
        while (inputDepth < reader.Depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "NodeType":
                        {
                            NodeTypeIndex readTypeIndex = (NodeTypeIndex)FetchInt(reader, "Index", -1);
                            int size = FetchInt(reader, "Size");
                            string typeName = reader.GetAttribute("Name");
                            string moduleName = reader.GetAttribute("Module");

                            if (typeName == null)
                            {
                                throw new ApplicationException("NodeType element does not have a Name attribute");
                            }

                            if (readTypeIndex == NodeTypeIndex.Invalid)
                            {
                                throw new ApplicationException("NodeType element does not have a Index attribute.");
                            }

                            if (readTypeIndex != 0 || typeName != "UNDEFINED")
                            {
                                NodeTypeIndex typeIndex = graph.CreateType(typeName, moduleName, size);
                                if (readTypeIndex != typeIndex)
                                {
                                    throw new ApplicationException("NodeType Indexes do not start at 1 and increase consecutively.");
                                }
                            }
                            reader.Skip();
                        }
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                        reader.Skip();
                        break;
                }
            }
            else if (!reader.Read())
            {
                break;
            }
        }
    }
    /// <summary>
    /// Reads the Nodes Element
    /// </summary>
    private static void ReadNodesFromXml(XmlReader reader, MemoryGraph graph)
    {
        Debug.Assert(reader.NodeType == XmlNodeType.Element);
        var inputDepth = reader.Depth;
        reader.Read();      // Advance to children 

        var children = new GrowableArray<NodeIndex>(1000);
        var typeStorage = graph.AllocTypeNodeStorage();
        while (inputDepth < reader.Depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "Node":
                        {
                            NodeIndex readNodeIndex = (NodeIndex)FetchInt(reader, "Index", -1);
                            NodeTypeIndex typeIndex = (NodeTypeIndex)FetchInt(reader, "TypeIndex", -1);
                            int size = FetchInt(reader, "Size");

                            if (readNodeIndex == NodeIndex.Invalid)
                            {
                                throw new ApplicationException("Node element does not have a Index attribute.");
                            }

                            if (typeIndex == NodeTypeIndex.Invalid)
                            {
                                throw new ApplicationException("Node element does not have a TypeIndex attribute.");
                            }

                            // TODO FIX NOW very inefficient.   Use ReadValueChunk and FastStream to make more efficient.  
                            children.Clear();
                            var body = reader.ReadElementContentAsString();
                            foreach (var num in Regex.Split(body, @"\s+"))
                            {
                                if (num.Length > 0)
                                {
                                    children.Add((NodeIndex)int.Parse(num));
                                }
                            }

                            if (size == 0)
                            {
                                size = graph.GetType(typeIndex, typeStorage).Size;
                            }

                            // TODO should probably just reserve node index 0 to be an undefined object?
                            NodeIndex nodeIndex = 0;
                            if (readNodeIndex != 0)
                            {
                                nodeIndex = graph.CreateNode();
                            }

                            if (readNodeIndex != nodeIndex)
                            {
                                throw new ApplicationException("Node Indexes do not start at 0 or 1 and increase consecutively.");
                            }

                            graph.SetNode(nodeIndex, typeIndex, size, children);
                        }
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                        reader.Skip();
                        break;
                }
            }
            else if (!reader.Read())
            {
                break;
            }
        }
    }
    /// <summary>
    /// Reads an given attribute as a integer
    /// </summary>
    private static int FetchInt(XmlReader reader, string attributeName, int defaultValue = 0)
    {
        int ret = defaultValue;
        var attrValue = reader.GetAttribute(attributeName);
        if (attrValue != null)
        {
            int.TryParse(attrValue, out ret);
        }

        return ret;
    }

    private static float FetchFloat(XmlReader reader, string attributeName, float defaultValue = 0)
    {
        float ret = defaultValue;
        var attrValue = reader.GetAttribute(attributeName);
        if (attrValue != null)
        {
            float.TryParse(attrValue, out ret);
        }

        return ret;
    }

    #endregion


}

