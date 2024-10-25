using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TracyNET;

/// Direct bindings for the native C functions
internal static unsafe partial class Native
{
    private const string LibraryName = "TracyClient";

    #region Functions

    [LibraryImport(LibraryName, EntryPoint = "___tracy_set_thread_name")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracySetThreadName(CString name);

    [LibraryImport(LibraryName, EntryPoint = "TracyCSetProgramName")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracySetProgramName(CString name);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_alloc_srcloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial TracySourceLocationData* TracyAllocSrcloc(uint line, CString source, ulong sourceSz, CString function, ulong functionSz, uint color);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_alloc_srcloc_name")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial TracySourceLocationData* TracyAllocSrclocName(uint line, CString source, ulong sourceSz, CString function, ulong functionSz, CString name, ulong nameSz, uint color);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_begin")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial TracyCZoneContext TracyEmitZoneBegin(TracySourceLocationData* srcloc, int active);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_begin_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial TracyCZoneContext TracyEmitZoneBeginAlloc(TracySourceLocationData* srcloc, int active);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_end")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitZoneEnd(TracyCZoneContext ctx);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitZoneText(TracyCZoneContext ctx, CString txt, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_name")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitZoneName(TracyCZoneContext ctx, CString txt, ulong size);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_color")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitZoneColor(TracyCZoneContext ctx, uint color);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_zone_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitZoneValue(TracyCZoneContext ctx, ulong value);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_plot_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitPlotConfig(CString name, int type, int step, int fill, uint color);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_plot_int")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitPlotInt(CString name, long val);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_plot_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitPlotFloat(CString name, float val);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_plot")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitPlotDouble(CString name, double val);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_message")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitMessage(CString txt, ulong size, int callstack);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_messageC")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitMessageColor(CString txt, ulong size, uint color, int callstack);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_connected")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int TracyConnected();

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_frame_mark")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitFrameMark(CString name);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_frame_mark_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitFrameMarkStart(CString name);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_frame_mark_end")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitFrameMarkEnd(CString name);

    [LibraryImport(LibraryName, EntryPoint = "___tracy_emit_frame_image")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TracyEmitFrameImage(void* image, ushort w, ushort h, byte offset, int flip);

    #endregion
    #region Types

    /// Wrapper around C strings
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct CString(IntPtr data) : IEquatable<CString>, IDisposable
    {
        public static readonly CString Null = new(IntPtr.Zero);

        [FieldOffset(0)]
        private readonly IntPtr data = data;

        public CString(string? str) : this(Marshal.StringToHGlobalAnsi(str)) { }

        /// Allocates a new unmanaged C string
        public static CString Create(string? str, out ulong len) {
            len = (ulong)(str?.Length ?? 0);
            return new CString(str);
        }

        /// Frees the allocated string
        public void Dispose() => Marshal.FreeHGlobal(data);

        /// Compares both strings for the same content
        public bool Equals(CString other)
        {
            byte* a = (byte*)this.data, b = (byte*)other.data;
            while (*a != 0 || *b != 0)
            {
                if (*a != *b) return false;
                a++;
                b++;
            }
            return *a == *b;
        }

        public override bool Equals(object? obj) => obj is CString other && Equals(other);
        public override int GetHashCode() => data.GetHashCode();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TracySourceLocationData
    {
        public CString name;
        public CString function;
        public CString file;
        public uint line;
        public uint color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TracyCZoneContext
    {
        public uint id;
        public int active;
    }

    #endregion
}
