#pragma once

// Headers needed for CLR Profiling
#include <cor.h>
#include <corprof.h>
#include <corhlpr.h>

#include <unordered_map> 

class ClassInfo;
class ModuleInfo;

// ==========================================================================
// CorProfileTracer is the main routine that implemented that .NET Profiler
// API and responds by generating ETW events.   Basically it implemented a
// bunch of callback functions that simply log the cooresponding ETW event 
//
// Because this class's COM GUID is passed to the .Net Runtime (either in
// the COR_PROFILER environment variable or in the AttachProfiler API
// the runtime will create one of these objects and immediately call the
// Initialize or InitializeForAttach callback.  It is in these routines
// where the real initializaiton occurs.  
//
// See comment in the beginning of CorProfilerTracer.cpp for more 
class CorProfilerTracer : public ICorProfilerCallback3
{
public:
	CorProfilerTracer();
	~CorProfilerTracer();

	// IUnknown interface implementation
	STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&m_refCount); }
	STDMETHODIMP_(ULONG) Release() { auto ret = InterlockedDecrement(&m_refCount); if (ret <= 0) delete(this); return ret; }
	STDMETHODIMP QueryInterface(REFIID riid, void **ppInterface);

	// ICorProfilerCallback interface implementation
	STDMETHODIMP Initialize(IUnknown * pICorProfilerInfoUnk) { return InitializeForAttach(pICorProfilerInfoUnk, NULL, -1); }
	STDMETHODIMP Shutdown();

	// ICorProfilerCallback3
	HRESULT STDMETHODCALLTYPE InitializeForAttach(
		/* [in] */ IUnknown *pCorProfilerInfoUnk,
		/* [in] */ void *pvClientData,
		/* [in] */ UINT cbClientData);
	HRESULT STDMETHODCALLTYPE ProfilerAttachComplete(void) { return S_OK; };
	HRESULT STDMETHODCALLTYPE ProfilerDetachSucceeded(void)
	{
		LOG_TRACE(L"ProfilerDetachSucceeded\n");
		return Shutdown();
	}

	STDMETHODIMP AppDomainCreationStarted(AppDomainID appDomainId) { return S_OK; };
	STDMETHODIMP AppDomainCreationFinished(AppDomainID appDomainId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP AppDomainShutdownStarted(AppDomainID appDomainId) { return S_OK; };
	STDMETHODIMP AppDomainShutdownFinished(AppDomainID appDomainId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP AssemblyLoadStarted(AssemblyID assemblyId) { return S_OK; };
	STDMETHODIMP AssemblyLoadFinished(AssemblyID assemblyId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP AssemblyUnloadStarted(AssemblyID assemblyId) { return S_OK; };
	STDMETHODIMP AssemblyUnloadFinished(AssemblyID assemblyId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP ModuleLoadStarted(ModuleID moduleId) { return S_OK; };
	STDMETHODIMP ModuleLoadFinished(ModuleID moduleId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP ModuleUnloadStarted(ModuleID moduleId) { return S_OK; };
	STDMETHODIMP ModuleUnloadFinished(ModuleID moduleId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP ModuleAttachedToAssembly(ModuleID moduleId, AssemblyID assemblyId);
	STDMETHODIMP ClassLoadStarted(ClassID classId) { return S_OK; };
	STDMETHODIMP ClassLoadFinished(ClassID classId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP ClassUnloadStarted(ClassID classId) { return S_OK; };
	STDMETHODIMP ClassUnloadFinished(ClassID classId, HRESULT hrStatus) { return S_OK; };
	STDMETHODIMP FunctionUnloadStarted(FunctionID functionId) { return S_OK; };
	STDMETHODIMP JITCompilationStarted(FunctionID functionId, BOOL fIsSafeToBlock) { return S_OK; };
	STDMETHODIMP JITCompilationFinished(FunctionID functionId, HRESULT hrStatus, BOOL fIsSafeToBlock) { return S_OK; };
	STDMETHODIMP JITCachedFunctionSearchStarted(FunctionID functionId, BOOL * pbUseCachedFunction) { return S_OK; };
	STDMETHODIMP JITCachedFunctionSearchFinished(FunctionID functionId, COR_PRF_JIT_CACHE result) { return S_OK; };
	STDMETHODIMP JITFunctionPitched(FunctionID functionId) { return S_OK; };
	STDMETHODIMP JITInlining(FunctionID callerId, FunctionID calleeId, BOOL * pfShouldInline) { return S_OK; };
	STDMETHODIMP ThreadCreated(ThreadID threadId) { return S_OK; };
	STDMETHODIMP ThreadDestroyed(ThreadID threadId) { return S_OK; };
	STDMETHODIMP ThreadAssignedToOSThread(ThreadID managedThreadId, ULONG osThreadId) { return S_OK; };
	STDMETHODIMP RemotingClientInvocationStarted() { return S_OK; };
	STDMETHODIMP RemotingClientSendingMessage(GUID * pCookie, BOOL fIsAsync) { return S_OK; };
	STDMETHODIMP RemotingClientReceivingReply(GUID * pCookie, BOOL fIsAsync) { return S_OK; };
	STDMETHODIMP RemotingClientInvocationFinished() { return S_OK; };
	STDMETHODIMP RemotingServerReceivingMessage(GUID * pCookie, BOOL fIsAsync) { return S_OK; };
	STDMETHODIMP RemotingServerInvocationStarted() { return S_OK; };
	STDMETHODIMP RemotingServerInvocationReturned() { return S_OK; };
	STDMETHODIMP RemotingServerSendingReply(GUID * pCookie, BOOL fIsAsync) { return S_OK; };
	STDMETHODIMP UnmanagedToManagedTransition(FunctionID functionId, COR_PRF_TRANSITION_REASON reason) { return S_OK; };
	STDMETHODIMP ManagedToUnmanagedTransition(FunctionID functionId, COR_PRF_TRANSITION_REASON reason) { return S_OK; };
	STDMETHODIMP RuntimeSuspendStarted(COR_PRF_SUSPEND_REASON suspendReason) { return S_OK; };
	STDMETHODIMP RuntimeSuspendFinished() { return S_OK; };
	STDMETHODIMP RuntimeSuspendAborted() { return S_OK; };
	STDMETHODIMP RuntimeResumeStarted() { return S_OK; };
	STDMETHODIMP RuntimeResumeFinished() { return S_OK; };
	STDMETHODIMP RuntimeThreadSuspended(ThreadID threadId) { return S_OK; };
	STDMETHODIMP RuntimeThreadResumed(ThreadID threadId) { return S_OK; };
	STDMETHODIMP MovedReferences(ULONG cMovedObjectIDRanges, ObjectID oldObjectIDRangeStart[], ObjectID newObjectIDRangeStart[], ULONG cObjectIDRangeLength[]);
	STDMETHODIMP ObjectAllocated(ObjectID objectId, ClassID classId);
	STDMETHODIMP ObjectsAllocatedByClass(ULONG cClassCount, ClassID classIds[], ULONG cObjects[]) { return S_OK; };
	STDMETHODIMP ObjectReferences(ObjectID objectId, ClassID classId, ULONG cObjectRefs, ObjectID objectRefIds[]);
	STDMETHODIMP RootReferences(ULONG cRootRefs, ObjectID rootRefIds[]) { return S_OK; }
	STDMETHODIMP ExceptionThrown(ObjectID thrownObjectId) { return S_OK; };
	STDMETHODIMP ExceptionSearchFunctionEnter(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionSearchFunctionLeave() { return S_OK; };
	STDMETHODIMP ExceptionSearchFilterEnter(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionSearchFilterLeave() { return S_OK; };
	STDMETHODIMP ExceptionSearchCatcherFound(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionOSHandlerEnter(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionOSHandlerLeave(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionUnwindFunctionEnter(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionUnwindFunctionLeave() { return S_OK; };
	STDMETHODIMP ExceptionUnwindFinallyEnter(FunctionID functionId) { return S_OK; };
	STDMETHODIMP ExceptionUnwindFinallyLeave() { return S_OK; };
	STDMETHODIMP ExceptionCatcherEnter(FunctionID functionId, ObjectID objectId) { return S_OK; };
	STDMETHODIMP ExceptionCatcherLeave() { return S_OK; };
	STDMETHODIMP COMClassicVTableCreated(ClassID wrappedClassId, REFGUID implementedIID, void *pVTable, ULONG cSlots) { return S_OK; };
	STDMETHODIMP COMClassicVTableDestroyed(ClassID wrappedClassId, REFGUID implementedIID, void *pVTable) { return S_OK; };
	STDMETHODIMP ExceptionCLRCatcherFound(void) { return S_OK; };
	STDMETHODIMP ExceptionCLRCatcherExecute(void) { return S_OK; };

	// End of ICorProfilerCallback interface implementation

	// ICorProfilerCallback2 interface implementation

	STDMETHODIMP ThreadNameChanged(ThreadID threadId, ULONG cchName, WCHAR* name) { return S_OK; };
	STDMETHODIMP GarbageCollectionStarted(int cGenerations, BOOL generationCollected[], COR_PRF_GC_REASON reason);
	STDMETHODIMP SurvivingReferences(ULONG cSurvivingObjectIDRanges, ObjectID objectIDRangeStart[], ULONG cObjectIDRangeLength[]);

	STDMETHODIMP GarbageCollectionFinished(void);
	STDMETHODIMP FinalizeableObjectQueued(DWORD finalizerFlags, ObjectID objectID);
	STDMETHODIMP RootReferences2(ULONG cRootRefs, ObjectID rootRefIds[], COR_PRF_GC_ROOT_KIND rootKinds[], COR_PRF_GC_ROOT_FLAGS rootFlags[], UINT_PTR rootIds[]);
	STDMETHODIMP HandleCreated(GCHandleID handleId, ObjectID initialObjectId);
	STDMETHODIMP HandleDestroyed(GCHandleID handleId);

	void DoETWCommand(ULONG IsEnabled, UCHAR Level, ULONGLONG MatchAnyKeywords, struct _EVENT_FILTER_DESCRIPTOR* filterData);
private: // Methods
	ClassInfo* GetClassInfo(ClassID classId);
	ModuleInfo* GetModuleInfo(ModuleID moduleId);
	void ClearTables();
	void DumpClassInfo();
	static DWORD WINAPI ForceGCBody(LPVOID lpParameter);
	void ForceGC();

private: // Fields
	CRITICAL_SECTION          m_lock;

	LONG                      m_refCount;

	bool                      m_profilerLoadedAtStartup;
	bool					  m_forcingGC;
	bool					  m_detaching;
	bool                      m_sentManifest;
	ULONGLONG                 m_currentKeywords;

	// handle to query the runtime about stuff. 
	struct ICorProfilerInfo3* m_info;
	// If we have sampling turned on, m_curAllocSize tells you how close to the sampleSize we are.  
	int					     m_curAllocSize;
	// Do we have smart sampling that does sampling per type after a certain number of instances are collected.  
	bool					 m_smartSampling;
	int						 m_gcCount;

	// We want to cache the information (e.g. name, token, ...) on classes and modules.  
	std::unordered_map<ClassID, ClassInfo*> m_classInfo;
	std::unordered_map<ModuleID, ModuleInfo*> m_moduleInfo;
};
