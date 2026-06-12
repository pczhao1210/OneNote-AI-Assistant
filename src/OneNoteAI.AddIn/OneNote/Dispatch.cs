using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OneNoteAI.OneNote
{
    /// <summary>
    /// Calls COM objects via IDispatch::GetIDsOfNames + IDispatch::Invoke directly,
    /// bypassing both C# 'dynamic' (which calls IDispatch::GetTypeInfo, failing on
    /// OneNote with E_FAIL) and .NET reflection's COM binder (which tries to load
    /// the registered TypeLib, failing with TYPE_E_LIBNOTREGISTERED on unified OneNote).
    /// </summary>
    internal static class Dispatch
    {
        private static readonly Guid IID_NULL = Guid.Empty;
        private const int LOCALE_USER_DEFAULT = 0x0400;

        // DISPATCH_* flags
        private const ushort DISPATCH_METHOD = 0x1;
        private const ushort DISPATCH_PROPERTYGET = 0x2;
        private const ushort DISPATCH_PROPERTYPUT = 0x4;

        // Special dispid for property-put named arg
        private const int DISPID_PROPERTYPUT = -3;

        public static object GetProperty(object target, string name)
        {
            return Invoke(target, name, DISPATCH_PROPERTYGET, null, null);
        }

        public static object CallMethod(object target, string name, object[] args)
        {
            return Invoke(target, name, DISPATCH_METHOD | DISPATCH_PROPERTYGET, args, null);
        }

        /// <summary>
        /// Calls a COM method that has [out] / [in,out] parameters and updates
        /// <paramref name="args"/> in place with the returned values.
        /// </summary>
        public static object CallMethodWithOuts(object target, string name, object[] args, bool[] isOut)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (isOut == null || isOut.Length != args.Length)
                throw new ArgumentException("isOut must match args length", nameof(isOut));

            IDispatch disp = target as IDispatch;
            if (disp == null) throw new InvalidOperationException("Target does not support IDispatch");

            int dispId = GetDispId(disp, name);

            // VARIANT layout in DISPPARAMS: arguments are passed in REVERSE order
            int n = args.Length;
            IntPtr rgvarg = n == 0 ? IntPtr.Zero : Marshal.AllocCoTaskMem(VariantSize * n);
            try
            {
                // Build reversed variant array, marking [out] params as VT_BYREF | VT_VARIANT
                IntPtr[] outVariants = new IntPtr[n];
                for (int i = 0; i < n; i++)
                {
                    int reversedIndex = n - 1 - i;
                    IntPtr varPtr = IntPtr.Add(rgvarg, reversedIndex * VariantSize);
                    if (isOut[i])
                    {
                        // Allocate a child VARIANT to hold the actual value, point to it via VT_BYREF|VT_VARIANT
                        IntPtr child = Marshal.AllocCoTaskMem(VariantSize);
                        ZeroVariant(child);
                        // Initialize child with the input value (in case it's [in,out])
                        Marshal.GetNativeVariantForObject(args[i] ?? Type.Missing, child);
                        outVariants[i] = child;

                        ZeroVariant(varPtr);
                        // VT_BYREF (0x4000) | VT_VARIANT (0x000C) = 0x400C
                        Marshal.WriteInt16(varPtr, unchecked((short)0x400C));
                        // The data pointer goes at offset 8 in the VARIANT struct (on x86 / x64 layout)
                        Marshal.WriteIntPtr(varPtr, 8, child);
                    }
                    else
                    {
                        ZeroVariant(varPtr);
                        Marshal.GetNativeVariantForObject(args[i] ?? Type.Missing, varPtr);
                    }
                }

                DISPPARAMS dp = new DISPPARAMS
                {
                    rgvarg = rgvarg,
                    rgdispidNamedArgs = IntPtr.Zero,
                    cArgs = n,
                    cNamedArgs = 0,
                };

                EXCEPINFO ei = new EXCEPINFO();
                object result;
                int hr = disp.Invoke(
                    dispId,
                    ref IID_NULL_Ref,
                    LOCALE_USER_DEFAULT,
                    DISPATCH_METHOD | DISPATCH_PROPERTYGET,
                    ref dp,
                    out result,
                    out ei,
                    IntPtr.Zero);

                if (hr < 0)
                {
                    string msg = ei.bstrDescription ?? "IDispatch.Invoke failed";
                    throw new COMException("[" + name + "] " + msg, hr);
                }

                // Read back [out] params
                for (int i = 0; i < n; i++)
                {
                    if (isOut[i] && outVariants[i] != IntPtr.Zero)
                    {
                        args[i] = Marshal.GetObjectForNativeVariant(outVariants[i]);
                    }
                }

                return result;
            }
            finally
            {
                if (rgvarg != IntPtr.Zero)
                {
                    // Clean up all child VARIANTs and the array
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr varPtr = IntPtr.Add(rgvarg, i * VariantSize);
                        try { NativeMethods.VariantClear(varPtr); } catch { }
                    }
                    Marshal.FreeCoTaskMem(rgvarg);
                }
            }
        }

        private static object Invoke(object target, string name, ushort flags, object[] args, int[] dispIdNamedArgs)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            IDispatch disp = target as IDispatch;
            if (disp == null) throw new InvalidOperationException("Target does not support IDispatch (type=" + target.GetType().FullName + ")");

            int dispId = GetDispId(disp, name);

            int n = args == null ? 0 : args.Length;
            IntPtr rgvarg = n == 0 ? IntPtr.Zero : Marshal.AllocCoTaskMem(VariantSize * n);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    int reversedIndex = n - 1 - i;
                    IntPtr varPtr = IntPtr.Add(rgvarg, reversedIndex * VariantSize);
                    ZeroVariant(varPtr);
                    Marshal.GetNativeVariantForObject(args[i] ?? Type.Missing, varPtr);
                }

                DISPPARAMS dp = new DISPPARAMS
                {
                    rgvarg = rgvarg,
                    rgdispidNamedArgs = IntPtr.Zero,
                    cArgs = n,
                    cNamedArgs = 0,
                };

                EXCEPINFO ei = new EXCEPINFO();
                object result;
                int hr = disp.Invoke(
                    dispId,
                    ref IID_NULL_Ref,
                    LOCALE_USER_DEFAULT,
                    flags,
                    ref dp,
                    out result,
                    out ei,
                    IntPtr.Zero);

                if (hr < 0)
                {
                    string msg = ei.bstrDescription ?? "IDispatch.Invoke failed";
                    throw new COMException("[" + name + "] " + msg, hr);
                }

                return result;
            }
            finally
            {
                if (rgvarg != IntPtr.Zero)
                {
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr varPtr = IntPtr.Add(rgvarg, i * VariantSize);
                        try { NativeMethods.VariantClear(varPtr); } catch { }
                    }
                    Marshal.FreeCoTaskMem(rgvarg);
                }
            }
        }

        private static int GetDispId(IDispatch disp, string name)
        {
            string[] names = new[] { name };
            int[] ids = new int[1];
            int hr = disp.GetIDsOfNames(ref IID_NULL_Ref, names, 1, LOCALE_USER_DEFAULT, ids);
            if (hr < 0)
            {
                throw new COMException("GetIDsOfNames failed for '" + name + "'", hr);
            }
            return ids[0];
        }

        // VARIANT size: 16 on x86, 24 on x64
        private static readonly int VariantSize = IntPtr.Size == 8 ? 24 : 16;
        private static Guid IID_NULL_Ref = Guid.Empty;

        private static void ZeroVariant(IntPtr varPtr)
        {
            // Zero out the whole VARIANT structure
            for (int i = 0; i < VariantSize; i += 8)
            {
                if (i + 8 <= VariantSize)
                {
                    Marshal.WriteInt64(varPtr, i, 0L);
                }
            }
            // tail bytes (x86 = 0, x64 = 0)
        }

        // ---- IDispatch (manual, no TLB) ----
        [ComImport]
        [Guid("00020400-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            [PreserveSig]
            int GetTypeInfoCount(out uint pctinfo);

            [PreserveSig]
            int GetTypeInfo(uint iTInfo, int lcid, out IntPtr ppTInfo);

            [PreserveSig]
            int GetIDsOfNames(
                [In] ref Guid riid,
                [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] rgszNames,
                int cNames,
                int lcid,
                [Out, MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);

            [PreserveSig]
            int Invoke(
                int dispIdMember,
                [In] ref Guid riid,
                int lcid,
                ushort wFlags,
                [In, Out] ref DISPPARAMS pDispParams,
                [Out] out object pVarResult,
                [Out] out EXCEPINFO pExcepInfo,
                IntPtr puArgErr);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPPARAMS
        {
            public IntPtr rgvarg;
            public IntPtr rgdispidNamedArgs;
            public int cArgs;
            public int cNamedArgs;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EXCEPINFO
        {
            public ushort wCode;
            public ushort wReserved;
            [MarshalAs(UnmanagedType.BStr)] public string bstrSource;
            [MarshalAs(UnmanagedType.BStr)] public string bstrDescription;
            [MarshalAs(UnmanagedType.BStr)] public string bstrHelpFile;
            public uint dwHelpContext;
            public IntPtr pvReserved;
            public IntPtr pfnDeferredFillIn;
            public int scode;
        }

        private static class NativeMethods
        {
            [DllImport("oleaut32.dll", PreserveSig = false)]
            public static extern void VariantClear(IntPtr pvarg);
        }
    }
}
