//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Tracing.Extensions;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Utilities;

namespace Microsoft.Diagnostics.Tracing.Session
{
    /// <summary>
    /// A TraceEventSession represents a single ETW Tracing Session.   A session is and event sink that 
    /// can enable or disable event logging from event providers).    TraceEventSessions can log their
    /// events either to a file, or by issuing callbacks when events arrive (a so-called 'real time' 
    /// session).   
    /// <para>
    /// Session are MACHINE wide and unlike most OS resources the operating system does NOT reclaim 
    /// them when the process that created it dies.  By default TraceEventSession tries is best to
    /// do this reclamation, but it is possible that for 'orphan' session to accidentally survive
    /// if the process is ended abruptly (e.g. by the debugger or a user explicitly killing it).  It is 
    /// possible to turn off TraceEventSession automatic reclamation by setting the StopOnDispose 
    /// property to false (its default is true).  
    /// </para>
    /// <para> 
    /// Kernel events have additional restrictions.   In particular there is a special API (EnableKernelProvider).  
    /// Before Windows 8, there was a restriction that kernel events could only be enabled from a session 
    /// with a special name (see KernelTraceEventParser.KernelSessionName) and thus there could only be a single
    /// session that could log kernel events (and that session could not log non-kernel events).  These
    /// restrictions were dropped in windows 8. 
    /// </para>
    /// </summary>
    public sealed unsafe class TraceEventSession : IDisposable
    {
        /// <summary>
        /// Create a new logging session sending the output to a given file.  
        /// </summary>
        /// <param name="sessionName">
        /// The name of the session. Since session can exist beyond the lifetime of the process this name is
        /// used to refer to the session from other processes after it is created.   By default TraceEventSessions
        /// do their best to close down if the TraceEventSession dies (see StopOnDispose), however if StopOnDispose
        /// is set to false, the session can live on after process death, and you use the name to refer to it later.  
        /// </param>
        /// <param name="fileName">
        /// The output moduleFile (by convention .ETL) to put the event data.  If this is null, and CircularMB is set
        /// to something non-zero, then it will do an in-memory circular buffer.   You can get this buffer by 
        /// using the 'SetFileName()' method which dumps the data in the buffer.  
        /// </param>
        /// <param name="options">Additional flags that influence behavior.  Note that the 'Create' option is implied for file mode sessions. </param>
        public TraceEventSession(string sessionName, string fileName, TraceEventSessionOptions options = TraceEventSessionOptions.Create)
        {
            EnableProviderTimeoutMSec = 0;         // Currently by default it is async (TODO change to 10000? by default)
            m_BufferSizeMB = Math.Max(64, System.Environment.ProcessorCount * 2);       // The default size.  
            m_BufferQuantumKB = 64;
            m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
            m_FileName = fileName;               // filename = null means real time session
            m_SessionName = sessionName;
            m_Create = true;
            m_ResartIfExist = (options & TraceEventSessionOptions.NoRestartOnCreate) == 0;
            m_NoPerProcessBuffering = (options & TraceEventSessionOptions.NoPerProcessorBuffering) != 0;
            m_CpuSampleIntervalMSec = 1.0F;
            m_SessionId = -1;
            m_StopOnDispose = true;
            CaptureStateOnSetFileName = true;
        }
        /// <summary>
        /// Open a logging session.   By default (if options is not specified) a new 'real time' session is created if
        /// the session already existed it is closed and reopened (thus orphans are cleaned up on next use).  By default
        /// sessions are closed on Dispose, but if the destructor does not run it can produce 'orphan' session that will
        /// live beyond the lifetime of the process.   You can use the StopOnDispose property to force sessions to live
        /// beyond the TraceEventSession that created them and use the TraceEventSessionOptions.Attach option to reattach
        /// to these sessions.
        /// </summary>
        /// <param name="sessionName"> The name of the session to open.  Should be unique across the machine.</param>
        /// <param name="options"> Construction options.  TraceEventSessionOptions.Attach indicates a desire to attach
        /// to an existing session. </param>
        public TraceEventSession(string sessionName, TraceEventSessionOptions options = TraceEventSessionOptions.Create)
        {
            EnableProviderTimeoutMSec = 0;         // Currently by default it is async (TODO change to 10000? by default)
            m_SessionId = -1;
            m_BufferQuantumKB = 64;
            m_CpuSampleIntervalMSec = 1.0F;
            m_SessionName = sessionName;
            StopOnDispose = true;
            CaptureStateOnSetFileName = true;
            m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
            if ((options & TraceEventSessionOptions.Attach) != 0)
            {
                // Attaching to an existing session 

                // Get the filename
                var propertiesBuff = stackalloc byte[PropertiesSize];
                var properties = GetProperties(propertiesBuff);
                int hr = TraceEventNativeMethods.ControlTrace(0UL, sessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_QUERY);
                if (hr == TraceEventNativeMethods.ERROR_WMI_INSTANCE_NOT_FOUND)     // Instance name not found.  This means we did not start
                {
                    throw new FileNotFoundException("The session " + sessionName + " is not active.");  // Not really a file, but not bad. 
                }

                m_SessionId = (int)properties->Wnode.HistoricalContext;
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
                if (properties->LogFileNameOffset != 0)
                {
                    m_FileName = new string((char*)(((byte*)properties) + properties->LogFileNameOffset));
                    if (m_FileName.Length == 0)
                    {
                        m_FileName = null;
                    }
                }
                if (properties->BufferSize != 0)
                {
                    m_BufferQuantumKB = (int)properties->BufferSize;
                }

                m_BufferSizeMB = (int)(properties->MinimumBuffers * m_BufferQuantumKB) / 1024;
                if ((properties->LogFileMode & TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_CIRCULAR) != 0)
                {
                    m_CircularBufferMB = (int)properties->MaximumFileSize;
                }

                if ((properties->LogFileMode & TraceEventNativeMethods.EVENT_TRACE_BUFFERING_MODE) != 0)
                {
                    Debug.Assert(m_FileName == null);   // It should already not have a file name associated with it
                    m_FileName = null;
                    m_CircularBufferMB = m_BufferSizeMB;
                }
                if ((properties->LogFileMode & TraceEventNativeMethods.EVENT_TRACE_NO_PER_PROCESSOR_BUFFERING) != 0)
                {
                    m_NoPerProcessBuffering = true;
                }
            }
            else
            {
                // Creating a new session (however we defer it).  
                m_BufferSizeMB = Math.Max(64, System.Environment.ProcessorCount * 2);       // The default size.  
                m_FileName = null;               // filename = null means real time session
                m_Create = true;
                m_ResartIfExist = (options & TraceEventSessionOptions.NoRestartOnCreate) == 0;
                m_NoPerProcessBuffering = (options & TraceEventSessionOptions.NoPerProcessorBuffering) != 0;
            }
        }
        /// <summary>
        /// Looks for an existing active session named 'sessionName; and returns the TraceEventSession associated with it if it exists. 
        /// Returns null if the session does not exist.   You can use the GetActiveSessionNames() to get a list of names to pass to this method. 
        /// </summary>
        public static TraceEventSession GetActiveSession(string sessionName)
        {
            // TODO avoid the throw/catch when not present as it is inefficient.  
            TraceEventSession session = null;
            try
            {
                session = new TraceEventSession(sessionName, TraceEventSessionOptions.Attach);
            }
            catch (FileNotFoundException) { }
            return session;
        }

        /// <summary>
        /// Enable a NON-KERNEL provider (see also EnableKernelProvider) which has a given provider name.  
        /// This API first checks if a published provider exists by that name, otherwise it 
        /// assumes it is an EventSouce and determines the provider Guid by hashing the name according to a
        /// well known algorithm.  Thus it will never return a failure for a incorrect spelling of the name.  
        /// </summary>
        /// <param name="providerName">
        /// The name of the provider.  It must either be registered with the operating system (logman query providers returns it)
        /// or it must be an EventSource (see GetEventSourceGuidFromName)</param>
        /// <param name="providerLevel">The verbosity to turn on</param>
        /// <param name="matchAnyKeywords">A bitvector representing the areas to turn on. Only the
        /// low 32 bits are used by classic providers and passed as the 'flags' value.  Zero
        /// is a special value which is a provider defined default, which is usually 'everything'</param>
        /// <param name="options">Additional options for the provider (e.g. taking a stack trace), arguments ... </param>
        /// <returns>true if the session already existed and needed to be restarted.</returns>
        public bool EnableProvider(string providerName, TraceEventLevel providerLevel = TraceEventLevel.Verbose, ulong matchAnyKeywords = ulong.MaxValue, TraceEventProviderOptions options = null)
        {
            var providerGuid = TraceEventProviders.GetProviderGuidByName(providerName);
            if (providerGuid == Guid.Empty)
            {
                providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerName);
            }

