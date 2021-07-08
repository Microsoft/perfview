using Microsoft.Diagnostics.Tracing.Stacks;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Diagnostics.Tracing.StackSources
{
    /// <summary>
    /// A DebuggerStackSource knows how to read a text file from the cdb (windbg) kc command output (clean stacks)
    /// </summary>
    internal class DebuggerStackSource : InternStackSource
    {
        public DebuggerStackSource(string fileName)
        {
            using (var file = File.OpenText(fileName))
            {
                Read(file);
            }
        }

        public DebuggerStackSource(TextReader reader)
        {
            Read(reader);
        }


        #region private
        private struct DebuggerCallStackFrame
        {
            public StackSourceFrameIndex frame;
        }

        private void Read(TextReader reader)
        {
            var framePattern = new Regex(@"\b(\w+?)\!(\S\(?[\S\s]*\)?)");
            var stackStart = new Regex(@"Call Site");

            // the call stack from the debugger kc command looksl like this 
            //Call Site
            //coreclr!JIT_MonEnterWorker_Portable
            //System_Windows_ni!MS.Internal.ManagedPeerTable.TryGetManagedPeer(IntPtr, Boolean, System.Object ByRef)
            //System_Windows_ni!MS.Internal.ManagedPeerTable.EnsureManagedPeer(IntPtr, Int32, System.Type, Boolean)
            //System_Windows_ni!MS.Internal.FrameworkCallbacks.CheckPeerType(IntPtr, System.String, Boolean)
            //System_Windows_ni!DomainBoundILStubClass.IL_STUB_ReversePInvoke(Int32, IntPtr, Int32)
            //coreclr!UM2MThunk_WrapperHelper
            //coreclr!UMThunkStubWorker
            //coreclr!UMThunkStub
            //agcore!CParser::StartObjectElement
            //agcore!CParser::Attribute
            //agcore!CParser::LoadXaml

            var stack = new GrowableArray<DebuggerCallStackFrame>();
            bool newCallStackFound = false;
            var sample = new StackSourceSample(this);
            double time = 0;
            for (; ; )
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                var match = framePattern.Match(line);
                if (match.Success && newCallStackFound)
                {
                    var module = match.Groups[1].Value;
                    var methodName = match.Groups[2].Value;

                    // trim the methodName if it has file name info (if the trace is collected with kv instead of kc)
                    int index = methodName.LastIndexOf(")+");
                    if (index != -1)
                    {
                        methodName = methodName.Substring(0, index + 1);
                    }


                    var moduleIndex = Interner.ModuleIntern(module);
                    var frameIndex = Interner.FrameIntern(methodName, moduleIndex);

                    DebuggerCallStackFrame frame = new DebuggerCallStackFrame();
                    frame.frame = frameIndex;
                    stack.Add(frame);

                }
                else
                {
                    var stackStartMatch = stackStart.Match(line);
                    if (stackStartMatch.Success)
                    {
                        // start a new sample.
                        // add the previous sample 
                        // clear the stack
                        if (stack.Count != 0)
                        {

                            StackSourceCallStackIndex parent = StackSourceCallStackIndex.Invalid;
                            for (int i = stack.Count - 1; i >= 0; --i)
                            {
                                parent = Interner.CallStackIntern(stack[i].frame, parent);
                            }

                            stack.Clear();

                            sample.StackIndex = parent;
                            sample.TimeRelativeMSec = time;
                            time++;
                            AddSample(sample);
                        }
                        newCallStackFound = true;

                    }
                }
            }
            Interner.DoneInterning();
        }
        #endregion

    }
}