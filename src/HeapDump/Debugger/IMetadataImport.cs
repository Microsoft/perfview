//---------------------------------------------------------------------
//  This file is part of the CLR Managed Debugger (mdbg) Sample.
// 
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//---------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Text;

// NOTE: For MDBG_FAKE_COM we compile this file by itself into a .dll, disassemble to IL, and then strip 
// the assembly header out and include the il along with the others in the interop assembly
namespace Microsoft.Samples.Debugging.CorMetadata.NativeApi
{

    // GUID Copied from Cor.h
    [Guid("7DAC8207-D3AE-4c75-9B67-92801A497D44"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ] // IID_IMetadataImport from cor.h

    // This should be a private interface, but
    // we cannot do that because we are then getting an exception
    // "The specified type must be visible from COM." @ CorMetadataImport::GetRawInterface
    public interface IMetadataImport
    {
        //STDMETHOD_(void, CloseEnum)(HCORENUM hEnum) PURE;
        [PreserveSig] void CloseEnum(IntPtr hEnum);

        //STDMETHOD(CountEnum)(HCORENUM hEnum, ULONG *pulCount) PURE;
        void CountEnum(IntPtr hEnum, [ComAliasName("ULONG*")] out int pulCount);

        //STDMETHOD(ResetEnum)(HCORENUM hEnum, ULONG ulPos) PURE;
        void ResetEnum(IntPtr hEnum, int ulPos);

        //STDMETHOD(EnumTypeDefs)(HCORENUM *phEnum, mdTypeDef rTypeDefs[],ULONG cMax, ULONG *pcTypeDefs) PURE;
        //void EnumTypeDefs(out IntPtr phEnum,int[] rTypeDefs,uint cMax, out uint pcTypeDefs);  
        void EnumTypeDefs(
                            ref IntPtr phEnum,
                            [ComAliasName("mdTypeDef*")] out int rTypeDefs,
                            uint cMax /*must be 1*/,
                            [ComAliasName("ULONG*")] out uint pcTypeDefs);

        //STDMETHOD(EnumInterfaceImpls)(HCORENUM *phEnum, mdTypeDef td, mdInterfaceImpl rImpls[], ULONG cMax, ULONG* pcImpls) PURE;
        void EnumInterfaceImpls_(IntPtr phEnum, int td);

        //STDMETHOD(EnumTypeRefs)(HCORENUM *phEnum, mdTypeRef rTypeRefs[], ULONG cMax, ULONG* pcTypeRefs) PURE;
        void EnumTypeRefs_();

        //     STDMETHOD(FindTypeDefByName)(           // S_OK or error.
        //         LPCWSTR     szTypeDef,              // [IN] Name of the Type.
        //         mdToken     tkEnclosingClass,       // [IN] TypeDef/TypeRef for Enclosing class.
        //         mdTypeDef   *ptd) PURE;             // [OUT] Put the TypeDef token here.
        void FindTypeDefByName(
                               [In, MarshalAs(UnmanagedType.LPWStr)] string szTypeDef,
                               [In] int tkEnclosingClass,
                               [ComAliasName("mdTypeDef*")] [Out] out int token
                               );

        //     STDMETHOD(GetScopeProps)(               // S_OK or error.
        //         LPWSTR      szName,                 // [OUT] Put the name here.
        //         ULONG       cchName,                // [IN] Size of name buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Put size of name (wide chars) here.
        //         GUID        *pmvid) PURE;           // [OUT, OPTIONAL] Put MVID here.
        void GetScopeProps(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
            [In] int cchName,
            [ComAliasName("ULONG*")] out int pchName,
            out Guid mvid
        );

        //     STDMETHOD(GetModuleFromScope)(          // S_OK.
        //         mdModule    *pmd) PURE;             // [OUT] Put mdModule token here.
        void GetModuleFromScope_();

        //     STDMETHOD(GetTypeDefProps)(             // S_OK or error.
        //         mdTypeDef   td,                     // [IN] TypeDef token for inquiry.
        //         LPWSTR      szTypeDef,              // [OUT] Put name here.
        //         ULONG       cchTypeDef,             // [IN] size of name buffer in wide chars.
        //         ULONG       *pchTypeDef,            // [OUT] put size of name (wide chars) here.
        //         DWORD       *pdwTypeDefFlags,       // [OUT] Put flags here.
        //         mdToken     *ptkExtends) PURE;      // [OUT] Put base class TypeDef/TypeRef here.
        void GetTypeDefProps([In] int td,
                             [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szTypeDef,
                             [In] int cchTypeDef,
                             [ComAliasName("ULONG*")] [Out] out int pchTypeDef,
                             [Out, MarshalAs(UnmanagedType.U4)] out System.Reflection.TypeAttributes pdwTypeDefFlags,
                             [ComAliasName("mdToken*")] [Out] out int ptkExtends
                             );

        //     STDMETHOD(GetInterfaceImplProps)(       // S_OK or error.
        //         mdInterfaceImpl iiImpl,             // [IN] InterfaceImpl token.
        //         mdTypeDef   *pClass,                // [OUT] Put implementing class token here.
        //         mdToken     *ptkIface) PURE;        // [OUT] Put implemented interface token here.
        void GetInterfaceImplProps_();

        //     STDMETHOD(GetTypeRefProps)(             // S_OK or error.
        //         mdTypeRef   tr,                     // [IN] TypeRef token.
        //         mdToken     *ptkResolutionScope,    // [OUT] Resolution scope, ModuleRef or AssemblyRef.
        //         LPWSTR      szName,                 // [OUT] Name of the TypeRef.
        //         ULONG       cchName,                // [IN] Size of buffer.
        //         ULONG       *pchName) PURE;         // [OUT] Size of Name.
        void GetTypeRefProps(
                             int tr,
                             [ComAliasName("mdToken*")] [Out] out int ptkResolutionScope,
                             [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
                             [In] int cchName,
                             [ComAliasName("ULONG*")] out int pchName
                             );

        // This API is evil. Don't use it.
        //     STDMETHOD(ResolveTypeRef)(mdTypeRef tr, REFIID riid, IUnknown **ppIScope, mdTypeDef *ptd) PURE;
        void ResolveTypeRef(int tr, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object scope, out int typeDef);

        //     STDMETHOD(EnumMembers)(                 // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdToken     rMembers[],             // [OUT] Put MemberDefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMembers_();

        //     STDMETHOD(EnumMembersWithName)(         // S_OK, S_FALSE, or error.             
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdToken     rMembers[],             // [OUT] Put MemberDefs here.                   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMembersWithName_();

        //     STDMETHOD(EnumMethods)(                 // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdMethodDef rMethods[],             // [OUT] Put MethodDefs here.   
        //         ULONG       cMax,                   // [IN] Max MethodDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMethods(ref IntPtr phEnum,
                         int cl,
                         [ComAliasName("mdMethodDef*")] out int mdMethodDef,
                         int cMax, /*must be 1*/
                         [ComAliasName("ULONG*")] out int pcTokens
                         );

        //     STDMETHOD(EnumMethodsWithName)(         // S_OK, S_FALSE, or error.             
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdMethodDef rMethods[],             // [OU] Put MethodDefs here.    
        //         ULONG       cMax,                   // [IN] Max MethodDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMethodsWithName_();

        //     STDMETHOD(EnumFields)(                 // S_OK, S_FALSE, or error.  
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdFieldDef  rFields[],              // [OUT] Put FieldDefs here.    
        //         ULONG       cMax,                   // [IN] Max FieldDefs to put.   
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        //void EnumFields_();
        /*[PreserveSig]*/
        void EnumFields(ref IntPtr phEnum,
                        int cl,
                        [ComAliasName("mdFieldDef*")] out int mdFieldDef,
                        int cMax /*must be 1*/,
                        [ComAliasName("ULONG*")] out uint pcTokens);


        //     STDMETHOD(EnumFieldsWithName)(         // S_OK, S_FALSE, or error.              
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdFieldDef  rFields[],              // [OUT] Put MemberDefs here.                   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumFieldsWithName_();

        //     STDMETHOD(EnumParams)(                  // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdMethodDef mb,                     // [IN] MethodDef to scope the enumeration. 
        //         mdParamDef  rParams[],              // [OUT] Put ParamDefs here.    
        //         ULONG       cMax,                   // [IN] Max ParamDefs to put.   
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.
        void EnumParams(ref IntPtr phEnum,
                        int mdMethodDef,
                        [ComAliasName("mdParamDef*")] out int mdParamDef,
                        int cMax /*must be 1*/,
                        [ComAliasName("ULONG*")] out uint pcTokens);

        //     STDMETHOD(EnumMemberRefs)(              // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     tkParent,               // [IN] Parent token to scope the enumeration.  
        //         mdMemberRef rMemberRefs[],          // [OUT] Put MemberRefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberRefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMemberRefs_();

        //     STDMETHOD(EnumMethodImpls)(             // S_OK, S_FALSE, or error  
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdToken     rMethodBody[],          // [OUT] Put Method Body tokens here.   
        //         mdToken     rMethodDecl[],          // [OUT] Put Method Declaration tokens here.
        //         ULONG       cMax,                   // [IN] Max tokens to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumMethodImpls_();

        //     STDMETHOD(EnumPermissionSets)(          // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     tk,                     // [IN] if !NIL, token to scope the enumeration.    
        //         DWORD       dwActions,              // [IN] if !0, return only these actions.   
        //         mdPermission rPermission[],         // [OUT] Put Permissions here.  
        //         ULONG       cMax,                   // [IN] Max Permissions to put. 
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        void EnumPermissionSets_();

        //     STDMETHOD(FindMember)(  
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdToken     *pmb) PURE;             // [OUT] matching memberdef 
        void FindMember_();

        //     STDMETHOD(FindMethod)(  
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdMethodDef *pmb) PURE;             // [OUT] matching memberdef 
        void FindMethod_();

        //     STDMETHOD(FindField)(   
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdFieldDef  *pmb) PURE;             // [OUT] matching memberdef 
        void FindField_();

