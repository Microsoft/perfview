using Graphs;
using Microsoft.Diagnostics.Symbols;
using System.Collections.Generic;

internal static class ImageFileMemoryGraph
{
    public static MemoryGraph Create(string dllPath, SymbolReader symbolReader)
    {
        var ret = new MemoryGraph(1000);

        string pdbPath = symbolReader.FindSymbolFilePathForModule(dllPath);
        symbolReader.Log.WriteLine("Got PDB path {0}", pdbPath);

        NativeSymbolModule module = symbolReader.OpenNativeSymbolFile(pdbPath);
        List<Symbol> symbols = new List<Symbol>();
        AddAllChildren(symbols, module.GlobalSymbol);

        symbols.Sort();

        /****** Make a graph out of the symbols ******/
        // Put all nodes under this root.  
        var rootChildren = new HashSet<NodeIndex>();

        // Create a node for each symbol 
        uint lastRVA = 0;
        string lastName = "Header";
        var empty = new HashSet<NodeIndex>();

        foreach (var symbol in symbols)
        {
            var symRVA = symbol.RVA;
            int lastSize = (int)symRVA - (int)lastRVA;

            NodeTypeIndex typeIdx = ret.CreateType(lastName, null, lastSize);
            NodeIndex nodeIdx = ret.CreateNode();
            ret.SetNode(nodeIdx, typeIdx, lastSize, empty);
            rootChildren.Add(nodeIdx);

            lastName = symbol.Name;
            lastRVA = symRVA;
        }
        // TODO FIX NOW dropping the last symbol.   

        // Create the root node.  
        NodeIndex rootIdx = ret.CreateNode();
        NodeTypeIndex rootTypeIdx = ret.CreateType("METHODS");
        ret.SetNode(rootIdx, rootTypeIdx, 0, rootChildren);
        ret.RootIndex = rootIdx;

        ret.AllowReading();

        return ret;
    }

    #region private
    private static void AddAllChildren(List<Symbol> symbols, Symbol symbol)
    {
        var children = symbol.GetChildren();
        if (children != null)
        {
            foreach (Symbol child in children)
            {
                symbols.Add(child);
                AddAllChildren(symbols, child);
            }
        }
    }

    #endregion
}
