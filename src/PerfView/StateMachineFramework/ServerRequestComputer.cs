using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.ApplicationServer;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PerfView
{
    /// <summary>
    /// ServerRequestScenarioConfiguration is an implementation of the ScenarioConfiguration abstract class that allows
    /// ComputingResourceStateMachine to track resources according to ASP.NET or WCF requests.
    /// 
    /// Its job is to keep track of what requests are in flight and what threads
    /// </summary>
    public sealed class ServerRequestScenarioConfiguration : ScenarioConfiguration
    {
        public ServerRequestScenarioConfiguration(TraceLog traceLog)
            : base(traceLog)
        {
            // The thread state map must be created before the request computer because
            // a reference to the thread state map is held by the request computer.
            m_ThreadStateMap = new ServerRequestThreadStateMap(traceLog);
            m_ServerRequestComputer = new ServerRequestComputer(this);
        }

        public override ScenarioStateMachine ScenarioStateMachine
        {
            get { return m_ServerRequestComputer; }
        }

        public override ScenarioThreadState[] ScenarioThreadState
        {
            get { return m_ThreadStateMap.ThreadState; }
        }

        internal ServerRequestThreadStateMap ThreadStateMap
        {
            get { return m_ThreadStateMap; }
        }

        #region Private
        private ServerRequestComputer m_ServerRequestComputer;

        private ServerRequestThreadStateMap m_ThreadStateMap;
        #endregion
    }

    /// <summary>
    /// Represents the state machine that encapsulates a set of server requests.
    /// </summary>
    public sealed class ServerRequestComputer : ScenarioStateMachine
    {
        public ServerRequestComputer(ServerRequestScenarioConfiguration configuration)
            : base(configuration)
        {
            m_ThreadStateMap = configuration.ThreadStateMap;
        }

        /// <summary>
        /// Execute the server request computer.
        /// </summary>
        public override void RegisterEventHandlers(TraceEventDispatcher eventDispatcher)
        {
            var aspNetParser = new AspNetTraceEventParser(eventDispatcher);
            var WCFParser = new ApplicationServerTraceEventParser(eventDispatcher);

            // ASP.NET Events.
            aspNetParser.AspNetReqStart += ASPNetReqStart;
            aspNetParser.AspNetReqStop += AspNetReqEnd;
            aspNetParser.AspNetReqAppDomainEnter += delegate (AspNetAppDomainEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqStartHandler += delegate (AspNetStartHandlerTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId, true);
            };
            aspNetParser.AspNetReqRoleManagerBegin += delegate (AspNetRoleManagerBeginTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqRoleManagerGetUserRoles += delegate (AspNetRoleManagerGetUserRolesTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqRoleManagerEnd += delegate (AspNetRoleManagerEndTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqMapHandlerEnter += delegate (AspNetMapHandlerEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqMapHandlerLeave += delegate (AspNetMapHandlerLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqHttpHandlerEnter += delegate (AspNetHttpHandlerEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqHttpHandlerLeave += delegate (AspNetHttpHandlerLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreInitEnter += delegate (AspNetPagePreInitEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreInitLeave += delegate (AspNetPagePreInitLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageInitEnter += delegate (AspNetPageInitEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageInitLeave += delegate (AspNetPageInitLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageLoadEnter += delegate (AspNetPageLoadEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageLoadLeave += delegate (AspNetPageLoadLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreRenderEnter += delegate (AspNetPagePreRenderEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPagePreRenderLeave += delegate (AspNetPagePreRenderLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageSaveViewstateEnter += delegate (AspNetPageSaveViewstateEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageSaveViewstateLeave += delegate (AspNetPageSaveViewstateLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageRenderEnter += delegate (AspNetPageRenderEnterTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };
            aspNetParser.AspNetReqPageRenderLeave += delegate (AspNetPageRenderLeaveTraceData data)
            {
                SetAspNetRequestForThread(data, data.ContextId);
            };

            // Explicitly skip the EndHandler event.  Consider replacing the End event with EndHandler.

            // WCF server operations.
            WCFParser.WebHostRequestStart += WebHostRequestStart;
            WCFParser.MessageReceivedByTransport += MessageReceivedByTransport;
            WCFParser.WebHostRequestStop += WebHostRequestStop;
            WCFParser.DecrementBusyCount += WebHostRequestStop;
            WCFParser.ServiceActivationStart += SetWCFRequestForThread;
            WCFParser.ServiceHostFactoryCreationStart += SetWCFRequestForThread;
            WCFParser.ServiceHostStarted += SetWCFRequestForThread;
            WCFParser.OperationInvoked += SetWCFRequestForThread;
            WCFParser.OperationCompleted += SetWCFRequestForThread;
            WCFParser.HttpMessageReceiveStart += SetWCFRequestForThread;
            WCFParser.HttpContextBeforeProcessAuthentication += SetWCFRequestForThread;
            WCFParser.TokenValidationStarted += SetWCFRequestForThread;
            WCFParser.MessageReadByEncoder += SetWCFRequestForThread;
            WCFParser.HttpResponseReceiveStart += SetWCFRequestForThread;
            WCFParser.SocketReadStop += SetWCFRequestForThread;
            WCFParser.SocketAsyncReadStop += SetWCFRequestForThread;
            WCFParser.SignatureVerificationStart += SetWCFRequestForThread;
            WCFParser.SignatureVerificationSuccess += SetWCFRequestForThread;
            WCFParser.ChannelReceiveStop += SetWCFRequestForThread;
            WCFParser.DispatchMessageStart += SetWCFRequestForThread;
            WCFParser.IncrementBusyCount += SetWCFRequestForThread;
            WCFParser.DispatchMessageBeforeAuthorization += SetWCFRequestForThread;
            WCFParser.ActionItemScheduled += SetWCFRequestForThread;
            WCFParser.GetServiceInstanceStart += SetWCFRequestForThread;
            WCFParser.GetServiceInstanceStop += SetWCFRequestForThread;
            WCFParser.ActionItemCallbackInvoked += SetWCFRequestForThread;
            WCFParser.ChannelReceiveStart += SetWCFRequestForThread;
            WCFParser.OutgoingMessageSecured += SetWCFRequestForThread;
            WCFParser.SocketWriteStart += SetWCFRequestForThread;
            WCFParser.SocketAsyncWriteStart += SetWCFRequestForThread;
            WCFParser.BinaryMessageEncodingStart += SetWCFRequestForThread;
            WCFParser.MtomMessageEncodingStart += SetWCFRequestForThread;
            WCFParser.TextMessageEncodingStart += SetWCFRequestForThread;
            WCFParser.BinaryMessageDecodingStart += SetWCFRequestForThread;
            WCFParser.MtomMessageDecodingStart += SetWCFRequestForThread;
            WCFParser.TextMessageDecodingStart += SetWCFRequestForThread;
            WCFParser.StreamedMessageWrittenByEncoder += SetWCFRequestForThread;
            WCFParser.MessageWrittenAsynchronouslyByEncoder += SetWCFRequestForThread;
            WCFParser.BufferedAsyncWriteStop += SetWCFRequestForThread;
            WCFParser.HttpPipelineProcessResponseStop += SetWCFRequestForThread;
            WCFParser.WebSocketAsyncWriteStop += SetWCFRequestForThread;
            WCFParser.MessageSentByTransport += SetWCFRequestForThread;
            WCFParser.HttpSendStop += SetWCFRequestForThread;
            WCFParser.DispatchMessageStop += SetWCFRequestForThread;
            WCFParser.DispatchSuccessful += SetWCFRequestForThread;

            // Server-side quota information.
            WCFParser.MaxReceivedMessageSizeExceeded += SetWCFRequestForThread;
            WCFParser.MaxPendingConnectionsExceeded += SetWCFRequestForThread;
            WCFParser.ReaderQuotaExceeded += SetWCFRequestForThread;
            WCFParser.NegotiateTokenAuthenticatorStateCacheExceeded += SetWCFRequestForThread;
            WCFParser.NegotiateTokenAuthenticatorStateCacheRatio += SetWCFRequestForThread;
            WCFParser.SecuritySessionRatio += SetWCFRequestForThread;
            WCFParser.PendingConnectionsRatio += SetWCFRequestForThread;
            WCFParser.ConcurrentCallsRatio += SetWCFRequestForThread;
            WCFParser.ConcurrentSessionsRatio += SetWCFRequestForThread;
            WCFParser.ConcurrentInstancesRatio += SetWCFRequestForThread;
            WCFParser.PendingAcceptsAtZero += SetWCFRequestForThread;

            // WCF client operations.
            WCFParser.ClientOperationPrepared += ClientOperationPrepared;
            WCFParser.ServiceChannelCallStop += ServiceChannelCallStop;
        }

        #region private
        /* Event Callbacks */
        private void ASPNetReqStart(AspNetStartTraceData data)
        {
            // Get the thread.
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "ASPNetReqStart {0}", data.ContextId);

            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ContextId);

            // TODO This is for robustness, it should not exist, but if it does remove it.   
            RemoveAspNetServerRequest(requestKey);

            ASPNetServerRequest aspNetServerRequest = GetOrCreateASPNetServerRequest(requestKey, thread, data.Path);
            Debug.Assert(aspNetServerRequest != null);
            m_ThreadStateMap[thread.ThreadIndex].Request = aspNetServerRequest;
        }

        private void AspNetReqEnd(AspNetStopTraceData data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "AspNetReqEnd {0}", data.ContextId);

            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ContextId);
            RemoveAspNetServerRequest(requestKey);
        }

        private void SetAspNetRequestForThread(TraceEvent data, Guid contextId, bool forceUpdate = false)
        {
            Debug.Assert(data != null);

            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "SetAspNetRequestForThread {0} force {1}", contextId, forceUpdate);

            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, contextId);
            var request = GetOrCreateASPNetServerRequest(requestKey, thread, "Unknown");

            // Set the state if we have nothing. 
            var threadState = m_ThreadStateMap[thread.ThreadIndex];
            if (threadState.Request == null || forceUpdate)
            {
                threadState.Request = request;
            }
        }

        private void WebHostRequestStart(Multidata69TemplateATraceData data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "WebHostRequestStart {0}, ASPNET ID {1}", data.ActivityID, data.RelatedActivityID);

            // Create the request key.
            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);

            var wcfServerRequest = GetOrCreateWCFServerRequest(requestKey, thread, data.RelatedActivityID);
            if (wcfServerRequest == null)
            {
                return;
            }

            // Mark this request as the overhead request.  This value will not get copied when we transfer to
            // a new request id when a message comes in on the wire.
            wcfServerRequest.OverheadRequest = true;

            var threadState = m_ThreadStateMap[thread.ThreadIndex];
            Debug.Assert(threadState.Request as WCFClientRequest == null);
            threadState.Request = wcfServerRequest;
        }

        // This represents the beginning of the message.
        // Each message is sent over the wire independently, but can be send within the same WCF "request" or ASP.NET request.
        private void MessageReceivedByTransport(Multidata29TemplateHATraceData data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "MessageReceivedByTransport {0}, PARENT ID {1}", data.ActivityID, data.RelatedActivityID);

            // Create the request for the 'group' of requests
            RequestKey parentRequestKey = new RequestKey(thread.Process.ProcessIndex, data.RelatedActivityID);

            // Map the request started by the invocation of the channel to request that runs the business logic.
            WCFServerRequest wcfServerRequest = null;
            if (m_wcfServerRequests.TryGetValue(parentRequestKey, out wcfServerRequest))
            {
                // Create a new request to represent this message.
                // Do not mark this as an overhead request, because this transfer event actually represents
                // a message on the wire.
                var requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);
                wcfServerRequest = GetOrCreateWCFServerRequest(requestKey, thread, wcfServerRequest.ASPNetRequest.RequestID);
                if (wcfServerRequest == null)
                {
                    return;
                }

                var threadState = m_ThreadStateMap[thread.ThreadIndex];
                if (threadState.Request as WCFClientRequest == null)
                {
                    threadState.Request = wcfServerRequest;
                }
            }
        }

        private void WebHostRequestStop(TraceEvent data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "WebHostRequestStop {0}", data.ActivityID);

            // Create the request key.
            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);
            RemoveWCFServerRequest(requestKey, null);
        }

        private void ClientOperationPrepared(Multidata22TemplateHATraceData data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "ClientOperationPrepared {0}", data.ActivityID);

            // Create the request key.
            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);

            // Create a client request.  We don't know the ASP.NET context so we take the innermost one.  
            WCFClientRequest clientRequest = GetOrCreateWCFClientRequest(requestKey, thread, Guid.Empty);
            if (clientRequest == null)
            {
                // IF we call a WCF call directly from ASP, then the call above will fail because there is
                // no WCF request.   In this case the WCF activity ID also happens to be the ASP.NET
                // context id.  Thus we can go ahead and make a WCF activity for it out of nothing.  
                if (m_aspNetServerRequests.ContainsKey(requestKey))
                {
                    clientRequest = GetOrCreateWCFClientRequest(requestKey, thread, data.ActivityID);
                }

                if (clientRequest == null)
                {
                    return;
                }
            }

            // Store necessary data
            clientRequest.ServiceAction = data.ServiceAction;
            clientRequest.RequestUrl = data.Destination;

#if DEBUG
            WCFServerRequest asWcf = m_ThreadStateMap[thread.ThreadIndex].Request as WCFServerRequest;
            Debug.Assert(asWcf == null || clientRequest.ServerRequest == asWcf);
            ASPNetServerRequest asAsp = m_ThreadStateMap[thread.ThreadIndex].Request as ASPNetServerRequest;
            Debug.Assert(asAsp == null || clientRequest.ServerRequest.ASPNetRequest == asAsp);
#endif
            m_ThreadStateMap[thread.ThreadIndex].Request = clientRequest;
        }

        private void ServiceChannelCallStop(Multidata22TemplateHATraceData data)
        {
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "ServiceChannelCallStop {0}", data.ActivityID);

            // Create the request key.
            RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);
            RemoveWCFClientRequest(requestKey);
        }

        private void SetWCFRequestForThread(TraceEvent data)
        {
            // The idea here is whenver we get an event that we can identify as belonging to a in flight WCF request we assume 
            // that from then on that that thread is processing the request.  
            TraceThread thread = data.Thread();
            if (null == thread)
            {
                return;
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "SetWCFRequestForThread {0}", data.ActivityID);

            Debug.Assert(data.ActivityID != Guid.Empty);
            if (data.ActivityID != Guid.Empty)
            {
                RequestKey requestKey = new RequestKey(thread.Process.ProcessIndex, data.ActivityID);
                var wcfServerRequest = GetOrCreateWCFServerRequest(requestKey, thread, Guid.Empty);
                if (wcfServerRequest == null)
                {
                    return;
                }

                var threadState = m_ThreadStateMap[thread.ThreadIndex];
                // Replace it if it is strictly better.  
                if (threadState.Request == null || wcfServerRequest.ASPNetRequest == threadState.Request as ASPNetServerRequest)
                {
                    threadState.Request = wcfServerRequest;
                }
            }
        }

        /* ASP.NET REQUEST */
        /// <summary>
        /// retrieves the ASP.NET request with ID 'requestKey'.  If it does not exist it is created with the url 'url' 
        /// (you should pass 'unknown' if you don't know it).   It also marks 'thread' to point at this new request
        /// and insures that no other thread is pointing at it.   This routine never returns null. 
        /// </summary>
        private ASPNetServerRequest GetOrCreateASPNetServerRequest(RequestKey requestKey, TraceThread thread, string url)
        {
            Debug.Assert(requestKey != null);

            ASPNetServerRequest aspNetServerRequest = null;
            if (m_aspNetServerRequests.TryGetValue(requestKey, out aspNetServerRequest))
            {
                if (m_ThreadStateMap[thread.ThreadIndex].Request == aspNetServerRequest)
                {
                    return aspNetServerRequest;
                }

                // TODO REVIEW:  This is moderately expensive.  
                // We currently only allow one active thread per ASP.NET request.  Thus by starting work on this thread 
                // we necessarily stop work on any thread that currently is doing work on this request.  
                ServerRequestComputer.DebugLog(0, thread.ThreadID, "SINGLE THREADED ASSUMPTION any existing ASP.NET Requests for {0}", requestKey.RequestID);
                m_ThreadStateMap.ReplaceRequest(aspNetServerRequest, null);
            }
            else
            {
                aspNetServerRequest = new ASPNetServerRequest(requestKey.RequestID, url);
                m_aspNetServerRequests[requestKey] = aspNetServerRequest;
            }

            Debug.Assert(aspNetServerRequest != null);
            return aspNetServerRequest;
        }

        private void RemoveAspNetServerRequest(RequestKey requestKey)
        {
            // Do nothing if it does not exist.  
            ASPNetServerRequest request;
            if (m_aspNetServerRequests.TryGetValue(requestKey, out request))
            {
                // Kill all my WCF requestss
                if (request.WCFServerRequests != null)
                {
                    var reqs = request.WCFServerRequests;
                    while (reqs.Count > 0)
                    {
                        var wcfServerRequestKey = new RequestKey(requestKey.ProcessIndex, reqs[reqs.Count - 1].RequestID);
                        Debug.Assert(m_wcfServerRequests.ContainsKey(wcfServerRequestKey));

                        int curCount = reqs.Count;
                        RemoveWCFServerRequest(wcfServerRequestKey, request);

                        // This is for robustness it should not happen 
                        if (curCount == reqs.Count)
                        {
                            Debug.Assert(false, "failure deleting wcfServer request");
                            reqs.RemoveAt(reqs.Count - 1);
                        }
                    }
                }

                // Remove from the per-request table
                m_aspNetServerRequests.Remove(requestKey);

                // Make sure no threads are pointing at us.  
                m_ThreadStateMap.ReplaceRequest(request, null);

#if DEBUG
                // If there are no requests active.  
                if (m_aspNetServerRequests.Count == 0)
                {
                    // All WCF server requests should be empty
                    Debug.Assert(m_wcfServerRequests.Count == 0);

                    // And there should be no threads pointing at anything.  
                    foreach (var thread in m_ThreadStateMap.ThreadState)
                    {
                        Debug.Assert(thread.Request == null);
                    }
                }
#endif
            }
        }

        /* WCF SERVER  */
        /// <summary>
        /// Gets a WCF request for the request with ID 'requestKey'.  Also marks 'thread' as pointing to this request.   
        /// If 'aspNetRequestId' is non-empty it attempts to make a new request with that ASP.NET request.  This can
        /// fail, in which case null is returned.  
        /// </summary>
        private WCFServerRequest GetOrCreateWCFServerRequest(RequestKey requestKey, TraceThread thread, Guid aspNetRequestId)
        {
            Debug.Assert(requestKey != null);

            WCFServerRequest next = null;
            WCFServerRequest request;
            if (m_wcfServerRequests.TryGetValue(requestKey, out request))
            {
#if false // TODO FIX NOW remove 
                // WCF Server request reuse the IDs for nested calls.  This is unfortunate.   We detect this situation because WCF requests 
                // should always be inside ASP.NET requests an those requests DO create distict IDs.   Thus if two WCF request have the
                // same ASP.NET parent, then they are not nested (we reuse the current WCF request), but otherwise we assume that they
                // are nested and 'push' this WCF request on a stack of WCF requests with the same ID.  
                var threadAspNetThreadRequest = GetAspNetServerRequestForThread(thread);

                // We really should have set an ASP.NET request before processing a WCF request, in this case give up on detecting nesting
                if (threadAspNetThreadRequest == null)
                    return request;

                // The requests are the same we don't have nested 
                if (request.ASPNetRequest == threadAspNetThreadRequest)
                {
                    Debug.Assert(aspNetRequestId == request.ASPNetRequest.RequestID || aspNetRequestId == Guid.Empty);
                       return request;
                }
#endif
                // If we are in the right context, return the request.   If  return if we are the right context.  
                if (request.ASPNetRequest.RequestID == aspNetRequestId)
                {
                    return request;
                }

                // Give up if we don't know our ASP.NET context we assume we are non-nested and return the most deeply nested thing
                if (aspNetRequestId == Guid.Empty)
                {
                    return request;
                }

                // Fall through and push a new request on the stack of WCFServerRequests.  
                next = request;
            }
            RequestKey aspNetRequestKey = new RequestKey(thread.Process.ProcessIndex, aspNetRequestId);

            ASPNetServerRequest aspNetRequest;
            if (!m_aspNetServerRequests.TryGetValue(aspNetRequestKey, out aspNetRequest))
            {
                // Most likely this is the case where ASP.NET logging is not enabled (which occurs in Server 2012)
                aspNetRequest = new ASPNetServerRequest(aspNetRequestId, "URL Unavailable");
            }

            // Make the new quest, and hook it up to the ASP.NET request as well as put it in the wcf server request table.  
            request = new WCFServerRequest(requestKey.RequestID, aspNetRequest, next);
            m_wcfServerRequests[requestKey] = request;
            aspNetRequest.WCFServerRequests.Add(request);

            return request;
        }
        /// <summary>
        /// Remove the WCF server request with id 'requestKey'  Since there an be several requests
        /// with this same ID, also supply the ASP.NET Request context.   Note that it will remove
        /// all requests until it finds 'aspNetRequest' (so if events were missing, the algorithm is
        /// robust (it cleans up).  If 'thread' is non-null, then update that thread to 
        /// </summary>
        private void RemoveWCFServerRequest(RequestKey requestKey, ASPNetServerRequest aspNetRequest)
        {
            WCFServerRequest request;
            if (m_wcfServerRequests.TryGetValue(requestKey, out request))
            {
                Debug.Assert(request != null);
                for (; ; )
                {
                    var done = (request.ASPNetRequest == aspNetRequest || aspNetRequest == null);
                    RemoveWCFServerRequestWorker(request, requestKey.ProcessIndex);
                    request = request.NextRequestWithTheSameID;
                    if (done || request == null)
                    {
                        break;
                    }
                }
                if (request != null)
                {
                    m_wcfServerRequests[requestKey] = request;
                }
                else
                {
                    m_wcfServerRequests.Remove(requestKey);
                }
            }
        }
        private void RemoveWCFServerRequestWorker(WCFServerRequest request, ProcessIndex processIndex)
        {
            var inList = request.ASPNetRequest.WCFServerRequests.Remove(request);
            Debug.Assert(inList);

            // Make sure no threads are pointing at my call out
            if (request.ClientRequest != null)
            {
                m_ThreadStateMap.ReplaceRequest(request.ClientRequest, request.ASPNetRequest);
            }

            // Make sure no threads are pointing at us.  
            m_ThreadStateMap.ReplaceRequest(request, request.ASPNetRequest);
        }

        /* WCF CLIENT */
        private WCFClientRequest GetOrCreateWCFClientRequest(RequestKey requestKey, TraceThread thread, Guid aspNetRequestID)
        {
            Debug.Assert(thread != null);

            // Try to take the most nested WCF request
            WCFServerRequest wcfServerRequest = GetOrCreateWCFServerRequest(requestKey, thread, aspNetRequestID);
            if (wcfServerRequest == null)
            {
                ServerRequestComputer.DebugLog(0, thread.ThreadID, "GetOrCreateWCFClientRequest FAILED NO SERVER REQUEST");
                return null;
            }

            WCFClientRequest request = wcfServerRequest.ClientRequest;
            if (request == null)
            {
                request = new WCFClientRequest(wcfServerRequest, requestKey.RequestID);
                wcfServerRequest.ClientRequest = request;
                ServerRequestComputer.DebugLog(0, thread.ThreadID, "GetOrCreateWCFClientRequest MAKING NEW REQUEST {0}", requestKey.RequestID);
            }
            else
            {
                ServerRequestComputer.DebugLog(0, thread.ThreadID, "GetOrCreateWCFClientRequest FINDING EXISTING REQUEST {0}", requestKey.RequestID);
            }

            return request;
        }
        private void RemoveWCFClientRequest(RequestKey requestKey)
        {
            WCFServerRequest wcfServerRequest;
            if (m_wcfServerRequests.TryGetValue(requestKey, out wcfServerRequest))
            {
                var wcfClientRequest = wcfServerRequest.ClientRequest;
                if (wcfClientRequest != null)
                {
                    wcfServerRequest.ClientRequest = null;
                    m_ThreadStateMap.ReplaceRequest(wcfClientRequest, wcfClientRequest.ServerRequest);
                }
            }
        }

#if DEBUG
        static TextWriter s_logFile = File.CreateText("ServerRequestLog.txt");
#endif
        [Conditional("DEBUG")]
        public static void DebugLog(double time, int threadID, string format)
        {
#if DEBUG
            if (s_logFile != null)
            {
                s_logFile.WriteLine("{0,9:f3} {1,4} {2}", time, threadID, format);
            }
#endif
        }
        [Conditional("DEBUG")]
        public static void DebugLog(double time, int threadID, string format, params object[] args)
        {
#if DEBUG
            if (s_logFile != null)
            {
                s_logFile.WriteLine("{0,9:f3} {1,4} {2}", time, threadID, string.Format(format, args));
            }
#endif
        }

        /// <summary>
        /// Maps thread Indexes to their current request.   Can also clear the state of any thread currently pointing at a particular request.  
        /// </summary>
        private ServerRequestThreadStateMap m_ThreadStateMap;
        /// <summary>
        /// The map of ASP.NET request ids to ASP.NET request objects.
        /// </summary>
        private Dictionary<RequestKey, ASPNetServerRequest> m_aspNetServerRequests = new Dictionary<RequestKey, ASPNetServerRequest>();
        /// <summary>
        /// The map of WCF request ids to WCF request objects.
        /// </summary>
        private Dictionary<RequestKey, WCFServerRequest> m_wcfServerRequests = new Dictionary<RequestKey, WCFServerRequest>();

        #endregion
    }

    /// <summary>
    /// The base class for thread state associated with a server request.
    /// </summary>
    internal class ServerRequestThreadState : ScenarioThreadState
    {
        /// <summary>
        /// The request currently associated with the thread.
        /// </summary>
        internal ServerRequest Request { get; set; }

        /// <summary>
        /// Computes that part of the frame from the root of app processes up to (but not including)
        /// the thread frame.   
        /// </summary>
        public override StackSourceCallStackIndex GetCallStackIndex(MutableTraceEventStackSource stackSource, TraceThread thread, TraceEvent data)
        {
            // If there is an active request, add those frames 
            if (Request == null || IsCSwitchBlockedInThreadPool(data))
            {
                Request = null;
                return base.GetCallStackIndex(stackSource, thread, data);
            }

            // Return a stack that has a request.  
            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, thread.ThreadID, "GetCallStackIndex adding request logic INCLUDE");

            // Get the call stack index for the process and root of all processes
            StackSourceCallStackIndex callStackIndex = stackSource.GetCallStackForProcess(thread.Process);

            // Call out to the request to get the stack.
            callStackIndex = Request.GetCallStackIndex(callStackIndex, stackSource, thread);
            return callStackIndex;
        }

        public static bool IsCSwitchBlockedInThreadPool(TraceEvent data)
        {
            if (!(data is CSwitchTraceData))
            {
                return false;
            }

            var callStack = data.CallStackIndex();
            if (callStack == CallStackIndex.Invalid)
            {
                return false;
            }

            // Does the CSWITCH stack only contain thread pool modules.  In which case we assume we blocked in the thread pool.  
            var traceLog = (TraceLog)data.Source;
            var callStacks = traceLog.CallStacks;
            var codeAddresses = traceLog.CodeAddresses;
            while (callStack != CallStackIndex.Invalid)
            {
                var codeAddrIdx = callStacks.CodeAddressIndex(callStack);
                var module = codeAddresses.ModuleFile(codeAddrIdx);
                if (module == null)
                {
                    return false;
                }

                var moduleName = module.Name;
                if (!moduleName.StartsWith("wow", StringComparison.OrdinalIgnoreCase) &&
                    !moduleName.StartsWith("kernel", StringComparison.OrdinalIgnoreCase) &&
                    string.Compare(moduleName, "ntoskrnl", StringComparison.OrdinalIgnoreCase) != 0 &&
                    string.Compare(moduleName, "ntdll", StringComparison.OrdinalIgnoreCase) != 0 &&
                    string.Compare(moduleName, "w3tp", StringComparison.OrdinalIgnoreCase) != 0 &&
                    string.Compare(moduleName, "clr", StringComparison.OrdinalIgnoreCase) != 0 &&
                    string.Compare(moduleName, "mscorwks", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }

                callStack = callStacks.Caller(callStack);
            }

            ServerRequestComputer.DebugLog(data.TimeStampRelativeMSec, data.ThreadID, "GetCallStackIndex CSWITCH in threadpool EXCLUDE");
            return true;
        }
    }

    #region private classes

    /// <summary>
    /// The base class for all request types.
    /// </summary>
    /// <remarks>
    /// This class needs to exist such that we don't have to keep creating thread state objects when we clear a request off of a thread.
    /// </remarks>
    internal class ServerRequest
    {
        /// <summary>
        /// Get the call stack index for the group of all requests.
        /// </summary>
        public virtual StackSourceCallStackIndex GetCallStackIndex(
            StackSourceCallStackIndex rootCallStackIndex,
            MutableTraceEventStackSource stackSource,
            TraceThread thread)
        {
            // Add a pseudo node for requests.
            string frameName = "Requests";
            StackSourceFrameIndex requestsFrameIndex = stackSource.Interner.FrameIntern(frameName);
            StackSourceCallStackIndex callStackIndex = stackSource.Interner.CallStackIntern(requestsFrameIndex, rootCallStackIndex);

            return callStackIndex;
        }

        /// <summary>
        /// The unique id of the request.
        /// </summary>
        public Guid RequestID { get; protected set; }
    }

    /// <summary>
    /// Represents a server-side ASP.NET request.  
    /// </summary>
    internal sealed class ASPNetServerRequest : ServerRequest
    {
        public ASPNetServerRequest(Guid requestID, string requestUrl) { RequestID = requestID; RequestUrl = requestUrl; }

        /// <summary>
        /// The request url.
        /// </summary>
        public string RequestUrl { get; private set; }

        /// <summary>
        /// Get the list of WCF server-side requests spawned by this ASP.NET request.
        /// </summary>
        public List<WCFServerRequest> WCFServerRequests
        {
            get
            {
                if (null == m_WCFServerRequests)
                {
                    m_WCFServerRequests = new List<WCFServerRequest>();
                }

                return m_WCFServerRequests;
            }
        }

        /// <summary>
        /// Get the call stack index for the current request.
        /// </summary>
        public override StackSourceCallStackIndex GetCallStackIndex(
            StackSourceCallStackIndex rootCallStackIndex,
            MutableTraceEventStackSource stackSource,
            TraceThread thread)
        {
            if (null == stackSource)
            {
                throw new ArgumentNullException("stackSource");
            }

            if (null == thread)
            {
                throw new ArgumentNullException("thread");
            }

            // Get the root call stack index.
            rootCallStackIndex = base.GetCallStackIndex(rootCallStackIndex, stackSource, thread);

            // Add a stack frame for the request url.
            // Decide if the grouping by URL is a good idea.  
            // StackSourceFrameIndex aspNetRequestUrlFrameIndex = stackSource.Interner.FrameIntern("URL: " + this.RequestUrl);
            // rootCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestUrlFrameIndex, rootCallStackIndex);

            // Add a stack frame for the ASP.NET request.
            StackSourceFrameIndex aspNetRequestFrameIndex = stackSource.Interner.FrameIntern(ToString());
            StackSourceCallStackIndex aspNetCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestFrameIndex, rootCallStackIndex);

            return aspNetCallStackIndex;
        }

        /// <summary>
        /// Get a displayable string for the request.
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                "ASP.NET Request: {0} URL: {1}",
                RequestID.ToString(), RequestUrl);
        }

        #region private
        /// <summary>
        /// The list of WCF server-side requests spawned by this ASP.NET request.
        /// </summary>
        private List<WCFServerRequest> m_WCFServerRequests;

        #endregion
    }

    /// <summary>
    /// Represents a server-side WCF request.
    /// </summary>
    internal sealed class WCFServerRequest : ServerRequest
    {
        public WCFServerRequest(Guid requestID, ASPNetServerRequest aspNetRequest, WCFServerRequest nextRequestWithSameID)
        {
            RequestID = requestID;
            ASPNetRequest = aspNetRequest;
            NextRequestWithTheSameID = nextRequestWithSameID;
        }

        /// <summary>
        /// The ASP.NET request associated with this WCF request.
        /// </summary>
        public ASPNetServerRequest ASPNetRequest { get; private set; }


        /// <summary>
        /// True iff this request is the WCF overhead request (used before a message is received).
        /// </summary>
        public bool OverheadRequest { get; set; }

        /// <summary>
        /// WCF makes the request ID the ID of the top most WCF request.  
        /// This works fine if there all calls from one WCF component to another
        /// are on different processes (e.g. machines).  However if not, then
        /// the same request ID might have more than one request associated with it
        /// We supprot this by making a linked list of these.  
        /// </summary>
        public WCFServerRequest NextRequestWithTheSameID { get; private set; }

        public WCFClientRequest ClientRequest { get; set; }

        /// <summary>
        /// Get the call stack index for the current request.
        /// </summary>
        public override StackSourceCallStackIndex GetCallStackIndex(
            StackSourceCallStackIndex rootCallStackIndex,
            MutableTraceEventStackSource stackSource,
            TraceThread thread)
        {
            if (null == stackSource)
            {
                throw new ArgumentNullException("stackSource");
            }

            if (null == thread)
            {
                throw new ArgumentNullException("thread");
            }

            // Get the root call stack index.
            StackSourceCallStackIndex callStackIndex = base.GetCallStackIndex(rootCallStackIndex, stackSource, thread);

            // Add a stack frame for the ASP.NET request url.
            StackSourceFrameIndex aspNetRequestUrlFrameIndex = stackSource.Interner.FrameIntern(ASPNetRequest.RequestUrl);
            StackSourceCallStackIndex aspNetRequestCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestUrlFrameIndex, callStackIndex);

            // Add a stack frame for the ASP.NET request.
            StackSourceFrameIndex aspNetRequestFrameIndex = stackSource.Interner.FrameIntern(ASPNetRequest.ToString());
            StackSourceCallStackIndex aspNetCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestFrameIndex, aspNetRequestCallStackIndex);

            // Add a stack frame for the WCF server request.
            StackSourceFrameIndex requestFrameIndex = stackSource.Interner.FrameIntern(ToString());
            StackSourceCallStackIndex requestStackIndex = stackSource.Interner.CallStackIntern(requestFrameIndex, aspNetCallStackIndex);

            return requestStackIndex;
        }

        /// <summary>
        /// Serialize the request to a displayable string.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string requestString = null;
            if (ASPNetRequest.RequestID == Guid.Empty)
            {
                if (OverheadRequest)
                {
                    requestString = "WCF Transport Overhead";
                }
                else
                {
                    requestString = string.Format("WCF Request: {0}", RequestID);
                }
            }
            else
            {
                if (OverheadRequest)
                {
                    requestString = string.Format("WCF Transport Overhead - ASP.NET Request: {0}", ASPNetRequest.RequestID);
                }
                else
                {
                    requestString = string.Format("WCF Request: {0} ASP.NET Request: {1}", RequestID, ASPNetRequest.RequestID);
                }
            }

            return requestString;
        }
    }

    /// <summary>
    /// Represents a client-side WCF request.
    /// </summary>
    internal sealed class WCFClientRequest : ServerRequest
    {
        public WCFClientRequest(WCFServerRequest serverRequest, Guid requestID)
        {
            if (null == serverRequest)
            {
                throw new ArgumentNullException("serverRequest");
            }

            m_ServerRequest = serverRequest;
            RequestID = requestID;
        }

        public string RequestUrl { get; set; }

        /// <summary>
        /// The service action.
        /// </summary>
        public string ServiceAction { get; set; }

        /// <summary>
        /// The parent server request.
        /// </summary>
        public WCFServerRequest ServerRequest { get { return m_ServerRequest; } }

        /// <summary>
        /// Serialize this request to a displayable string.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format(
                "Outgoing WCF Request with ID '{0}' to '{1}' with service action '{2}'",
                RequestID,
                RequestUrl,
                ServiceAction);
        }

        /// <summary>
        /// Get the call stack index for the current request.
        /// </summary>
        public override StackSourceCallStackIndex GetCallStackIndex(
            StackSourceCallStackIndex rootCallStackIndex,
            MutableTraceEventStackSource stackSource,
            TraceThread thread)
        {
            if (null == stackSource)
            {
                throw new ArgumentNullException("stackSource");
            }

            if (null == thread)
            {
                throw new ArgumentNullException("thread");
            }

            // Get the root call stack index.
            StackSourceCallStackIndex callStackIndex = base.GetCallStackIndex(rootCallStackIndex, stackSource, thread);

            // Add a stack frame for the ASP.NET request url.
            StackSourceFrameIndex aspNetRequestUrlFrameIndex = stackSource.Interner.FrameIntern(m_ServerRequest.ASPNetRequest.RequestUrl);
            StackSourceCallStackIndex aspNetRequestCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestUrlFrameIndex, callStackIndex);

            // Add a stack frame for the ASP.NET request.
            StackSourceFrameIndex aspNetRequestFrameIndex = stackSource.Interner.FrameIntern(m_ServerRequest.ASPNetRequest.ToString());
            StackSourceCallStackIndex aspNetCallStackIndex = stackSource.Interner.CallStackIntern(aspNetRequestFrameIndex, aspNetRequestCallStackIndex);

            // Add a stack frame for the WCF server request.
            StackSourceFrameIndex serverRequestFrameIndex = stackSource.Interner.FrameIntern(m_ServerRequest.ToString());
            StackSourceCallStackIndex serverRequestStackIndex = stackSource.Interner.CallStackIntern(serverRequestFrameIndex, aspNetCallStackIndex);

            // Add a stack frame for the WCF client request.
            StackSourceFrameIndex requestFrameIndex = stackSource.Interner.FrameIntern(ToString());
            StackSourceCallStackIndex requestStackIndex = stackSource.Interner.CallStackIntern(requestFrameIndex, serverRequestStackIndex);

            return requestStackIndex;
        }

        #region private
        private WCFServerRequest m_ServerRequest;
        #endregion
    }

    /// <summary>
    /// The composite key used to lookup a request.  The key is composed of the request id and the process id.
    /// </summary>
    internal sealed class RequestKey : IEquatable<RequestKey>
    {
        public RequestKey(ProcessIndex processIndex, Guid requestId) { m_ProcessIndex = (int)processIndex; m_RequestID = requestId; }
        public ProcessIndex ProcessIndex { get { return (ProcessIndex)m_ProcessIndex; } }
        public Guid RequestID { get { return m_RequestID; } }
        public override bool Equals(object obj) { throw new NotImplementedException(); }
        public bool Equals(RequestKey other) { return m_RequestID == other.m_RequestID && m_ProcessIndex == other.m_ProcessIndex; }
        public override int GetHashCode() { return m_RequestID.GetHashCode() ^ m_ProcessIndex; }
        #region private
        private int m_ProcessIndex;
        private Guid m_RequestID;
        #endregion
    }

    /// <summary>
    /// A simple class that immplements lookup by Thread index and bulk clearning by request identity (Clear Request)
    /// 
    /// It holds all state (ServerRequestThreadState) for all threads.  
    /// </summary>
    internal sealed class ServerRequestThreadStateMap
    {
        internal ServerRequestThreadStateMap(TraceLog traceLog)
        {
            m_traceLog = traceLog;
            m_ThreadState = new ServerRequestThreadState[traceLog.Threads.Count];
            for (int i = 0; i < m_ThreadState.Length; ++i)
            {
                m_ThreadState[i] = new ServerRequestThreadState();
            }
        }

        internal ServerRequestThreadState this[ThreadIndex index]
        {
            get { return m_ThreadState[(int)index]; }
        }

        internal ServerRequestThreadState[] ThreadState
        {
            get { return m_ThreadState; }
        }

        internal void ReplaceRequest(ServerRequest serverRequest, ServerRequest replacementRequest)
        {
            for (int i = 0; i < m_ThreadState.Length; ++i)
            {
                ServerRequestThreadState threadState = m_ThreadState[i];
                if (threadState.Request == serverRequest)
                {
                    ServerRequestComputer.DebugLog(0, m_traceLog.Threads[(ThreadIndex)i].ThreadID, "ReplaceRequest {0} -> {1}", threadState.Request.RequestID, replacementRequest == null ? "NULL" : replacementRequest.ToString());
                    threadState.Request = replacementRequest;
                }
            }
        }

        private ServerRequestThreadState[] m_ThreadState;
        private TraceLog m_traceLog;
    }
    #endregion
}
