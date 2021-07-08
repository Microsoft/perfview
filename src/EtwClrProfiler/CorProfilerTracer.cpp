#include "Stdafx.h"
// #define PIN_INVESTIGATION 1
//
// The GUID for our ETW provider is 6652970f-1756-5d8d-0805-e9aad152aa84 
// This GUID follows EventSource conventions for the name 'ETWClrProfiler', 
// which means  PerfView /providers=*ETWClrProfiler works.
// 
// The GUID for our CLR Profiler COM object is the ETW provider GUID 6652970f-1756-5d8d-0805-e9aad152aa84
// 
// Thus if you do the following
//
// set COR_PROFILER={6652970f-1756-5d8d-0805-e9aad152aa84}
// set COR_PROFILER_PATH=<Path to ETWClrProfiler.dll>
// set COR_ENABLE_PROFILING=1
// 
// <Run your app, ETWClrProfiler should be loaded>
// 
// REM to turn on the profiler you can then do 
// 
// PerfView /Providers=*ETWClrProfiler collect 
//
// The debug version of this code will generate a log file 
//      trace_provider.log
//============================================================================

// Defines the EventWrite* operations.  
#include "ETWInterface.h"

#define MaxEventPayload 0xFD00       // Maximum payload size for an ETW event (with some spare for small amounts of 'header' information. 

//============================================================================
// Elements of this class are put in the m_classInfo to remember things about our class
class ClassInfo
{
public:
	ClassInfo() {
		ID = 0; Token = 0; ModuleInfo = NULL; Size = 0; Flags = (CorTypeAttr)0; Name = NULL;
		elemType = ELEMENT_TYPE_END; elemClassId = 0; rank = 0;
		TickOfCurrentTimeBucket = 0; AllocCountInCurrentBucket = 0; AllocPerMSec = 0; SamplingRate = 0; AllocsIgnored = 0; IgnoredSize = 0;
		ForceKeepSize = 10000;			// By default we keep all instances greater than 10K for all types.  
	}
	~ClassInfo() { if (Name != NULL) delete Name; }

	ClassID ID;
	wchar_t* Name;              // We DO own this pointer (we delete it when we die)
	bool IsArray;

	// Set if this an array
	CorElementType elemType;
	ClassID elemClassId;
	ULONG rank;

	// Only set if this is a normal class
	mdTypeDef Token;
	ULONGLONG Size;
	CorTypeAttr Flags;
	ModuleInfo* ModuleInfo;     // We don't own this pointer (we don't delete it when we die)

	/* Used for smart sampling.  */
	int TickOfCurrentTimeBucket;
	int AllocCountInCurrentBucket;
	float AllocPerMSec;			// This is a exponential window average of the allocation rate. 

	ULONG SamplingRate;			// The number of data points to ignore before taking a sample (start out 0, adjusted to keep 'AllocPerSec' in line)
	ULONG AllocsIgnored;			// The current number of data points ignored.  
	ULONG IgnoredSize;
	ULONG ForceKeepSize;			// objects above this value will be kept unconditionally.   Setting to 0 forces all instances of this type to be kept. 
};

//============================================================================
// Elements of this class are put in the m_moduleInfo to remember things about our module
class ModuleInfo
{
public:
	ModuleInfo(ModuleID moduleId) : ID(moduleId), MetaDataFailed(false) { AssemblyID = 0; MetaDataImport = NULL; Path = NULL; }
	~ModuleInfo() {
		if (MetaDataImport != NULL) MetaDataImport->Release();
		if (Path != NULL) delete Path;
	}

	const ModuleID ID;
	bool MetaDataFailed;
	AssemblyID AssemblyID;
	IMetaDataImport* MetaDataImport;    // We Release() this pointer on when we die
	wchar_t* Path;                      // We DO own this pointer (we delete it when we die)
};

//============================================================================
// We registered this in ::Initialize to be invoked when there are ETW commands
// It just forwards to DoETWCommand
void WINAPI ProfilerControlCallback(
	LPCGUID SourceId,
	ULONG IsEnabled,
	UCHAR Level,
	ULONGLONG MatchAnyKeywords,
	ULONGLONG MatchAllKeywords,
	PEVENT_FILTER_DESCRIPTOR FilterData,
	PVOID CallbackContext)
{
	CorProfilerTracer* profiler = (CorProfilerTracer*)CallbackContext;
	LOG_TRACE(L"ProfilerControlCallback DoETWCommand IsEnabled 0x%x Level 0x%xI64 MatchAny 0x%x\n", IsEnabled, Level, MatchAnyKeywords);
	profiler->DoETWCommand(IsEnabled, Level, MatchAnyKeywords, FilterData);
}