            return EnableProvider(providerGuid, providerLevel, matchAnyKeywords, options);
        }
        /// <summary>
        /// Enable a NON-KERNEL provider (see also EnableKernelProvider) which has a given provider Guid.  
        /// </summary>
        /// <param name="providerGuid">
        /// The Guid that represents the event provider enable. </param>
        /// <param name="providerLevel">The verbosity to turn on</param>
        /// <param name="matchAnyKeywords">A bitvector representing the areas to turn on. Only the
        /// low 32 bits are used by classic providers and passed as the 'flags' value.  Zero
        /// is a special value which is a provider defined default, which is usually 'everything'</param>
        /// <param name="options">Additional options for the provider (e.g. taking a stack trace), arguments ... </param>
        /// <returns>true if the session already existed and needed to be restarted.</returns>
        public bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel = TraceEventLevel.Verbose, ulong matchAnyKeywords = ulong.MaxValue, TraceEventProviderOptions options = null)
        {
            lock (this)
            {
                if (options == null)
                {
                    options = new TraceEventProviderOptions();
                }

                byte[] valueData = null;
                int valueDataSize = 0;
                ControllerCommand valueDataType = ControllerCommand.Update;
                bool V4_5EventSource = false;

                if (options.Arguments != null)
                {
                    valueDataType = ControllerCommand.Update;
                    valueData = new byte[1024];
                    foreach (KeyValuePair<string, string> keyValue in options.Arguments)
                    {
                        if (keyValue.Key == "V4_5EventSource" && keyValue.Value == "true")
                        {
                            V4_5EventSource = true;
                        }

                        if (keyValue.Key == "Command")
                        {
                            if (keyValue.Value == "SendManifest")
                            {
                                valueDataType = ControllerCommand.SendManifest;
                            }
                            else
                            {
                                int val;
                                if (int.TryParse(keyValue.Value, out val))
                                {
                                    valueDataType = (ControllerCommand)val;
                                }
                            }
                        }
                        valueDataSize += Encoding.UTF8.GetBytes(keyValue.Key, 0, keyValue.Key.Length, valueData, valueDataSize);
                        valueData[valueDataSize++] = 0;
                        valueDataSize += Encoding.UTF8.GetBytes(keyValue.Value, 0, keyValue.Value.Length, valueData, valueDataSize);
                        valueData[valueDataSize++] = 0;
                    }
                }

                if (m_SessionName == KernelTraceEventParser.KernelSessionName)
                {
                    throw new NotSupportedException("Can only enable kernel events on a kernel session.");
                }

                EnsureStarted();

                // If we have provider data we add some predefined key-value pairs for infrastructure purposes. 
                ulong matchAllKeywords = 0;
                if (valueData != null)
                {
                    if (!V4_5EventSource)
                    {
                        // We add the EtwSessionName=NAME as the first key-value pairs,  This allows us to identify this
                        // data as coming from 'us' and garbage collect it if that session dies.  It is also likely to be
                        // useful to the provider.
                        var etwSessionName = "EtwSessionName";
                        var etwSessionNameKeyValueSize = etwSessionName.Length + 1 + Encoding.UTF8.GetByteCount(m_SessionName) + 1;

                        var etwSessionKeyword = "EtwSessionKeyword";
                        int sessionKeyword = FindFreeSessionKeyword(providerGuid);
                        matchAllKeywords = ((ulong)1) << sessionKeyword;

                        var etwSessionKeywordValue = sessionKeyword.ToString();
                        var etwSessionKeywordKeyValueSize = etwSessionKeyword.Length + 1 + Encoding.UTF8.GetByteCount(etwSessionKeywordValue) + 1;

                        // Set the registry key so providers get the information even if they are not active now
                        // We allocate a 4 byte header to allow us to easily version this in the future.  
                        var newProviderData = new byte[valueDataSize + etwSessionNameKeyValueSize + etwSessionKeywordKeyValueSize];
                        var curIdx = 0;
                        curIdx += Encoding.UTF8.GetBytes(etwSessionName, 0, etwSessionName.Length, newProviderData, curIdx);
                        newProviderData[curIdx++] = 0;       // Null terminate the string
                        curIdx += Encoding.UTF8.GetBytes(m_SessionName, 0, m_SessionName.Length, newProviderData, curIdx);
                        newProviderData[curIdx++] = 0;       // Null terminate the string
                        curIdx += Encoding.UTF8.GetBytes(etwSessionKeyword, 0, etwSessionKeyword.Length, newProviderData, curIdx);
                        newProviderData[curIdx++] = 0;       // Null terminate the string
                        curIdx += Encoding.UTF8.GetBytes(etwSessionKeywordValue, 0, etwSessionKeywordValue.Length, newProviderData, curIdx);
                        newProviderData[curIdx++] = 0;       // Null terminate the string
                        Debug.Assert(curIdx + valueDataSize == newProviderData.Length);
                        Array.Copy(valueData, 0, newProviderData, curIdx, valueDataSize);
                        valueData = newProviderData;
                        valueDataSize = newProviderData.Length;
                    }
                    else
                    {
                        // V4.5 data has a 4 bytes of 0s before the actual data.   
                        var newProviderData = new byte[valueDataSize + 4];
                        Array.Copy(valueData, 0, newProviderData, 4, valueDataSize);
                        valueData = newProviderData;
                        valueDataSize = newProviderData.Length;
                    }
                    // Working around an ETW limitation.   It turns out that filter data is transmitted only
                    // to providers that are actually alive at the time the controller enables the provider.  
                    // This makes filter data second class (keywords and levels ARE remembered and as providers
                    // become alive they are enabled with that information). Arguably this is a bug.  
                    // To work around this we remember the filter data in the registry and EventSources look
                    // for this data if we don't already have non-null filter data so that even providers that 
                    // have not yet started will get the data.  

                    if (valueDataType != ControllerCommand.SendManifest) // don't write anything to the registry for SendManifest commands
                    {
                        SetFilterDataForEtwSession(providerGuid.ToString(), valueData, V4_5EventSource);
                    }
                }

                const int MaxDesc = 7;  // This number needs to be bumped for to insure that all curDescrIdx never exceeds it below.  
                TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR* filterDescrPtr = stackalloc TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR[MaxDesc];
                int curDescrIdx = 0;
                fixed (byte* providerDataPtr = valueData)
                {
                    if (valueData != null)
                    {
                        // This one must be first so it works pre-8.1
                        filterDescrPtr[curDescrIdx].Ptr = providerDataPtr;
                        filterDescrPtr[curDescrIdx].Size = valueDataSize;
                        filterDescrPtr[curDescrIdx].Type = (int)valueDataType;
                        curDescrIdx++;
                    }

                    bool etwFilteringSupported = TraceEventProviderOptions.FilteringSupported;
                    if (etwFilteringSupported)
                    {
                        if (options.ProcessIDFilter != null && 0 < options.ProcessIDFilter.Count)
                        {
                            int* pids = stackalloc int[options.ProcessIDFilter.Count];
                            for (int i = 0; i < options.ProcessIDFilter.Count; i++)
                            {
                                pids[i] = options.ProcessIDFilter[i];
                            }

                            filterDescrPtr[curDescrIdx].Ptr = (byte*)pids;
                            filterDescrPtr[curDescrIdx].Size = options.ProcessIDFilter.Count * sizeof(int);
                            filterDescrPtr[curDescrIdx].Type = TraceEventNativeMethods.EVENT_FILTER_TYPE_PID;
                            curDescrIdx++;
                        }
                        if (options.ProcessNameFilter != null && 0 < options.ProcessNameFilter.Count)
                        {
                            int charCount = 0;
                            for (int i = 0; i < options.ProcessNameFilter.Count; i++)
                            {
                                charCount += options.ProcessNameFilter[i].Length + 1;     // +1 for the separate or the null terminator.  
                            }

                            byte* namesBuffer = stackalloc byte[charCount * 2];           // *2 because it is unicode. 
                            char* namesPtr = (char*)namesBuffer;
                            // Fill in the names, ; separating them.  
                            for (int i = 0; i < options.ProcessNameFilter.Count; i++)
                            {
                                if (i != 0)
                                {
                                    *namesPtr++ = ';';
                                }

                                string name = options.ProcessNameFilter[i];
                                for (int j = 0; j < name.Length; j++)
                                {
                                    *namesPtr++ = name[j];
                                }
                            }
                            *namesPtr++ = '\0';
                            filterDescrPtr[curDescrIdx].Ptr = namesBuffer;
                            Debug.Assert(&filterDescrPtr[curDescrIdx].Ptr[charCount * 2] == (byte*)namesPtr);
                            filterDescrPtr[curDescrIdx].Size = charCount * 2;       // *2 because it is unicode.  
                            filterDescrPtr[curDescrIdx].Type = TraceEventNativeMethods.EVENT_FILTER_TYPE_EXECUTABLE_NAME;
                            if (filterDescrPtr[curDescrIdx].Size >= 1024)
                            {
                                throw new ArgumentException("ProcessNameFilters too large.");
                            }

                            curDescrIdx++;
                        }

                        var eventIdsBufferSize = ComputeEventIdsBufferSize(options.EventIDsToEnable);
                        if (0 < eventIdsBufferSize)
                        {
                            var eventIds = stackalloc byte[eventIdsBufferSize];
                            ComputeEventIds(&filterDescrPtr[curDescrIdx++], eventIds, eventIdsBufferSize,
                                options.EventIDsToEnable, true, TraceEventNativeMethods.EVENT_FILTER_TYPE_EVENT_ID);
                        }
                        eventIdsBufferSize = ComputeEventIdsBufferSize(options.EventIDsToDisable);
                        if (0 < eventIdsBufferSize)
                        {
                            var eventIds = stackalloc byte[eventIdsBufferSize];
                            ComputeEventIds(&filterDescrPtr[curDescrIdx++], eventIds, eventIdsBufferSize,
                                options.EventIDsToDisable, false, TraceEventNativeMethods.EVENT_FILTER_TYPE_EVENT_ID);
                        }
                        eventIdsBufferSize = ComputeEventIdsBufferSize(options.EventIDStacksToEnable);
                        if (0 < eventIdsBufferSize)
                        {
                            var eventIds = stackalloc byte[eventIdsBufferSize];
                            ComputeEventIds(&filterDescrPtr[curDescrIdx++], eventIds, eventIdsBufferSize,
                                options.EventIDStacksToEnable, true, TraceEventNativeMethods.EVENT_FILTER_TYPE_STACKWALK);
                        }
                        eventIdsBufferSize = ComputeEventIdsBufferSize(options.EventIDStacksToDisable);
                        if (0 < eventIdsBufferSize)
                        {
                            var eventIds = stackalloc byte[eventIdsBufferSize];
                            ComputeEventIds(&filterDescrPtr[curDescrIdx++], eventIds, eventIdsBufferSize,
                                options.EventIDStacksToDisable, false, TraceEventNativeMethods.EVENT_FILTER_TYPE_STACKWALK);
                        }
                    }
                    Debug.Assert(curDescrIdx <= MaxDesc);
                    if (curDescrIdx == 0)
                    {
                        filterDescrPtr = null;
                    }

                    int hr;
                    try
                    {
                        // Try the Win7 API
                        TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS parameters = new TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS();

                        parameters.Version = TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS_VERSION;
                        parameters.FilterDescCount = curDescrIdx;
                        parameters.EnableFilterDesc = filterDescrPtr;

                        if (options.StacksEnabled || options.EventIDStacksToEnable != null || options.EventIDStacksToDisable != null)
                        {
                            parameters.EnableProperty |= TraceEventNativeMethods.EVENT_ENABLE_PROPERTY_STACK_TRACE;
                        }
                        if(options.EnableInContainers)
                        {
                            parameters.EnableProperty |= TraceEventNativeMethods.EVENT_ENABLE_PROPERTY_ENABLE_SILOS;
                        }
                        if(options.EnableSourceContainerTracking)
                        {
                            parameters.EnableProperty |= TraceEventNativeMethods.EVENT_ENABLE_PROPERTY_SOURCE_CONTAINER_TRACKING;
                        }

                        if (etwFilteringSupported)      // If we are on 8.1 we can use the newer API.  
                        {
                            parameters.Version = TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS_VERSION_2;
                        }
                        else
                        {
                            Debug.Assert(curDescrIdx <= 1);
                            Debug.Assert(filterDescrPtr == null || -100 <= filterDescrPtr[0].Type);   // We are not using any of the Win8.1 defined types.  
                        }

                        uint eventControlCode = (valueDataType == ControllerCommand.SendManifest
                                                     ? TraceEventNativeMethods.EVENT_CONTROL_CODE_CAPTURE_STATE
                                                     : TraceEventNativeMethods.EVENT_CONTROL_CODE_ENABLE_PROVIDER);
                        hr = TraceEventNativeMethods.EnableTraceEx2(m_SessionHandle, ref providerGuid,
                            eventControlCode, (byte)providerLevel,
                            matchAnyKeywords, matchAllKeywords, EnableProviderTimeoutMSec, ref parameters);
                    }
                    catch (TypeLoadException)
                    {
                        // OK that did not work, try the VISTA API
                        hr = TraceEventNativeMethods.EnableTraceEx(ref providerGuid, null, m_SessionHandle, 1,
                            (byte)providerLevel, matchAnyKeywords, matchAllKeywords, 0, filterDescrPtr);
                    }
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
                }

                // Track our current enabled providers so we can request manifests upon filename changes.
                if (valueDataType == ControllerCommand.Update)
                {
                    lock (m_enabledProviders)
                    {
                        m_enabledProviders[providerGuid] = matchAnyKeywords;
                    }
                }

                m_IsActive = true;
                return m_restarted;
            }
        }

        /// <summary>
        /// Enable a NON-KERNEL provider (see also EnableKernelProvider) which has a given provider name.  
        /// This API first checks if a published provider exists by that name, otherwise it 
        /// assumes it is an EventSouce and determines the provider Guid by hashing the name according to a
        /// well known algorithm.  Thus it will never return a failure for a incorrect spelling of the name.  
        /// </summary>
        /// <param name="providerName">
        /// The name of the provider.  It must either be registered with the operating system (logman query providers returns it)
        /// or it must be an EventSource (see GetEventSourceGuidFromName)</param>
        /// <param name="providerLevel">The verbosity to turn on</param>
        /// <param name="matchAnyKeywords">A bitvector representing the areas to turn on. Only the
        /// low 32 bits are used by classic providers and passed as the 'flags' value.  Zero
        /// is a special value which is a provider defined default, which is usually 'everything'</param>
        /// <param name="options">Additional options for the provider (e.g. taking a stack trace)</param>
        /// <param name="values">This is set of key-value strings that are passed to the provider
        /// for provider-specific interpretation. Can be null if no additional args are needed.  
        /// If the special key-value pair 'Command'='SendManifest' is provided, then the 'SendManifest'
        /// command will be sent (which causes EventSources to re-dump their manifest to the ETW log.  </param>
        /// <returns>true if the session already existed and needed to be restarted.</returns>
        [Obsolete("Use EnableProvider(string, TraceEventLevel, ulong, TraceEventProviderOptions) overload instead")]
        public bool EnableProvider(string providerName, TraceEventLevel providerLevel, ulong matchAnyKeywords, TraceEventOptions options, IEnumerable<KeyValuePair<string, string>> values = null)
        {
            var providerGuid = TraceEventProviders.GetProviderGuidByName(providerName);
            if (providerGuid == Guid.Empty)
            {
                providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerName);
            }

            return EnableProvider(providerGuid, providerLevel, matchAnyKeywords, options, values);
        }
        /// <summary>
        /// Enable a NON-KERNEL provider (see also EnableKernelProvider) represented by 'providerGuid'.
        /// </summary>
        /// <param name="providerGuid">
        /// The Guid that represents the event provider enable. </param>
        /// <param name="providerLevel">The verbosity to turn on</param>
        /// <param name="matchAnyKeywords">A bitvector representing the areas to turn on. Only the
        /// low 32 bits are used by classic providers and passed as the 'flags' value.  Zero
        /// is a special value which is a provider defined default, which is usually 'everything'</param>
        /// <param name="options">Additional options for the provider (e.g. taking a stack trace)</param>
        /// <param name="values">This is set of key-value strings that are passed to the provider
        /// for provider-specific interpretation. Can be null if no additional args are needed.  
        /// If the special key-value pair 'Command'='SendManifest' is provided, then the 'SendManifest'
        /// command will be sent (which causes EventSources to re-dump their manifest to the ETW log.  </param>
        /// <returns>true if the session already existed and needed to be restarted.</returns>
        [Obsolete("Use EnableProvider(Guid, TraceEventLevel, ulong, TraceEventProviderOptions) overload instead")]
        public bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel, ulong matchAnyKeywords, TraceEventOptions options, IEnumerable<KeyValuePair<string, string>> values = null)
        {
            var args = new TraceEventProviderOptions() { Arguments = values };
            if ((options & TraceEventOptions.Stacks) != 0)
            {
                args.StacksEnabled = true;
            }

            return EnableProvider(providerGuid, providerLevel, matchAnyKeywords, args);
        }
        /// <summary>
        /// Enable an ETW provider, passing a raw blob of data to the provider as a Filter specification.   
        /// 
        /// Note that this routine is only provided to interact with old ETW providers that can interpret EVENT_FILTER_DESCRIPTOR data
        /// but did not conform to the key-value string conventions.   This allows this extra information to be passed to these old
        /// providers.   Ideally new providers follow the key-value convention and EnableProvider can be used.  
        /// </summary>
        [Obsolete("Use TraceEventProviderOptions.RawArguments overload instead")]
        public void EnableProviderWithRawProviderData(Guid providerGuid, TraceEventLevel providerLevel, ulong matchAnyKeywords, TraceEventOptions options, byte[] providerData, int providerDataSize)
        {
            var exactArray = providerData;
            if (exactArray.Length != providerDataSize)
            {
                exactArray = new byte[providerDataSize];
                Array.Copy(providerData, exactArray, exactArray.Length);
            }
            var args = new TraceEventProviderOptions() { RawArguments = exactArray };
            if ((options & TraceEventOptions.Stacks) != 0)
            {
                args.StacksEnabled = true;
            }

            EnableProvider(providerGuid, providerLevel, matchAnyKeywords, args);
        }
        /// <summary>
        /// Helper function that is useful when using EnableProvider with key value pairs. 
        /// Given a list of key-value pairs, create a dictionary of the keys mapping to the values.   
        /// </summary>
        [Obsolete("Use TraceEventProviderOptions.AddArgument instead")]
        public static Dictionary<string, string> MakeDictionary(params string[] keyValuePairs)
        {
            var ret = new Dictionary<string, string>();
            for (int i = 1; i < keyValuePairs.Length; i += 2)
            {
                ret.Add(keyValuePairs[i - 1], keyValuePairs[i]);
            }

            return ret;
        }

        // OS Kernel Provider support 
        /// <summary>
        /// Enable the kernel provider for the session. Before windows 8 this session must be called 'NT Kernel Session'.   
        /// This API is OK to call from one thread while Process() is being run on another
        /// <param name="flags">Specifies the particular kernel events of interest</param>
        /// <param name="stackCapture">
        /// Specifies which events should have their stack traces captured when an event is logged</param>
        /// <returns>Returns true if the session existed before and was restarted (see TraceEventSession)</returns>
        /// </summary>
        public unsafe bool EnableKernelProvider(KernelTraceEventParser.Keywords flags, KernelTraceEventParser.Keywords stackCapture = KernelTraceEventParser.Keywords.None)
        {
            // Setting stack capture implies that it is on.  
            flags |= stackCapture;
            lock (this)
            {
#if !CONTAINER_WORKAROUND_NOT_NEEDED
                // This is a work-around because in containers if you try to turn on kernel events that
                // it does not support it simply silently fails.   We work around this by insuring that 
                // we detect if we are in a container and if so strip out kernel events that might cause 
                // problems.   Can be removed when containers do this automatically 
                var containerTypeObj = Registry.GetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control", "ContainerType", null);
                if (containerTypeObj is int)    // false if containerTypeObj is null
                {
                    flags &= ~KernelTraceEventParser.Keywords.NonContainer;
                    stackCapture &= ~KernelTraceEventParser.Keywords.NonContainer;
                }
#endif 
                // many of the kernel events are missing the process or thread information and have to be fixed up.  In order to do this I need the
                // process and thread events to do this, so we turn those on if any other keyword is on.  
                if (flags != KernelTraceEventParser.Keywords.None)
                {
                    flags |= (KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread);
                }

                bool systemTraceProvider = false;
                if (!OperatingSystemVersion.AtLeast(60))
                {
                    throw new NotSupportedException("Kernel Event Tracing is only supported on Windows 6.0 (Vista) and above.");
                }

                if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE || m_kernelSession != null)
                {
                    throw new Exception("The kernel provider must be enabled first and only once in a session.");
                }

                if (m_SessionName != KernelTraceEventParser.KernelSessionName)
                {
                    if ((flags & KernelTraceEventParser.NonOSKeywords) != 0)
                    {
                        throw new NotSupportedException("Keyword specified this is only supported on the " + KernelTraceEventParser.KernelSessionName + " session.");
                    }

                    if (!OperatingSystemVersion.AtLeast(62))
                    {
                        if (m_FileName != null)
                        {
                            throw new NotSupportedException("System Tracing is only supported on Windows 8 and above.");
                        }

                        // On windows 7 and Vista, fake the systemTraceProvider for real time sessions, and do the EnableKernelProvider on that.  
                        var kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
                        var nestedRet = kernelSession.EnableKernelProvider(flags, stackCapture);
                        m_kernelSession = kernelSession;
                        return nestedRet;
                    }
                    else
                    {
                        systemTraceProvider = true;
                    }
                }

                // The Profile event requires the SeSystemProfilePrivilege to succeed, so set it.  
                if ((flags & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.PMCProfile)) != 0)
                {
                    TraceEventNativeMethods.SetPrivilege(TraceEventNativeMethods.SE_SYSTEM_PROFILE_PRIVILEGE);
                    double cpu100ns = (CpuSampleIntervalMSec * 10000.0 + .5);
                    // The API seems to have an upper bound of 1 second.  
                    if (cpu100ns >= int.MaxValue || ((int)cpu100ns) > 10000000)
                    {
                        throw new ApplicationException("CPU Sampling interval is too large.");
                    }

                    var succeeded = ETWControl.SetCpuSamplingRate((int)cpu100ns);       // Always try to set, since it may not be the default
                    if (!succeeded && CpuSampleIntervalMSec != 1.0F)
                    {
                        throw new ApplicationException("Can't set CPU sampling to " + CpuSampleIntervalMSec.ToString("f3") + "MSec.");
                    }
                }

                if (IsInMemoryCircular && (flags & KernelTraceEventParser.NonOSKeywords) != 0)
                {
                    throw new ApplicationException("Using kernel flags that are Incompatible with InMemoryCircularBuffer.");
                }

                var propertiesBuff = stackalloc byte[PropertiesSize];
                var properties = GetProperties(propertiesBuff);

                // Initialize the stack collecting information
                const int stackTracingIdsMax = 96;      // As of 2/2015, we have a max of 56 so we are in good shape.  
                int numIDs = 0;
                var stackTracingIds = stackalloc STACK_TRACING_EVENT_ID[stackTracingIdsMax];