        //     STDMETHOD(FindMemberRef)(   
        //         mdTypeRef   td,                     // [IN] given typeRef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdMemberRef *pmr) PURE;             // [OUT] matching memberref 
        void FindMemberRef_();

        //     STDMETHOD (GetMethodProps)( 
        //         mdMethodDef mb,                     // The method for which to get props.   
        //         mdTypeDef   *pClass,                // Put method's class here. 
        //         LPWSTR      szMethod,               // Put method's name here.  
        //         ULONG       cchMethod,              // Size of szMethod buffer in wide chars.   
        //         ULONG       *pchMethod,             // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         ULONG       *pulCodeRVA,            // [OUT] codeRVA    
        //         DWORD       *pdwImplFlags) PURE;    // [OUT] Impl. Flags    
        void GetMethodProps([In] uint md,
                            [ComAliasName("mdTypeDef*")] [Out] out int pClass,
                            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szMethod,
                            [In] int cchMethod,
                            [ComAliasName("ULONG*")] [Out] out int pchMethod,
                            [ComAliasName("DWORD*")] [Out] out uint pdwAttr,
                            [ComAliasName("PCCOR_SIGNATURE*")] [Out] out IntPtr ppvSigBlob,
                            [ComAliasName("ULONG*")] [Out] out uint pcbSigBlob,
                            [ComAliasName("ULONG*")] [Out] out uint pulCodeRVA,
                            [ComAliasName("DWORD*")] [Out] out uint pdwImplFlags
                            );

