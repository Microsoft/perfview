// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// using Microsoft.Diagnostics.Tracing.Parsers;
//#define HTTP_SERVICE_EVENTS
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.ApplicationServer;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using StartStopKey = System.Guid;   // The start-stop key is unique in the trace.  We incorperate the process as well as activity ID to achieve this.

// TODO this leaks if stops are missing.  
namespace Microsoft.Diagnostics.Tracing
{
    /// <summary>
    /// Calculates start-stop activities (computes duration),  It uses the 'standard' mechanism of using 
    /// ActivityIDs to corelate the start and stop (and any other events between the start and stop, 
    /// and use the RelatedActivityID on START events to indicate the creator of the activity, so you can
    /// form nested start-stop activities.   
    /// </summary>
    public unsafe class StartStopActivityComputer
    {
        /// <summary>
        /// Create a new ServerRequest Computer.
        /// </summary>
        public StartStopActivityComputer(TraceLogEventSource source, ActivityComputer taskComputer, bool ignoreApplicationInsightsRequestsWithRelatedActivityId = true)
        {
            m_ignoreApplicationInsightsRequestsWithRelatedActivityId = ignoreApplicationInsightsRequestsWithRelatedActivityId;
            taskComputer.NoCache = true;            // Can't cache start-stops (at the moment)
            m_source = source;
            m_activeStartStopActivities = new Dictionary<StartStopKey, StartStopActivity>();
            m_taskComputer = taskComputer;

            // Whenever a new Activity is created, propagate the start-stop activity from the creator
            // to the created task.  
            taskComputer.Create += delegate (TraceActivity activity, TraceEvent data)
            {
                TraceActivity creator = activity.Creator;
                if (creator == null)
                {
                    return;
                }

                StartStopActivity startStopActivity = m_traceActivityToStartStopActivity.Get((int)creator.Index);
                if (startStopActivity == null)
                {
                    return;
                }

                m_traceActivityToStartStopActivity.Set((int)activity.Index, startStopActivity);
            };

            // Only need to fix up V4.6 Windows-ASP activity ids.   It can be removed after we
            // don't care about V4.6 runtimes (since it is fixed in V4.6.2 and beyond). 
            // It basically remembers the related activity ID of the last RequestSend event on
            // each thread, which we use to fix the activity ID of the RequestStart event 
            KeyValuePair<Guid, Guid>[] threadToLastAspNetGuids = new KeyValuePair<Guid, Guid>[m_source.TraceLog.Threads.Count];
#if HTTP_SERVICE_EVENTS
            // Sadly, the Microsoft-Windows-HttpService HTTP_OPCODE_DELIVER event gets logged in
            // the kernel, so you don't know what process or thread is going to be get the request
            // We hack around this by looking for a nearby ReadyTHread or CSwitch event.  These
            // variables remember the information needed to transfer these to cswitch in the 
            // correct process.  
            // remembers the last deliver event. 
            string lastHttpServiceDeliverUrl = null;
            Guid lastHttpServiceDeliverActivityID = new Guid();
            int lastHttpServiceReceiveRequestThreadId = 0;
            int lastHttpServiceReceiveRequestTargetProcessId = 0;

            Dictionary<long, int> mapConnectionToTargetProcess = new Dictionary<long, int>();
#endif 
            var dynamicParser = source.Dynamic;
            dynamicParser.All += delegate (TraceEvent data)
            {
                // Special case IIS.  It does not use start and stop opcodes (Ugg), but otherwise 
                // follows normal start-stop activity ID conventions.   We also want it to work even
                // though it is not a EventSource.  
                if (data.ID <= (TraceEventID)2 && data.ProviderGuid == MicrosoftWindowsIISProvider)
                {
                    if (data.ID == (TraceEventID)1)
                    {
                        // TODO HACK.  We have seen IIS Start and stop events that only have a 
                        // context ID and no more.  They also seem to be some sort of nested event
                        // It really looks like a bug that they were emitted.  Ignore them. 
                        if (16 < data.EventDataLength)
                        {
                            string extraStartInfo = data.PayloadByName("RequestURL") as string;
                            OnStart(data, extraStartInfo, null, null, null, "IISRequest");
                        }
                    }
                    else if (data.ID == (TraceEventID)2)
                    {
                        // TODO HACK.  We have seen IIS Start and stop events that only have a 
                        // context ID and no more.  They also seem to be some sort of nested event
                        // It really looks like a bug that they were emitted.  Ignore them.
                        if (16 < data.EventDataLength)
                        {
                            OnStop(data);
                        }
                    }
                }
#if HTTP_SERVICE_EVENTS
                else if (data.Task == (TraceEventTask)1 && data.ProviderGuid == MicrosoftWindowsHttpService)
                {
                    if (data.ID == (TraceEventID)1)  // HTTP_TASK_REQUEST / HTTP_OPCODE_RECEIVE_REQUEST
                    {
                        Debug.Assert(data.EventName == "HTTP_TASK_REQUEST/HTTP_OPCODE_RECEIVE_REQUEST");
                        lastHttpServiceReceiveRequestTargetProcessId = 0;
                        lastHttpServiceReceiveRequestThreadId = data.ThreadID;
                        object connectionID = data.PayloadByName("ConnectionId");
                        if (connectionID != null && connectionID is long)
                            mapConnectionToTargetProcess.TryGetValue((long)connectionID, out lastHttpServiceReceiveRequestTargetProcessId);
                    }
                    else if (data.ID == (TraceEventID)3) // HTTP_TASK_REQUEST/HTTP_OPCODE_DELIVER 
                    {
                        Debug.Assert(data.EventName == "HTTP_TASK_REQUEST/HTTP_OPCODE_DELIVER");
                        if (lastHttpServiceReceiveRequestThreadId == data.ThreadID && lastHttpServiceReceiveRequestTargetProcessId != 0)
                        {   
                            lastHttpServiceDeliverUrl = data.PayloadByName("Url") as string;
                            lastHttpServiceDeliverActivityID = data.ActivityID;
                        }
                        else
                        {
                            lastHttpServiceDeliverUrl = null;
                            lastHttpServiceReceiveRequestTargetProcessId = 0;
                        }
                        lastHttpServiceReceiveRequestThreadId = 0;
                    }
                    else if (data.ID == (TraceEventID)12 || data.ID == (TraceEventID)8) // HTTP_TASK_REQUEST/HTTP_OPCODE_FAST_SEND  HTTP_TASK_REQUEST/HTTP_OPCODE_FAST_RESPONSE
                    {
                        if (data.ID == (TraceEventID)8)
                        {
                            object connectionID = data.PayloadByName("ConnectionId");
                            if (connectionID != null && connectionID is long)
                                mapConnectionToTargetProcess[(long)connectionID] = data.ProcessID;
                        }
                        Debug.Assert(data.ID != (TraceEventID)12 || data.EventName == "HTTP_TASK_REQUEST/HTTP_OPCODE_FAST_SEND");
                        Debug.Assert(data.ID != (TraceEventID)8 || data.EventName == "HTTP_TASK_REQUEST/HTTP_OPCODE_FAST_RESPONSE");
                        OnStop(data);
                    }
                }
#endif
                // TODO decide what the correct heuristic for deciding what start-stop events are interesting.  
                // Currently I only do this for things that might be an EventSource 
                if (!TraceEventProviders.MaybeAnEventSource(data.ProviderGuid))
                {
                    return;
                }

                // Try to filter out things quickly.   We really only care about start and stop events 
                // (except in special cases where the conventions were not followed and we fix them up). 
                if (data.Opcode != TraceEventOpcode.Start && data.Opcode != TraceEventOpcode.Stop)
                {
                    // In V4.6 the activity ID for Microsoft-Windows-ASPNET/Request/Start is improperly set, but we can fix it by 
                    // looking at the 'send' event that happens just before it.   Can be removed when V4.6 no longer deployed.  
                    // TODO remove (including threadToLastAspNetGuid) after 9/2016
                    if (data.Opcode == (TraceEventOpcode)9 && data.ProviderGuid == MicrosoftWindowsASPNetProvider)
                    {
                        TraceThread thread = data.Thread();
                        if (thread != null)
                        {
                            threadToLastAspNetGuids[(int)thread.ThreadIndex] = new KeyValuePair<Guid, Guid>(data.ActivityID, data.RelatedActivityID);
                        }
                    }
                    // These providers are weird in that they don't event do start and stop opcodes.  This is unfortunate.  
                    else if (data.Opcode == TraceEventOpcode.Info && data.ProviderGuid == AdoNetProvider)
                    {
                        FixAndProcessAdoNetEvents(data);
                    }

                    return;
                }

                // OK so now we only have EventSources with start and stop opcodes.
                // There are a few that don't follow conventions completely, and then we handle the 'normal' case 
                if (data.ProviderGuid == MicrosoftApplicationInsightsDataProvider)
                {
                    FixAndProcessAppInsightsEvents(data);
                }
                else if (data.ProviderGuid == FrameworkEventSourceTraceEventParser.ProviderGuid)
                {
                    FixAndProcessFrameworkEvents(data);
                }
                else if (data.ProviderGuid == MicrosoftWindowsASPNetProvider)
                {
                    FixAndProcessWindowsASP(data, threadToLastAspNetGuids);
                }
                else if (data.ProviderGuid == MicrosoftDiagnosticsActivityTrackingProvider)
                {
                    ProcessActivityTrackingProviderEvents(data);
                }
                else // Normal case EventSource Start-Stop events that follow proper conventions.  
                {
                    // We currently only handle Start-Stops that use the ActivityPath convention
                    // We could change this, but it is not clear what value it has to do that.  
                    Guid activityID = data.ActivityID;
                    if (StartStopActivityComputer.IsActivityPath(activityID, data.ProcessID))
                    {
                        if (data.Opcode == TraceEventOpcode.Start)
                        {
                            if (data.ProviderGuid == MicrosoftDiagnosticsDiagnosticSourceProvider)
                            {
                                // Inside the function, it will filter the events by 'EventName'. 
                                // It will only process "Microsoft.EntityFrameworkCore.BeforeExecuteCommand" and "Microsoft.AspNetCore.Hosting.BeginRequest".
                                if (TryProcessDiagnosticSourceStartEvents(data))
                                {
                                    return;
                                }
                            }

                            string extraStartInfo = null;
                            // Include the first argument in extraInfo if it is a string (e.g. a URL or other identifier).  
                            if (0 < data.PayloadNames.Length)
                            {
                                try { extraStartInfo = data.PayloadValue(0) as string; }
                                catch (Exception) { }
                                if (extraStartInfo != null)
                                {
                                    extraStartInfo = "/" + data.payloadNames[0] + "=" + extraStartInfo;
                                }
                            }
                            OnStart(data, extraStartInfo);
                        }
                        else
                        {
                            Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                            OnStop(data);
                        }
                    }
                    else
                    {
                        Trace.WriteLine("Skipping start at  " + data.TimeStampRelativeMSec.ToString("n3") + " name = " + data.EventName);
                    }
                }
            };

#if HTTP_SERVICE_EVENTS
#if TODO_FIX_NOW // FIX NOW Use or remove. 
            // We monitor ReadyThread to make HttpService events more useful (see nodes above on lastHttpServiceUrl)
            m_source.Kernel.DispatcherReadyThread += delegate (DispatcherReadyThreadTraceData data)
            {
                if (lastHttpServiceUrl == null)
                    return;

            };
#endif

            m_source.Kernel.ThreadCSwitch += delegate (CSwitchTraceData data)
            {
                // This code is to transfer information from the Microsoft-Windows-HttpService HTTP_TASK_REQUEST/HTTP_OPCODE_DELIVER
                // event (that happens in the System process, not the target, and move it to the context switch that wakes up
                // in order to service the event. 
                if (lastHttpServiceDeliverUrl == null)
                    return;
                if (data.ProcessID != lastHttpServiceReceiveRequestTargetProcessId)
                    return;
                // Test Stack

                Guid activityID = lastHttpServiceDeliverActivityID;
                OnStart(data, "url=" + lastHttpServiceDeliverUrl, &activityID, null, null, "HttpServiceRec");
                lastHttpServiceDeliverUrl = null;
                lastHttpServiceDeliverActivityID = Guid.Empty;
            };
#endif
            // Show the exception handling call stacks as a separate Activity.
            // This can help users notice the time spent in the exception handling logic.
            var clrExceptionParser = m_source.Clr;
            clrExceptionParser.ExceptionCatchStart += delegate (ExceptionHandlingTraceData data)
            {
                OnStart(data, data.MethodName, null, null, null, "ExceptionHandling");
            };
            clrExceptionParser.ExceptionCatchStop += delegate (EmptyTraceData data)
            {
                OnStop(data);
            };

            var aspNetParser = new AspNetTraceEventParser(m_source);
            aspNetParser.AspNetReqStart += delegate (AspNetStartTraceData data)
            {
                // if the related activity is not present, try using the context ID as the creator ID to look up.   
                // The ASPNet events reuse the IIS ID which means first stop kills both.   As it turns out
                // IIS stops before ASP (also incorrect) but it mostly this is benign...  
                StartStopActivity creator = null;
                if (data.RelatedActivityID == Guid.Empty)
                {
                    creator = GetActiveStartStopActivityTable(data.ContextId, data.ProcessID);
                }

                Guid activityId = data.ContextId;
                OnStart(data, data.Path, &activityId, null, creator, null, false);
            };
            aspNetParser.AspNetReqStop += delegate (AspNetStopTraceData data)
            {
                Guid activityId = data.ContextId;
                OnStop(data, &activityId);
            };
            // There are other ASP.NET events that have context information and this is useful
            aspNetParser.AspNetReqStartHandler += delegate (AspNetStartHandlerTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPipelineModuleEnter += delegate (AspNetPipelineModuleEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqGetAppDomainEnter += delegate (AspNetGetAppDomainEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };

            // These are probably not important, but they may help.  
            aspNetParser.AspNetReqRoleManagerBegin += delegate (AspNetRoleManagerBeginTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqRoleManagerGetUserRoles += delegate (AspNetRoleManagerGetUserRolesTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqRoleManagerEnd += delegate (AspNetRoleManagerEndTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqMapHandlerEnter += delegate (AspNetMapHandlerEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqMapHandlerLeave += delegate (AspNetMapHandlerLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqHttpHandlerEnter += delegate (AspNetHttpHandlerEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqHttpHandlerLeave += delegate (AspNetHttpHandlerLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreInitEnter += delegate (AspNetPagePreInitEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreInitLeave += delegate (AspNetPagePreInitLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageInitEnter += delegate (AspNetPageInitEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageInitLeave += delegate (AspNetPageInitLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageLoadEnter += delegate (AspNetPageLoadEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageLoadLeave += delegate (AspNetPageLoadLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreRenderEnter += delegate (AspNetPagePreRenderEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreRenderLeave += delegate (AspNetPagePreRenderLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageSaveViewstateEnter += delegate (AspNetPageSaveViewstateEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageSaveViewstateLeave += delegate (AspNetPageSaveViewstateLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageRenderEnter += delegate (AspNetPageRenderEnterTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageRenderLeave += delegate (AspNetPageRenderLeaveTraceData data)
            {
                SetThreadToStartStopActivity(data, data.ContextId);
            };

            var WCFParser = new ApplicationServerTraceEventParser(m_source);

            WCFParser.WebHostRequestStart += delegate (Multidata69TemplateATraceData data)
            {
                OnStart(data, data.VirtualPath);
            };
            WCFParser.WebHostRequestStop += delegate (OneStringsTemplateATraceData data)
            {
                OnStop(data);
            };

            // Microsoft-Windows-Application Server-Applications/TransportReceive/Stop
            WCFParser.MessageReceivedByTransport += delegate (Multidata29TemplateHATraceData data)
            {
                // This actually looks like a Stop opcode, but it is really a start because
                // it has a RelatedActivityID   
                OnStart(data, data.ListenAddress, null, null, null, "OperationDispatch");
            };

            // These Stop the Starts.  We don't want leaks in the common case.   
            WCFParser.DispatchFailed += delegate (Multidata38TemplateHATraceData data)
            {
                OnStop(data);
            };
            WCFParser.DispatchSuccessful += delegate (Multidata38TemplateHATraceData data)
            {
                OnStop(data);
            };

            WCFParser.OperationInvoked += delegate (Multidata24TemplateHATraceData data)
            {
                // The creator uses the same ID as myself.  
                OnStart(data, data.MethodName, null, null, GetActiveStartStopActivityTable(data.ActivityID, data.ProcessID));
            };
            WCFParser.OperationCompleted += delegate (Multidata28TemplateHATraceData data)
            {
                OnStop(data);
            };

            WCFParser.ServiceActivationStart += SetThreadToStartStopActivity;
            WCFParser.ServiceHostFactoryCreationStart += SetThreadToStartStopActivity;
            WCFParser.ServiceHostStarted += SetThreadToStartStopActivity;
            WCFParser.HttpMessageReceiveStart += SetThreadToStartStopActivity;
            WCFParser.HttpContextBeforeProcessAuthentication += SetThreadToStartStopActivity;
            WCFParser.TokenValidationStarted += SetThreadToStartStopActivity;
            WCFParser.MessageReadByEncoder += SetThreadToStartStopActivity;
            WCFParser.HttpResponseReceiveStart += SetThreadToStartStopActivity;
            WCFParser.SocketReadStop += SetThreadToStartStopActivity;
            WCFParser.SocketAsyncReadStop += SetThreadToStartStopActivity;
            WCFParser.SignatureVerificationStart += SetThreadToStartStopActivity;
            WCFParser.SignatureVerificationSuccess += SetThreadToStartStopActivity;
            WCFParser.ChannelReceiveStop += SetThreadToStartStopActivity;
            WCFParser.DispatchMessageStart += SetThreadToStartStopActivity;
            WCFParser.IncrementBusyCount += SetThreadToStartStopActivity;
            WCFParser.DispatchMessageBeforeAuthorization += SetThreadToStartStopActivity;
            WCFParser.ActionItemScheduled += SetThreadToStartStopActivity;
            WCFParser.GetServiceInstanceStart += SetThreadToStartStopActivity;
            WCFParser.GetServiceInstanceStop += SetThreadToStartStopActivity;
            WCFParser.ActionItemCallbackInvoked += SetThreadToStartStopActivity;
            WCFParser.ChannelReceiveStart += SetThreadToStartStopActivity;
            WCFParser.OutgoingMessageSecured += SetThreadToStartStopActivity;
            WCFParser.SocketWriteStart += SetThreadToStartStopActivity;
            WCFParser.SocketAsyncWriteStart += SetThreadToStartStopActivity;
            WCFParser.BinaryMessageEncodingStart += SetThreadToStartStopActivity;
            WCFParser.MtomMessageEncodingStart += SetThreadToStartStopActivity;
            WCFParser.TextMessageEncodingStart += SetThreadToStartStopActivity;
            WCFParser.BinaryMessageDecodingStart += SetThreadToStartStopActivity;
            WCFParser.MtomMessageDecodingStart += SetThreadToStartStopActivity;
            WCFParser.TextMessageDecodingStart += SetThreadToStartStopActivity;
            WCFParser.StreamedMessageWrittenByEncoder += SetThreadToStartStopActivity;
            WCFParser.MessageWrittenAsynchronouslyByEncoder += SetThreadToStartStopActivity;
            WCFParser.BufferedAsyncWriteStop += SetThreadToStartStopActivity;
            WCFParser.HttpPipelineProcessResponseStop += SetThreadToStartStopActivity;
            WCFParser.WebSocketAsyncWriteStop += SetThreadToStartStopActivity;
            WCFParser.MessageSentByTransport += SetThreadToStartStopActivity;
            WCFParser.HttpSendStop += SetThreadToStartStopActivity;
            WCFParser.DispatchMessageStop += SetThreadToStartStopActivity;
            WCFParser.DispatchSuccessful += SetThreadToStartStopActivity;

            // Server-side quota information.
            WCFParser.MaxReceivedMessageSizeExceeded += SetThreadToStartStopActivity;
            WCFParser.MaxPendingConnectionsExceeded += SetThreadToStartStopActivity;
            WCFParser.ReaderQuotaExceeded += SetThreadToStartStopActivity;
            WCFParser.NegotiateTokenAuthenticatorStateCacheExceeded += SetThreadToStartStopActivity;
            WCFParser.NegotiateTokenAuthenticatorStateCacheRatio += SetThreadToStartStopActivity;
            WCFParser.SecuritySessionRatio += SetThreadToStartStopActivity;
            WCFParser.PendingConnectionsRatio += SetThreadToStartStopActivity;
            WCFParser.ConcurrentCallsRatio += SetThreadToStartStopActivity;
            WCFParser.ConcurrentSessionsRatio += SetThreadToStartStopActivity;
            WCFParser.ConcurrentInstancesRatio += SetThreadToStartStopActivity;
            WCFParser.PendingAcceptsAtZero += SetThreadToStartStopActivity;

            // WCF client operations.
            // TODO FIX NOW, I have never run these!  Get some data to test against.  
            WCFParser.ClientOperationPrepared += delegate (Multidata22TemplateHATraceData data)
            {
                string extraInformation = "/Action=" + data.ServiceAction + "/URL=" + data.Destination;
                OnStart(data, extraInformation, null, null, GetActiveStartStopActivityTable(data.ActivityID, data.ProcessID), "ClientOperation");
            };
            WCFParser.ServiceChannelCallStop += delegate (Multidata22TemplateHATraceData data)
            {
                OnStop(data);
            };
        }
        /// <summary>
        /// The current start-stop activity on the given thread.   
        /// If present 'context' is used to look up the current activityID and try to use that to repair missing Starts.  
        /// Basically if we can't figure out what StartStop activity the thread from just the threadID we can use the activityID 
        /// from the 'context' event to find it as a backup.     
        /// </summary>
        public StartStopActivity GetCurrentStartStopActivity(TraceThread thread, TraceEvent context = null)
        {
            DoStopIfNecessary();        // Do any deferred stops. 

            // Search up the stack of tasks, seeing if we have a start-stop activity. 
            TraceActivity curTaskActivity = m_taskComputer.GetCurrentActivity(thread);
            if (curTaskActivity == null)
            {
                return null;
            }

            int taskIndex = (int)curTaskActivity.Index;
            StartStopActivity ret = m_traceActivityToStartStopActivity.Get(taskIndex);

            if (ret == null && context != null)
            {
                if (context.ActivityID != Guid.Empty)
                {
                    ret = GetActiveStartStopActivityTable(context.ActivityID, context.ProcessID);
                }
            }

            // If the activity is stopped, then don't return it, return its parent.   
            while (ret != null)
            {
                if (!ret.IsStopped)
                {
                    return ret;
                }

                ret = ret.Creator;
            }
            return ret;
        }

        /// <summary>
        /// Gets the current Start-Stop activity for a given TraceActivity.  
        /// </summary>
        /// <param name="curActivity"></param>
        /// <returns></returns>
        public StartStopActivity GetStartStopActivityForActivity(TraceActivity curActivity)
        {
            int taskIndex = (int)curActivity.Index;
            StartStopActivity ret = m_traceActivityToStartStopActivity.Get(taskIndex);

            // If the activity is stopped, then don't return it, return its parent.   
            while (ret != null)
            {
                if (!ret.IsStopped)
                {
                    return ret;
                }

                ret = ret.Creator;
            }
            return ret;
        }

        /// <summary>
        /// Returns a stack index representing the nesting of Start-Stop activities for the thread 'curThread' at the current time
        /// (At this point of the current event for the computer).   The stack starts with a frame for the process of the thread, then 
        /// has all the start-stop activity frames, then a frame representing 'topThread' which may not be the same as 'thread' since
        /// 'topThread' is the thread that spawned the first task, not the currently executing thread.  
        /// 
        /// Normally this stack is for the current time, but if 'getAtCreationTime' is true, it will compute the
        /// stack at the time that the current activity was CREATED rather than the current time.  This works 
        /// better for await time
        /// </summary>
        public StackSourceCallStackIndex GetCurrentStartStopActivityStack(MutableTraceEventStackSource outputStackSource, TraceThread curThread, TraceThread topThread, bool getAtCreationTime = false)
        {
            StartStopActivity startStop = null;
            // Best effort to get the activity as it exists at creation time of the current activity (AWAIT).    
            if (getAtCreationTime)
            {
                TraceActivity curTaskActivity = m_taskComputer.GetCurrentActivity(curThread);
                if (curTaskActivity != null)
                {
                    startStop = m_traceActivityToStartStopActivity.Get((int)curTaskActivity.Index);
                    if (startStop != null)
                    {
                        // Check to make sure that the start-stop stopped AFTER the activity was created.   
                        if (startStop.IsStopped && startStop.StartTimeRelativeMSec + startStop.DurationMSec < curTaskActivity.CreationTimeRelativeMSec)
                        {
                            startStop = null;
                        }
                    }
                }
            }
            if (startStop == null)
            {
                startStop = GetCurrentStartStopActivity(curThread);
            }

            // Get the process, and activity frames. 
            StackSourceCallStackIndex stackIdx = GetStartStopActivityStack(outputStackSource, startStop, topThread.Process);

            // Add "Threads pesudo-node"
            stackIdx = outputStackSource.Interner.CallStackIntern(outputStackSource.Interner.FrameIntern("Threads"), stackIdx);

            // Add the thread.  
            stackIdx = outputStackSource.Interner.CallStackIntern(outputStackSource.Interner.FrameIntern(topThread.VerboseThreadName), stackIdx);
            return stackIdx;
        }

        /// <summary>
        /// Gets a stack that represents the nesting of the Start-Stop tasks.  curActivity can be null, in which case just he process node is returned.  
        /// </summary>
        public StackSourceCallStackIndex GetStartStopActivityStack(MutableTraceEventStackSource outputStackSource, StartStopActivity curActivity, TraceProcess process)
        {
            StackSourceCallStackIndex stackIdx = outputStackSource.GetCallStackForProcess(process);

            // Add Pseudo frame for whether it is an activity or not 
            string firstFrameName = curActivity == null ? "(Non-Activities)" : "(Activities)";
            stackIdx = outputStackSource.Interner.CallStackIntern(outputStackSource.Interner.FrameIntern(firstFrameName), stackIdx);

            if (curActivity != null)
            {
                stackIdx = curActivity.GetActivityStack(outputStackSource, stackIdx);
            }

            return stackIdx;
        }

        // Events 
        /// <summary>
        /// If set, called AFTER a Start-Stop activity starts, called with the activity and the event that caused the start. 
        /// </summary>
        public Action<StartStopActivity, TraceEvent> Start;
        /// <summary>
        /// If set, called BEFORE a Start-Stop activity stops, called with the activity and the event that caused the start. 
        /// </summary>
        public Action<StartStopActivity, TraceEvent> Stop;

        // TODO decide if we need this...
        // public Action<StartStopActivity> AfterStop;

        /// <summary>
        /// Returns true if 'guid' follow the EventSouce style activity ID for the process with ID processID.  
        /// You can pass a process ID of 0 to this routine and it will do the best it can, but the possibility
        /// of error is significantly higher (but still under .1%)
        /// </summary>
        public static unsafe bool IsActivityPath(Guid guid, int processID)
        {
            uint* uintPtr = (uint*)&guid;

            uint sum = uintPtr[0] + uintPtr[1] + uintPtr[2] + 0x599D99AD;
            if (processID == 0)
            {
                // We guess that the process ID is < 20 bits and because it was xored
                // with the lower bits, the upper 12 bits should be independent of the
                // particular process, so we can at least confirm that the upper bits
                // match. 
                return ((sum & 0xFFF00000) == (uintPtr[3] & 0xFFF00000));
            }

            if ((sum ^ (uint)processID) == uintPtr[3])  // This is the new style 
            {
                return true;
            }

            return (sum == uintPtr[3]);         // THis is old style where we don't make the ID unique machine wide.  
        }

        /// <summary>
        /// Assuming guid is an Activity Path, extract the process ID from it.   
        /// </summary>
        public static unsafe int ActivityPathProcessID(Guid guid)
        {
            uint* uintPtr = (uint*)&guid;
            uint sum = uintPtr[0] + uintPtr[1] + uintPtr[2] + 0x599D99AD;
            return (int)(sum ^ uintPtr[3]);
        }

        /// <summary>
        /// returns a string representation for the activity path.  If the GUID is not an activity path then it returns
        /// the normal string representation for a GUID.  
        /// </summary>
        public static unsafe string ActivityPathString(Guid guid)
        {
            return IsActivityPath(guid, 0) ? CreateActivityPathString(guid) : guid.ToString();
        }

        internal static unsafe string CreateActivityPathString(Guid guid)
        {
            Debug.Assert(IsActivityPath(guid, 0));

            var processID = ActivityPathProcessID(guid);
            StringBuilder sb = Utilities.StringBuilderCache.Acquire();
            if (processID != 0)
            {
                sb.Append("/#");    // Use /# to mark the fact that the first number is a process ID.   
                sb.Append(processID);
            }
            else
            {
                sb.Append('/'); // Use // to start to make it easy to anchor
            }
            byte* bytePtr = (byte*)&guid;
            byte* endPtr = bytePtr + 12;
            char separator = '/';
            while (bytePtr < endPtr)
            {
                uint nibble = (uint)(*bytePtr >> 4);
                bool secondNibble = false;              // are we reading the second nibble (low order bits) of the byte.
                NextNibble:
                if (nibble == (uint)NumberListCodes.End)
                {
                    break;
                }

                if (nibble <= (uint)NumberListCodes.LastImmediateValue)
                {
                    sb.Append('/').Append(nibble);
                    if (!secondNibble)
                    {
                        nibble = (uint)(*bytePtr & 0xF);
                        secondNibble = true;
                        goto NextNibble;
                    }
                    // We read the second nibble so we move on to the next byte. 
                    bytePtr++;
                    continue;
                }
                else if (nibble == (uint)NumberListCodes.PrefixCode)
                {
                    // This are the prefix codes.   If the next nibble is MultiByte, then this is an overflow ID.  
                    // we we denote with a $ instead of a / separator.  

                    // Read the next nibble.  
                    if (!secondNibble)
                    {
                        nibble = (uint)(*bytePtr & 0xF);
                    }
                    else
                    {
                        bytePtr++;
                        if (endPtr <= bytePtr)
                        {
                            break;
                        }

                        nibble = (uint)(*bytePtr >> 4);
                    }

                    if (nibble < (uint)NumberListCodes.MultiByte1)
                    {
                        // If the nibble is less than MultiByte we have not defined what that means 
                        // For now we simply give up, and stop parsing.  We could add more cases here...
                        return guid.ToString();
                    }
                    // If we get here we have a overflow ID, which is just like a normal ID but the separator is $
                    separator = '$';
                    // Fall into the Multi-byte decode case.  
                }

                Debug.Assert((uint)NumberListCodes.MultiByte1 <= nibble);
                // At this point we are decoding a multi-byte number, either a normal number or a 
                // At this point we are byte oriented, we are fetching the number as a stream of bytes. 
                uint numBytes = nibble - (uint)NumberListCodes.MultiByte1;

                uint value = 0;
                if (!secondNibble)
                {
                    value = (uint)(*bytePtr & 0xF);
                }

                bytePtr++;       // Advance to the value bytes

                numBytes++;     // Now numBytes is 1-4 and represents the number of bytes to read.  
                if (endPtr < bytePtr + numBytes)
                {
                    break;
                }

                // Compute the number (little endian) (thus backwards).  
                for (int i = (int)numBytes - 1; 0 <= i; --i)
                {
                    value = (value << 8) + bytePtr[i];
                }

                // Print the value
                sb.Append(separator).Append(value);

                bytePtr += numBytes;        // Advance past the bytes.
            }

            sb.Append('/');
            return Utilities.StringBuilderCache.GetStringAndRelease(sb);
        }

        #region private
        private static readonly Guid MicrosoftWindowsASPNetProvider = new Guid("ee799f41-cfa5-550b-bf2c-344747c1c668");
        private static readonly Guid MicrosoftWindowsIISProvider = new Guid("de4649c9-15e8-4fea-9d85-1cdda520c334");
        private static readonly Guid AdoNetProvider = new Guid("6a4dfe53-eb50-5332-8473-7b7e10a94fd1");
        private static readonly Guid MicrosoftWindowsHttpService = new Guid("dd5ef90a-6398-47a4-ad34-4dcecdef795f");
        private static readonly Guid MicrosoftDiagnosticsDiagnosticSourceProvider = new Guid("ADB401E1-5296-51F8-C125-5FDA75826144");

        // EventSourceName: Microsoft-ApplicationInsights-Data
        // Reference for definition: https://raw.githubusercontent.com/Microsoft/ApplicationInsights-dotnet/e8f047f6e48abae0e88a9c77bf65df858c442940/src/Microsoft.ApplicationInsights/Extensibility/Implementation/RichPayloadEventSource.cs
        private static readonly Guid MicrosoftApplicationInsightsDataProvider = new Guid("a62adddb-6b4b-519d-7ba1-f983d81623e0");

        // A generic EventSource ("Microsoft-Diagnostics-ActivityTracking") for marking the start and stop of an activity.
        // Used by non-.NET platforms such as Java
        private static readonly Guid MicrosoftDiagnosticsActivityTrackingProvider = new Guid("3b268b3d-903f-5835-c77e-790d518a26c4");

        // The main start and stop logic.  
        private unsafe StartStopActivity OnStart(TraceEvent data, string extraStartInfo = null, Guid* activityId = null, TraceThread thread = null, StartStopActivity creator = null, string taskName = null, bool useCurrentActivityForCreatorAsFallback = true)
        {
            // Because we want the stop to logically be 'after' the actual stop event (so that the stop looks like
            // it is part of the start-stop activity we defer it until the next event.   If there is already
            // a deferral, we can certainly do that one now.  
            DoStopIfNecessary();

            if (thread == null)
            {
                thread = data.Thread();
                if (thread == null)
                {
                    return null;
                }
            }
            TraceActivity curTaskActivity = m_taskComputer.GetCurrentActivity(thread);
            ActivityIndex taskIndex = curTaskActivity.Index;

            if (creator == null)
            {
                if (data.RelatedActivityID != Guid.Empty)
                {
                    creator = GetActiveStartStopActivityTable(data.RelatedActivityID, data.ProcessID);
                    if (creator == null)
                    {
                        Trace.WriteLine(data.TimeStampRelativeMSec.ToString("n3") + " Warning: Could not find creator Activity " + StartStopActivityComputer.ActivityPathString(data.RelatedActivityID));
                    }
                }
                // If there is no RelatedActivityID, or the activity ID we have is 'bad' (dead), fall back to the activity we track.  
                if (creator == null && useCurrentActivityForCreatorAsFallback)
                {
                    creator = GetStartStopActivityForActivity(curTaskActivity);
                }
            }

            if (taskName == null)
            {
                taskName = data.TaskName;
            }

            // Create the new activity.  
            StartStopActivity activity;
            if (activityId != null)
            {
                activity = new StartStopActivity(data, taskName, ref *activityId, creator, m_nextIndex++, extraStartInfo);
            }
            else
            {
                Guid activityIdValue = data.ActivityID;
                activity = new StartStopActivity(data, taskName, ref activityIdValue, creator, m_nextIndex++, extraStartInfo);
            }
            SetActiveStartStopActivityTable(activity.ActivityID, data.ProcessID, activity);       // Put it in our table of live activities.  
            m_traceActivityToStartStopActivity.Set((int)taskIndex, activity);

            // Issue callback if requested AFTER state update
            var onStartAfter = Start;
            if (onStartAfter != null)
            {
                onStartAfter(activity, data);
            }

            return activity;
        }

        private void SetThreadToStartStopActivity(TraceEvent data)
        {
            SetThreadToStartStopActivity(data, data.ActivityID);
        }

        private void SetThreadToStartStopActivity(TraceEvent data, Guid activityId)
        {
            StartStopActivity startStopActivity = GetActiveStartStopActivityTable(activityId, data.ProcessID);
            if (startStopActivity != null)
            {
                // Because we want the stop to logically be 'after' the actual stop event (so that the stop looks like
                // it is part of the start-stop activity we defer it until the next event.   If there is already
                // a deferral, we can certainly do that one now.  
                DoStopIfNecessary();

                TraceThread thread = data.Thread();
                if (thread == null)
                {
                    return;
                }

                TraceActivity curTaskActivity = m_taskComputer.GetCurrentActivity(thread);
                ActivityIndex taskIndex = curTaskActivity.Index;

                StartStopActivity previousStartStopActivity = m_traceActivityToStartStopActivity.Get((int)taskIndex);
                if (previousStartStopActivity == null)
                {
                    m_traceActivityToStartStopActivity.Set((int)taskIndex, startStopActivity);
                }
                else if (previousStartStopActivity != startStopActivity)
                {
                    Trace.WriteLine("Warning: Thread " + data.ThreadID + " at " + data.TimeStampRelativeMSec.ToString("n3") +
                        " wants to overwrite activity started at " + previousStartStopActivity.StartTimeRelativeMSec.ToString("n3"));
                }
            }
        }

        private unsafe void OnStop(TraceEvent data, Guid* activityID = null)
        {
            // Because we want the stop to logically be 'after' the actual stop event (so that the stop looks like
            // it is part of the start-stop activity we defer it until the next event.   If there is already
            // a deferral, we can certainly do that one now.  
            DoStopIfNecessary();

            TraceThread thread = data.Thread();
            if (thread == null)
            {
                return;
            }

            TraceActivity curTaskActivity = m_taskComputer.GetCurrentActivity(thread);
            ActivityIndex taskIndex = curTaskActivity.Index;

            StartStopActivity activity;
            if (activityID != null)
            {
                activity = GetActiveStartStopActivityTable(*activityID, data.ProcessID);
            }
            else
            {
                activity = GetActiveStartStopActivityTable(data.ActivityID, data.ProcessID);
            }

            if (activity != null)
            {
                // We defer the stop until the NEXT event (so that stops look like they are IN the activity). 
                // So here we just capture what is needed to do this later (basically on the next event).  
                activity.RememberStop(data.EventIndex, data.TimeStampRelativeMSec, taskIndex);
                m_deferredStop = activity;      // Remember this one for deferral.  

                // Issue callback if requested, this is before state update since we have deferred the stop.  
                var stop = Stop;
                if (stop != null)
                {
                    stop(activity, data);
                    if (activity.Creator != null && activity.Creator.killIfChildDies && !activity.Creator.IsStopped)
                    {
                        stop(activity.Creator, data);
                    }
                }
            }
            else
            {
                // TODO GetResponseStream Stops can sometime occur before the start (if they can be accomplished without I/O).  
                if (!(data.ProviderGuid == FrameworkEventSourceTraceEventParser.ProviderGuid))
                {
                    Guid activityIdValue = activityID != null ? *activityID : data.ActivityID;
                    Trace.WriteLine("Warning: Unmatched stop at " + data.TimeStampRelativeMSec.ToString("n3") + " ID = " + activityIdValue);
                }
            }
        }

        /// <summary>
        /// We don't do a stop all processing associated with the stop event is done.  Thus if we are not 'on'
        /// the stop event, then you can do any deferred processing.  
        /// </summary>
        private void DoStopIfNecessary()
        {
            // If we are not exactly on the StopEvent then do the deferred stop before doing any processing.  
            if (m_deferredStop != null && m_deferredStop.StopEventIndex != m_source.CurrentEventIndex)
            {
                var startStopActivity = m_deferredStop;

                var creator = startStopActivity.Creator;
                while (creator != null && creator.IsStopped)
                {
                    creator = creator.Creator;
                }

                m_traceActivityToStartStopActivity.Set((int)startStopActivity.activityIndex, creator);

                // If the creator shares the same activity ID, then set the activity ID to the creator,  This happens
                // when mulitple activities share the same activity ID (not the best practice but reasonably common).  
                // If this sharing is not present, we can remove the activity ID from the table. 
                if (creator != null && creator.ActivityID == startStopActivity.ActivityID && creator.ProcessID == startStopActivity.ProcessID)
                {
                    SetActiveStartStopActivityTable(startStopActivity.ActivityID, startStopActivity.ProcessID, creator);
                }
                else
                {
                    RemoveActiveStartStopActivityTable(startStopActivity.ActivityID, startStopActivity.ProcessID);
                }

                // We can also remove any 'extra ActivityID entries (only used for ASP.NET events that needed fixing up).  
                if (startStopActivity.unfixedActivityID != Guid.Empty)
                {
                    RemoveActiveStartStopActivityTable(startStopActivity.unfixedActivityID, startStopActivity.ProcessID);
                }

                // If we are supposed to auto-stop our creator, go ahead and do that.  (Only used on virtual RecHttp events)
                if (startStopActivity.Creator != null && startStopActivity.Creator.killIfChildDies && !startStopActivity.Creator.IsStopped)    // Some activities die when their child dies.  
                {
                    startStopActivity.Creator.RememberStop(startStopActivity.StopEventIndex, startStopActivity.StartTimeRelativeMSec + startStopActivity.DurationMSec, startStopActivity.activityIndex);
                    m_deferredStop = startStopActivity.Creator;
                    startStopActivity.activityIndex = ActivityIndex.Invalid;    // This also marks the activity as truly stopped.  
                    DoStopIfNecessary();
                }
                startStopActivity.activityIndex = ActivityIndex.Invalid;        // This also marks the activity as truly stopped.  
                m_deferredStop = null;
            }
        }

        private void FixAndProcessAdoNetEvents(TraceEvent data)
        {
            Debug.Assert(data.ProviderGuid == AdoNetProvider);
            if (data.ID == (TraceEventID)1 || data.ID == (TraceEventID)2) // BeginExecute || EndExecute
            {
                Debug.Assert(data.payloadNames[0] == "objectId");
                Guid startStopId = new Guid((int)data.PayloadValue(0), 1234, 5, 7, 7, 8, 9, 10, 11, 12, 13);  // Tail 7,7,8,9,10,11,12,13 means SQL Execute

                if (data.ID == (TraceEventID)1) // BeginExecute
                {
                    // TODO as of 2/2015 there was a bug where the ActivityPath logic was getting this ID wrong, so we get to from the thread. 
                    // relatedActivityID = activityID;
                    TraceThread thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    StartStopActivity creator = GetCurrentStartStopActivity(thread, data);

                    string extraStartInfo = null;
                    if (3 < data.payloadNames.Length)
                    {
                        Debug.Assert(data.payloadNames[1] == "dataSource");
                        Debug.Assert(data.payloadNames[3] == "commandText");
                        string dataSource = data.PayloadValue(1).ToString();
                        string commandText = data.PayloadValue(3).ToString();
                        extraStartInfo = "DS=" + dataSource;
                        // The SQL command often important, if it is not too long.  
                        if (!string.IsNullOrEmpty(commandText))
                        {
                            if (50 < commandText.Length)
                            {
                                commandText = commandText.Substring(0, 50 - 3) + "...";
                            }

                            extraStartInfo = extraStartInfo + ",CMD=" + commandText;
                        }
                    }

                    OnStart(data, extraStartInfo, &startStopId, thread, creator, "SQLCommand");
                    return;
                }
                else
                {
                    Debug.Assert(data.ID == (TraceEventID)2); // EndExecute
                    OnStop(data, &startStopId);
                    return;
                }
            }
        }

        /// <summary>
        /// Try to process some predefined DiagnosticSource ("Microsoft.EntityFrameworkCore.BeforeExecuteCommand" and "Microsoft.AspNetCore.Hosting.BeginRequest") start events.
        /// This will try to filter the events by "EventName", if failed it will return false without any further processing.
        /// </summary>
        /// <returns>Whether or not succeeded in processing the event</returns>
        private bool TryProcessDiagnosticSourceStartEvents(TraceEvent data)
        {
            Debug.Assert(data.ProviderGuid == MicrosoftDiagnosticsDiagnosticSourceProvider);
            try
            {
                string taskName = data.PayloadByName("EventName") as string;
                if (taskName == null)
                {
                    return false;
                }

                string extraInfo = null;

                // In the converter from 'DiagnosticSource' to ETW, we have some default configurations.
                // By default, SQL (EntityFramework) DiagnosticSource event will be converted to 'Activity2Start' and 'Activity2Stop' events
                // of provider 'Microsoft-Diagnostics-DiagnosticSource'. Meanwhile, the sql command will be in the payload.
                // The following code is used to make the call stack looks better (converting 'Activity2Start' to 'SQLCommand')
                // and showing the SQL command.
                // Details:
                // https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/DiagnosticSourceEventSource.cs

                if (data.PayloadByName("Arguments") is DynamicTraceEventData.StructValue[] args)
                {
                    var sb = Utilities.StringBuilderCache.Acquire(64);
                    bool first = true;
                    foreach (var arg in args)
                    {
                        string key = arg["Key"] as string;
                        object value = arg["Value"] as string;
                        if (key != null && value != null)
                        {
                            string valueStr = value.ToString();
                            if (!string.IsNullOrEmpty(valueStr))
                            {
                                if (!first)
                                {
                                    sb.Append(' ');
                                }

                                sb.Append(key).Append("=").Append('"').Append(valueStr).Append('"');
                            }
                        }
                        first = false;
                    }
                    extraInfo = Utilities.StringBuilderCache.GetStringAndRelease(sb);
                }
                OnStart(data, extraInfo, null, null, null, taskName);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Threw exception while processing DiagnosticSource start events: " + ex.Message);
            }
            return false;
        }

        private void FixAndProcessAppInsightsEvents(TraceEvent data)
        {
            Debug.Assert(data.ProviderGuid == MicrosoftApplicationInsightsDataProvider);
            Debug.Assert(data.Opcode == TraceEventOpcode.Start || data.Opcode == TraceEventOpcode.Stop);

            if (data.TaskName.Equals("Request", StringComparison.Ordinal))
            {
                if (data.Opcode == TraceEventOpcode.Start)
                {
                    // If we have a related activity ID, then this start event
                    // is already being tracked by an outer activity (e.g. an
                    // ASP.Net request). Do not process it.
                    if (!IsTrivialActivityId(data.RelatedActivityID) && m_ignoreApplicationInsightsRequestsWithRelatedActivityId)
                    {
                        return;
                    }
                    else
                    {
                        var extraInfo = (string)data.PayloadByName("Name") ?? (string)data.PayloadByName("Id");
                        OnStart(data, extraInfo);
                    }
                }
                else
                {
                    Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                    OnStop(data);
                }
            }
            else if (data.TaskName.Equals("Operation", StringComparison.Ordinal))
            {
                // TODO: [BinDu] AppInsights SDK has an issue that can create unexpected nested activity for Operation task. 
                // Ignore the events for now until the issue is fixed in AppInsights SDK 2.4.
                // https://github.com/Microsoft/ApplicationInsights-dotnet-server/issues/540 
                return;
            }
            else
            {
                Debug.Assert(false, $"{data.TaskName} is not recognized");
                return;
            }
        }

        private void ProcessActivityTrackingProviderEvents(TraceEvent data)
        {
            Debug.Assert(data.ProviderGuid == MicrosoftDiagnosticsActivityTrackingProvider);

            if (data.Opcode == TraceEventOpcode.Start)
            {
                OnStart(data);
            }
            else if (data.Opcode == TraceEventOpcode.Stop)
            {
                OnStop(data);
            }
        }

        private static readonly Guid GUID_ONE = new Guid("00000001-0000-0000-0000-000000000000");

        private static bool IsTrivialActivityId(Guid g)
        {
            return g == Guid.Empty || g == GUID_ONE;
        }

        private void FixAndProcessFrameworkEvents(TraceEvent data)
        {
            Debug.Assert(data.ProviderGuid == FrameworkEventSourceTraceEventParser.ProviderGuid);
            Debug.Assert(data.Opcode == TraceEventOpcode.Start || data.Opcode == TraceEventOpcode.Stop);

            if (data.Task == (TraceEventTask)1 || data.Task == (TraceEventTask)2) // GetResponse or GetResponseStream
            {
                Debug.Assert(data.payloadNames[0] == "id");
                long id = (long)data.PayloadValue(0);
                Guid startStopId = new Guid((int)id, (short)(id >> 32), (short)(id >> 48), 6, 7, 8, 9, 10, 11, 12, 13);   // Tail 6,7,8,9,10,11,12,13 means GetResponse
                if (data.Opcode == TraceEventOpcode.Start)
                {
                    // TODO as of 2/2015 there was a bug where the ActivityPath logic was getting this ID wrong, so we get to from the thread. 
                    // relatedActivityID = activityID;
                    TraceThread thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    StartStopActivity creator = GetCurrentStartStopActivity(thread, data);

                    string taskName = "Http" + data.TaskName;
                    Debug.Assert(data.payloadNames[1] == "uri");
                    string url = data.PayloadValue(1) as string;
                    OnStart(data, url, &startStopId, thread, creator, taskName);
                }
                else
                {
                    Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                    OnStop(data, &startStopId);
                }
            }
        }

        /// <summary>
        /// fix ASP.NET receiving events  
        /// </summary>
        private void FixAndProcessWindowsASP(TraceEvent data, KeyValuePair<Guid, Guid>[] threadToLastAspNetGuids)
        {
            Debug.Assert(data.ProviderGuid == MicrosoftWindowsASPNetProvider);
            Debug.Assert(data.Opcode == TraceEventOpcode.Start || data.Opcode == TraceEventOpcode.Stop);

            TraceThread thread = data.Thread();
            if (thread == null)
            {
                return;
            }

            if (data.Opcode == TraceEventOpcode.Start)
            {
                string taskName = "RecASP" + data.TaskName;
                string extraStartInfo = (string)data.PayloadValue(1);        // This is the URL

                Guid activityID = data.ActivityID;

                // In V4.6 we use a ActivityPath but this is a bug in that the 'STOP will not necessarily match it. 
                // In V4.7 and beyond this is fixed (thus the ID will NOT be a ActivityPath).  
                // Thus this can be removed after V4.6 is aged out (e.g. 9/2016)
                // To fix V4.6 we set the ID 
                Guid unfixedActivityID = Guid.Empty;
                if (StartStopActivityComputer.IsActivityPath(activityID, data.ProcessID))
                {
                    Guid fixedActivityID = threadToLastAspNetGuids[(int)thread.ThreadIndex].Value;     // This is RelatedActivityID field of the Send event. 
                    if (fixedActivityID != Guid.Empty)
                    {
                        if (fixedActivityID != activityID)
                        {
                            // Also add the old activity path to the extraStartInfo, which is needed ServiceProfiler versioning between the monitor and the detailed file.  
                            extraStartInfo = StartStopActivityComputer.ActivityPathString(activityID) + "," + extraStartInfo;
                            unfixedActivityID = activityID;     // remember this one as well so we can look up by it as well.   
                            activityID = fixedActivityID;       // Make the fixed key the 'offiical' one.
                        }
                    }
                    else
                    {
                        Trace.WriteLine(data.TimeStampRelativeMSec.ToString("n3") + " Could not find ASP.NET Send event to fix Start event");
                    }
                }

                StartStopActivity creator = null;
                Guid relatedActivityId = data.RelatedActivityID;
                if (relatedActivityId == Guid.Empty)
                {
                    relatedActivityId = threadToLastAspNetGuids[(int)thread.ThreadIndex].Key;     // This is the ActivityID of the Send Event.  
                }

                if (relatedActivityId != Guid.Empty)
                {
                    creator = GetActiveStartStopActivityTable(relatedActivityId, data.ProcessID);
                }

                if (creator == null)        // If we don't have a creator for the ASP.NET event, make one since WCF and others are children of it and we want to see that 
                {
                    // We create another 
                    // It is closer to Start Event than the Send Event, uses &activityID.
                    creator = OnStart(data, extraStartInfo, &activityID, thread, null, "RecHttp", useCurrentActivityForCreatorAsFallback: false);
                    Debug.Assert(creator.Creator == null);  // will try to use the relatedActivityId field but we know that that was empty
                    creator.killIfChildDies = true;         // Mark that we should clean up
                }

                StartStopActivity startedActivity = OnStart(data, extraStartInfo, &activityID, thread, creator, taskName);

                // in V4.6 the Stop may use the fixed or unfixed ID, so put both IDs into the lookup table.  
                if (unfixedActivityID != Guid.Empty)
                {
                    SetActiveStartStopActivityTable(unfixedActivityID, data.ProcessID, startedActivity);
                    startedActivity.unfixedActivityID = unfixedActivityID;      // So we don't leak these entries.  
                }
            }
            else
            {
                Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                OnStop(data);
            }
            threadToLastAspNetGuids[(int)thread.ThreadIndex] = new KeyValuePair<Guid, Guid>();
        }

        // Code for looking up start-stop events by their activity ID and process ID

        /// <summary>
        /// Look up a start-stop activity by its ID.   Note that the 'activityID' needs to be unique for that instance 
        /// within a process.  (across ALL start-stop activities, which means it may need components that encode its 
        /// provider and task).   We pass the process ID as well so that it will be unique in the whole trace.  
        /// </summary>
        private unsafe StartStopActivity GetActiveStartStopActivityTable(Guid activityID, int processID)
        {
            StartStopActivity ret = null;
            long* asLongs = (long*)&activityID;
            asLongs[1] += processID;    // add in the process ID.       Note that this does not guarantee non-collision we may wish to do better.  
            m_activeStartStopActivities.TryGetValue(activityID, out ret);
            return ret;
        }

        private unsafe void SetActiveStartStopActivityTable(Guid activityID, int processID, StartStopActivity newValue)
        {
            long* asLongs = (long*)&activityID;
            asLongs[1] += processID;    // add in the process ID.       Note that this does not guarantee non-collision we may wish to do better.  
            m_activeStartStopActivities[activityID] = newValue;
        }

        private unsafe void RemoveActiveStartStopActivityTable(Guid activityID, int processID)
        {
            long* asLongs = (long*)&activityID;
            asLongs[1] += processID;    // add in the process ID.       Note that this does not guarantee non-collision we may wish to do better.  
            m_activeStartStopActivities.Remove(activityID);
        }

        /// <summary>
        /// The encoding for a list of numbers used to make Activity  Guids.   Basically
        /// we operate on nibbles (which are nice because they show up as hex digits).  The
        /// list is ended with a end nibble (0) and depending on the nibble value (Below)
        /// the value is either encoded into nibble itself or it can spill over into the
        /// bytes that follow.   
        /// </summary>
        private enum NumberListCodes : byte
        {
            End = 0x0,             // ends the list.   No valid value has this prefix.   
            LastImmediateValue = 0xA,
            PrefixCode = 0xB,
            MultiByte1 = 0xC,   // 1 byte follows.  If this Nibble is in the high bits, it the high bits of the number are stored in the low nibble.   
                                // commented out because the code does not explicitly reference the names (but they are logically defined).  
                                // MultiByte2 = 0xD,   // 2 bytes follow (we don't bother with the nibble optimzation
                                // MultiByte3 = 0xE,   // 3 bytes follow (we don't bother with the nibble optimzation
                                // MultiByte4 = 0xF,   // 4 bytes follow (we don't bother with the nibble optimzation
        }

        // Fields
        private TraceLogEventSource m_source;                                                // Where we get events from.  
        private ActivityComputer m_taskComputer;                                             // I need to be able to get the current Activity to keep track of start-stop activities. 
        private GrowableArray<StartStopActivity> m_traceActivityToStartStopActivity;         // Maps a TraceActivity (index) to a start stop activity at the current time. 
        private Dictionary<StartStopKey, StartStopActivity> m_activeStartStopActivities;     // Lookup activities by activityID&ProcessID (we call the start-stop key) at the current time
        private int m_nextIndex;                                                             // Used to create unique indexes for StartStopActivity.Index.  
        private StartStopActivity m_deferredStop;                                            // We defer doing the stop action until the next event.  This is what remembers to do this.  
        private bool m_ignoreApplicationInsightsRequestsWithRelatedActivityId;               // Until .NET Core 3.0, Application Insights events uses this activity id to de-dupe the rest of the nested activities.
        #endregion
    }

    /// <summary>
    /// An dense number that defines the identity of a StartStopActivity.  Used to create side arrays 
    /// for StartStopActivity info.  
    /// </summary>
    public enum StartStopActivityIndex
    {
        /// <summary>
        /// An illegal index, sutable for a sentinal.  
        /// </summary>
        Illegal = -1,
    }

    /// <summary>
    /// A StartStop reresents an activity between a start and stop event as generated by EvetSource.  
    /// </summary>
    public class StartStopActivity
    {
        /// <summary>
        /// The index (small dense numbers suitabilty for array indexing) for this activity. 
        /// </summary>
        public StartStopActivityIndex Index { get; private set; }
        /// <summary>
        /// The name of the activity (The Task name for the start-stop event as well as the activity ID)
        /// </summary>
        public string Name
        {
            get
            {
                var sb = Utilities.StringBuilderCache.Acquire(64);

                sb.Append(TaskName);
                sb.Append('(');
                AppendActivityPath(sb, ActivityID, ProcessID);

                if (ExtraInfo != null)
                {
                    sb.Append(',');
                    sb.Append(ExtraInfo);
                }

                sb.Append(')');
                return Utilities.StringBuilderCache.GetStringAndRelease(sb);
            }
        }

        private static unsafe StringBuilder AppendActivityPath(StringBuilder sb, Guid guid, int processId)
        {
            if (StartStopActivityComputer.IsActivityPath(guid, processId))
            {
                return sb.Append(StartStopActivityComputer.CreateActivityPathString(guid));
            }

            // There are a  couple of well-known activity ID patterns:
            // HTTP Command: xxxxxxxx-yyyy-zzzz-0607-08090a0b0c0d
            // SQL  Command: xxxxxxxx-yyyy-zzzz-0707-08090a0b0c0d
            switch (((ulong*)&guid)[1])
            {
                case 0x0d0c0b0a09080706: // HTTP Command
                    // The first bytes are the ID that links the start and stop.
                    return sb.Append("HTTP/Id=").Append(((uint*)&guid)[0].ToString("x8"));

                case 0x0d0c0b0a09080707: // SQL Command
                    // The first 8 bytes is the ID that links the start and stop.
                    return sb.Append("SQL/Id=").Append(((uint*)&guid)[0].ToString("x8"));

                default:
                    return sb.Append(guid.ToString());
            }
        }


        private string _knownType = null;
        /// <summary>
        /// Known Activity Type
        /// </summary>
        public string KnownType
        {
            get
            {
                if (_knownType == null)
                {
                    bool noSuffix = false;
                    StringBuilder sb = Utilities.StringBuilderCache.Acquire();
                    switch (TaskName)
                    {
                        case "RecHttp":
                        case "RecASPRequest":
                        case "AspNetReq":
                            {
                                // ASP.NET
                                sb.Append("ASP.NET");
                                break;
                            }
                        case "HttpGetRequestStream":
                        case "HttpGetResponse":
                            {
                                // HTTP
                                sb.Append("HTTP");
                                if (ExtraInfo != null)
                                {
                                    if (ExtraInfo.Contains(".core.windows.net"))
                                    {
                                        if (ExtraInfo.Contains(".blob."))
                                        {
                                            sb.Append(" (Azure Blob)");
                                        }
                                        else if (ExtraInfo.Contains(".table."))
                                        {
                                            sb.Append(" (Azure Table)");
                                        }
                                        else if (ExtraInfo.Contains(".queue."))
                                        {
                                            sb.Append(" (Azure Queue)");
                                        }
                                        else if (ExtraInfo.Contains(".file."))
                                        {
                                            sb.Append(" (Azure File)");
                                        }
                                    }
                                }

                                break;
                            }
                        case "SQLCommand":
                            {
                                // SQL
                                sb.Append("SQL");
                                if (ExtraInfo != null && ExtraInfo.Contains(".database.windows.net"))
                                {
                                    sb.Append(" (Azure Database)");
                                }
                                break;
                            }
                        case "OperationDispatch":
                        case "DispatchMessage":
                        case "WebHostRequest":
                        case "ClientOperation":
                            {
                                // WCF
                                sb.Append("WCF");
                                break;
                            }
                        case "ActorMethod":
                        case "ActorSaveState":
                            {
                                // Service Fabric
                                sb.Append("Service Fabric Reliable Actor");
                                break;
                            }
                        default:
                            {
                                noSuffix = true;  // Return the empty string. 
                                break;
                            }
                    }
                    if (!noSuffix)
                    {
                        sb.Append(" Activities");
                    }

                    _knownType = Utilities.StringBuilderCache.GetStringAndRelease(sb);
                }

                return _knownType;
            }
        }
        /// <summary>
        /// If the activity has additional information associated with it (e.g. a URL), put it here.  Can be null.
        /// </summary>
        public string ExtraInfo { get; private set; }
        /// <summary>
        /// The Task name (the name prefix that is common to both the start and stop event)
        /// </summary>
        public string TaskName { get; private set; }
        /// <summary>
        /// The processID associated with this activity
        /// </summary>
        public int ProcessID { get; private set; }
        /// <summary>
        /// The Activity ID (as a GUID) that matches the start and stop together. 
        /// </summary>
        public Guid ActivityID { get; private set; }
        /// <summary>
        /// The path of creators that created this activity.  
        /// </summary>
        public string ActivityPathString
        {
            get
            {
                var ret = Name;
                if (Creator != null)
                {
                    ret = Creator.ActivityPathString + " " + ret;
                }

                return ret;
            }
        }
        /// <summary>
        /// The start-stop activity that created this activity (thus it makes a tree)
        /// </summary>
        public StartStopActivity Creator { get; private set; }
        /// <summary>
        /// The TraceLog event Index, of the start event (you can get addition info)
        /// </summary>
        public EventIndex StartEventIndex { get; private set; }
        /// <summary>
        /// The TraceLog event Index, of the stop event (you can get addition info)
        /// </summary>
        public EventIndex StopEventIndex { get; private set; }
        /// <summary>
        /// The time in MSec from the start of the trace when the start event happened. 
        /// </summary>
        public double StartTimeRelativeMSec { get; private set; }
        /// <summary>
        /// The duration of activity in MSec (diff between stop and start)
        /// </summary>
        public double DurationMSec { get; private set; }
        /// <summary>
        /// This activity has completed (the Stop event has been received).  Thus Duration is valid.
        /// </summary>
        public bool IsStopped { get { return DurationMSec != 0 && activityIndex == ActivityIndex.Invalid; } }
        /// <summary>
        /// Returns a stack on the outputStackSource which has a frame for each activity that
        /// caused this activity, as well as the root of the given 'rootStack' (often a stack representing the process).    
        /// </summary>
        public StackSourceCallStackIndex GetActivityStack(MutableTraceEventStackSource outputStackSource, StackSourceCallStackIndex rootStack)
        {
            StackSourceCallStackIndex stackIdx = rootStack;
            if (Creator != null)
            {
                stackIdx = Creator.GetActivityStack(outputStackSource, stackIdx);

                // Add type name to the list of frames. Skip ASP.NET as it adds unnecessary complexity in most cases
                if (KnownType.Length != 0 && KnownType != "ASP.NET Activities")
                {
                    stackIdx = outputStackSource.Interner.CallStackIntern(outputStackSource.Interner.FrameIntern(KnownType), stackIdx);
                }
            }

            // Add my name to the list of frames.  
            stackIdx = outputStackSource.Interner.CallStackIntern(outputStackSource.Interner.FrameIntern("Activity " + Name), stackIdx);
            return stackIdx;
        }
        #region private
        /// <summary>
        /// override.   Gives the name and start time.  
        /// </summary>
        public override string ToString()
        {
            return "StartStopActivity(" + Name + ", Start=" + StartTimeRelativeMSec.ToString("n3") + ")";
        }

        internal StartStopActivity(TraceEvent startEvent, string taskName, ref Guid activityID, StartStopActivity creator, int index, string extraInfo = null)
        {
            ProcessID = startEvent.ProcessID;
            ActivityID = activityID;
            StartTimeRelativeMSec = startEvent.TimeStampRelativeMSec;
            StartEventIndex = startEvent.EventIndex;
            TaskName = taskName;
            Creator = creator;
            ExtraInfo = extraInfo;
            Index = (StartStopActivityIndex)index;
        }

        /// <summary>
        /// We don't update the state for the stop at the time of the stop, but at the next call to any of the StartStopActivityComputer APIs.  
        /// </summary>
        internal void RememberStop(EventIndex stopEventIndex, double stopTimeRelativeMSec, ActivityIndex activityIndex)
        {
            if (DurationMSec == 0)
            {
                this.activityIndex = activityIndex;
                StopEventIndex = stopEventIndex;
                DurationMSec = stopTimeRelativeMSec - StartTimeRelativeMSec;
            }
        }

        // these are used to implement deferred stops.  
        internal ActivityIndex activityIndex;     // the index for the task that was active at the time of the stop.  
        internal bool killIfChildDies;            // Used by ASP.NET events in some cases. 

        internal Guid unfixedActivityID;          // This can be removed when we don't care about V4.6 runtimes.  
        #endregion
    };

}