#if DEBUG
                // Try setting all flags, if we overflow an assert in SetStackTraceIds will fire.  
                SetStackTraceIds((KernelTraceEventParser.Keywords)(-1), stackTracingIds, stackTracingIdsMax);
#endif
                if (stackCapture != KernelTraceEventParser.Keywords.None)
                {
                    numIDs = SetStackTraceIds(stackCapture, stackTracingIds, stackTracingIdsMax);
                }

                bool ret = false;
                int dwErr;
                if (systemTraceProvider)
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_SYSTEM_LOGGER_MODE;
                    EnsureStarted(properties);

                    dwErr = TraceEventNativeMethods.TraceSetInformation(m_SessionHandle,
                                                                        TraceEventNativeMethods.TRACE_INFO_CLASS.TraceStackTracingInfo,
                                                                        stackTracingIds,
                                                                        (numIDs * sizeof(STACK_TRACING_EVENT_ID)));
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));

                    ulong* systemTraceFlags = stackalloc ulong[1];
                    systemTraceFlags[0] = (ulong)(flags & ~KernelTraceEventParser.NonOSKeywords);
                    dwErr = TraceEventNativeMethods.TraceSetInformation(m_SessionHandle,
                                                                        TraceEventNativeMethods.TRACE_INFO_CLASS.TraceSystemTraceEnableFlagsInfo,
                                                                        systemTraceFlags,
                                                                        sizeof(ulong));
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));
                    ret = true;
                }
                else
                {
                    properties->Wnode.Guid = KernelTraceEventParser.ProviderGuid;
                    properties->EnableFlags = (uint)flags;

                    dwErr = ETWKernelControl.StartKernelSession(out m_SessionHandle, properties, PropertiesSize, stackTracingIds, numIDs);
                    if (dwErr == 0xB7) // STIERR_HANDLEEXISTS
                    {
                        ret = true;
                        Stop();
                        m_Stopped = false;
                        Thread.Sleep(100);  // Give it some time to stop. 
                        dwErr = ETWKernelControl.StartKernelSession(out m_SessionHandle, properties, PropertiesSize, stackTracingIds, numIDs);
                    }
                }

                if (dwErr == 5 && OperatingSystemVersion.AtLeast(51))     // On Vista and we get a 'Accessed Denied' message
                {
                    throw new UnauthorizedAccessException("Error Starting ETW:  Access Denied (Administrator rights required to start ETW)");
                }

                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));
                m_IsActive = true;

                if (OperatingSystemVersion.AtLeast(62) && StackCompression)
                {
                    ETWControl.EnableStackCaching(m_SessionHandle);
                }

                return ret;
            }
        }

        // OS Heap Provider support.  
        /// <summary>
        /// Turn on windows heap logging (stack for allocation) for a particular existing process.
        /// </summary>
        public void EnableWindowsHeapProvider(int pid)
        {
            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
            {
                throw new ApplicationException("Heap Provider can only be used in its own session.");
            }

            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);

            int dwErr = ETWKernelControl.StartWindowsHeapSession(out m_SessionHandle, properties, PropertiesSize, pid);
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));

            m_IsActive = true;
        }
        /// <summary>
        /// Turn on windows heap logging for a particular EXE file name (just the file name, no directory, but it DOES include the .exe extension)
        /// This API is OK to call from one thread while Process() is being run on another
        /// </summary>
        /// <param name="exeFileName"></param>
        public void EnableWindowsHeapProvider(string exeFileName)
        {
            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
            {
                throw new ApplicationException("Heap Provider can only be used in its own session.");
            }

            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);

            int dwErr = ETWKernelControl.StartWindowsHeapSession(out m_SessionHandle, properties, PropertiesSize, exeFileName);
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));

            m_IsActive = true;
        }

        /// <summary>
        /// Disables a provider with the given provider ID completely
        /// </summary>
        public void DisableProvider(Guid providerGuid)
        {
            lock (this)
            {
                int hr;
                try
                {
                    try
                    {
                        // Try the Win7 API
                        var parameters = new TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS { Version = TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS_VERSION };
                        hr = TraceEventNativeMethods.EnableTraceEx2(
                            m_SessionHandle, ref providerGuid, TraceEventNativeMethods.EVENT_CONTROL_CODE_DISABLE_PROVIDER,
                            0, 0, 0, EnableProviderTimeoutMSec, ref parameters);
                    }
                    catch (TypeLoadException)
                    {
                        // OK that did not work, try the VISTA API
                        hr = TraceEventNativeMethods.EnableTraceEx(ref providerGuid, null, m_SessionHandle, 0, 0, 0, 0, 0, null);
                    }
                }
                catch (TypeLoadException)
                {
                    // Try with the old pre-vista API
                    hr = TraceEventNativeMethods.EnableTrace(0, 0, 0, ref providerGuid, m_SessionHandle);
                }
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            }
        }
        /// <summary>
        /// Disables a provider with the given name completely
        /// </summary>
        public void DisableProvider(string providerName)
        {
            var providerGuid = TraceEventProviders.GetProviderGuidByName(providerName);
            if (providerGuid == Guid.Empty)
            {
                providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerName);
            }

            DisableProvider(providerGuid);
        }

        /// <summary>
        /// Once started, event sessions will persist even after the process that created them dies.  They will also be
        /// implicitly stopped when the TraceEventSession is closed unless the StopOnDispose property is set to false.  
        /// This API is OK to call from one thread while Process() is being run on another
        /// </summary>
        public bool Stop(bool noThrow = false)
        {
            lock (this)
            {
                if (m_Stopped)
                {
                    return true;
                }

                m_Stopped = true;

                try
                {
                    // Do this first because we look for active sessions to clean up.   
                    CleanFilterDataForEtwSession();                                      // Remove any filter data associated with the session. 
                }
                catch (Exception) { Debug.Assert(false); }

                ETWControl.SetCpuSamplingRate(10000);      // Set sample rate back to default 1 Msec 
                var propertiesBuff = stackalloc byte[PropertiesSize];
                var properties = GetProperties(propertiesBuff);
                int hr = TraceEventNativeMethods.ControlTrace(0UL, m_SessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_STOP);
                ETWKernelControl.ResetWindowsHeapTracingFlags(m_SessionName, noThrow);

                if (hr != 0 && hr != TraceEventNativeMethods.ERROR_WMI_INSTANCE_NOT_FOUND)     // Instance name not found.  This means we did not start
                {
                    if (!noThrow)
                    {
                        Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
                    }

                    return false;   // Stop failed
                }

                m_SessionId = -1;
                return true;
            }
        }
        /// <summary>
        /// Close the session and clean up any resources associated with the session.     It is OK to call this more than once.  
        /// This API is OK to call from one thread while Process() is being run on another.   Calling Dispose is on 
        /// a real time session is the way you can force a real time session to stop in a timely manner.  
        /// </summary>
        public void Dispose()
        {
            lock (this)         // It is pretty common to want do do this on different threads. 
            {
                if (m_StopOnDispose)
                {
                    m_StopOnDispose = false;

                    // Only stop the session when we were the original creator of it and not for cases where we attach.
                    // For session just attached to check if it's active, we must not call stop method.
                    // Otherwise, it will caused unexpected stop of trace sessions.
                    if (m_Create)
                    {
                        Stop(true);
                    }
                }

                // TODO need safe handles
                if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                {
                    TraceEventNativeMethods.CloseTrace(m_SessionHandle);
                    m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
                }

                // If we have a source, dispose of that too. 
                if (m_source != null)
                {
                    m_source.Dispose();
                    m_source = null;
                }

                // on Win7 we might have a real time kernel session, dispose of that if present.  
                if (m_kernelSession != null)
                {
                    m_kernelSession.Dispose();
                    m_kernelSession = null;
                }

                GC.SuppressFinalize(this);
            }
        }
        /// <summary>
        /// Asks all providers to flush events to the session
        /// This API is OK to call from one thread while Process() is being run on another
        /// </summary>
        public void Flush()
        {
            lock (this)
            {
                if (m_Stopped)
                {
                    return;
                }

                var propertiesBuff = stackalloc byte[PropertiesSize];
                var properties = GetProperties(propertiesBuff);
                int hr = TraceEventNativeMethods.ControlTrace(0UL, m_SessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_FLUSH);
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            }
        }

        /// <summary>
        /// For either session create with a file name this method can be used to redirect the data to a
        /// new file (so the previous one can be uploaded or processed offline), 
        /// 
        /// It can also be used for a in-memory circular buffer session (FileName == null and CircularMB != 0)
        /// but its semantics is that simply writes the snapshot to the file (and closes it).  It does not
        /// actually make the FileName property become non-null because it only flushes the data, it does
        /// not cause persistent redirection of the data stream.  (it is like it auto-reverts).  
        /// 
        /// It is an error to call this on a real time session.  (FileName == null and CircularMB == 0)
        /// </summary>
        /// <param name="newName">The path to the file to write the data to.</param>
        public void SetFileName(string newName)
        {
            if (m_MultiFileMB != 0)
            {
                throw new InvalidOperationException("Cannot set file name when MultiFileMB is also non-zero.");
            }

            var origFileName = m_FileName;      // Remember the original name we had.  

            // If we are in file mode, flush it before we close it and move on to the next file.  
            if (origFileName != null)
            {
                Flush();
            }

            // Set up the properties for the new file name 
            var propertiesBuff = stackalloc byte[PropertiesSize];
            m_FileName = newName;
            var properties = GetProperties(propertiesBuff);

            int retCode;
            if (origFileName != null)
            {
                // if we had a file name before, then simply do th update
                retCode = TraceEventNativeMethods.ControlTrace(0UL, m_SessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_UPDATE);
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(retCode));
            }
            else
            {
                if (m_CircularBufferMB == 0)        // We are not the in memory circular buffer case.
                {
                    // if it is a real time session currently it is illegal to make it a file based session (we may be able to relax this). 
                    throw new InvalidOperationException("Can only update the file name of a file base session.");
                }

                // For the in-memory circular buffer, this just mean flush
                Flush();
                m_FileName = null;          // We don't really consider this as setting the file name, it is either a flush or an error.  so put it back to null.  
            }

            if (CaptureStateOnSetFileName)
            {
                lock (m_enabledProviders)
                {
                    foreach (var kvp in m_enabledProviders)
                    {
                        var providerGuid = kvp.Key;
                        var matchAnyKeywords = kvp.Value;

                        CaptureState(providerGuid, matchAnyKeywords);
                    }
                }
            }
        }
        /// <summary>
        /// If set, whenever a SetFileName is called (causing a new ETL file to be created), force 
        /// a capture state for every provider that is currently turned on.    This way the file
        /// will be self-contained (will contain all the capture state information needed to decode events)
        /// This setting is true by default.  
        /// </summary>
        public bool CaptureStateOnSetFileName { get; set; }

        /// <summary>
        /// Sends the CAPTURE_STATE command to the provider.  This instructs the provider to log any events that are needed to
        /// reconstruct important state that was set up before the session started.  What is actually done is provider specific.  
        /// EventSources will re-dump their manifest on this command. 
        /// This API is OK to call from one thread while Process() is being run on another
        /// <para>
        /// This routine only works Win7 and above, since previous versions don't have this concept.   The providers also has 
        /// to support it.  
        /// </para>
        /// </summary>
        /// <param name="providerGuid">The GUID that identifies the provider to send the CaptureState command to</param>
        /// <param name="matchAnyKeywords">The Keywords to send as part of the command (can influence what is sent back)</param>
        /// <param name="filterType">if non-zero, this is passed along to the provider as type of the filter data.</param>
        /// <param name="data">If non-null this is either an int, or a byte array and is passed along as filter data.</param>
        public void CaptureState(Guid providerGuid, ulong matchAnyKeywords = ulong.MaxValue, int filterType = 0, object data = null)
        {
            // TODO FIX NOW support TraceEventProviderOptions.  
            lock (this)
            {
                if (m_SessionName == KernelTraceEventParser.KernelSessionName)
                {
                    throw new NotSupportedException("Can only capture state on user mode sessions.");
                }

                EnsureStarted();
                var parameters = new TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS();
                var filter = new TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR();
                parameters.Version = TraceEventNativeMethods.ENABLE_TRACE_PARAMETERS_VERSION;

                byte[] asArray = data as byte[];
                if (data is int)
                {
                    int intVal = (int)data;
                    asArray = new byte[4];
                    asArray[0] = (byte)intVal;
                    asArray[1] = (byte)(intVal >> 8);
                    asArray[2] = (byte)(intVal >> 16);
                    asArray[3] = (byte)(intVal >> 24);
                }
                fixed (byte* filterDataPtr = asArray)
                {
                    if (asArray != null)
                    {
                        parameters.EnableFilterDesc = &filter;
                        filter.Type = filterType;
                        filter.Size = asArray.Length;
                        filter.Ptr = filterDataPtr;
                    }
                    int hr = TraceEventNativeMethods.EnableTraceEx2(
                        m_SessionHandle, ref providerGuid, TraceEventNativeMethods.EVENT_CONTROL_CODE_CAPTURE_STATE,
                        (byte)TraceEventLevel.Verbose, matchAnyKeywords, 0, EnableProviderTimeoutMSec, ref parameters);
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
                }
            }
        }

        // These properties can be set both before and after a provider has been enabled in the session.  

        /// <summary>
        /// When you issue a EnableProvider command, on windows 7 and above it can be done synchronously (that is you know that because 
        /// the EnableProvider returned that the provider actually got the command).   However synchronous behavior means that
        /// you may wait forever.   This is the time EnableProvider waits until it gives up.   Setting this
        /// to 0 means asynchronous (fire and forget).   The default is 10000 (wait 10 seconds) 
        /// Before windows 7 EnableProvider is always asynchronous.  
        /// </summary>
        public int EnableProviderTimeoutMSec { get; set; }
        /// <summary>
        /// If set then Stop() will be called automatically when this object is Disposed or Finalized by the GC.  
        /// This is true BY DEFAULT, so if you want your session to survive past the end of the process
        /// you must set this to false.  
        /// </summary>
        public bool StopOnDispose { get { return m_StopOnDispose; } set { m_StopOnDispose = value; } }

        // Things that you can set before enabling a provider, but cannot afterward.  
        /// <summary>
        /// Cause the log to be a circular buffer.  The buffer size (in MegaBytes) is the value of this property.
        /// Setting this to 0 will cause it to revert to non-circular mode.  
        /// The setter can only be called BEFORE any provider is enabled.  
        /// </summary>
        public int CircularBufferMB
        {
            get { return m_CircularBufferMB; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                if (m_MultiFileMB != 0)
                {
                    throw new InvalidOperationException("Cannot specify both CircularBufferMB and MultiFileMB.");
                }

                m_CircularBufferMB = value;
            }

        }
        /// <summary>
        /// Cause the as a set of files with a given maximum size.   The file name must end in .ETL and the
        /// output is then a series of files of the form *NNN.ETL (That is it adds a number just before the
        /// .etl suffix).   If you make your file name *.user.etl then the output will be *.user1.etl, *.user2.etl ...
        /// And the MergeInPlace command below will merge them all nicely.  
        /// 
        /// You can have more control over this by using a normal sequential file but use the SetFileName()
        /// method to redirect the data to new files as needed.    
        /// </summary>
        public int MultiFileMB
        {
            get { return m_MultiFileMB; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                if (m_FileName == null)
                {
                    throw new InvalidOperationException("MultiFile is only allowed on sessions with files.");
                }

                if (m_CircularBufferMB != 0)
                {
                    throw new InvalidOperationException("Cannot specify both CircularBufferMB and MultiFileMB.");
                }

                if (!m_FileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("FileName must have .etl suffix");
                }

                if (value == 0)
                {
                    if (m_FileName.EndsWith("%d.etl", StringComparison.OrdinalIgnoreCase))
                    {
                        m_FileName = m_FileName.Substring(0, m_FileName.Length - 6) + ".etl";
                    }
                }
                else
                {
                    if (!m_FileName.EndsWith("%d.etl", StringComparison.OrdinalIgnoreCase))
                    {
                        m_FileName = m_FileName.Substring(0, m_FileName.Length - 4) + "%d.etl";
                    }
                }
                m_MultiFileMB = value;
            }
        }
        /// <summary>
        /// Sets the size of the buffer the operating system should reserve to avoid lost packets.   Starts out 
        /// as a very generous 64MB for files.  If events are lost, this can be increased, but keep in mind that
        /// no value will help if the average incoming rate is faster than the processing rate.  
        /// The setter can only be called BEFORE any provider is enabled.  
        /// </summary>
        public int BufferSizeMB
        {
            get { return m_BufferSizeMB; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                m_BufferSizeMB = value;
            }
        }
        /// <summary>
        /// This is the unit in which data is flushed in Kilobytes.   By default it is 64 (KB).  
        /// By default a TraceEventSession will flush every second, and this amount of space will be transferred
        /// to the file.   Ideally it is smaller than the number data bytes you expect in a second from any
        /// particular processor.  It can't be less than 1K per processor on the machine.   However if you make 
        /// it less than 64 (K) you will limit the size of the event that the process can send 
        /// (they will simply be discarded).   
        /// </summary>
        public int BufferQuantumKB
        {
            get { return m_BufferQuantumKB; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                m_BufferQuantumKB = value;
                if (m_BufferQuantumKB < Environment.ProcessorCount)
                {
                    m_BufferQuantumKB = Environment.ProcessorCount;
                }
            }
        }
        /// <summary>
        /// The rate at which CPU samples are collected.  By default this is 1 (once a millisecond per CPU).
        /// There is a lower bound on this (typically .125 Msec)
        /// </summary>
        public float CpuSampleIntervalMSec
        {
            get { return m_CpuSampleIntervalMSec; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                m_CpuSampleIntervalMSec = value;
            }
        }
        /// <summary>
        /// Indicate that this session should use compress the stacks to save space.  
        /// Must be set before any providers are enabled.  Currently only works for kernel events.  
        /// </summary>
        public bool StackCompression
        {
            get { return m_StackCompression; }
            set
            {
                if (IsActive)
                {
                    throw new InvalidOperationException("Property can't be changed after a provider has started.");
                }

                m_StackCompression = value;
            }
        }

        // These properties are read-only 
        /// <summary>
        /// The name of the session that can be used by other threads to attach to the session. 
        /// </summary>
        public string SessionName
        {
            get { return m_SessionName; }
        }
        /// <summary>
        /// The name of the moduleFile that events are logged to.  Null means the session is real time
        /// or is a circular in-memory buffer.    See also SetFileName() method. 
        /// </summary>
        public string FileName
        {
            get
            {
                return m_FileName;
            }
        }
        /// <summary>
        /// If this is a real time session you can fetch the source associated with the session to start receiving events.  
        /// Currently does not work on file based sources (we expect you to wait until the file is complete).  
        /// </summary>
        public ETWTraceEventSource Source
        {
            get
            {
                if (m_source == null)
                {
                    if (!IsRealTimeSession)
                    {
                        throw new InvalidOperationException("Only non-file based, non-circular ('real time') sessions have can have a source associated with them.");
                    }

                    if (m_kernelSession != null && !m_associatedWithTraceLog)
                    {
                        throw new InvalidOperationException("Can only use Kernel events in real time sessions on Windows 7 if you use TraceLog.CreateFromTraceEventSession");
                    }

                    if (m_SessionHandle == TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                    {
                        if (m_SessionName == KernelTraceEventParser.KernelSessionName)
                        {
                            throw new NotSupportedException("Kernel sessions must be started (EnableKernelProvider called) before accessing the source.");
                        }

                        EnsureStarted();
                    }
                    m_source = new ETWTraceEventSource(SessionName, TraceEventSourceType.Session);
                }
                return m_source;
            }
        }
        /// <summary>
        /// Creating a TraceEventSession does not actually interact with the operating system until a
        /// provider is enabled. At that point the session is considered active (OS state that survives a
        /// process exit has been modified). IsActive returns true if the session is active. 
        /// 
        /// </summary>
        public bool IsActive
        {
            get
            {
                return m_IsActive && !m_Stopped;
            }
        }
        /// <summary>
        /// Returns the number of events that should have been delivered to this session but were lost 
        /// (typically because the incoming rate was too high).   This value is up-to-date for real time
        /// sessions.  
        /// </summary>
        public int EventsLost
        {
            get
            {
                var propertiesBuff = stackalloc byte[PropertiesSize];
                var properties = GetProperties(propertiesBuff);
                int hr = TraceEventNativeMethods.ControlTrace(0UL, m_SessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_QUERY);
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));

                // TODO determine what properties->RealTimeBuffersLost is (in my experiments it was always 0)
                return (int)(properties->EventsLost);
            }
        }
        /// <summary>
        /// Returns true if the session is logging to a circular buffer.  This may be in-memory (FileName == null)
        /// or to a file (FileName != null)
        /// </summary>
        public bool IsCircular { get { return m_CircularBufferMB != 0; } }
        /// <summary>
        /// Returns true if the session is Real Time.  This means it is not to a file, and not circular.  
        /// </summary>
        public bool IsRealTime { get { return m_FileName == null && !IsCircular; } }
        /// <summary>
        /// Returns true if this is a in-memory circular buffer (it is circular without an output file).  
        /// Use SetFileName() to dump the in-memory buffer to a file.  
        /// </summary>
        public bool IsInMemoryCircular { get { return m_FileName == null && IsCircular; } }

        /// <summary>
        /// ETW trace sessions survive process shutdown. Thus you can attach to existing active sessions.
        /// GetActiveSessionNames() returns a list of currently existing session names.  These can be passed
        /// to the TraceEventSession constructor to open it.   
        /// </summary>
        /// <returns>A enumeration of strings, each of which is a name of a session</returns>
        public static unsafe List<string> GetActiveSessionNames()
        {
            int MAX_SESSIONS = GetETWMaxLoggers();
            int sizeOfProperties = sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) +
                                   sizeof(char) * TraceEventSession.MaxNameSize +     // For log moduleFile name 
                                   sizeof(char) * TraceEventSession.MaxNameSize;      // For session name

            byte* sessionsArray = stackalloc byte[MAX_SESSIONS * sizeOfProperties];
            TraceEventNativeMethods.EVENT_TRACE_PROPERTIES** propetiesArray = stackalloc TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*[MAX_SESSIONS];

            for (int i = 0; i < MAX_SESSIONS; i++)
            {
                TraceEventNativeMethods.EVENT_TRACE_PROPERTIES* properties = (TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*)&sessionsArray[sizeOfProperties * i];
                properties->Wnode.BufferSize = (uint)sizeOfProperties;
                properties->LoggerNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES);
                properties->LogFileNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) + sizeof(char) * TraceEventSession.MaxNameSize;
                propetiesArray[i] = properties;
            }
            int sessionCount = 0;
            int hr = TraceEventNativeMethods.QueryAllTraces((IntPtr)propetiesArray, MAX_SESSIONS, ref sessionCount);
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));

            List<string> activeTraceNames = new List<string>(sessionCount);
            for (int i = 0; i < sessionCount; i++)
            {
                byte* propertiesBlob = (byte*)propetiesArray[i];
                string sessionName = new string((char*)(&propertiesBlob[propetiesArray[i]->LoggerNameOffset]));
                activeTraceNames.Add(sessionName);
            }
            return activeTraceNames;
        }

        /// <summary>
        /// Maximum Number of MaxEtwLoggers the system supports
        /// </summary>
        private static int? MaxEtwLoggers = null;

        /// <summary>
        /// Get the maximum number of ETW loggers supported by the current machine
        /// </summary>
        /// <returns>The maximum number of supported ETW loggers</returns>
        private static int GetETWMaxLoggers()
        {
            const string MaxEtwRegistryKey = "SYSTEM\\CurrentControlSet\\Control\\WMI";
            const string MaxEtwPropertyName = "EtwMaxLoggers";
            const int DefaultMaxETWLoggers = 64;

            if (MaxEtwLoggers == null)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(MaxEtwRegistryKey))
                    {
                        if (key != null)
                        {
                            var property = key.GetValue(MaxEtwPropertyName);
                            if (property != null)
                            {
                                if (int.TryParse(property.ToString(), out int propertyValue))
                                {
                                    // Ensure registry was set within permissable range as defined by
                                    // https://docs.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracew
                                    if (propertyValue >= 32 && propertyValue <= 256)
                                    {
                                        MaxEtwLoggers = propertyValue;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception) { }

                // If the value does not exist or cannot be read from the registry, return the default value
                MaxEtwLoggers = MaxEtwLoggers ?? DefaultMaxETWLoggers;
            }

            return (int)MaxEtwLoggers;
        }

        // Post processing (static methods)
        /// <summary>
        /// It is sometimes useful to merge the contents of several ETL files into a single 
        /// output ETL file.   This routine does that.  It also will attach additional 
        /// information that will allow correct file name and symbolic lookup if the 
        /// ETL file is used on a machine other than the one that the data was collected on.
        /// If you wish to transport the file to another machine you need to merge them, even 
        /// if you have only one file so that this extra information get incorporated.  
        /// </summary>
        /// <param name="inputETLFileNames">The input ETL files to merge</param>
        /// <param name="outputETLFileName">The output ETL file to produce.</param>
        /// <param name="options">Optional Additional options for the Merge (seeTraceEventMergeOptions) </param>
        public static void Merge(string[] inputETLFileNames, string outputETLFileName, TraceEventMergeOptions options = TraceEventMergeOptions.None)
        {
            EVENT_TRACE_MERGE_EXTENDED_DATA flags =
                EVENT_TRACE_MERGE_EXTENDED_DATA.IMAGEID |
                EVENT_TRACE_MERGE_EXTENDED_DATA.BUILDINFO |
                EVENT_TRACE_MERGE_EXTENDED_DATA.WINSAT |
                EVENT_TRACE_MERGE_EXTENDED_DATA.EVENT_METADATA |
                EVENT_TRACE_MERGE_EXTENDED_DATA.VOLUME_MAPPING;

            if ((options & TraceEventMergeOptions.Compress) != 0 && OperatingSystemVersion.AtLeast(62))
            {
                flags |= EVENT_TRACE_MERGE_EXTENDED_DATA.COMPRESS_TRACE;
            }

            // Clear all other flags and only specify IMAGEID.
            if((options == TraceEventMergeOptions.ImageIDsOnly))
            {
                flags = EVENT_TRACE_MERGE_EXTENDED_DATA.IMAGEID;
            }

            ETWKernelControl.Merge(inputETLFileNames, outputETLFileName, flags);
        }

        /// <summary>
        /// This variation of the Merge command takes the 'primary' etl file name (X.etl)
        /// and will merge in any files that match .clr*.etl .user*.etl. and .kernel.etl.  
        /// </summary>
        public static void MergeInPlace(string etlFileName, TextWriter log)
        {
            var dir = Path.GetDirectoryName(etlFileName);
            if (dir.Length == 0)
            {
                dir = ".";
            }

            var baseName = Path.GetFileNameWithoutExtension(etlFileName);
            List<string> mergeInputs = new List<string>();
            mergeInputs.Add(etlFileName);
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".kernel*.etl"));
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".clr*.etl"));
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".user*.etl"));

            string tempName = Path.ChangeExtension(etlFileName, ".etl.new");
            try
            {
                // Do the merge;
                Merge(mergeInputs.ToArray(), tempName);

                // Delete the originals.  
                foreach (var mergeInput in mergeInputs)
                {
                    FileUtilities.ForceDelete(mergeInput);
                }

                // Place the output in its final resting place.  
                FileUtilities.ForceMove(tempName, etlFileName);
            }
            finally
            {
                // Insure we clean up.  
                if (File.Exists(tempName))
                {
                    File.Delete(tempName);
                }
            }
        }

        /// <summary>
        /// Is the current process Elevated (allowed to turn on a ETW provider).   This is useful because
        /// you need to be elevated to enable providers on a TraceEventSession.  
        /// </summary>
        public static bool? IsElevated() { return TraceEventNativeMethods.IsElevated(); }
        /// <summary>
        /// Set the Windows Debug Privilege.   Useful because some event providers require this privilege, and 
        /// and it must be enabled explicitly (even if the process is elevated). 
        /// </summary>
        public static void SetDebugPrivilege()
        {
            TraceEventNativeMethods.SetPrivilege(TraceEventNativeMethods.SE_DEBUG_PRIVILEGE);
        }

        #region Private
        /// <summary>
        /// The 'properties' field is only the header information.  There is 'tail' that is 
        /// required.  'ToUnmangedBuffer' fills in this tail properly. 
        /// </summary>
        ~TraceEventSession()
        {
            Dispose();
        }

        /// <summary>
        /// Returns a sorted dictionary of  names and Guids for every provider registered on the system.   
        /// </summary>
        internal static SortedDictionary<string, Guid> ProviderNameToGuid
        {
            get
            {
                if (s_providersByName == null)
                {
                    s_providersByName = new SortedDictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                    int buffSize = 0;
                    var hr = TraceEventNativeMethods.TdhEnumerateProviders(null, ref buffSize);
                    Debug.Assert(hr == 122);     // ERROR_INSUFFICIENT_BUFFER
                    var buffer = stackalloc byte[buffSize];
                    var providersDesc = (TraceEventNativeMethods.PROVIDER_ENUMERATION_INFO*)buffer;

                    hr = TraceEventNativeMethods.TdhEnumerateProviders(providersDesc, ref buffSize);
                    if (hr != 0)
                    {
                        Trace.WriteLine("TdhEnumerateProviders failed HR = " + hr);
                        providersDesc->NumberOfProviders = 0;
                    }

                    var providers = (TraceEventNativeMethods.TRACE_PROVIDER_INFO*)&providersDesc[1];
                    for (int i = 0; i < providersDesc->NumberOfProviders; i++)
                    {
                        var name = new string((char*)&buffer[providers[i].ProviderNameOffset]);
                        s_providersByName[name] = providers[i].ProviderGuid;
                    }
                }
                return s_providersByName;
            }
        }

        internal static Dictionary<Guid, string> ProviderGuidToName
        {
            get
            {
                if (s_providerNames == null)
                {
                    s_providerNames = new Dictionary<Guid, string>(ProviderNameToGuid.Count);
                    foreach (var keyValue in ProviderNameToGuid)
                    {
                        s_providerNames[keyValue.Value] = keyValue.Key;
                    }
                }
                return s_providerNames;
            }
        }

        // We support file based, in memory circular, and real time.  
        private bool IsRealTimeSession { get { return m_FileName == null && m_CircularBufferMB == 0; } }

        /// <summary>
        /// sets up the EVENT_FILTER_DESCRIPTOR descr to represent the Event Ids in 'eventIds'.   You are given the buffer 
        /// necessary for this (precomputed) for the EVENT_FILTER_EVENT_ID structure.   'enable' is true if this is to enable
        /// (otherwise disable) the events, and descrType indicates the descriptor type (either EVENT_FILTER_TYPE_EVENT_ID or
        /// EVENT_FILTER_TYPE_STACKWALK)
        /// </summary>
        private unsafe void ComputeEventIds(TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR* descr, byte* eventIdsOut, int eventIdsBufferSize, IList<int> eventIds, bool enable, int descrType)
        {
            descr->Type = descrType;
            descr->Size = eventIdsBufferSize;
            descr->Ptr = eventIdsOut;

            var asEventIds = (TraceEventNativeMethods.EVENT_FILTER_EVENT_ID*)eventIdsOut;
            asEventIds->FilterIn = (byte)(enable ? 1 : 0);
            asEventIds->Reserved = 0;
            asEventIds->Count = (ushort)eventIds.Count;

            ushort* eventIdsPtr = &asEventIds->Events[0];
            foreach (var eventId in eventIds)
            {
                *eventIdsPtr++ = (ushort)eventId;
            }

            Debug.Assert((byte*)eventIdsPtr == &eventIdsOut[eventIdsBufferSize]);
        }

        /// <summary>
        /// Computes the number of bytes needed for the EVENT_FILTER_EVENT_ID structure to represent 'eventIds'
        /// return 0 if there is not need for the filter at all.  
        /// </summary>
        private int ComputeEventIdsBufferSize(IList<int> eventIds)
        {
            if (eventIds == null)
            {
                return 0;
            }

            if (eventIds.Count == 0)
            {
                return 0;
            }
            // -1 because struct has 1 elem in it by default, * 2 because ID are shorts.  
            return (eventIds.Count - 1) * 2 + sizeof(TraceEventNativeMethods.EVENT_FILTER_EVENT_ID);
        }

        private static SortedDictionary<string, Guid> s_providersByName;
        private static Dictionary<Guid, string> s_providerNames;

        private static int FindFreeSessionKeyword(Guid providerGuid)
        {
            // TODO FIX NOW.  there are races associated with this.  
            List<TraceEventNativeMethods.TRACE_ENABLE_INFO> infos = TraceEventProviders.SessionInfosForProvider(providerGuid, 0);
            for (int i = 44; ; i++)
            {
                if (i > 47)
                {
                    throw new NotSupportedException("Error enabling provider " + providerGuid + ": Exceeded the maximum of 4 sessions can simultaneously use provider key-value arguments on a single provider simultaneously");
                }

                long bit = ((long)1) << i;

                bool inUse = false;
                if (infos != null)
                {
                    foreach (TraceEventNativeMethods.TRACE_ENABLE_INFO info in infos)
                    {
                        if ((info.MatchAllKeyword & bit) != 0)
                        {
                            inUse = true;
                            break;
                        }
                    }
                }
                if (!inUse)
                {
                    return i;
                }
            }
        }

        /// <summary>
        /// Cleans out all provider data associated with this session.  
        /// </summary>
        private void CleanFilterDataForEtwSession()
        {
            // Optimization, kernel sessions don't need filter cleanup.  
            if (m_SessionName == KernelTraceEventParser.KernelSessionName)
            {
                return;
            }

            if (m_SessionId == -1)
            {
                return;             // don't do cleanup on sessions that are not ourselves.  
            }

            // What we want is actually pretty simple.  We want to enumerate all providers that this session has ever been associated with.  
            // Sadly providers might have died that this session did set data for, so we can't just look at the currently active providers.  
            // What we do today is to enumerate every provider, which is inefficient, but at least is not bad on 64 bit machines.  (Since 
            // most providers are not registered in the WOW which is where we put the data). 
            //
            // If perf becomes a problem, we CAN give up leave behind stale data on dead providers, it is just a bit more dangerous and unhygienic. 
            // For now, stopping providers is rare enough that we can live with the inefficiency.  
            var baseKeyName = GetEventSourceRegistryBaseLocation();
            using (var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(baseKeyName, true))
            {
                foreach (string subKeyName in regKey.GetSubKeyNames())
                {
                    if (!subKeyName.StartsWith("{") || !subKeyName.EndsWith("}"))
                    {
                        continue;
                    }

                    var providerGuid = subKeyName.Substring(1, subKeyName.Length - 2);
                    var providersToClearData = new List<KeyValuePair<string, bool>>();              // Do all the deleting after we have closed the keys, so that the delete will succeed. 
                    using (var subKey = regKey.OpenSubKey(subKeyName))
                    {
                        foreach (string valueName in subKey.GetValueNames())
                        {
                            int value;
                            if (valueName.StartsWith("ControllerData_Session_") && int.TryParse(valueName.Substring(23), out value))
                            {
                                if (value == m_SessionId)
                                {
                                    providersToClearData.Add(new KeyValuePair<string, bool>(providerGuid.ToString(), false));
                                    break;
                                }
                            }
                            // V4.5 style support.  Session can interfere with one another.   Remove eventually.  
                            else if (valueName == "ControllerData")
                            {
                                var infos = TraceEventProviders.SessionInfosForProvider(new Guid(providerGuid), 0);
                                bool aliveByAnotherSession = false;
                                if (infos != null)
                                {
                                    foreach (var info in infos)
                                    {
                                        if (info.LoggerId != m_SessionId)
                                        {
                                            aliveByAnotherSession = true;
                                        }
                                    }
                                }
                                if (!aliveByAnotherSession)
                                {
                                    providersToClearData.Add(new KeyValuePair<string, bool>(providerGuid.ToString(), true));
                                }
                            }
                        }
                    }

                    // Now that we have closed the enumeration handle, we can delete all the enties we have accumulated.  
                    foreach (var providerToClearData in providersToClearData)
                    {
                        SetFilterDataForEtwSession(providerToClearData.Key, null, providerToClearData.Value);
                    }
                }
            }
        }

        private string GetEventSourceRegistryBaseLocation()
        {
            if (System.Runtime.InteropServices.Marshal.SizeOf(typeof(IntPtr)) == 8)
            {
                return @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Winevt\Publishers";
            }
            else
            {
                return @"Software\Microsoft\Windows\CurrentVersion\Winevt\Publishers";
            }
        }

        /// <summary>
        /// SetDataForSession sets the filter data for an ETW session by storing it in the registry.
        /// This is basically a work-around for the fact that filter data does not get transmitted to
        /// the provider if the provider is not alive at the time the controller issues the EnableProvider 
        /// call.   We store in the registry and EventSource looks there for it if it is not present.  
        /// 
        /// Note that we support up to 'maxSession' etw sessions simultaneously active (having different 
        /// filter data).   The function return a sessionIndex that indicates which of the 'slots' 
        /// was used to store the data.   This routine also 'garbage collects' data for sessions that
        /// have died without cleaning up their filter data.  
        /// 
        /// If 'data' is null, then it indicates that no data should be stored and the registry entry
        /// is removed.
        /// 
        /// If 'allSesions' is true it means that you want 'old style' data filtering that affects all ETW sessions
        /// This is present only used for compatibilty 
        /// </summary>
        /// <returns>the session index that will be used for this session.  Returns -1 if an entry could not be found </returns>
        private void SetFilterDataForEtwSession(string providerGuid, byte[] data, bool V4_5EventSource = false)
        {
            string baseKeyName = GetEventSourceRegistryBaseLocation();
            string providerKeyName = "{" + providerGuid + "}";
            string regKeyName = baseKeyName + "\\" + providerKeyName;
            string valueName;
            if (!V4_5EventSource)
            {
                valueName = "ControllerData_Session_" + m_SessionId.ToString();
            }
            else
            {
                valueName = "ControllerData";
            }

            if (data != null)
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_LOCAL_MACHINE\" + regKeyName, valueName, data, RegistryValueKind.Binary);
            }
            else
            {
                // if data == null, Delete the value 
                bool deleteProviderKey = false;
                using (var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regKeyName, true))
                {
                    if (regKey != null)
                    {
                        regKey.DeleteValue(valueName, false);
                        if (regKey.GetValueNames().Length == 0)
                        {
                            deleteProviderKey = true;
                        }
                    }

                    // Hygene: if the provider has no values in it we can delete the key.
                    if (deleteProviderKey)
                    {
                        using (var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(baseKeyName, true))
                        {
                            // Try to delete the provider key too, but don't try too hard.  It is possible a race will prevent it from being deleted, and that is OK
                            try { baseKey.DeleteSubKey(providerKeyName); }
                            catch (Exception) { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Given a mask of kernel flags, set the array stackTracingIds of size stackTracingIdsMax to match.
        /// It returns the number of entries in stackTracingIds that were filled in.
        /// </summary>
        private static unsafe int SetStackTraceIds(KernelTraceEventParser.Keywords stackCapture, STACK_TRACING_EVENT_ID* stackTracingIds, int stackTracingIdsMax)
        {
            int curID = 0;

            // PerfInfo (sample profiling)
            if ((stackCapture & KernelTraceEventParser.Keywords.Profile) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PerfInfoTaskGuid;
                stackTracingIds[curID].Type = 0x2e;     // Sample Profile
                curID++;
            }

            // PCM sample profiling
            if ((stackCapture & KernelTraceEventParser.Keywords.PMCProfile) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PerfInfoTaskGuid;
                stackTracingIds[curID].Type = 0x2f;     // PMC Sample Profile
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.SystemCall) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PerfInfoTaskGuid;
                // stackTracingIds[curID].Type = 0x33;     // SysCallEnter
                stackTracingIds[curID].Type = 0x34;     // SysCallExit  (We want the stack on the exit as it has the return value).  
                curID++;
            }
            // Thread
            if ((stackCapture & KernelTraceEventParser.Keywords.Thread) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x01;     // Thread Create
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.ContextSwitch) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x24;     // Context Switch
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.ThreadPriority) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x30;     // Set Priority
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x31;     // Set Base Priority
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.Dispatcher) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x32;     // Ready Thread
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.IOQueue) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x3e;     // #define PERFINFO_LOG_TYPE_KQUEUE_ENQUEUE            (EVENT_TRACE_GROUP_THREAD | 0x3E) 
                curID++;
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x3f;     // #define PERFINFO_LOG_TYPE_KQUEUE_DEQUEUE            (EVENT_TRACE_GROUP_THREAD | 0x3F) 
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.Handle) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ObjectTaskGuid;
                stackTracingIds[curID].Type = 0x20;     // PERFINFO_LOG_TYPE_CREATE_HANDLE                (EVENT_TRACE_GROUP_OBJECT | 0x20)  
                curID++;
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ObjectTaskGuid;
                stackTracingIds[curID].Type = 0x21;     // PERFINFO_LOG_TYPE_CLOSE_HANDLE                 (EVENT_TRACE_GROUP_OBJECT | 0x21)   
                curID++;
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ObjectTaskGuid;
                stackTracingIds[curID].Type = 0x22;     // PERFINFO_LOG_TYPE_DUPLICATE_HANDLE             (EVENT_TRACE_GROUP_OBJECT | 0x22)   
                curID++;
            }

            // Image
            if ((stackCapture & KernelTraceEventParser.Keywords.ImageLoad) != 0)
            {
                // Confirm this is not ImageTaskGuid
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ProcessTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // EVENT_TRACE_TYPE_LOAD (Image Load)
                curID++;
            }

            // Process
            if ((stackCapture & KernelTraceEventParser.Keywords.Process) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ProcessTaskGuid;
                stackTracingIds[curID].Type = 0x01;        // Process Create
                stackTracingIds[curID].Type = 0x0B;        // EVENT_TRACE_TYPE_TERMINATE   
                curID++;
            }

            // Disk
            if ((stackCapture & KernelTraceEventParser.Keywords.DiskIOInit) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIOTaskGuid;
                stackTracingIds[curID].Type = 0x0c;     // Read Init
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIOTaskGuid;
                stackTracingIds[curID].Type = 0x0d;     // Write Init
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIOTaskGuid;
                stackTracingIds[curID].Type = 0x0f;     // Flush Init
                curID++;
            }

            // Virtual Alloc
            if ((stackCapture & (KernelTraceEventParser.Keywords.VirtualAlloc | KernelTraceEventParser.Keywords.ReferenceSet)) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.VirtualAllocTaskGuid;
                stackTracingIds[curID].Type = 0x62;     // Flush Init
                curID++;
            }

            // VAMap 
            if ((stackCapture & KernelTraceEventParser.Keywords.VAMap) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x25;
                curID++;
            }

            // Hard Faults
            if ((stackCapture & KernelTraceEventParser.Keywords.MemoryHardFaults) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x20;     // Hard Fault
                curID++;
            }

            // Page Faults 
            if ((stackCapture & KernelTraceEventParser.Keywords.Memory) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // Transition Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0B;     // Demand zero Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0C;     // Copy on Write Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0D;     // Guard Page Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0E;     // Hard Page Fault
                curID++;

                // Unconditionally turn on stack capture for Access Violations.  
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x0F;     // (access Violation) EVENT_TRACE_TYPE_MM_AV
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.ReferenceSet) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x49;     //  PERFINFO_LOG_TYPE_PFMAPPED_SECTION_CREATE 
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x4F;     // PERFINFO_LOG_TYPE_PFMAPPED_SECTION_DELETE
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x76;     // PERFINFO_LOG_TYPE_PAGE_ACCESS 
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x77;     // PERFINFO_LOG_TYPE_PAGE_RELEASE
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x78;     // PERFINFO_LOG_TYPE_PAGE_RANGE_ACCESS 
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x79;     // PERFINFO_LOG_TYPE_PAGE_RANGE_RELEASE 
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x82;     // PERFINFO_LOG_TYPE_PAGE_ACCESS_EX 
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.MemoryTaskGuid;
                stackTracingIds[curID].Type = 0x83;     // PERFINFO_LOG_TYPE_REMOVEFROMWS 
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.FileIOInit) != 0)
            {
                // TODO allow stacks only on open and close;
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x40;     // Create
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x41;     // Cleanup
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x42;     // Close
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x43;     // Read
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIOTaskGuid;
                stackTracingIds[curID].Type = 0x44;     // Write
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.Registry) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // NtCreateKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0B;     // NtOpenKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0C;     // NtDeleteKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0D;     // NtQueryKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0E;     // NtSetValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0F;     // NtDeleteValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x10;     // NtQueryValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x11;     // NtEnumerateKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x12;     // NtEnumerateValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x13;     // NtQueryMultipleValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x14;     // NtSetInformationKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x15;     // NtFlushKey
                curID++;

                // TODO What are these?  
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x16;     // KcbCreate
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x17;     // KcbDelete
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x1A;     // VirtualizeKey
                curID++;
            }

            // ALPC
            if ((stackCapture & KernelTraceEventParser.Keywords.AdvancedLocalProcedureCalls) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ALPCTaskGuid;
                stackTracingIds[curID].Type = 33;  // send message   
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ALPCTaskGuid;
                stackTracingIds[curID].Type = 34;  // receive message   
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ALPCTaskGuid;
                stackTracingIds[curID].Type = 35;  // wait for reply
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ALPCTaskGuid;
                stackTracingIds[curID].Type = 36;  // wait for new message
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ALPCTaskGuid;
                stackTracingIds[curID].Type = 37;  // unwait
                curID++;
            }

            // Confirm we did not overflow.  
            Debug.Assert(curID <= stackTracingIdsMax);
            return curID;
        }
        private void EnsureStarted(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES* properties = null)
        {
            if (!m_Create)
            {
                throw new NotSupportedException("Can not enable providers on opened with TraceEventSessionOptions.Attach.");
            }

            // Already initialized, nothing to do.  
            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
            {
                return;
            }

            var propertiesBuff = stackalloc byte[PropertiesSize];
            if (properties == null)
            {
                properties = GetProperties(propertiesBuff);
            }

            int retCode = TraceEventNativeMethods.StartTraceW(out m_SessionHandle, m_SessionName, properties);
            if (retCode == 0xB7 && m_ResartIfExist)      // STIERR_HANDLEEXISTS
            {
                m_restarted = true;
                Stop();
                m_Stopped = false;
                Thread.Sleep(100);  // Give it some time to stop. 
                retCode = TraceEventNativeMethods.StartTraceW(out m_SessionHandle, m_SessionName, properties);
            }
            if (retCode == 5 && OperatingSystemVersion.AtLeast(51))     // On Vista and we get a 'Accessed Denied' message
            {
                m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE; // StartTrace sets to 0 on failure.  We use INVALID_HANDLE_VALUE to represent failure
                throw new UnauthorizedAccessException("Error Starting ETW:  Access Denied (Administrator rights required to start ETW)");
            }

            if (retCode != 0)
            {
                m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;  // StartTrace sets to 0 on failure.  We use INVALID_HANDLE_VALUE to represent failure
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(retCode));
            }

            m_SessionId = (int)properties->Wnode.HistoricalContext;              // Set the ID
        }

        /// <summary>
        /// Get a EVENT_TRACE_PROPERTIES structure suitable for passing the the ETW out of a 'buffer' which must be PropertiesSize bytes
        /// in size.
        /// </summary>
        private TraceEventNativeMethods.EVENT_TRACE_PROPERTIES* GetProperties(byte* buffer)
        {
            Marshal.Copy(PropertiesMemoryInitializer, 0, (IntPtr)buffer, PropertiesSize);
            var properties = (TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*)buffer;

            properties->LoggerNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES);
            properties->LogFileNameOffset = properties->LoggerNameOffset + MaxNameSize * sizeof(char);

            // Copy in the session name
            if (m_SessionName.Length > MaxNameSize - 1)
            {
                throw new ArgumentException("File name too long", "sessionName");
            }

            char* sessionNamePtr = (char*)(((byte*)properties) + properties->LoggerNameOffset);
            CopyStringToPtr(sessionNamePtr, m_SessionName);

            properties->Wnode.BufferSize = (uint)PropertiesSize;
            properties->Wnode.Flags = TraceEventNativeMethods.WNODE_FLAG_TRACED_GUID;
            properties->FlushTimer = 60;                // flush every minute for file based collection.  

            Debug.Assert(m_BufferQuantumKB != 0);
            properties->BufferSize = (uint)m_BufferQuantumKB;
            properties->MinimumBuffers = (uint)(m_BufferSizeMB * 1024 / m_BufferQuantumKB);
            properties->LogFileMode = TraceEventNativeMethods.EVENT_TRACE_INDEPENDENT_SESSION_MODE;

            properties->LogFileMode = TraceEventNativeMethods.EVENT_TRACE_INDEPENDENT_SESSION_MODE;
            if (m_FileName == null)
            {
                properties->FlushTimer = 1;              // flush every second (as fast as possible) for real time. 
                if (m_CircularBufferMB == 0)
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE;
                }
                else
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_BUFFERING_MODE;
                    properties->MinimumBuffers = (uint)(m_CircularBufferMB * 1024 / m_BufferQuantumKB);
                    properties->BufferSize = (uint)m_CircularBufferMB;
                }
                properties->LogFileNameOffset = 0;
            }
            else
            {
                if (m_CircularBufferMB != 0)
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_CIRCULAR;
                    properties->MaximumFileSize = (uint)m_CircularBufferMB;
                }
                else if (m_MultiFileMB != 0)
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_NEWFILE;
                    properties->MaximumFileSize = (uint)m_MultiFileMB;
                }
                else
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_SEQUENTIAL;
                }

                if (m_FileName.Length > MaxNameSize - 1)
                {
                    throw new ArgumentException("File name too long", "fileName");
                }

                char* fileNamePtr = (char*)(((byte*)properties) + properties->LogFileNameOffset);
                CopyStringToPtr(fileNamePtr, m_FileName);
            }

            properties->MaximumBuffers = properties->MinimumBuffers * 5 / 4 + 10;

            properties->Wnode.ClientContext = 1;    // set Timer resolution to 100ns.

            if (m_NoPerProcessBuffering)
            {
                properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_NO_PER_PROCESSOR_BUFFERING;
            }
            return properties;
        }

        private static unsafe void CopyStringToPtr(char* toPtr, string str)
        {
            fixed (char* fromPtr = str)
            {
                int i = 0;
                while (i < str.Length)
                {
                    toPtr[i] = fromPtr[i];
                    i++;
                }
                toPtr[i] = '\0';   // Null terminate
            }
        }

        internal const int MaxNameSize = 1024;
        private const int MaxExtensionSize = 256;
        private static readonly int PropertiesSize = sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) + 2 * MaxNameSize * sizeof(char) + MaxExtensionSize;
        private static readonly byte[] PropertiesMemoryInitializer = new byte[PropertiesSize];

        // Data that is exposed through properties.  
        private string m_SessionName;             // Session name (identifies it uniquely on the machine)
        private int m_SessionId;                  // This is a small integer representing the session (only unique while session alive)
        private string m_FileName;                // Where to log (null means real time session)
        private int m_BufferSizeMB;
        private int m_BufferQuantumKB;
        private int m_CircularBufferMB;
        private int m_MultiFileMB;

        private float m_CpuSampleIntervalMSec;
        private bool m_StackCompression;
        private bool m_restarted;

        // Internal state
        private bool m_Create;                    // Should create if it does not exist.
        private bool m_ResartIfExist;             // Try to restart if it exists
        private bool m_NoPerProcessBuffering;     // Don't use per-processor buffers.  Use a single buffer.
        private bool m_IsActive;                  // Session is active (InsureSession has been called)
        private bool m_Stopped;                   // The Stop() method was called (avoids reentrant)
        private bool m_StopOnDispose;             // Should we Stop() when the object is destroyed?
        private ulong m_SessionHandle;            // OS handle
        private ETWTraceEventSource m_source;     // Sessions can have a source associated with them. 

        internal TraceEventSession m_kernelSession; // Only needed in Windows 7.   Before windows 8 you could not enable Kernel
        // events on 'normal' user mode session.  This tried to 'fake' Win 8 behavior
        // on Win 7.   We only do this for real time sessions that are using TraceLog.  
        internal bool m_associatedWithTraceLog;     // Currently we only allow m_kernelSession to be used if you are using TraceLog on the session. 

        // TODO: we track the enabled providers but it is better to get it from the 
        // by asking the OS (using EnumerateTraceGuidsEx), but that is a bit of a pain.  
        // When we do that, we don't need this.  
        private readonly Dictionary<Guid, ulong> m_enabledProviders = new Dictionary<Guid, ulong>();

        #endregion
    }

    /// <summary>
    /// Used in the TraceEventSession.Merge method 
    /// </summary>
    public enum TraceEventMergeOptions
    {
        /// <summary>
        /// No special options 
        /// </summary>
        None = 0,
        /// <summary>
        /// Compress the resulting file.  
        /// </summary>
        Compress = 1,
        /// <summary>
        /// Only perform image ID injection.
        /// </summary>
        ImageIDsOnly = 2,
    }

    /// <summary>
    /// TraceEventProviderOptions represents all the optional arguments that can be passed to EnableProvider command.   
    /// </summary>
    public class TraceEventProviderOptions
    {
        /// <summary>
        /// Create new options object with no options set
        /// </summary>
        public TraceEventProviderOptions() { }
        /// <summary>
        /// Create new options object with a set of given provider arguments key-value pairs.  There must be a even number
        /// of strings provided and each pair forms a key-value pair that is passed to the AddArgument() operator.   
        /// </summary>
        public TraceEventProviderOptions(params string[] keyValuePairs)
        {
            for (int i = 1; i < keyValuePairs.Length; i += 2)
            {
                AddArgument(keyValuePairs[i - 1], keyValuePairs[i]);
            }
        }
        /// <summary>
        /// Arguments are a set of key-value strings that are passed uninterpreted to the EventSource.   These can be accessed
        /// from the EventSource's command callback.  
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> Arguments { get; set; }
        /// <summary>
        /// As a convenience, the 'Arguments' property can be modified by calling AddArgument that adds another Key-Value pair
        /// to it.   If 'Arguments' is not a IDictionary, it is replaced with an IDictionary with the same key-value pairs before
        /// the new pair is added.  
        /// </summary>
        public void AddArgument(string key, string value)
        {
            var asIDictionary = (IDictionary<string, string>)Arguments;
            if (asIDictionary == null)
            {
                asIDictionary = new Dictionary<string, string>();
                if (Arguments != null)
                {
                    foreach (var keyValue in Arguments)
                    {
                        asIDictionary.Add(keyValue.Key, keyValue.Value);
                    }
                }
                Arguments = asIDictionary;
            }
            asIDictionary.Add(key, value);
        }
        /// <summary>
        /// For EventSources, you pass arguments to the EventSource by using key value pairs (this 'Arguments' property). 
        /// However other ETW providers may expect arguments using another convention.  RawArguments give a way of passing
        /// raw bytes to the provider as arguments.   This is only meant for compatibility with old providers.   Setting
        /// this property will cause the 'Arguments' property to be ignored.   
        /// </summary>
        public byte[] RawArguments { get; set; }
        /// <summary>
        /// Setting StackEnabled to true will cause all events in the provider to collect stacks when events are fired. 
        /// </summary>
        public bool StacksEnabled { get; set; }
        /// <summary>
        /// Setting ProcessIDFilter will limit the providers that receive the EnableCommand to those that match one of
        /// the given Process IDs.  
        /// </summary>
        public IList<int> ProcessIDFilter { get; set; }
        /// <summary>
        /// Setting ProcessNameFilter will limit the providers that receive the EnableCommand to those that match one of
        /// the given Process names (a process name is the name of the EXE without the PATH but WITH the extension).  
        /// </summary>
        public IList<string> ProcessNameFilter { get; set; }
        /// <summary>
        /// Setting EventIDs to Enable will enable a particular event of a provider by EventID (in addition to those 
        /// enabled by keywords). 
        /// </summary>
        public IList<int> EventIDsToEnable { get; set; }
        /// <summary>
        /// Setting EventIDs to Enable will enable the collection of stacks for an event of a provider by EventID 
        /// (Has no effect if StacksEnabled is also set since that enable stacks for all events IDs)
        /// </summary>
        public IList<int> EventIDStacksToEnable { get; set; }
        /// <summary>
        /// Setting EventIDsToDisable to Enable will disable the event of a provider by EventID 
        /// This happens after keywords have been processed, so disabling overrides enabling.   
        /// </summary>
        public IList<int> EventIDsToDisable { get; set; }
        /// <summary>
        /// Setting EventIDs to Enable will disable the collection of stacks for an event of a provider by EventID 
        /// Has no effect unless StacksEnabled is also set (since otherwise stack collection is off).   
        /// </summary>
        public IList<int> EventIDStacksToDisable { get; set; }
        /// <summary>
        /// Setting this to true will cause this provider to be enabled inside of any silos (containers) running on the machine.
        /// </summary>
        public bool EnableInContainers { get; set; }
        /// <summary>
        /// Setting this to true will cause all events emitted inside of a container to contain the container ID in its payload.
        /// Has no effect if <code>EnableInContainers == false</code>.
        /// </summary>
        public bool EnableSourceContainerTracking { get; set; }

        /// <summary>
        /// Make a deep copy of options and return it.  
        /// </summary>
        /// <returns></returns>
        public TraceEventProviderOptions Clone()
        {
            var ret = new TraceEventProviderOptions();
            if (Arguments != null)
            {
                ret.Arguments = new Dictionary<string, string>();
                foreach (var keyValue in Arguments)
                {
                    ret.AddArgument(keyValue.Key, keyValue.Value);
                }
            }
            if (RawArguments != null)
            {
                ret.RawArguments = new byte[RawArguments.Length];
                Array.Copy(RawArguments, ret.RawArguments, RawArguments.Length);
            }
            if (StacksEnabled)
            {
                ret.StacksEnabled = true;
            }

            if (ProcessIDFilter != null)
            {
                ret.ProcessIDFilter = new List<int>(ProcessIDFilter);
            }

            if (ProcessNameFilter != null)
            {
                ret.ProcessNameFilter = new List<string>(ProcessNameFilter);
            }

            if (EventIDsToEnable != null)
            {
                ret.EventIDsToEnable = new List<int>(EventIDsToEnable);
            }

            if (EventIDStacksToEnable != null)
            {
                ret.EventIDStacksToEnable = new List<int>(EventIDStacksToEnable);
            }

            if (EventIDsToDisable != null)
            {
                ret.EventIDsToDisable = new List<int>(EventIDsToDisable);
            }

            if (EventIDStacksToDisable != null)
            {
                ret.EventIDStacksToDisable = new List<int>(EventIDStacksToDisable);
            }
            if(EnableInContainers)
            {
                ret.EnableInContainers = true;
            }
            if(EnableSourceContainerTracking)
            {
                ret.EnableSourceContainerTracking = true;
            }

            return ret;
        }
        // Payload Filters not implemented yet.  

        /// <summary>
        /// This return true on OS version beyond 8.1 (windows Version 6.3).   It means most of the
        /// per-event filtering is supported.  
        /// </summary>
        public static bool FilteringSupported
        {
            get
            {
                if (!s_IsEtwFilteringSupported.HasValue)
                {
                    var ret = false;

                    // For Windows Versions above windows 8, OSVersion lies and returns 6.2 (window 8) even though
                    // the windows version is higher.  We have to try harder to figure out whether we are windows 8 or something
                    // later.   Currently we look at the file version number of an OS DLL.  
                    // There is probably a better way.   
                    var winDir = Environment.GetEnvironmentVariable("WinDir");
                    var kernel32 = Path.Combine(winDir, @"system32\Kernel32.dll");
                    if (File.Exists(kernel32))
                    {
                        using (var kernel32PE = new PEFile.PEFile(kernel32))
                        {
                            var versionInfo = kernel32PE.GetFileVersionInfo();
                            if (versionInfo != null)
                            {
                                // versionInfo.FileVersion is now the real version number we want but it is a string, not a 
                                // number.   Our tests is if version number bigger than 6.3 (as a string) or a two or more digit 
                                // major version.   
                                if (string.Compare("6.3", versionInfo.FileVersion) <= 0 || 2 <= versionInfo.FileVersion.IndexOf('.'))
                                {
                                    ret = true;
                                }
                            }
                        }
                    }
                    s_IsEtwFilteringSupported = ret;
                }
                return s_IsEtwFilteringSupported.Value;
            }
        }

        /// <summary>
        /// This is the backing field for the lazily-computed <see cref="FilteringSupported"/> property.
        /// </summary>
        private static bool? s_IsEtwFilteringSupported;
    }


    /// <summary>
    /// TraceEventSessionOptions indicates special handling when creating a TraceEventSession.
    /// </summary>
    [Flags]
    public enum TraceEventSessionOptions
    {
        /// <summary>
        /// Create a new session, stop and recreated it if it already exists.  This is the default.  
        /// </summary>
        Create = 1,
        /// <summary>
        /// Attach to an existing session, fail if the session does NOT already exist.  
        /// </summary>
        Attach = 2,
        /// <summary>
        /// Normally if you create a session it will stop and restart it if it exists already.  Setting
        /// this flat will disable the 'stop and restart' behavior.   This is useful if only a single
        /// monitoring process is intended. 
        /// </summary>
        NoRestartOnCreate = 4,
        /// <summary>
        /// Write events that were logged on different processors to a common buffer.  This is useful when
        /// it is important to capture the events in the order in which they were logged.  This is not recommended
        /// for sessions that expect more than 1K events per second.
        /// </summary>
        NoPerProcessorBuffering = 8,
    }

    /// <summary>
    /// TraceEventProviders returns information about providers on the system.  
    /// </summary>
    public static class TraceEventProviders
    {
        /// <summary>
        /// Given the friendly name of a provider (e.g. Microsoft-Windows-DotNETRuntimeStress) return the
        /// GUID for the provider.   It does this by looking at all the PUBLISHED providers on the system
        /// (that is those registered with wevtutuil).   EventSources in particular do not register themselves
        /// in this way (see GetEventSourceGuidFromName).  Names are case insensitive.   
        /// It also checks to see if the name is an actual GUID and if so returns that.  
        /// Returns Guid.Empty on failure.   
        /// </summary>
        public static Guid GetProviderGuidByName(string name)
        {
            Guid ret;
            TraceEventSession.ProviderNameToGuid.TryGetValue(name, out ret);

            // See if it a GUID itself.  
#if !DOTNET_V35
            if (ret == Guid.Empty)
            {
                Guid.TryParse(name, out ret);
            }
#endif
            return ret;
        }
        /// <summary>
        /// EventSources have a convention for converting its name to a GUID.  Use this convention to 
        /// convert 'name' to a GUID.   In this way you can get the provider GUID for a EventSource
        /// however it can't check for misspellings.   Names are case insensitive.  
        /// </summary>
        public static Guid GetEventSourceGuidFromName(string name)
        {
            if (name.StartsWith("*"))
            {
                name = name.Substring(1);       // Remove *, which was a common marker that it is an EventSource. 
            }

            name = name.ToUpperInvariant();     // names are case insensitive.  

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

            Debug.Assert(TraceEventProviders.MaybeAnEventSource(guid));
            return guid;
        }
        /// <summary>
        /// Finds the friendly name for 'providerGuid'  Returns the Guid as a string if can't be found.
        /// </summary>
        public static string GetProviderName(Guid providerGuid)
        {
            string ret;
            TraceEventSession.ProviderGuidToName.TryGetValue(providerGuid, out ret);
            if (ret == null)
            {
                ret = providerGuid.ToString();
            }

            return ret;
        }

        /// <summary>
        /// Returns true if 'providerGuid' can be an eventSource.   If it says true, there is a 1/16 chance it is not.  
        /// However if it returns false, it is definitely not following EventSource Guid generation conventions.
        /// </summary>
        public static unsafe bool MaybeAnEventSource(Guid providerGuid)
        {
            byte octet7 = ((byte*)(&providerGuid))[7];
            if ((octet7 & 0xF0) == 0x50)
            {
                return true;
            }
            // FrameworkEventSource predated the Guid selection convention that most eventSources use.  
            // Opt it in explicitly 
            if (providerGuid == FrameworkEventSourceTraceEventParser.ProviderGuid)
            {
                return true;
            }

            return false;
        }

        // Enumerating PUBLISHED providers (that is providers with manifests registered with wevtutil) 
        /// <summary>
        /// Returns the Guid of every event provider that published its manifest on the machine.  This is the 
        /// same list that the 'logman query providers' command will generate.  It is pretty long (&gt; 1000 entries)
        /// <para>
        /// A event provider publishes a manifest by compiling its manifest into a special binary form and calling
        /// the wevtutil utility.   Typically EventSource do NOT publish their manifest but most operating
        /// system provider do publish their manifest.   
        /// </para>
        /// </summary>  
        public static IEnumerable<Guid> GetPublishedProviders()
        {
            return TraceEventSession.ProviderGuidToName.Keys;
        }

        /// <summary>
        /// Returns the GUID of all event provider that either has registered itself in a running process (that is
        /// it CAN be enabled) or that a session has enabled (even if no instances of the provider exist in any process).  
        /// <para>
        /// This is a relatively small list (less than 1000), unlike GetPublishedProviders. 
        /// </para>
        /// </summary>
        public static unsafe List<Guid> GetRegisteredOrEnabledProviders()
        {
            // See what process it is in.  
            int buffSize = 0;
            var hr = TraceEventNativeMethods.EnumerateTraceGuidsEx(TraceEventNativeMethods.TRACE_QUERY_INFO_CLASS.TraceGuidQueryList,
                null, 0, null, 0, ref buffSize);
            if (hr != 122 && hr != 0)
            {
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            }

            var buffer = stackalloc byte[buffSize];
            hr = TraceEventNativeMethods.EnumerateTraceGuidsEx(TraceEventNativeMethods.TRACE_QUERY_INFO_CLASS.TraceGuidQueryList,
                null, 0, buffer, buffSize, ref buffSize);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            }

            Guid* asGuids = (Guid*)buffer;
            int guidCount = buffSize / sizeof(Guid);
            List<Guid> ret = new List<Guid>(guidCount);
            for (int i = 0; i < guidCount; i++)
            {
                ret.Add(asGuids[i]);
            }

            return ret;
        }

        /// <summary>
        /// Returns a list of provider GUIDs that are registered in a process with 'processID'.   Useful for discovering
        /// what providers are available for enabling for a particular process.  
        /// </summary>
        public static List<Guid> GetRegisteredProvidersInProcess(int processID)
        {
            var ret = new List<Guid>();
            var registeredProviders = GetRegisteredOrEnabledProviders();
            foreach (var guid in registeredProviders)
            {
                var infos = SessionInfosForProvider(guid, processID);
                if (infos != null && infos.Count > 0)
                {
                    ret.Add(guid);
                }
            }
            return ret;
        }

        /// <summary>
        /// Returns a description of the keywords a particular provider provides.  Only works if the provider has 
        /// published its manifest to the operating system.  
        /// Throws an exception if providerGuid is not found
        /// </summary>
        public static List<ProviderDataItem> GetProviderKeywords(Guid providerGuid)
        {
            return GetProviderFields(providerGuid, TraceEventNativeMethods.EVENT_FIELD_TYPE.EventKeywordInformation);
        }

        #region private

        /// <summary>
        /// Returns a list of TRACE_ENABLE_INFO structures that tell about each session (what keywords and level they are
        /// set to, for the provider associated with 'providerGuid'.  If 'processId != 0, then only providers in that process
        /// are returned.  
        /// </summary>
        internal static unsafe List<TraceEventNativeMethods.TRACE_ENABLE_INFO> SessionInfosForProvider(Guid providerGuid, int processId)
        {
            int buffSize = 256;     // An initial guess that probably works most of the time.  
            byte* buffer;
            for (; ; )
            {
                var space = stackalloc byte[buffSize];
                buffer = space;
                var hr = TraceEventNativeMethods.EnumerateTraceGuidsEx(TraceEventNativeMethods.TRACE_QUERY_INFO_CLASS.TraceGuidQueryInfo,
                    &providerGuid, sizeof(Guid), buffer, buffSize, ref buffSize);
                if (hr == 0)
                {
                    break;
                }

                if (hr != 122)      // Error 122 means buffer not big enough.   For that one retry, everything else simply fail.  
                {
                    return null;
                }
            }

            var ret = new List<TraceEventNativeMethods.TRACE_ENABLE_INFO>();
            var providerInfos = (TraceEventNativeMethods.TRACE_GUID_INFO*)buffer;
            var providerInstance = (TraceEventNativeMethods.TRACE_PROVIDER_INSTANCE_INFO*)&providerInfos[1];
            for (int i = 0; i < providerInfos->InstanceCount; i++)
            {
                if (processId == 0 || providerInstance->Pid == processId)
                {
                    var enableInfos = (TraceEventNativeMethods.TRACE_ENABLE_INFO*)&providerInstance[1];
                    for (int j = 0; j < providerInstance->EnableCount; j++)
                    {
                        ret.Add(enableInfos[j]);
                    }
                }
                if (providerInstance->NextOffset == 0)
                {
                    break;
                }

                Debug.Assert(0 <= providerInstance->NextOffset && providerInstance->NextOffset < buffSize);
                var structBase = (byte*)providerInstance;
                providerInstance = (TraceEventNativeMethods.TRACE_PROVIDER_INSTANCE_INFO*)&structBase[providerInstance->NextOffset];
            }
            return ret;
        }

        private static unsafe List<ProviderDataItem> GetProviderFields(Guid providerGuid, TraceEventNativeMethods.EVENT_FIELD_TYPE fieldType)
        {
            var ret = new List<ProviderDataItem>();

            int buffSize = 0;
            var hr = TraceEventNativeMethods.TdhEnumerateProviderFieldInformation(ref providerGuid, fieldType, null, ref buffSize);
            if (hr != 122)
            {
                return ret;     // TODO FIX NOW Do I want to simply return nothing or give a more explicit error? 
            }

            Debug.Assert(hr == 122);     // ERROR_INSUFFICIENT_BUFFER 

            var buffer = stackalloc byte[buffSize];
            var fieldsDesc = (TraceEventNativeMethods.PROVIDER_FIELD_INFOARRAY*)buffer;
            hr = TraceEventNativeMethods.TdhEnumerateProviderFieldInformation(ref providerGuid, fieldType, fieldsDesc, ref buffSize);
            if (hr != 0)
            {
                throw new InvalidOperationException("Provider with ID " + providerGuid.ToString() + " not found.");
            }

            var fields = (TraceEventNativeMethods.PROVIDER_FIELD_INFO*)&fieldsDesc[1];
            for (int i = 0; i < fieldsDesc->NumberOfElements; i++)
            {
                var field = new ProviderDataItem();
                field.Name = new string((char*)&buffer[fields[i].NameOffset]);
                field.Description = new string((char*)&buffer[fields[i].DescriptionOffset]);
                field.Value = fields[i].Value;
                ret.Add(field);
            }

            return ret;
        }

        #endregion
    }

    /// <summary>
    /// A list of these is returned by GetProviderKeywords
    /// </summary>
    public struct ProviderDataItem
    {
        /// <summary>
        /// The name of the provider keyword. 
        /// </summary>
        public string Name;
        /// <summary>
        /// The description for the keyword for the provider 
        /// </summary>
        public string Description;
        /// <summary>
        /// the value (bitvector) for the keyword.
        /// </summary>
        public ulong Value;

        /// <summary>
        /// and XML representation for the ProviderDataItem (for debugging)
        /// </summary>
        public override string ToString()
        {
            return string.Format("<ProviderDataItem Name=\"{0}\" Description=\"{1}\" Value=\"0x{2:x}\"/>", Name, Description, Value);
        }
    }

    /// <summary>
    /// TraceEventProfileSources is the interface for the Windows processor CPU counter support
    /// (e.g. causing a stack to be taken every N dcache misses, or branch mispredicts etc)
    /// <para>
    /// Note that the interface to these is machine global (That is when you set these you 
    /// cause any session with the kernel PMCProfile keyword active to start emitting
    /// PMCCounterProf events for each ProfileSouce that is enabled.  
    /// </para>
    /// /// </summary>
    public static class TraceEventProfileSources
    {
        /// <summary>
        /// Returns a dictionary of keyed by name of ProfileSourceInfo structures for all the CPU counters available on the machine. 
        /// </summary>
        public static unsafe Dictionary<string, ProfileSourceInfo> GetInfo()
        {
            if (!OperatingSystemVersion.AtLeast(62))
            {
                throw new ApplicationException("Profile source only available on Win8 and beyond.");
            }

            var ret = new Dictionary<string, ProfileSourceInfo>(StringComparer.OrdinalIgnoreCase);

            // Figure out how much space we need.  
            int sourceListLen = 0;
            var result = TraceEventNativeMethods.TraceQueryInformation(0,
                TraceEventNativeMethods.TRACE_INFO_CLASS.TraceProfileSourceListInfo,
                null, 0, ref sourceListLen);
            Debug.Assert(sizeof(TraceEventNativeMethods.PROFILE_SOURCE_INFO) <= sourceListLen);     // Not enough space.  
            if (sourceListLen != 0)
            {
                // Do it for real.  
                byte* sourceListBuff = stackalloc byte[sourceListLen];
                result = TraceEventNativeMethods.TraceQueryInformation(0,
                    TraceEventNativeMethods.TRACE_INFO_CLASS.TraceProfileSourceListInfo,
                    sourceListBuff, sourceListLen, ref sourceListLen);

                if (result == 0)
                {
                    var interval = new TraceEventNativeMethods.TRACE_PROFILE_INTERVAL();
                    var profileSource = (TraceEventNativeMethods.PROFILE_SOURCE_INFO*)sourceListBuff;
                    for (; ; )
                    {
                        char* namePtr = (char*)&profileSource[1];       // points off the end of the array;

                        interval.Source = profileSource->Source;
                        interval.Interval = 0;
                        int intervalInfoLen = 0;
                        result = TraceEventNativeMethods.TraceQueryInformation(0,
                            TraceEventNativeMethods.TRACE_INFO_CLASS.TraceSampledProfileIntervalInfo,
                            &interval, sizeof(TraceEventNativeMethods.TRACE_PROFILE_INTERVAL), ref intervalInfoLen);
                        if (result != 0)
                        {
                            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(result));
                        }

                        var name = new string(namePtr);
                        ret.Add(name, new ProfileSourceInfo()
                        {
                            Name = name,
                            ID = profileSource->Source,
                            Interval = interval.Interval,
                            MinInterval = profileSource->MinInterval,
                            MaxInterval = profileSource->MaxInterval,
                        });
                        if (profileSource->NextEntryOffset == 0)
                        {
                            break;
                        }

                        var newProfileSource = (TraceEventNativeMethods.PROFILE_SOURCE_INFO*)(profileSource->NextEntryOffset + (byte*)profileSource);
                        Debug.Assert(profileSource < newProfileSource);
                        Debug.Assert((byte*)newProfileSource < &sourceListBuff[sourceListLen]);
                        profileSource = newProfileSource;
                    }
                }
                else
                {
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(result));
                }
            }
            return ret;
        }

        /// <summary>
        /// Sets a single Profile Source (CPU machine counters) that will be used if PMC (Precise Machine Counters)
        /// are turned on.   The profileSourceID is the ID field from the ProfileSourceInfo returned from 'GetInfo()'.
        /// and the profileSourceInterval is the interval between sampples (the number of events before a stack
        /// is recoreded.    If you need more that one (the OS allows up to 4 I think), use the variation of this
        /// routine that takes two int[].   Calling this will clear all Profiler sources previously set (it is NOT
        /// additive).  
        /// </summary>
        public static unsafe void Set(int profileSourceID, int profileSourceInterval)
        {
            var profileSourceIDs = new int[1] { profileSourceID };
            var profileSourceIntervals = new int[1] { profileSourceInterval };
            Set(profileSourceIDs, profileSourceIntervals);
        }

        /// <summary>
        /// Sets the Profile Sources (CPU machine counters) that will be used if PMC (Precise Machine Counters)
        /// are turned on.   Each CPU counter is given a id (the profileSourceID) and has an interval 
        /// (the number of counts you skip for each event you log).   You can get the human name for 
        /// all the supported CPU counters by calling GetProfileSourceInfo.  Then choose the ones you want
        /// and configure them here (the first array indicating the CPU counters to enable, and the second
        /// array indicating the interval.  The second array can be shorter then the first, in which case
        /// the existing interval is used (it persists and has a default on boot).  
        /// </summary>
        public static unsafe void Set(int[] profileSourceIDs, int[] profileSourceIntervals)
        {
            if (!OperatingSystemVersion.AtLeast(62))
            {
                throw new ApplicationException("Profile source only available on Win8 and beyond.");
            }

            TraceEventNativeMethods.SetPrivilege(TraceEventNativeMethods.SE_SYSTEM_PROFILE_PRIVILEGE);
            var interval = new TraceEventNativeMethods.TRACE_PROFILE_INTERVAL();
            for (int i = 0; i < profileSourceIntervals.Length; i++)
            {
                interval.Source = profileSourceIDs[i];
                interval.Interval = profileSourceIntervals[i];
                var result = TraceEventNativeMethods.TraceSetInformation(0,
                    TraceEventNativeMethods.TRACE_INFO_CLASS.TraceSampledProfileIntervalInfo,
                    &interval, sizeof(TraceEventNativeMethods.TRACE_PROFILE_INTERVAL));
                if (result != 0)
                {
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(result));
                }
            }

            fixed (int* sourcesPtr = profileSourceIDs)
            {
                var result = TraceEventNativeMethods.TraceSetInformation(0,
                    TraceEventNativeMethods.TRACE_INFO_CLASS.TraceProfileSourceConfigInfo,
                    sourcesPtr, profileSourceIDs.Length * sizeof(int));
                if (result != 0)
                {
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(result));
                }
            }
        }
    }

    /// <summary>
    /// Returned by GetProfileSourceInfo, describing the CPU counter (ProfileSource) available on the machine. 
    /// </summary>
    public class ProfileSourceInfo
    {
        /// <summary>
        /// Human readable name of the CPU performance counter (eg BranchInstructions, TotalIssues ...)
        /// </summary>
        public string Name;
        /// <summary>
        /// The ID that can be passed to SetProfileSources
        /// </summary>
        public int ID;
        /// <summary>
        /// This many events are skipped for each sample that is actually recorded
        /// </summary>
        public int Interval;
        /// <summary>
        /// The smallest Interval can be (typically 4K)
        /// </summary>
        public int MinInterval;
        /// <summary>
        /// The largest Interval can be (typically maxInt).
        /// </summary>
        public int MaxInterval;
    }

    /// <summary>
    /// These are options to EnableProvider
    /// </summary>
    [Flags, Obsolete("Use TraceEventArguments.Stacks instead")]
    public enum TraceEventOptions
    {
        /// <summary>
        /// No options
        /// </summary>
        None = 0,
        /// <summary>
        /// Take a stack trace with the event
        /// </summary>
        Stacks = 1,
    }
}