        //     STDMETHOD(GetMemberRefProps)(           // S_OK or error.   
        //         mdMemberRef mr,                     // [IN] given memberref 
        //         mdToken     *ptk,                   // [OUT] Put classref or classdef here. 
        //         LPWSTR      szMember,               // [OUT] buffer to fill for member's name   
        //         ULONG       cchMember,              // [IN] the count of char of szMember   
        //         ULONG       *pchMember,             // [OUT] actual count of char in member name    
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to meta data blob value  
        //         ULONG       *pbSig) PURE;           // [OUT] actual size of signature blob  
        void GetMemberRefProps([In] uint mr,
                               [ComAliasName("mdMemberRef*")] [Out] out int ptk,
                               [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szMember,
                               [In] int cchMember,
                               [ComAliasName("ULONG*")] [Out] out uint pchMember,
                               [ComAliasName("PCCOR_SIGNATURE*")] [Out] out IntPtr ppvSigBlob,
                               [ComAliasName("ULONG*")] [Out] out int pbSig
                               );

        //     STDMETHOD(EnumProperties)(              // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdProperty  rProperties[],          // [OUT] Put Properties here.   
        //         ULONG       cMax,                   // [IN] Max properties to put.  
        //         ULONG       *pcProperties) PURE;    // [OUT] Put # put here.    
        void EnumProperties(ref IntPtr phEnum,
                            int mdTypeDef,
                            [ComAliasName("mdPropertyDef*")] out int mdPropertyDef,
                            int countMax /*must be 1*/,
                            [ComAliasName("ULONG*")] out uint pcTokens);

        //     STDMETHOD(EnumEvents)(                  // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdEvent     rEvents[],              // [OUT] Put events here.   
        //         ULONG       cMax,                   // [IN] Max events to put.  
        //         ULONG       *pcEvents) PURE;        // [OUT] Put # put here.    
        void EnumEvents_();

        //     STDMETHOD(GetEventProps)(               // S_OK, S_FALSE, or error. 
        //         mdEvent     ev,                     // [IN] event token 
        //         mdTypeDef   *pClass,                // [OUT] typedef containing the event declarion.    
        //         LPCWSTR     szEvent,                // [OUT] Event name 
        //         ULONG       cchEvent,               // [IN] the count of wchar of szEvent   
        //         ULONG       *pchEvent,              // [OUT] actual count of wchar for event's name 
        //         DWORD       *pdwEventFlags,         // [OUT] Event flags.   
        //         mdToken     *ptkEventType,          // [OUT] EventType class    
        //         mdMethodDef *pmdAddOn,              // [OUT] AddOn method of the event  
        //         mdMethodDef *pmdRemoveOn,           // [OUT] RemoveOn method of the event   
        //         mdMethodDef *pmdFire,               // [OUT] Fire method of the event   
        //         mdMethodDef rmdOtherMethod[],       // [OUT] other method of the event  
        //         ULONG       cMax,                   // [IN] size of rmdOtherMethod  
        //         ULONG       *pcOtherMethod) PURE;   // [OUT] total number of other method of this event 
        void GetEventProps_();

        //     STDMETHOD(EnumMethodSemantics)(         // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdMethodDef mb,                     // [IN] MethodDef to scope the enumeration. 
        //         mdToken     rEventProp[],           // [OUT] Put Event/Property here.   
        //         ULONG       cMax,                   // [IN] Max properties to put.  
        //         ULONG       *pcEventProp) PURE;     // [OUT] Put # put here.    
        void EnumMethodSemantics_();

        //     STDMETHOD(GetMethodSemantics)(          // S_OK, S_FALSE, or error. 
        //         mdMethodDef mb,                     // [IN] method token    
        //         mdToken     tkEventProp,            // [IN] event/property token.   
        //         DWORD       *pdwSemanticsFlags) PURE; // [OUT] the role flags for the method/propevent pair 
        void GetMethodSemantics_();

        //     STDMETHOD(GetClassLayout) ( 
        //         mdTypeDef   td,                     // [IN] give typedef    
        //         DWORD       *pdwPackSize,           // [OUT] 1, 2, 4, 8, or 16  
        //         COR_FIELD_OFFSET rFieldOffset[],    // [OUT] field offset array 
        //         ULONG       cMax,                   // [IN] size of the array   
        //         ULONG       *pcFieldOffset,         // [OUT] needed array size  
        //         ULONG       *pulClassSize) PURE;        // [OUT] the size of the class  
        void GetClassLayout_();

        //     STDMETHOD(GetFieldMarshal) (    
        //         mdToken     tk,                     // [IN] given a field's memberdef   
        //         PCCOR_SIGNATURE *ppvNativeType,     // [OUT] native type of this field  
        //         ULONG       *pcbNativeType) PURE;   // [OUT] the count of bytes of *ppvNativeType   
        void GetFieldMarshal_();

        //     STDMETHOD(GetRVA)(                      // S_OK or error.   
        //         mdToken     tk,                     // Member for which to set offset   
        //         ULONG       *pulCodeRVA,            // The offset   
        //         DWORD       *pdwImplFlags) PURE;    // the implementation flags 
        void GetRVA_();

        //     STDMETHOD(GetPermissionSetProps) (  
        //         mdPermission pm,                    // [IN] the permission token.   
        //         DWORD       *pdwAction,             // [OUT] CorDeclSecurity.   
        //         void const  **ppvPermission,        // [OUT] permission blob.   
        //         ULONG       *pcbPermission) PURE;   // [OUT] count of bytes of pvPermission.    
        void GetPermissionSetProps_();

        //     STDMETHOD(GetSigFromToken)(             // S_OK or error.   
        //         mdSignature mdSig,                  // [IN] Signature token.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] return pointer to token.   
        //         ULONG       *pcbSig) PURE;          // [OUT] return size of signature.  
        void GetSigFromToken_();

        //     STDMETHOD(GetModuleRefProps)(           // S_OK or error.   
        //         mdModuleRef mur,                    // [IN] moduleref token.    
        //         LPWSTR      szName,                 // [OUT] buffer to fill with the moduleref name.    
        //         ULONG       cchName,                // [IN] size of szName in wide characters.  
        //         ULONG       *pchName) PURE;         // [OUT] actual count of characters in the name.    
        void GetModuleRefProps_();

        //     STDMETHOD(EnumModuleRefs)(              // S_OK or error.   
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdModuleRef rModuleRefs[],          // [OUT] put modulerefs here.   
        //         ULONG       cmax,                   // [IN] max memberrefs to put.  
        //         ULONG       *pcModuleRefs) PURE;    // [OUT] put # put here.    
        void EnumModuleRefs_();

        //     STDMETHOD(GetTypeSpecFromToken)(        // S_OK or error.   
        //         mdTypeSpec typespec,                // [IN] TypeSpec token.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] return pointer to TypeSpec signature  
        //         ULONG       *pcbSig) PURE;          // [OUT] return size of signature.  
        void GetTypeSpecFromToken_();

        //     STDMETHOD(GetNameFromToken)(            // Not Recommended! May be removed!
        //         mdToken     tk,                     // [IN] Token to get name from.  Must have a name.
        //         MDUTF8CSTR  *pszUtf8NamePtr) PURE;  // [OUT] Return pointer to UTF8 name in heap.
        void GetNameFromToken_();


        //     STDMETHOD(EnumUnresolvedMethods)(       // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     rMethods[],             // [OUT] Put MemberDefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.
        void EnumUnresolvedMethods_();

        //     STDMETHOD(GetUserString)(               // S_OK or error.
        //         mdString    stk,                    // [IN] String token.
        //         LPWSTR      szString,               // [OUT] Copy of string.
        //         ULONG       cchString,              // [IN] Max chars of room in szString.
        //         ULONG       *pchString) PURE;       // [OUT] How many chars in actual string.
        void GetUserString([In] int stk,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString,
                           [In] int cchString,
                           [ComAliasName("ULONG*")] out int pchString
                           );

        //     STDMETHOD(GetPinvokeMap)(               // S_OK or error.
        //         mdToken     tk,                     // [IN] FieldDef or MethodDef.
        //         DWORD       *pdwMappingFlags,       // [OUT] Flags used for mapping.
        //         LPWSTR      szImportName,           // [OUT] Import name.
        //         ULONG       cchImportName,          // [IN] Size of the name buffer.
        //         ULONG       *pchImportName,         // [OUT] Actual number of characters stored.
        //         mdModuleRef *pmrImportDLL) PURE;    // [OUT] ModuleRef token for the target DLL.
        void GetPinvokeMap_();

        //     STDMETHOD(EnumSignatures)(              // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdSignature rSignatures[],          // [OUT] put signatures here.   
        //         ULONG       cmax,                   // [IN] max signatures to put.  
        //         ULONG       *pcSignatures) PURE;    // [OUT] put # put here.
        void EnumSignatures_();

        //     STDMETHOD(EnumTypeSpecs)(               // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdTypeSpec  rTypeSpecs[],           // [OUT] put TypeSpecs here.   
        //         ULONG       cmax,                   // [IN] max TypeSpecs to put.  
        //         ULONG       *pcTypeSpecs) PURE;     // [OUT] put # put here.
        void EnumTypeSpecs_();

        //     STDMETHOD(EnumUserStrings)(             // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN/OUT] pointer to the enum.
        //         mdString    rStrings[],             // [OUT] put Strings here.
        //         ULONG       cmax,                   // [IN] max Strings to put.
        //         ULONG       *pcStrings) PURE;       // [OUT] put # put here.
        void EnumUserStrings_();

        //     STDMETHOD(GetParamForMethodIndex)(      // S_OK or error.
        //         mdMethodDef md,                     // [IN] Method token.
        //         ULONG       ulParamSeq,             // [IN] Parameter sequence.
        //         mdParamDef  *ppd) PURE;             // [IN] Put Param token here.
        void GetParamForMethodIndex_();

        //     STDMETHOD(EnumCustomAttributes)(        // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN, OUT] COR enumerator.
        //         mdToken     tk,                     // [IN] Token to scope the enumeration, 0 for all.
        //         mdToken     tkType,                 // [IN] Type of interest, 0 for all.
        //         mdCustomAttribute rCustomAttributes[], // [OUT] Put custom attribute tokens here.
        //         ULONG       cMax,                   // [IN] Size of rCustomAttributes.
        //         ULONG       *pcCustomAttributes) PURE;  // [OUT, OPTIONAL] Put count of token values here.
        void EnumCustomAttributes(ref IntPtr phEnum,
                         int tk,
                         int tkType,
                         [ComAliasName("mdCustomAttribute*")]out int mdCustomAttribute,
                         uint cMax /*must be 1*/,
                         [ComAliasName("ULONG*")]out uint pcTokens
                         );

        //     STDMETHOD(GetCustomAttributeProps)(     // S_OK or error.
        //         mdCustomAttribute cv,               // [IN] CustomAttribute token.
        //         mdToken     *ptkObj,                // [OUT, OPTIONAL] Put object token here.
        //         mdToken     *ptkType,               // [OUT, OPTIONAL] Put AttrType token here.
        //         void const  **ppBlob,               // [OUT, OPTIONAL] Put pointer to data here.
        //         ULONG       *pcbSize) PURE;         // [OUT, OPTIONAL] Put size of date here.
        void GetCustomAttributeProps_();

        //     STDMETHOD(FindTypeRef)(   
        //         mdToken     tkResolutionScope,      // [IN] ModuleRef, AssemblyRef or TypeRef.
        //         LPCWSTR     szName,                 // [IN] TypeRef Name.
        //         mdTypeRef   *ptr) PURE;             // [OUT] matching TypeRef.
        void FindTypeRef_();

        //     STDMETHOD(GetMemberProps)(  
        //         mdToken     mb,                     // The member for which to get props.   
        //         mdTypeDef   *pClass,                // Put member's class here. 
        //         LPWSTR      szMember,               // Put member's name here.  
        //         ULONG       cchMember,              // Size of szMember buffer in wide chars.   
        //         ULONG       *pchMember,             // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         ULONG       *pulCodeRVA,            // [OUT] codeRVA    
        //         DWORD       *pdwImplFlags,          // [OUT] Impl. Flags    
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppValue,              // [OUT] constant value 
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        void GetMemberProps_();

        //     STDMETHOD(GetFieldProps)(  
        //         mdFieldDef  mb,                     // The field for which to get props.    
        //         mdTypeDef   *pClass,                // Put field's class here.  
        //         LPWSTR      szField,                // Put field's name here.   
        //         ULONG       cchField,               // Size of szField buffer in wide chars.    
        //         ULONG       *pchField,              // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppValue,              // [OUT] constant value 
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        void GetFieldProps(int mb,
                           [ComAliasName("mdTypeDef*")] out int mdTypeDef,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szField,
                           int cchField,
                           [ComAliasName("ULONG*")] out int pchField,
                           [ComAliasName("DWORD*")] out int pdwAttr,
                           [ComAliasName("PCCOR_SIGNATURE*")] out IntPtr ppvSigBlob,
                           [ComAliasName("ULONG*")] out int pcbSigBlob,
                           [ComAliasName("DWORD*")] out int pdwCPlusTypeFlab,
                           [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppValue,
                           [ComAliasName("ULONG*")] out int pcchValue
                           );

        //     STDMETHOD(GetPropertyProps)(            // S_OK, S_FALSE, or error. 
        //         mdProperty  prop,                   // [IN] property token  
        //         mdTypeDef   *pClass,                // [OUT] typedef containing the property declarion. 
        //         LPCWSTR     szProperty,             // [OUT] Property name  
        //         ULONG       cchProperty,            // [IN] the count of wchar of szProperty    
        //         ULONG       *pchProperty,           // [OUT] actual count of wchar for property name    
        //         DWORD       *pdwPropFlags,          // [OUT] property flags.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] property type. pointing to meta data internal blob 
        //         ULONG       *pbSig,                 // [OUT] count of bytes in *ppvSig  
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppDefaultValue,       // [OUT] constant value 
        //         ULONG       *pcchDefaultValue,      // [OUT] size of constant string in chars, 0 for non-strings.
        //         mdMethodDef *pmdSetter,             // [OUT] setter method of the property  
        //         mdMethodDef *pmdGetter,             // [OUT] getter method of the property  
        //         mdMethodDef rmdOtherMethod[],       // [OUT] other method of the property   
        //         ULONG       cMax,                   // [IN] size of rmdOtherMethod  
        //         ULONG       *pcOtherMethod) PURE;   // [OUT] total number of other method of this property
        void GetPropertyProps(int mb,
                    [ComAliasName("mdTypeDef*")] out int mdTypeDef,
                    [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szProperty,
                    int cchProperty,
                    [ComAliasName("ULONG*")] out int pchProperty,
                    [ComAliasName("DWORD*")] out int pdwPropFlags,
                    [ComAliasName("PCCOR_SIGNATURE*")] out IntPtr ppvSigBlob,
                    [ComAliasName("ULONG*")] out int pcbSigBlob,
                    [ComAliasName("DWORD*")] out int pdwCPlusTypeFlag,
                    [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppDefaultValue,
                    [ComAliasName("ULONG*")] out int pcchDefaultValue,
                    [ComAliasName("mdMethodDef*")] out int mdSetter,
                    [ComAliasName("mdMethodDef*")] out int mdGetter,
                    [ComAliasName("mdMethodDef*")] out int rmdOtherMethod,
                    [ComAliasName("ULONG")] int cMax, /* must be 1 */
                    [ComAliasName("ULONG*")] out int pcOtherMethod
                    );

        //     STDMETHOD(GetParamProps)(               // S_OK or error.
        //         mdParamDef  tk,                     // [IN]The Parameter.
        //         mdMethodDef *pmd,                   // [OUT] Parent Method token.
        //         ULONG       *pulSequence,           // [OUT] Parameter sequence.
        //         LPWSTR      szName,                 // [OUT] Put name here.
        //         ULONG       cchName,                // [OUT] Size of name buffer.
        //         ULONG       *pchName,               // [OUT] Put actual size of name here.
        //         DWORD       *pdwAttr,               // [OUT] Put flags here.
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] Flag for value type. selected ELEMENT_TYPE_*.
        //         void const  **ppValue,              // [OUT] Constant value.
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        void GetParamProps(int tk,
                           [ComAliasName("mdMethodDef*")] out int pmd,
                           [ComAliasName("ULONG*")] out uint pulSequence,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
                           uint cchName,
                           [ComAliasName("ULONG*")] out uint pchName,
                           [ComAliasName("DWORD*")] out uint pdwAttr,
                           [ComAliasName("DWORD*")] out uint pdwCPlusTypeFlag,
                           [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppValue,
                           [ComAliasName("ULONG*")] out uint pcchValue
                           );

        //     STDMETHOD(GetCustomAttributeByName)(    // S_OK or error.
        //         mdToken     tkObj,                  // [IN] Object with Custom Attribute.
        //         LPCWSTR     szName,                 // [IN] Name of desired Custom Attribute.
        //         const void  **ppData,               // [OUT] Put pointer to data here.
        //         ULONG       *pcbData) PURE;         // [OUT] Put size of data here.
        [PreserveSig]
        int GetCustomAttributeByName(
                            int tkObj,
                            [MarshalAs(UnmanagedType.LPWStr)]string szName,
                            out IntPtr ppData,
                            out uint pcbData);

        //     STDMETHOD_(BOOL, IsValidToken)(         // True or False.
        //         mdToken     tk) PURE;               // [IN] Given token.
        [PreserveSig] bool IsValidToken([In, MarshalAs(UnmanagedType.U4)] uint tk);

        //     STDMETHOD(GetNestedClassProps)(         // S_OK or error.
        //         mdTypeDef   tdNestedClass,          // [IN] NestedClass token.
        //         mdTypeDef   *ptdEnclosingClass) PURE; // [OUT] EnclosingClass token.
        void GetNestedClassProps(int tdNestedClass, [ComAliasName("mdTypeDef*")] out int tdEnclosingClass);

        //     STDMETHOD(GetNativeCallConvFromSig)(    // S_OK or error.
        //         void const  *pvSig,                 // [IN] Pointer to signature.
        //         ULONG       cbSig,                  // [IN] Count of signature bytes.
        //         ULONG       *pCallConv) PURE;       // [OUT] Put calling conv here (see CorPinvokemap).
        void GetNativeCallConvFromSig_();

        //     STDMETHOD(IsGlobal)(                    // S_OK or error.
        //         mdToken     pd,                     // [IN] Type, Field, or Method token.
        //         int         *pbGlobal) PURE;        // [OUT] Put 1 if global, 0 otherwise.
        void IsGlobal_();

    }      // IMetadataImport



    // IMetaDataImport2
    [Guid("FCE5EFA0-8BBA-4f8e-A036-8F2022B08466"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ]
    public interface IMetadataImport2 : IMetadataImport
    {
        #region inheritted methods from IMetadataImport
        // <strip>@todo - check if there will be be better way how to declare IMetadataImport2 interface.
        //       - Would be very nice if we could avoid this (tried deriving from IM, doesn't work).</strip>
        // Need imports from IMetaDataImport to adjust IM2 vtable slots.

        //STDMETHOD_(void, CloseEnum)(HCORENUM hEnum) PURE;
        [PreserveSig] new void CloseEnum(IntPtr hEnum);

        //STDMETHOD(CountEnum)(HCORENUM hEnum, ULONG *pulCount) PURE;
        new void CountEnum(IntPtr hEnum, [ComAliasName("ULONG*")] out int pulCount);

        //STDMETHOD(ResetEnum)(HCORENUM hEnum, ULONG ulPos) PURE;
        new void ResetEnum(IntPtr hEnum, int ulPos);

        //STDMETHOD(EnumTypeDefs)(HCORENUM *phEnum, mdTypeDef rTypeDefs[],ULONG cMax, ULONG *pcTypeDefs) PURE;
        //void EnumTypeDefs(out IntPtr phEnum,int[] rTypeDefs,uint cMax, out uint pcTypeDefs);  
        new void EnumTypeDefs(
                            ref IntPtr phEnum,
                            [ComAliasName("mdTypeDef*")] out int rTypeDefs,
                            uint cMax /*must be 1*/,
                            [ComAliasName("ULONG*")] out uint pcTypeDefs);

        //STDMETHOD(EnumInterfaceImpls)(HCORENUM *phEnum, mdTypeDef td, mdInterfaceImpl rImpls[], ULONG cMax, ULONG* pcImpls) PURE;
        new void EnumInterfaceImpls_(IntPtr phEnum, int td);

        //STDMETHOD(EnumTypeRefs)(HCORENUM *phEnum, mdTypeRef rTypeRefs[], ULONG cMax, ULONG* pcTypeRefs) PURE;
        new void EnumTypeRefs_();

        //     STDMETHOD(FindTypeDefByName)(           // S_OK or error.
        //         LPCWSTR     szTypeDef,              // [IN] Name of the Type.
        //         mdToken     tkEnclosingClass,       // [IN] TypeDef/TypeRef for Enclosing class.
        //         mdTypeDef   *ptd) PURE;             // [OUT] Put the TypeDef token here.
        new void FindTypeDefByName(
                               [In, MarshalAs(UnmanagedType.LPWStr)] string szTypeDef,
                               [In] int tkEnclosingClass,
                               [ComAliasName("mdTypeDef*")] [Out] out int token
                               );

        //     STDMETHOD(GetScopeProps)(               // S_OK or error.
        //         LPWSTR      szName,                 // [OUT] Put the name here.
        //         ULONG       cchName,                // [IN] Size of name buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Put size of name (wide chars) here.
        //         GUID        *pmvid) PURE;           // [OUT, OPTIONAL] Put MVID here.
        new void GetScopeProps(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
            [In] int cchName,
            [ComAliasName("ULONG*")] out int pchName,
            out Guid mvid
        );

        //     STDMETHOD(GetModuleFromScope)(          // S_OK.
        //         mdModule    *pmd) PURE;             // [OUT] Put mdModule token here.
        new void GetModuleFromScope_();

        //     STDMETHOD(GetTypeDefProps)(             // S_OK or error.
        //         mdTypeDef   td,                     // [IN] TypeDef token for inquiry.
        //         LPWSTR      szTypeDef,              // [OUT] Put name here.
        //         ULONG       cchTypeDef,             // [IN] size of name buffer in wide chars.
        //         ULONG       *pchTypeDef,            // [OUT] put size of name (wide chars) here.
        //         DWORD       *pdwTypeDefFlags,       // [OUT] Put flags here.
        //         mdToken     *ptkExtends) PURE;      // [OUT] Put base class TypeDef/TypeRef here.
        new void GetTypeDefProps([In] int td,
                             [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szTypeDef,
                             [In] int cchTypeDef,
                             [ComAliasName("ULONG*")] [Out] out int pchTypeDef,
                             [Out, MarshalAs(UnmanagedType.U4)] out System.Reflection.TypeAttributes pdwTypeDefFlags,
                             [ComAliasName("mdToken*")] [Out] out int ptkExtends
                             );

        //     STDMETHOD(GetInterfaceImplProps)(       // S_OK or error.
        //         mdInterfaceImpl iiImpl,             // [IN] InterfaceImpl token.
        //         mdTypeDef   *pClass,                // [OUT] Put implementing class token here.
        //         mdToken     *ptkIface) PURE;        // [OUT] Put implemented interface token here.
        new void GetInterfaceImplProps_();

        //     STDMETHOD(GetTypeRefProps)(             // S_OK or error.
        //         mdTypeRef   tr,                     // [IN] TypeRef token.
        //         mdToken     *ptkResolutionScope,    // [OUT] Resolution scope, ModuleRef or AssemblyRef.
        //         LPWSTR      szName,                 // [OUT] Name of the TypeRef.
        //         ULONG       cchName,                // [IN] Size of buffer.
        //         ULONG       *pchName) PURE;         // [OUT] Size of Name.
        new void GetTypeRefProps(
                             int tr,
                             [ComAliasName("mdToken*")] [Out] out int ptkResolutionScope,
                             [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
                             [In] int cchName,
                             [ComAliasName("ULONG*")] out int pchName
                             );

        // This API is evil. Don't use it.
        //     STDMETHOD(ResolveTypeRef)(mdTypeRef tr, REFIID riid, IUnknown **ppIScope, mdTypeDef *ptd) PURE;
        new void ResolveTypeRef(int tr, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object scope, out int typeDef);

        //     STDMETHOD(EnumMembers)(                 // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdToken     rMembers[],             // [OUT] Put MemberDefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMembers_();

        //     STDMETHOD(EnumMembersWithName)(         // S_OK, S_FALSE, or error.             
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdToken     rMembers[],             // [OUT] Put MemberDefs here.                   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMembersWithName_();

        //     STDMETHOD(EnumMethods)(                 // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdMethodDef rMethods[],             // [OUT] Put MethodDefs here.   
        //         ULONG       cMax,                   // [IN] Max MethodDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMethods(ref IntPtr phEnum,
                         int cl,
                         [ComAliasName("mdMethodDef*")] out int mdMethodDef,
                         int cMax, /*must be 1*/
                         [ComAliasName("ULONG*")] out int pcTokens
                         );

        //     STDMETHOD(EnumMethodsWithName)(         // S_OK, S_FALSE, or error.             
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdMethodDef rMethods[],             // [OU] Put MethodDefs here.    
        //         ULONG       cMax,                   // [IN] Max MethodDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMethodsWithName_();

        //     STDMETHOD(EnumFields)(                 // S_OK, S_FALSE, or error.  
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         mdFieldDef  rFields[],              // [OUT] Put FieldDefs here.    
        //         ULONG       cMax,                   // [IN] Max FieldDefs to put.   
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        /*[PreserveSig]*/
        new void EnumFields(ref IntPtr phEnum,
                        int cl,
                        [ComAliasName("mdFieldDef*")] out int mdFieldDef,
                        int cMax /*must be 1*/,
                        [ComAliasName("ULONG*")] out uint pcTokens);

        //     STDMETHOD(EnumFieldsWithName)(         // S_OK, S_FALSE, or error.              
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.                
        //         mdTypeDef   cl,                     // [IN] TypeDef to scope the enumeration.   
        //         LPCWSTR     szName,                 // [IN] Limit results to those with this name.              
        //         mdFieldDef  rFields[],              // [OUT] Put MemberDefs here.                   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.              
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumFieldsWithName_();

        //     STDMETHOD(EnumParams)(                  // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdMethodDef mb,                     // [IN] MethodDef to scope the enumeration. 
        //         mdParamDef  rParams[],              // [OUT] Put ParamDefs here.    
        //         ULONG       cMax,                   // [IN] Max ParamDefs to put.   
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.
        new void EnumParams(ref IntPtr phEnum,
                        int mdMethodDef,
                        [ComAliasName("mdParamDef*")] out int mdParamDef,
                        int cMax /*must be 1*/,
                        [ComAliasName("ULONG*")] out uint pcTokens);

        //     STDMETHOD(EnumMemberRefs)(              // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     tkParent,               // [IN] Parent token to scope the enumeration.  
        //         mdMemberRef rMemberRefs[],          // [OUT] Put MemberRefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberRefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMemberRefs_();

        //     STDMETHOD(EnumMethodImpls)(             // S_OK, S_FALSE, or error  
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdToken     rMethodBody[],          // [OUT] Put Method Body tokens here.   
        //         mdToken     rMethodDecl[],          // [OUT] Put Method Declaration tokens here.
        //         ULONG       cMax,                   // [IN] Max tokens to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumMethodImpls_();

        //     STDMETHOD(EnumPermissionSets)(          // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     tk,                     // [IN] if !NIL, token to scope the enumeration.    
        //         DWORD       dwActions,              // [IN] if !0, return only these actions.   
        //         mdPermission rPermission[],         // [OUT] Put Permissions here.  
        //         ULONG       cMax,                   // [IN] Max Permissions to put. 
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.    
        new void EnumPermissionSets_();

        //     STDMETHOD(FindMember)(  
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdToken     *pmb) PURE;             // [OUT] matching memberdef 
        new void FindMember_();

        //     STDMETHOD(FindMethod)(  
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdMethodDef *pmb) PURE;             // [OUT] matching memberdef 
        new void FindMethod_();

        //     STDMETHOD(FindField)(   
        //         mdTypeDef   td,                     // [IN] given typedef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdFieldDef  *pmb) PURE;             // [OUT] matching memberdef 
        new void FindField_();

        //     STDMETHOD(FindMemberRef)(   
        //         mdTypeRef   td,                     // [IN] given typeRef   
        //         LPCWSTR     szName,                 // [IN] member name 
        //         PCCOR_SIGNATURE pvSigBlob,          // [IN] point to a blob value of CLR signature 
        //         ULONG       cbSigBlob,              // [IN] count of bytes in the signature blob    
        //         mdMemberRef *pmr) PURE;             // [OUT] matching memberref 
        new void FindMemberRef_();

        //     STDMETHOD (GetMethodProps)( 
        //         mdMethodDef mb,                     // The method for which to get props.   
        //         mdTypeDef   *pClass,                // Put method's class here. 
        //         LPWSTR      szMethod,               // Put method's name here.  
        //         ULONG       cchMethod,              // Size of szMethod buffer in wide chars.   
        //         ULONG       *pchMethod,             // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         ULONG       *pulCodeRVA,            // [OUT] codeRVA    
        //         DWORD       *pdwImplFlags) PURE;    // [OUT] Impl. Flags    
        new void GetMethodProps([In] uint md,
                            [ComAliasName("mdTypeDef*")] [Out] out int pClass,
                            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szMethod,
                            [In] int cchMethod,
                            [ComAliasName("ULONG*")] [Out] out int pchMethod,
                            [ComAliasName("DWORD*")] [Out] out uint pdwAttr,
                            [ComAliasName("PCCOR_SIGNATURE*")] [Out] out IntPtr ppvSigBlob,
                            [ComAliasName("ULONG*")] [Out] out uint pcbSigBlob,
                            [ComAliasName("ULONG*")] [Out] out uint pulCodeRVA,
                            [ComAliasName("DWORD*")] [Out] out uint pdwImplFlags
                            );

        //     STDMETHOD(GetMemberRefProps)(           // S_OK or error.   
        //         mdMemberRef mr,                     // [IN] given memberref 
        //         mdToken     *ptk,                   // [OUT] Put classref or classdef here. 
        //         LPWSTR      szMember,               // [OUT] buffer to fill for member's name   
        //         ULONG       cchMember,              // [IN] the count of char of szMember   
        //         ULONG       *pchMember,             // [OUT] actual count of char in member name    
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to meta data blob value  
        //         ULONG       *pbSig) PURE;           // [OUT] actual size of signature blob  
        new void GetMemberRefProps([In] uint mr,
                               [ComAliasName("mdMemberRef*")] [Out] out int ptk,
                               [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szMember,
                               [In] int cchMember,
                               [ComAliasName("ULONG*")] [Out] out uint pchMember,
                               [ComAliasName("PCCOR_SIGNATURE*")] [Out] out IntPtr ppvSigBlob,
                               [ComAliasName("ULONG*")] [Out] out int pbSig
                               );

        //     STDMETHOD(EnumProperties)(              // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdProperty  rProperties[],          // [OUT] Put Properties here.   
        //         ULONG       cMax,                   // [IN] Max properties to put.  
        //         ULONG       *pcProperties) PURE;    // [OUT] Put # put here.    
        new void EnumProperties(ref IntPtr phEnum,
                                int mdTypeDef,
                                [ComAliasName("mdPropertyDef*")] out int mdPropertyDef,
                                int countMax /*must be 1*/,
                                [ComAliasName("ULONG*")] out uint pcTokens);

        //     STDMETHOD(EnumEvents)(                  // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdTypeDef   td,                     // [IN] TypeDef to scope the enumeration.   
        //         mdEvent     rEvents[],              // [OUT] Put events here.   
        //         ULONG       cMax,                   // [IN] Max events to put.  
        //         ULONG       *pcEvents) PURE;        // [OUT] Put # put here.    
        new void EnumEvents_();

        //     STDMETHOD(GetEventProps)(               // S_OK, S_FALSE, or error. 
        //         mdEvent     ev,                     // [IN] event token 
        //         mdTypeDef   *pClass,                // [OUT] typedef containing the event declarion.    
        //         LPCWSTR     szEvent,                // [OUT] Event name 
        //         ULONG       cchEvent,               // [IN] the count of wchar of szEvent   
        //         ULONG       *pchEvent,              // [OUT] actual count of wchar for event's name 
        //         DWORD       *pdwEventFlags,         // [OUT] Event flags.   
        //         mdToken     *ptkEventType,          // [OUT] EventType class    
        //         mdMethodDef *pmdAddOn,              // [OUT] AddOn method of the event  
        //         mdMethodDef *pmdRemoveOn,           // [OUT] RemoveOn method of the event   
        //         mdMethodDef *pmdFire,               // [OUT] Fire method of the event   
        //         mdMethodDef rmdOtherMethod[],       // [OUT] other method of the event  
        //         ULONG       cMax,                   // [IN] size of rmdOtherMethod  
        //         ULONG       *pcOtherMethod) PURE;   // [OUT] total number of other method of this event 
        new void GetEventProps_();

        //     STDMETHOD(EnumMethodSemantics)(         // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdMethodDef mb,                     // [IN] MethodDef to scope the enumeration. 
        //         mdToken     rEventProp[],           // [OUT] Put Event/Property here.   
        //         ULONG       cMax,                   // [IN] Max properties to put.  
        //         ULONG       *pcEventProp) PURE;     // [OUT] Put # put here.    
        new void EnumMethodSemantics_();

        //     STDMETHOD(GetMethodSemantics)(          // S_OK, S_FALSE, or error. 
        //         mdMethodDef mb,                     // [IN] method token    
        //         mdToken     tkEventProp,            // [IN] event/property token.   
        //         DWORD       *pdwSemanticsFlags) PURE; // [OUT] the role flags for the method/propevent pair 
        new void GetMethodSemantics_();

        //     STDMETHOD(GetClassLayout) ( 
        //         mdTypeDef   td,                     // [IN] give typedef    
        //         DWORD       *pdwPackSize,           // [OUT] 1, 2, 4, 8, or 16  
        //         COR_FIELD_OFFSET rFieldOffset[],    // [OUT] field offset array 
        //         ULONG       cMax,                   // [IN] size of the array   
        //         ULONG       *pcFieldOffset,         // [OUT] needed array size  
        //         ULONG       *pulClassSize) PURE;        // [OUT] the size of the class  
        new void GetClassLayout_();

        //     STDMETHOD(GetFieldMarshal) (    
        //         mdToken     tk,                     // [IN] given a field's memberdef   
        //         PCCOR_SIGNATURE *ppvNativeType,     // [OUT] native type of this field  
        //         ULONG       *pcbNativeType) PURE;   // [OUT] the count of bytes of *ppvNativeType   
        new void GetFieldMarshal_();

        //     STDMETHOD(GetRVA)(                      // S_OK or error.   
        //         mdToken     tk,                     // Member for which to set offset   
        //         ULONG       *pulCodeRVA,            // The offset   
        //         DWORD       *pdwImplFlags) PURE;    // the implementation flags 
        new void GetRVA_();

        //     STDMETHOD(GetPermissionSetProps) (  
        //         mdPermission pm,                    // [IN] the permission token.   
        //         DWORD       *pdwAction,             // [OUT] CorDeclSecurity.   
        //         void const  **ppvPermission,        // [OUT] permission blob.   
        //         ULONG       *pcbPermission) PURE;   // [OUT] count of bytes of pvPermission.    
        new void GetPermissionSetProps_();

        //     STDMETHOD(GetSigFromToken)(             // S_OK or error.   
        //         mdSignature mdSig,                  // [IN] Signature token.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] return pointer to token.   
        //         ULONG       *pcbSig) PURE;          // [OUT] return size of signature.  
        new void GetSigFromToken_();

        //     STDMETHOD(GetModuleRefProps)(           // S_OK or error.   
        //         mdModuleRef mur,                    // [IN] moduleref token.    
        //         LPWSTR      szName,                 // [OUT] buffer to fill with the moduleref name.    
        //         ULONG       cchName,                // [IN] size of szName in wide characters.  
        //         ULONG       *pchName) PURE;         // [OUT] actual count of characters in the name.    
        new void GetModuleRefProps_();

        //     STDMETHOD(EnumModuleRefs)(              // S_OK or error.   
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdModuleRef rModuleRefs[],          // [OUT] put modulerefs here.   
        //         ULONG       cmax,                   // [IN] max memberrefs to put.  
        //         ULONG       *pcModuleRefs) PURE;    // [OUT] put # put here.    
        new void EnumModuleRefs_();

        //     STDMETHOD(GetTypeSpecFromToken)(        // S_OK or error.   
        //         mdTypeSpec typespec,                // [IN] TypeSpec token.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] return pointer to TypeSpec signature  
        //         ULONG       *pcbSig) PURE;          // [OUT] return size of signature.  
        new void GetTypeSpecFromToken_();

        //     STDMETHOD(GetNameFromToken)(            // Not Recommended! May be removed!
        //         mdToken     tk,                     // [IN] Token to get name from.  Must have a name.
        //         MDUTF8CSTR  *pszUtf8NamePtr) PURE;  // [OUT] Return pointer to UTF8 name in heap.
        new void GetNameFromToken_();


        //     STDMETHOD(EnumUnresolvedMethods)(       // S_OK, S_FALSE, or error. 
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken     rMethods[],             // [OUT] Put MemberDefs here.   
        //         ULONG       cMax,                   // [IN] Max MemberDefs to put.  
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.
        new void EnumUnresolvedMethods_();

        //     STDMETHOD(GetUserString)(               // S_OK or error.
        //         mdString    stk,                    // [IN] String token.
        //         LPWSTR      szString,               // [OUT] Copy of string.
        //         ULONG       cchString,              // [IN] Max chars of room in szString.
        //         ULONG       *pchString) PURE;       // [OUT] How many chars in actual string.
        new void GetUserString([In] int stk,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString,
                           [In] int cchString,
                           [ComAliasName("ULONG*")] out int pchString
                           );

        //     STDMETHOD(GetPinvokeMap)(               // S_OK or error.
        //         mdToken     tk,                     // [IN] FieldDef or MethodDef.
        //         DWORD       *pdwMappingFlags,       // [OUT] Flags used for mapping.
        //         LPWSTR      szImportName,           // [OUT] Import name.
        //         ULONG       cchImportName,          // [IN] Size of the name buffer.
        //         ULONG       *pchImportName,         // [OUT] Actual number of characters stored.
        //         mdModuleRef *pmrImportDLL) PURE;    // [OUT] ModuleRef token for the target DLL.
        new void GetPinvokeMap_();

        //     STDMETHOD(EnumSignatures)(              // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdSignature rSignatures[],          // [OUT] put signatures here.   
        //         ULONG       cmax,                   // [IN] max signatures to put.  
        //         ULONG       *pcSignatures) PURE;    // [OUT] put # put here.
        new void EnumSignatures_();

        //     STDMETHOD(EnumTypeSpecs)(               // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN|OUT] pointer to the enum.    
        //         mdTypeSpec  rTypeSpecs[],           // [OUT] put TypeSpecs here.   
        //         ULONG       cmax,                   // [IN] max TypeSpecs to put.  
        //         ULONG       *pcTypeSpecs) PURE;     // [OUT] put # put here.
        new void EnumTypeSpecs_();

        //     STDMETHOD(EnumUserStrings)(             // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN/OUT] pointer to the enum.
        //         mdString    rStrings[],             // [OUT] put Strings here.
        //         ULONG       cmax,                   // [IN] max Strings to put.
        //         ULONG       *pcStrings) PURE;       // [OUT] put # put here.
        new void EnumUserStrings_();

        //     STDMETHOD(GetParamForMethodIndex)(      // S_OK or error.
        //         mdMethodDef md,                     // [IN] Method token.
        //         ULONG       ulParamSeq,             // [IN] Parameter sequence.
        //         mdParamDef  *ppd) PURE;             // [IN] Put Param token here.
        new void GetParamForMethodIndex_();

        //     STDMETHOD(EnumCustomAttributes)(        // S_OK or error.
        //         HCORENUM    *phEnum,                // [IN, OUT] COR enumerator.
        //         mdToken     tk,                     // [IN] Token to scope the enumeration, 0 for all.
        //         mdToken     tkType,                 // [IN] Type of interest, 0 for all.
        //         mdCustomAttribute rCustomAttributes[], // [OUT] Put custom attribute tokens here.
        //         ULONG       cMax,                   // [IN] Size of rCustomAttributes.
        //         ULONG       *pcCustomAttributes) PURE;  // [OUT, OPTIONAL] Put count of token values here.
        new void EnumCustomAttributes(ref IntPtr phEnum,
                         int tk,
                         int tkType,
                         [ComAliasName("mdCustomAttribute*")]out int mdCustomAttribute,
                         uint cMax /*must be 1*/,
                         [ComAliasName("ULONG*")]out uint pcTokens
                         );

        //     STDMETHOD(GetCustomAttributeProps)(     // S_OK or error.
        //         mdCustomAttribute cv,               // [IN] CustomAttribute token.
        //         mdToken     *ptkObj,                // [OUT, OPTIONAL] Put object token here.
        //         mdToken     *ptkType,               // [OUT, OPTIONAL] Put AttrType token here.
        //         void const  **ppBlob,               // [OUT, OPTIONAL] Put pointer to data here.
        //         ULONG       *pcbSize) PURE;         // [OUT, OPTIONAL] Put size of date here.
        new void GetCustomAttributeProps_();

        //     STDMETHOD(FindTypeRef)(   
        //         mdToken     tkResolutionScope,      // [IN] ModuleRef, AssemblyRef or TypeRef.
        //         LPCWSTR     szName,                 // [IN] TypeRef Name.
        //         mdTypeRef   *ptr) PURE;             // [OUT] matching TypeRef.
        new void FindTypeRef_();

        //     STDMETHOD(GetMemberProps)(  
        //         mdToken     mb,                     // The member for which to get props.   
        //         mdTypeDef   *pClass,                // Put member's class here. 
        //         LPWSTR      szMember,               // Put member's name here.  
        //         ULONG       cchMember,              // Size of szMember buffer in wide chars.   
        //         ULONG       *pchMember,             // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         ULONG       *pulCodeRVA,            // [OUT] codeRVA    
        //         DWORD       *pdwImplFlags,          // [OUT] Impl. Flags    
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppValue,              // [OUT] constant value 
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        new void GetMemberProps_();

        //     STDMETHOD(GetFieldProps)(  
        //         mdFieldDef  mb,                     // The field for which to get props.    
        //         mdTypeDef   *pClass,                // Put field's class here.  
        //         LPWSTR      szField,                // Put field's name here.   
        //         ULONG       cchField,               // Size of szField buffer in wide chars.    
        //         ULONG       *pchField,              // Put actual size here 
        //         DWORD       *pdwAttr,               // Put flags here.  
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob,            // [OUT] actual size of signature blob  
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppValue,              // [OUT] constant value 
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        new void GetFieldProps(int mb,
                           [ComAliasName("mdTypeDef*")] out int mdTypeDef,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szField,
                           int cchField,
                           [ComAliasName("ULONG*")] out int pchField,
                           [ComAliasName("DWORD*")] out int pdwAttr,
                           [ComAliasName("PCCOR_SIGNATURE*")] out IntPtr ppvSigBlob,
                           [ComAliasName("ULONG*")] out int pcbSigBlob,
                           [ComAliasName("DWORD*")] out int pdwCPlusTypeFlab,
                           [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppValue,
                           [ComAliasName("ULONG*")] out int pcchValue
                           );

        //     STDMETHOD(GetPropertyProps)(            // S_OK, S_FALSE, or error. 
        //         mdProperty  prop,                   // [IN] property token  
        //         mdTypeDef   *pClass,                // [OUT] typedef containing the property declarion. 
        //         LPCWSTR     szProperty,             // [OUT] Property name  
        //         ULONG       cchProperty,            // [IN] the count of wchar of szProperty    
        //         ULONG       *pchProperty,           // [OUT] actual count of wchar for property name    
        //         DWORD       *pdwPropFlags,          // [OUT] property flags.    
        //         PCCOR_SIGNATURE *ppvSig,            // [OUT] property type. pointing to meta data internal blob 
        //         ULONG       *pbSig,                 // [OUT] count of bytes in *ppvSig  
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] flag for value type. selected ELEMENT_TYPE_*   
        //         void const  **ppDefaultValue,       // [OUT] constant value 
        //         ULONG       *pcchDefaultValue,      // [OUT] size of constant string in chars, 0 for non-strings.
        //         mdMethodDef *pmdSetter,             // [OUT] setter method of the property  
        //         mdMethodDef *pmdGetter,             // [OUT] getter method of the property  
        //         mdMethodDef rmdOtherMethod[],       // [OUT] other method of the property   
        //         ULONG       cMax,                   // [IN] size of rmdOtherMethod  
        //         ULONG       *pcOtherMethod) PURE;   // [OUT] total number of other method of this property
        new void GetPropertyProps(int mb,
                    [ComAliasName("mdTypeDef*")] out int mdTypeDef,
                    [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szProperty,
                    int cchProperty,
                    [ComAliasName("ULONG*")] out int pchProperty,
                    [ComAliasName("DWORD*")] out int pdwPropFlags,
                    [ComAliasName("PCCOR_SIGNATURE*")] out IntPtr ppvSigBlob,
                    [ComAliasName("ULONG*")] out int pcbSigBlob,
                    [ComAliasName("DWORD*")] out int pdwCPlusTypeFlag,
                    [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppDefaultValue,
                    [ComAliasName("ULONG*")] out int pcchDefaultValue,
                    [ComAliasName("mdMethodDef*")] out int mdSetter,
                    [ComAliasName("mdMethodDef*")] out int mdGetter,
                    [ComAliasName("mdMethodDef*")] out int rmdOtherMethod,
                    [ComAliasName("ULONG")] int cMax, /* must be 1 */
                    [ComAliasName("ULONG*")] out int pcOtherMethod
                    );


        //     STDMETHOD(GetParamProps)(               // S_OK or error.
        //         mdParamDef  tk,                     // [IN]The Parameter.
        //         mdMethodDef *pmd,                   // [OUT] Parent Method token.
        //         ULONG       *pulSequence,           // [OUT] Parameter sequence.
        //         LPWSTR      szName,                 // [OUT] Put name here.
        //         ULONG       cchName,                // [OUT] Size of name buffer.
        //         ULONG       *pchName,               // [OUT] Put actual size of name here.
        //         DWORD       *pdwAttr,               // [OUT] Put flags here.
        //         DWORD       *pdwCPlusTypeFlag,      // [OUT] Flag for value type. selected ELEMENT_TYPE_*.
        //         void const  **ppValue,              // [OUT] Constant value.
        //         ULONG       *pcchValue) PURE;       // [OUT] size of constant string in chars, 0 for non-strings.
        new void GetParamProps(int tk,
                           [ComAliasName("mdMethodDef*")] out int pmd,
                           [ComAliasName("ULONG*")] out uint pulSequence,
                           [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder szName,
                           uint cchName,
                           [ComAliasName("ULONG*")] out uint pchName,
                           [ComAliasName("DWORD*")] out uint pdwAttr,
                           [ComAliasName("DWORD*")] out uint pdwCPlusTypeFlag,
                           [ComAliasName("UVCP_CONSTANT*")] out IntPtr ppValue,
                           [ComAliasName("ULONG*")] out uint pcchValue
                           );

        //     STDMETHOD(GetCustomAttributeByName)(    // S_OK or error.
        //         mdToken     tkObj,                  // [IN] Object with Custom Attribute.
        //         LPCWSTR     szName,                 // [IN] Name of desired Custom Attribute.
        //         const void  **ppData,               // [OUT] Put pointer to data here.
        //         ULONG       *pcbData) PURE;         // [OUT] Put size of data here.
        [PreserveSig]
        new int GetCustomAttributeByName(
                            int tkObj,
                            [MarshalAs(UnmanagedType.LPWStr)]string szName,
                            out IntPtr ppData,
                            out uint pcbData);

        //     STDMETHOD_(BOOL, IsValidToken)(         // True or False.
        //         mdToken     tk) PURE;               // [IN] Given token.
        [PreserveSig] new bool IsValidToken([In, MarshalAs(UnmanagedType.U4)] uint tk);

        //     STDMETHOD(GetNestedClassProps)(         // S_OK or error.
        //         mdTypeDef   tdNestedClass,          // [IN] NestedClass token.
        //         mdTypeDef   *ptdEnclosingClass) PURE; // [OUT] EnclosingClass token.
        new void GetNestedClassProps(int tdNestedClass, [ComAliasName("mdTypeDef*")] out int tdEnclosingClass);

        //     STDMETHOD(GetNativeCallConvFromSig)(    // S_OK or error.
        //         void const  *pvSig,                 // [IN] Pointer to signature.
        //         ULONG       cbSig,                  // [IN] Count of signature bytes.
        //         ULONG       *pCallConv) PURE;       // [OUT] Put calling conv here (see CorPinvokemap).
        new void GetNativeCallConvFromSig_();


        //     STDMETHOD(IsGlobal)(                    // S_OK or error.
        //         mdToken     pd,                     // [IN] Type, Field, or Method token.
        //         int         *pbGlobal) PURE;        // [OUT] Put 1 if global, 0 otherwise.
        new void IsGlobal_();
        #endregion // inheritted methods from IMetadataImport

        //-----------------------------------------------------------------------------
        // Begin IMetaDataImport2
        //-----------------------------------------------------------------------------

        /*
          STDMETHOD(EnumGenericParams)(
          HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
          mdToken      tk,                    // [IN] TypeDef or MethodDef whose generic parameters are requested
          mdGenericParam rGenericParams[],    // [OUT] Put GenericParams here.   
          ULONG       cMax,                   // [IN] Max GenericParams to put.  
          ULONG       *pcGenericParams) PURE; // [OUT] Put # put here.    
        */
        void EnumGenericParams(
                                ref IntPtr hEnum,
                                int tk,
                                [ComAliasName("mdGenericParam*")] out int rGenericParams, // <strip>@todo - this is a total marshalling shack. Only works if cMax == 1.</strip>
                                uint cMax, // must be 1
                                [ComAliasName("ULONG*")] out uint pcGenericParams
        );

        //         STDMETHOD(GetGenericParamProps)(        // S_OK or error.
        //         mdGenericParam gp,                  // [IN] GenericParam
        //         ULONG        *pulParamSeq,          // [OUT] Index of the type parameter
        //         DWORD        *pdwParamFlags,        // [OUT] Flags, for future use (e.g. variance)
        //         mdToken      *ptOwner,              // [OUT] Owner (TypeDef or MethodDef)
        //         mdToken      *ptkKind,              // [OUT] For future use (e.g. non-type parameters)
        //         LPWSTR       wzname,                // [OUT] Put name here
        //         ULONG        cchName,               // [IN] Size of buffer
        //         ULONG        *pchName) PURE;        // [OUT] Put size of name (wide chars) here.
        void GetGenericParamProps(int gp,
                                  [ComAliasName("ULONG*")] out uint pulParamSeq,
                                  [ComAliasName("DWORD*")] out int pdwParamFlags,
                                  [ComAliasName("mdToken*")] out int ptOwner,
                                  [ComAliasName("mdToken*")] out int ptkKind,
                                  [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder wzName,
                                  ulong cchName,
                                  [ComAliasName("ULONG*")] out ulong pchName
                                  );

        //         STDMETHOD(GetMethodSpecProps)(
        //         mdMethodSpec mi,                    // [IN] The method instantiation
        //         mdToken *tkParent,                  // [OUT] MethodDef or MemberRef
        //         PCCOR_SIGNATURE *ppvSigBlob,        // [OUT] point to the blob value of meta data   
        //         ULONG       *pcbSigBlob) PURE;      // [OUT] actual size of signature blob  

        void GetMethodSpecProps([ComAliasName("mdMethodSpec")] int mi,
                                [ComAliasName("mdToken*")] out int tkParent,
                                [ComAliasName("PCCOR_SIGNATURE*")] out IntPtr ppvSigBlob,
                                [ComAliasName("ULONG*")] out int pcbSigBlob
                                );

        //         STDMETHOD(EnumGenericParamConstraints)(
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdGenericParam tk,                  // [IN] GenericParam whose constraints are requested
        //         mdGenericParamConstraint rGenericParamConstraints[],    // [OUT] Put GenericParamConstraints here.   
        //         ULONG       cMax,                   // [IN] Max GenericParamConstraints to put.  
        //         ULONG       *pcGenericParamConstraints) PURE; // [OUT] Put # put here.
        void EnumGenericParamConstraints_();

        //         STDMETHOD(GetGenericParamConstraintProps)( // S_OK or error.
        //         mdGenericParamConstraint gpc,       // [IN] GenericParamConstraint
        //         mdGenericParam *ptGenericParam,     // [OUT] GenericParam that is constrained
        //         mdToken      *ptkConstraintType) PURE; // [OUT] TypeDef/Ref/Spec constraint
        void GetGenericParamConstraintProps_();

        //         STDMETHOD(GetPEKind)(                   // S_OK or error.
        //         DWORD* pdwPEKind,                   // [OUT] The kind of PE (0 - not a PE)
        //         DWORD* pdwMachine) PURE;            // [OUT] Machine as defined in NT header
        void GetPEKind_();

        //         STDMETHOD(GetVersionString)(            // S_OK or error.
        //         LPWSTR      pwzBuf,                 // [OUT[ Put version string here.
        //         DWORD       ccBufSize,              // [IN] size of the buffer, in wide chars
        //         DWORD       *pccBufSize) PURE;      // [OUT] Size of the version string, wide chars, including terminating nul.
        void GetVersionString_();

        //         STDMETHOD(EnumMethodSpecs)(
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.    
        //         mdToken      tk,                    // [IN] MethodDef or MemberRef whose MethodSpecs are requested
        //         mdMethodSpec rMethodSpecs[],        // [OUT] Put MethodSpecs here.   
        //         ULONG       cMax,                   // [IN] Max tokens to put.  
        //         ULONG       *pcMethodSpecs) PURE;   // [OUT] Put actual count here.
        void EnumMethodSpecs_();
    }

    // GUID Copied from Cor.h
    [Guid("EE62470B-E94B-424e-9B7C-2F00C9249F93"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ] // IID_IMetadataAssemblyImport from cor.h

    // This should be a private interface, but
    // we cannot do that because we are then getting an exception
    // "The specified type must be visible from COM." @ CorMetadataImport::GetRawInterface
    public interface IMetadataAssemblyImport
    {
        //     STDMETHOD(GetAssemblyProps)(            // S_OK or error.
        //         mdAssembly  mda,                    // [IN] The Assembly for which to get the properties.
        //         const void  **ppbPublicKey,         // [OUT] Pointer to the public key.
        //         ULONG       *pcbPublicKey,          // [OUT] Count of bytes in the public key.
        //         ULONG       *pulHashAlgId,          // [OUT] Hash Algorithm.
        //         LPWSTR      szName,                 // [OUT] Buffer to fill with assembly's simply name.
        //         ULONG       cchName,                // [IN] Size of buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Actual # of wide chars in name.
        //         ASSEMBLYMETADATA *pMetaData,        // [OUT] Assembly MetaData.
        //         DWORD       *pdwAssemblyFlags) PURE;    // [OUT] Flags.

        //     STDMETHOD(GetAssemblyRefProps)(         // S_OK or error.
        //         mdAssemblyRef mdar,                 // [IN] The AssemblyRef for which to get the properties.
        //         const void  **ppbPublicKeyOrToken,  // [OUT] Pointer to the public key or token.
        //         ULONG       *pcbPublicKeyOrToken,   // [OUT] Count of bytes in the public key or token.
        //         LPWSTR      szName,                 // [OUT] Buffer to fill with name.
        //         ULONG       cchName,                // [IN] Size of buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Actual # of wide chars in name.
        //         ASSEMBLYMETADATA *pMetaData,        // [OUT] Assembly MetaData.
        //         const void  **ppbHashValue,         // [OUT] Hash blob.
        //         ULONG       *pcbHashValue,          // [OUT] Count of bytes in the hash blob.
        //         DWORD       *pdwAssemblyRefFlags) PURE; // [OUT] Flags.

        //     STDMETHOD(GetFileProps)(                // S_OK or error.
        //         mdFile      mdf,                    // [IN] The File for which to get the properties.
        //         LPWSTR      szName,                 // [OUT] Buffer to fill with name.
        //         ULONG       cchName,                // [IN] Size of buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Actual # of wide chars in name.
        //         const void  **ppbHashValue,         // [OUT] Pointer to the Hash Value Blob.
        //         ULONG       *pcbHashValue,          // [OUT] Count of bytes in the Hash Value Blob.
        //         DWORD       *pdwFileFlags) PURE;    // [OUT] Flags.

        //     STDMETHOD(GetExportedTypeProps)(        // S_OK or error.
        //         mdExportedType   mdct,              // [IN] The ExportedType for which to get the properties.
        //         LPWSTR      szName,                 // [OUT] Buffer to fill with name.
        //         ULONG       cchName,                // [IN] Size of buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Actual # of wide chars in name.
        //         mdToken     *ptkImplementation,     // [OUT] mdFile or mdAssemblyRef or mdExportedType.
        //         mdTypeDef   *ptkTypeDef,            // [OUT] TypeDef token within the file.
        //         DWORD       *pdwExportedTypeFlags) PURE; // [OUT] Flags.

        //     STDMETHOD(GetManifestResourceProps)(    // S_OK or error.
        //         mdManifestResource  mdmr,           // [IN] The ManifestResource for which to get the properties.
        //         LPWSTR      szName,                 // [OUT] Buffer to fill with name.
        //         ULONG       cchName,                // [IN] Size of buffer in wide chars.
        //         ULONG       *pchName,               // [OUT] Actual # of wide chars in name.
        //         mdToken     *ptkImplementation,     // [OUT] mdFile or mdAssemblyRef that provides the ManifestResource.
        //         DWORD       *pdwOffset,             // [OUT] Offset to the beginning of the resource within the file.
        //         DWORD       *pdwResourceFlags) PURE;// [OUT] Flags.

        //     STDMETHOD(EnumAssemblyRefs)(            // S_OK or error
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.
        //         mdAssemblyRef rAssemblyRefs[],      // [OUT] Put AssemblyRefs here.
        //         ULONG       cMax,                   // [IN] Max AssemblyRefs to put.
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.

        //     STDMETHOD(EnumFiles)(                   // S_OK or error
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.
        //         mdFile      rFiles[],               // [OUT] Put Files here.
        //         ULONG       cMax,                   // [IN] Max Files to put.
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.

        //     STDMETHOD(EnumExportedTypes)(           // S_OK or error
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.
        //         mdExportedType   rExportedTypes[],  // [OUT] Put ExportedTypes here.
        //         ULONG       cMax,                   // [IN] Max ExportedTypes to put.
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.

        //     STDMETHOD(EnumManifestResources)(       // S_OK or error
        //         HCORENUM    *phEnum,                // [IN|OUT] Pointer to the enum.
        //         mdManifestResource  rManifestResources[],   // [OUT] Put ManifestResources here.
        //         ULONG       cMax,                   // [IN] Max Resources to put.
        //         ULONG       *pcTokens) PURE;        // [OUT] Put # put here.

        //     STDMETHOD(GetAssemblyFromScope)(        // S_OK or error
        //         mdAssembly  *ptkAssembly) PURE;     // [OUT] Put token here.

        //     STDMETHOD(FindExportedTypeByName)(      // S_OK or error
        //         LPCWSTR     szName,                 // [IN] Name of the ExportedType.
        //         mdToken     mdtExportedType,        // [IN] ExportedType for the enclosing class.
        //         mdExportedType   *ptkExportedType) PURE; // [OUT] Put the ExportedType token here.

        //     STDMETHOD(FindManifestResourceByName)(  // S_OK or error
        //         LPCWSTR     szName,                 // [IN] Name of the ManifestResource.
        //         mdManifestResource *ptkManifestResource) PURE;  // [OUT] Put the ManifestResource token here.

        //     STDMETHOD_(void, CloseEnum)(
        //         HCORENUM hEnum) PURE;               // Enum to be closed.

        //     STDMETHOD(FindAssembliesByName)(        // S_OK or error
        //         LPCWSTR  szAppBase,                 // [IN] optional - can be NULL
        //         LPCWSTR  szPrivateBin,              // [IN] optional - can be NULL
        //         LPCWSTR  szAssemblyName,            // [IN] required - this is the assembly you are requesting
        //         IUnknown *ppIUnk[],                 // [OUT] put IMetaDataAssemblyImport pointers here
        //         ULONG    cMax,                      // [IN] The max number to put
        //         ULONG    *pcAssemblies) PURE;       // [OUT] The number of assemblies returned.
        // };  // IMetaDataAssemblyImport
    }


    // Get the geometry of the tables. This is useful for GetTableInfo, which can tell how
    // many rows a table has, which can then be used for quick enumeration of tokens.
    [Guid("D8F579AB-402D-4b8e-82D9-5D63B1065C68"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMetadataTables
    {
        //STDMETHOD (GetStringHeapSize) (    
        //    ULONG   *pcbStrings) PURE;          // [OUT] Size of the string heap.
        void GetStringHeapSize(out uint countBytesStrings);

        //STDMETHOD (GetBlobHeapSize) (    
        //    ULONG   *pcbBlobs) PURE;            // [OUT] Size of the Blob heap.
        void GetBlobHeapSize(out uint countBytesBlobs);

        //STDMETHOD (GetGuidHeapSize) (    
        //    ULONG   *pcbGuids) PURE;            // [OUT] Size of the Guid heap.
        void GetGuidHeapSize(out uint countBytesGuids);

        //STDMETHOD (GetUserStringHeapSize) (  
        //    ULONG   *pcbBlobs) PURE;            // [OUT] Size of the User String heap.
        void GetUserStringHeapSize(out uint countByteBlobs);

        //STDMETHOD (GetNumTables) (    
        //    ULONG   *pcTables) PURE;            // [OUT] Count of tables.
        void GetNumTables(out uint countTables);


        //STDMETHOD (GetTableIndex) (   
        //    ULONG   token,                      // [IN] Token for which to get table index.
        //    ULONG   *pixTbl) PURE;              // [OUT] Put table index here.
        void GetTableIndex(uint token, out uint tableIndex);


        //STDMETHOD (GetTableInfo) (    
        //    ULONG   ixTbl,                      // [IN] Which table.
        //    ULONG   *pcbRow,                    // [OUT] Size of a row, bytes.
        //    ULONG   *pcRows,                    // [OUT] Number of rows.
        //    ULONG   *pcCols,                    // [OUT] Number of columns in each row.
        //    ULONG   *piKey,                     // [OUT] Key column, or -1 if none.
        //    const char **ppName) PURE;          // [OUT] Name of the table.
        void GetTableInfo(
            uint tableIndex,
            out uint countByteRows,
            out uint countRows,
            out uint countColumns,
            out uint columnPrimaryKey,
            [Out, MarshalAs(UnmanagedType.LPStr)] out String name);


        // Other methods are not yet imported...
    }
}