//==============================================================================
// Used for call count profiling

// TODO presently unused.   Only needed if the callback needs the TracerState
// static CorProfilerTracer* s_tracer = NULL;

//************************
EXTERN_C int CallSampleCount = 1;	// This counts down to 0 for sampling 
int CallSamplingRate = 1;			// The number of calls to skip before taking a sample.  

EXTERN_C void __stdcall EnterMethod(FunctionID functionID)
{
	EventWriteCallEnterEvent(functionID, CallSamplingRate);
	CallSampleCount = CallSamplingRate;
}

#if defined(_M_IX86)
// see http://msdn.microsoft.com/en-us/library/4ks26t93.aspx  for inline assembly.   Not supported on X64.   

void __declspec(naked) __stdcall EnterMethodNaked(FunctionIDOrClientID funcID)
{
	__asm
	{
		lock dec[CallSampleCount]
		jle TakeSample
		ret 4

		TakeSample:
		push eax
			push ecx
			push edx
			push[esp + 16]		// Push the function ID
			call EnterMethod
			pop edx
			pop ecx
			pop eax
			ret 4
	}
} // EnterNaked

void __declspec(naked) __stdcall TailcallMethodNaked(FunctionIDOrClientID funcID)
{
	__asm
	{
		jmp EnterMethodNaked
	}
}

#else
EXTERN_C void __stdcall EnterMethodNaked(FunctionIDOrClientID functionID);
EXTERN_C void __stdcall TailcallMethodNaked(FunctionIDOrClientID functionID);
#endif 

//==========================================================================
// The constructor does almost nothing because we need the ability to call
// back to the runtime.   Thus the 'real' initialization happens in
// this routine.   In particular we register ProfilerControlCallback with
// ETW.
// We make Initialize call this routine with pvClientData = NULL, cbClientData = -1;
HRESULT STDMETHODCALLTYPE CorProfilerTracer::InitializeForAttach(
	/* [in] */ IUnknown *pICorProfilerInfoUnk,
	/* [in] */ void *pvClientData,
	/* [in] */ UINT cbClientData)
{
	HRESULT             hr = S_OK;
	LOG_TRACE(L"ClrProfiler Initializing\n");
	CALL_N_LOGONBADHR(pICorProfilerInfoUnk->QueryInterface(__uuidof(ICorProfilerInfo3), (void **)&m_info));

	// In my Initialize() method I call InitializeForAttach with cbClientData == -1.   This is convenient 
	// since most of the logic is the same.  
	m_profilerLoadedAtStartup = ((int)cbClientData < 0);

	// Initialize the ETW Provider.  
	LOG_TRACE(L"Registering the ETW provider\n");
	CALL_N_LOGONBADHR(EventRegisterETWClrProfiler(ProfilerControlCallback, this));

	// If we are not attaching, turn on all the events we can only turned on at init time.  
	if (m_profilerLoadedAtStartup)
	{
		// Even if we did not ask for them, turn on the profiler flags that can ONLY be done at startup. 
		DWORD oldFlags = 0;
		CALL_N_LOGONBADHR(m_info->GetEventMask(&oldFlags));
		CALL_N_LOGONBADHR(m_info->SetEventMask(oldFlags | COR_PRF_MONITOR_MODULE_LOADS | COR_PRF_ENABLE_OBJECT_ALLOCATED));

		// See if we asked for call sampling or not.  
		DWORD keywords = 0;
		DWORD keywordsSize = sizeof(keywords);
		int hrRegGetValue = RegGetValue(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\.NETFramework", L"PerfView_Keywords", RRF_RT_DWORD, NULL, &keywords, &keywordsSize);
		if (hrRegGetValue == ERROR_SUCCESS)
		{
			if ((keywords & DisableInliningKeyword) != 0)
			{
				CALL_N_LOGONBADHR(m_info->GetEventMask(&oldFlags));
				CALL_N_LOGONBADHR(m_info->SetEventMask(oldFlags | COR_PRF_DISABLE_INLINING));
			}

			if ((keywords & (CallKeyword | CallSampledKeyword)) != 0)
			{
				// assert(s_tracer == NULL);	// Don't need any information passed around so I don't need this.  
				// s_tracer = this;

				// Turn on the Call entry and leave hooks.  
				CALL_N_LOGONBADHR(m_info->SetEnterLeaveFunctionHooks3(EnterMethodNaked, 0, TailcallMethodNaked));
				CALL_N_LOGONBADHR(m_info->GetEventMask(&oldFlags));
				CALL_N_LOGONBADHR(m_info->SetEventMask(oldFlags | COR_PRF_MONITOR_ENTERLEAVE));


				if ((keywords & CallSampledKeyword) != 0)
					CallSamplingRate = 997;		// TODO make it configurable.    We choose 997 because it is prime and thus likely to be uncorrelated with things.  
			}
		}
	}
exit:
	LOG_TRACE(L"Initialize() returns %x\n", hr);
	return hr;
}

//==============================================================================
// This routine does the work of responding to a ETW request from the controller 
void CorProfilerTracer::DoETWCommand(ULONG IsEnabled, UCHAR Level, ULONGLONG MatchAnyKeywords, struct _EVENT_FILTER_DESCRIPTOR* filterData)
{
	LOG_TRACE(L"DoETWCommand(IsEnabled=%d, Level=%d Keywords=0x%x,%x)\n", IsEnabled, Level, (int)(MatchAnyKeywords >> 32), (int)MatchAnyKeywords);

	const DWORD FLAGS_CAN_SET = (COR_PRF_MONITOR_OBJECT_ALLOCATED | COR_PRF_MONITOR_MODULE_LOADS | COR_PRF_MONITOR_GC);
	DWORD oldFlags = 0;
	m_info->GetEventMask(&oldFlags);
	DWORD newFlags = oldFlags;

	if (IsEnabled == EVENT_CONTROL_CODE_ENABLE_PROVIDER)
	{
#if 0 // TODO FIX NOW, implement filtering.   
		if (filterData != NULL)
		{
			if (filterData->Ptr != NULL && filterData->Size > 0)
			{
				byte* ptr = (byte*)filterData->Ptr;

			}
		}
#endif 

		m_currentKeywords = MatchAnyKeywords;

		// Depending on what we asked for in the Keywords, turn on the cooresponding Profiler callbacks.  
		newFlags = (oldFlags & ~FLAGS_CAN_SET);
		newFlags |= COR_PRF_MONITOR_MODULE_LOADS;

		if ((MatchAnyKeywords & (GCKeyword | GCAllocKeyword | GCAllocSampledKeyword | GCHeapKeyword)))
			newFlags |= COR_PRF_MONITOR_GC;
		if ((MatchAnyKeywords & (GCAllocKeyword | GCAllocSampledKeyword)) != 0 && m_profilerLoadedAtStartup)
		{
			newFlags |= COR_PRF_MONITOR_OBJECT_ALLOCATED;
			if ((MatchAnyKeywords & GCAllocSampledKeyword) != 0)
				m_smartSampling = true;
		}
		if ((MatchAnyKeywords & CallKeyword) != 0 && m_profilerLoadedAtStartup)
			newFlags |= COR_PRF_MONITOR_ENTERLEAVE;

		// We send the manifest on Provider startup.   
		if (MatchAnyKeywords != 0 && !m_sentManifest)
		{
#if 0 
			LOG_TRACE(L"Sending Manifest\n");
			EventWriteSendManifestEvent(1, 1, 0, 0x5B, 1, 0, "<instrumentationManifest/>"); THis is not right, it is not null terminated.
				m_sentManifest = true;
#endif 
		}
	}
	else if (IsEnabled == EVENT_CONTROL_CODE_CAPTURE_STATE)
	{
		EventWriteCaptureStateStart();

		// You send the manifest if you don't ask for any other type of rundown or you asked for every other type of rundown.
		if (MatchAnyKeywords == 0 || MatchAnyKeywords == 0xFFFFFFFFFFFFFFFF)
		{
#if 0 
			LOG_TRACE(L"Sending Manifest\n");
			EventWriteSendManifestEvent(1, 1, 0, 0x5B, 1, 0, "<instrumentationManifest/>"); THis is not right, it is not null terminated.
#endif 
		}
		if ((MatchAnyKeywords & GCHeapKeyword) != 0)
		{
			LOG_TRACE(L"Forcing GC\n");
			ForceGC();
			LOG_TRACE(L"Done Forcing GC\n");
		}
		if ((MatchAnyKeywords & GCKeyword) != 0)
		{
			LOG_TRACE(L"Dumping Class Information\n");
			DumpClassInfo();
			LOG_TRACE(L"Dumping Class Information\n");
		}

		// Indicate that we are done with capture state processing
		EventWriteCaptureStateStop();

		// Detach is special, we do it after logging we are done with CaptureState activity (Because we could start tearing down)
		if ((MatchAnyKeywords & DetachKeyword) != 0 && !m_profilerLoadedAtStartup && !m_detaching)
		{
			m_detaching = true;
			LOG_TRACE(L"Detaching\n");
			HRESULT hr = m_info->RequestProfilerDetach(1000);
			LOG_TRACE(L"Done Detaching Detaching ret = 0x%x\n", hr);
			if (hr != S_OK)
				EventWriteProfilerError(hr, L"Request Profiler Detach Failed");
		}
	}
	else    // (IsEnabled == EVENT_CONTROL_CODE_DISABLE_PROVIDER)   
	{
		ClearTables();
		m_sentManifest = 0;
		// We reset all flags on disable. 
		newFlags = (oldFlags & ~FLAGS_CAN_SET);
		m_currentKeywords = 0;
	}

	// If we updated the profiler flags, actually send the update to the profiler.  
	if (newFlags != oldFlags && !m_detaching)
	{
		HRESULT hr = m_info->SetEventMask(newFlags);
		LOG_TRACE(L"DoETWCommand SetEventMask 0x%x returning 0x%x\n", newFlags, hr);
		if (hr != S_OK)
			EventWriteProfilerError(hr, L"Profiler SetEventMask Failed");
	}
}

//==============================================================================
// The constuctor does almost nothing, the real action happens
// in initialize (where we have a ProfilerInfo to call back to
// the runtime with)
CorProfilerTracer::CorProfilerTracer()
{
#if DEBUG
	// We put the log in the temp directory in a file called ETWClrProfiler.log
	wchar_t logFilePath[MAX_PATH];
	GetEnvironmentVariableW(L"Temp", logFilePath, MAX_PATH);
	wcscat_s(logFilePath, MAX_PATH, L"\\ETWClrProfiler.log");

	// the convention is that if the file exists, then we write to it, otherwise we do nothing.  
	DWORD attr = GetFileAttributesW(logFilePath);
	if (attr != INVALID_FILE_ATTRIBUTES)
		OPEN_LOG(TRACE_LOGGER, logFilePath);
#endif 

	LOG_TRACE(L"Creating new CorProfilerInstance\n");
	m_refCount = 0;
	m_info = NULL;
	m_gcCount = 0;
	m_curAllocSize = 0;
	m_smartSampling = false;
	m_forcingGC = false;
	m_currentKeywords = 0;
	m_profilerLoadedAtStartup = false;
	m_detaching = false;
	m_sentManifest = false;
	memset(&m_lock, 0, sizeof(CRITICAL_SECTION));
	InitializeCriticalSection(&m_lock);
}

//==============================================================================
CorProfilerTracer :: ~CorProfilerTracer()
{
	if (m_info != NULL)
		m_info->Release();

	DeleteCriticalSection(&m_lock);
	LOG_TRACE(L"Destroying CorProfilerInstance\n");
	CLOSE_LOG(TRACE_LOGGER);
}

//==============================================================================
HRESULT CorProfilerTracer::QueryInterface(
	REFIID  riid,
	void ** ppInterface)
{
	if (riid == IID_IUnknown)
		*ppInterface = static_cast<ICorProfilerCallback*>(this);
	else if (riid == IID_ICorProfilerCallback)
		*ppInterface = static_cast<ICorProfilerCallback*>(this);
	else if (riid == IID_ICorProfilerCallback2)
		*ppInterface = static_cast<ICorProfilerCallback2*>(this);
	else if (riid == IID_ICorProfilerCallback3)
		*ppInterface = static_cast<ICorProfilerCallback3*>(this);
	// TODO FIX NOW add support or ICorProfilerCallback4 (for large objects)
	else
	{
		*ppInterface = NULL;
		return E_NOTIMPL;
	}
	reinterpret_cast<IUnknown *>(*ppInterface)->AddRef();
	return S_OK;
}

//==============================================================================
HRESULT CorProfilerTracer::Shutdown()
{
	LOG_TRACE(L"Shutdown \n");
	EventWriteProfilerShutdown();
	EventUnregisterETWClrProfiler();
	ClearTables();

	if (m_info != NULL)
		m_info->Release();
	m_info = NULL;

	return S_OK;
}

//==============================================================================
// Sends out the accumulated class (and module) information events.  This is for
// rundown.  
void CorProfilerTracer::DumpClassInfo()
{
	for (auto moduleIter = m_moduleInfo.begin(); moduleIter != m_moduleInfo.end(); moduleIter++)
	{
		ModuleInfo* moduleInfo = moduleIter->second;
		EventWriteModuleIDDefintionEvent(moduleInfo->ID, moduleInfo->AssemblyID, moduleInfo->Path);
	}
	for (auto classIter = m_classInfo.begin(); classIter != m_classInfo.end(); classIter++)
	{
		ClassInfo* classInfo = classIter->second;
		EventWriteClassIDDefintionEvent(classInfo->ID, classInfo->Token, classInfo->Flags, classInfo->ModuleInfo->ID, classInfo->Name);
	}
}

//==============================================================================
// Clears out all remembered information from our tables.  
void CorProfilerTracer::ClearTables()
{
	for (auto classIter = m_classInfo.begin(); classIter != m_classInfo.end(); classIter++)
		delete classIter->second;
	m_classInfo.clear();

	for (auto moduleIter = m_moduleInfo.begin(); moduleIter != m_moduleInfo.end(); moduleIter++)
		delete moduleIter->second;
	m_moduleInfo.clear();
}

//==============================================================================
DWORD WINAPI CorProfilerTracer::ForceGCBody(LPVOID lpParameter)
{
	LOG_TRACE(L"ForceGCBody");
	CorProfilerTracer* profiler = (CorProfilerTracer*)lpParameter;
	HRESULT hr = profiler->m_info->ForceGC();
	LOG_TRACE(L"ForceGC Call returns 0x%x\n", hr);
	if (hr != S_OK)
		EventWriteProfilerError(hr, L"Profiler ForceGC Failed");
	profiler->m_forcingGC = false;
	return hr;
}

//==============================================================================
// We do the GC on another thread to avoid 'poluting' the ETW callback thread. 
void CorProfilerTracer::ForceGC()
{
	m_forcingGC = true;
	HANDLE thread = CreateThread(0, 0, ForceGCBody, this, 0, NULL);
	LOG_TRACE(L"ForceGC: thread 0x%x\n", thread);
	for (int i = 0; i < 2000; i++)
	{
		if (!m_forcingGC)
			break;
		Sleep(10);
	}
}

STDMETHODIMP CorProfilerTracer::ModuleAttachedToAssembly(ModuleID moduleId, AssemblyID assemblyId)
{
	auto moduleInfo = GetModuleInfo(moduleId);
	if (moduleInfo && moduleInfo->AssemblyID != assemblyId)
	{
		if (!moduleInfo->Path)
		{
			ULONG pathLength;
			AppDomainID appDomainId;
			ModuleID manifestModuleId;
			m_info->GetAssemblyInfo(assemblyId, 0, &pathLength, nullptr, &appDomainId, &manifestModuleId);
			if (pathLength > 0)
			{
				moduleInfo->Path = new wchar_t[pathLength];
				m_info->GetAssemblyInfo(assemblyId, pathLength, &pathLength, moduleInfo->Path, &appDomainId, &manifestModuleId);
			}

			if (!moduleInfo->Path)
			{
				moduleInfo->Path = new wchar_t[1];
				moduleInfo->Path[0] = L'\0';
			}
		}

		moduleInfo->AssemblyID = assemblyId;
		EventWriteModuleIDDefintionEvent(moduleId, assemblyId, moduleInfo->Path);
	}

	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::ObjectAllocated(ObjectID objectId, ClassID classId)
{
	EnterCriticalSection(&m_lock);
	// TODO this is probably inefficient, should only call back on classes that are variable sized
	ULONG size = 0;
	m_info->GetObjectSize(objectId, &size);
	ULONG representativeSize = size;

	// We do this for also  the side effect of logging the class  
	ClassInfo* classInfo = GetClassInfo(classId);
	if (classInfo == 0)			// TODO FIX NOW, we should log something.  
		goto Done;

	if (m_smartSampling)
	{
		classInfo->AllocsIgnored++;
		classInfo->IgnoredSize += size;

		// If we are not yet triggering, and the size is below the force keep size, then filter out the sample 
		if (classInfo->AllocsIgnored < classInfo->SamplingRate && size < classInfo->ForceKeepSize)
			goto Done;			// Filter out the sample.  

		// TODO FIX NOW HACK for exchange data collection 
#ifdef PIN_INVESTIGATION
#define Near(x,y) (y-0x20 <= x && x <= y + 8)

		if (size < classInfo->ForceKeepSize)
			goto Done;
		// if (!(Near(size, 0x1e4) || Near(size, 0x4038) || Near(size, 0x403d) || Near(size, 0x1418) || Near(size, 0x2018) || Near(size, 0x53) || Near(size, 0xed8)))
		   // goto Done;
#endif 
		// At this point we will log an event 

		// Compute the average allocation rate for this type and from that compute a good sampling rate.  
		classInfo->AllocCountInCurrentBucket += classInfo->AllocsIgnored;
		int ticks = GetTickCount();
		int delta = (ticks - classInfo->TickOfCurrentTimeBucket) & 0x7FFFFFFF;	// make wrap around work.  

		int minAllocPerMSec = classInfo->AllocCountInCurrentBucket / 16;		// This is an underestimation of the true rate.  
		if (delta >= 16 || (minAllocPerMSec > 2 && minAllocPerMSec > classInfo->AllocPerMSec * 1.5F))
		{
			float newAllocPerMSec = 0;
			if (delta >= 16)
			{
				// This is the normal case, our allocation rate is under control with the current throttling.   
				newAllocPerMSec = ((float)classInfo->AllocCountInCurrentBucket) / delta;
				// Do a exponential decay window that is 5 * max(16, AllocationInterval)  
				classInfo->AllocPerMSec = 0.8F *  classInfo->AllocPerMSec + 0.2F * newAllocPerMSec;
				classInfo->TickOfCurrentTimeBucket = ticks;
				classInfo->AllocCountInCurrentBucket = 0;
			}
			else
			{
				newAllocPerMSec = (float)minAllocPerMSec;
				// This means the second clause above is true, which means our sampling rate is too low
				// so we need to throttle quickly. 
				classInfo->AllocPerMSec = (float)minAllocPerMSec;
			}

			// We want to sample at a rate that insures less 100 allocations per second per type.  
			// However don't sample less than 1/1000, 
			int oldSamplingRate = classInfo->SamplingRate;
			classInfo->SamplingRate = min((int)(classInfo->AllocPerMSec * 10), 1000);
			if (classInfo->SamplingRate == 1)
				classInfo->SamplingRate = 0;

			// TODO This is for debugging.  Can remove after we are happy with the algorithm.   
			// if (classInfo->SamplingRate != oldSamplingRate)
			// 	   EventWriteSamplingRateChangeEvent(classId, classInfo->Name, delta, minAllocPerMSec, newAllocPerMSec, classInfo->AllocPerMSec, classInfo->SamplingRate);
		}

		// We are done calculating the sampling rate since we are logging an event we can reset the 'Ignored' stats and log the event.  
		representativeSize = classInfo->IgnoredSize;
		classInfo->AllocsIgnored = 0;
		classInfo->IgnoredSize = 0;
	}
	EventWriteObjectAllocatedEvent(objectId, classId, size, representativeSize);

Done:
	LeaveCriticalSection(&m_lock);
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::GarbageCollectionStarted(int cGenerations, BOOL generationCollected[], COR_PRF_GC_REASON reason)
{
	LOG_TRACE(L"GC Started\n");
	int maxGenCollected = 0;
	for (int i = 0; i < cGenerations; i++)
		if (generationCollected[i])
			maxGenCollected = i;

	m_gcCount++;

#if 0	// TODO FIX NOW implement
	ULONG boundsCount = 0;
	COR_PRF_GC_GENERATION_RANGE* bounds = new COR_PRF_GC_GENERATION_RANGE[4];
	m_info->GetGenerationBounds(4, &boundsCount, bounds);
	delete bounds;
#endif

	EventWriteGCStartEvent(m_gcCount, min(maxGenCollected, 2), reason == COR_PRF_GC_INDUCED);

	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::GarbageCollectionFinished(void)
{
	LOG_TRACE(L"GC End\r\n");
	EventWriteGCStopEvent(m_gcCount);
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::FinalizeableObjectQueued(DWORD finalizerFlags, ObjectID objectID)
{
	LOG_TRACE(L"FinalizeableObjectQueued\n");
#ifndef PIN_INVESTIGATION
	// TODO FIX NOW HACK for exchange data collection 
	ClassID classID = 0;
	m_info->GetClassFromObject(objectID, &classID);
	EventWriteFinalizeableObjectQueuedEvent(objectID, classID);
#endif
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::MovedReferences(ULONG cMovedObjectIDRanges, ObjectID oldObjectIDRangeStart[], ObjectID newObjectIDRangeStart[], ULONG cObjectIDRangeLength[])
{
	LOG_TRACE(L"Moved Ref\n");
	const int maxCount = MaxEventPayload / (1 * sizeof(int) + 2 * sizeof(void*));
	for (ULONG idx = 0; idx < cMovedObjectIDRanges; idx += maxCount)
	{
		EventWriteObjectsMovedEvent(min(cMovedObjectIDRanges - idx, maxCount),
			(const void**)&oldObjectIDRangeStart[idx], (const void**)&newObjectIDRangeStart[idx], (const unsigned int*)&cObjectIDRangeLength[idx]);
	}
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::SurvivingReferences(ULONG cSurvivingObjectIDRanges, ObjectID objectIDRangeStart[], ULONG cObjectIDRangeLength[])
{
	LOG_TRACE(L"Surviving references\n");
	const int maxCount = MaxEventPayload / (1 * sizeof(int) + 1 * sizeof(void*));
	for (ULONG idx = 0; idx < cSurvivingObjectIDRanges; idx += maxCount)
	{
		EventWriteObjectsSurvivedEvent(min(cSurvivingObjectIDRanges - idx, maxCount),
			(const void**)&objectIDRangeStart[idx], (const unsigned int*)&cObjectIDRangeLength[idx]);
	}
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::RootReferences2(ULONG cRootRefs, ObjectID rootRefIds[], COR_PRF_GC_ROOT_KIND rootKinds[], COR_PRF_GC_ROOT_FLAGS rootFlags[], UINT_PTR rootIds[])
{
	// If we did not ask for the GCHeap events, do nothing.  
	if ((m_currentKeywords & GCHeapKeyword) == 0)
		return S_OK;

	LOG_TRACE(L"RootReferences2\n");
	const int maxCount = MaxEventPayload / (2 * sizeof(int) + 2 * sizeof(void*));
	for (ULONG idx = 0; idx < cRootRefs; idx += maxCount)
	{
		EventWriteRootReferencesEvent(min(cRootRefs - idx, maxCount),
			(const void**)&rootRefIds[idx], (unsigned int*)&rootKinds[idx], (unsigned int*)&rootFlags[idx], (const void**)&rootIds[idx]);
	}
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::ObjectReferences(ObjectID objectId, ClassID classId, ULONG cObjectRefs, ObjectID objectRefIds[])
{
	if ((m_currentKeywords & GCHeapKeyword) == 0)
		return S_OK;
	// LOG_TRACE(L"ObjectReferences\n");

	// We do this for the side effect of logging the class  
	ClassInfo* classInfo = GetClassInfo(classId);
	/** TODO FIX NOW
	if (classInfo == NULL)
	return E_FAIL;
	**/
	ULONG size = 0;
	m_info->GetObjectSize(objectId, &size);

	EventWriteObjectReferencesEvent(objectId, classId, size, cObjectRefs, (const void**)objectRefIds);
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::HandleCreated(GCHandleID handleId, ObjectID initialObjectId)
{
	if ((m_currentKeywords & (GCHeapKeyword | GCAllocKeyword | GCAllocSampledKeyword)) == 0)
		return S_OK;

	LOG_TRACE(L"HandleCreated\n");
#ifndef PIN_INVESTIGATION
	// TODO FIX NOW HACK for exchange data collection 
	EventWriteHandleCreatedEvent(handleId, initialObjectId);
#endif
	return S_OK;
}

//==============================================================================
STDMETHODIMP CorProfilerTracer::HandleDestroyed(GCHandleID handleId)
{
	if ((m_currentKeywords & (GCHeapKeyword | GCAllocKeyword | GCAllocSampledKeyword)) == 0)
		return S_OK;

	LOG_TRACE(L"HandleDestroyed\n");
#ifndef PIN_INVESTIGATION
	// TODO FIX NOW HACK for exchange data collection 
	EventWriteHandleDestroyedEvent(handleId);
#endif 
	return S_OK;
}

//==============================================================================
ClassInfo* CorProfilerTracer::GetClassInfo(ClassID classId)
{
	// Have I already looked up this class? 
	ClassInfo*& classInfo = m_classInfo[classId];
	if (classInfo == NULL)
		classInfo = new ClassInfo();
	if (classInfo->ID == -1)     // We failed to get info on the class. 
		return NULL;
	if (classInfo->ID == 0)
	{
		classInfo->ID = -1;
		DWORD classFlags = 0;           // TODO FIX NOW, set class flags properly.  
		ModuleID moduleId = 0;

		if (m_smartSampling)
			classInfo->TickOfCurrentTimeBucket = GetTickCount();

		if (m_info->IsArrayClass(classId, &classInfo->elemType, &classInfo->elemClassId, &classInfo->rank) == S_OK)
		{
			classInfo->IsArray = true;
			auto elemInfo = GetClassInfo(classInfo->elemClassId);
			auto elemName = L"?";
			if (elemInfo != NULL)
				elemName = elemInfo->Name;

			auto elemLen = wcslen(elemName);
			auto buffLen = elemLen + classInfo->rank + 2;
			classInfo->Name = new wchar_t[buffLen];
			wcscpy_s(classInfo->Name, buffLen, elemName);
			wchar_t* ptr = classInfo->Name + elemLen;
			*ptr++ = '[';
			for (unsigned int i = 1; i < classInfo->rank; i++)
				*ptr++ = ',';
			*ptr++ = ']';
			*ptr = '\0';
			classInfo->ID = classId;
		}
		else
		{
			ULONG numFields;
			ULONG size;
			m_info->GetClassLayout(classId, 0, 0, &numFields, &size);
			classInfo->Size = size;

			HRESULT hr = m_info->GetClassIDInfo(classId, &moduleId, &classInfo->Token);
			if (moduleId != 0)
			{
				ModuleInfo* moduleInfo = classInfo->ModuleInfo = GetModuleInfo(moduleId);
				if (moduleInfo != NULL)
				{
					ULONG classNameLength = 0;
					DWORD classFlagsBuff = 0;
					mdToken baseClass;
					// Get the size of the name 
					hr = moduleInfo->MetaDataImport->GetTypeDefProps(classInfo->Token, 0, 0, &classNameLength, &classFlagsBuff, &baseClass);
					if (0 < classNameLength)
					{
						classInfo->Flags = (CorTypeAttr)classFlagsBuff;

						// Actually get the name 
						classInfo->Name = new wchar_t[classNameLength];
						hr = moduleInfo->MetaDataImport->GetTypeDefProps(classInfo->Token, classInfo->Name, classNameLength, &classNameLength, &classFlagsBuff, &baseClass);
						if (hr == S_OK)
							classInfo->ID = classId;
					}
				}
			}
		}
		if (classInfo->Name == NULL)
		{
			classInfo->Name = new wchar_t[2];
			wcscpy_s(classInfo->Name, 2, L"?");
		}

		// For our experimentation we keep all Byte[] 
		// TODO FIX NOW remove after experimentation (actually make it so that it is configurable).  
#ifdef PIN_INVESTIGATION
		classInfo->ForceKeepSize = 1000000000;
		if (wcscmp(classInfo->Name, L"System.Byte[]") == 0)
			classInfo->ForceKeepSize = 0x100;
		else if (wcsstr(classInfo->Name, L"OverlappedData") != NULL)
			classInfo->ForceKeepSize = 0x0;
#endif 

		if (classInfo->ID != -1)
		{
			EventWriteClassIDDefintionEvent(classInfo->ID, classInfo->Token, classFlags, moduleId, classInfo->Name);
		}
		else
		{
			LOG_TRACE(L"Error getting information for class ID 0x%x\n", classId);
		}
	}

	return classInfo;
}

//==============================================================================
ModuleInfo* CorProfilerTracer::GetModuleInfo(ModuleID moduleId)
{
	// Have I already looked up this class? 
	ModuleInfo*& moduleInfo = m_moduleInfo[moduleId];
	if (moduleInfo == NULL)
		moduleInfo = new ModuleInfo(moduleId);

	if (moduleInfo->MetaDataFailed)
		return nullptr;

	if (!moduleInfo->MetaDataImport)
	{
		HRESULT hr = m_info->GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport, (IUnknown**)&moduleInfo->MetaDataImport);
		if (!moduleInfo->MetaDataImport)
		{
			moduleInfo->MetaDataFailed = true;
			return nullptr;
		}
	}

	if (!moduleInfo->Path)
	{
		LPCBYTE baseAddr = 0;
		ULONG pathLength;
		HRESULT hr = m_info->GetModuleInfo(moduleId, &baseAddr, 0, &pathLength, nullptr, &moduleInfo->AssemblyID);
		if (0 < pathLength)
		{
			moduleInfo->Path = new wchar_t[pathLength];
			hr = m_info->GetModuleInfo(moduleId, &baseAddr, pathLength, &pathLength, moduleInfo->Path, &moduleInfo->AssemblyID);
			if (hr == S_OK)
			{
				EventWriteModuleIDDefintionEvent(moduleInfo->ID, moduleInfo->AssemblyID, moduleInfo->Path);
			}
		}
	}

	return moduleInfo;
}

