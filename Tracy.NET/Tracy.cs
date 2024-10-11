using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static TracyNET.Native;

namespace TracyNET;

/// High-level C# bindings for Tracy
public unsafe class Tracy
{
    #region General

    /// Checks whether the Tracy Server is connected
    public static bool IsConnected() => TracyConnected() != 0;

    /// Changes the name of the program for the client-discovery
    public static void SetProgramName(string name)
    {
        var nameStr = GetCString(name, out _);
        TracySetProgramName(nameStr);
    }

    /// Changes the name of the current thread in the profiler
    public static void SetThreadName(string name)
    {
        var nameStr = GetCString(name, out _);
        TracySetThreadName(nameStr);
    }

    #endregion
    #region Frames

    /// Marks the current frame as completed
    /// Should be called right after the graphics buffers have been swapped
    /// <param name="name">If not null, creates a new secondary frame set</param>
    public static void MarkFrame(string? name = null)
    {
        var nameStr = GetCString(name, out _);
        TracyEmitFrameMark(nameStr);
    }

    /// Marks the beginning of a discontinuous frame
    /// <param name="name">If not null, creates a new secondary frame set</param>
    public static void MarkFrameStart(string? name = null)
    {
        var nameStr = GetCString(name, out _);
        TracyEmitFrameMarkStart(nameStr);
    }

    /// Marks the end of a discontinuous frame
    /// <param name="name">If not null, creates a new secondary frame set</param>
    public static void MarkFrameEnd(string? name = null)
    {
        var nameStr = GetCString(name, out _);
        TracyEmitFrameMarkEnd(nameStr);
    }

    /// Uploads an image associated with the current frame
    /// It is recommended to scale screenshots down to a size of 320x180
    /// <param name="data">Pointer to the RGBA image data (alpha is ignored)</param>
    /// <param name="width">Width of the image. Must be divisible by 4</param>
    /// <param name="height">Height of the image. Must be divisible by 4</param>
    /// <param name="offset">Amount of frames which have passed, since the image was captured</param>
    /// <param name="flipY">Whether to flip the image along the Y-axis</param>
    public static void UploadFrameImage(byte* data, ushort width, ushort height, byte offset, bool flipY)
    {
        TracyEmitFrameImage(data, width, height, offset, flipY ? 1 : 0);
    }

    #endregion
    #region Zones

    /// Inserts a profiling zone for this entire method
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ProfileMethod(
        string? name = null,
        uint color = 0x000000,
        bool active = true,
        [CallerMemberName] string? function = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0
    ) : Attribute;

    /// Represents a profiling zone
    public readonly struct ZoneContext : IDisposable
    {
        private readonly TracyCZoneContext context;

        internal ZoneContext(TracyCZoneContext context) {
            this.context = context;
        }

        /// Whether the zone is currently sent to the profiler
        public bool Active => context.active != 0;

        /// Additional text to display along with the zone information
        public string Text {
            set => TracyEmitZoneText(context, GetCString(value, out ulong textLen), textLen);
        }

        /// Additional numeric value to display along with the zone information
        public ulong Value {
            set => TracyEmitZoneValue(context, value);
        }

        /// Dynamically change the name of the zone
        public string Name {
            set => TracyEmitZoneName(context, GetCString(value, out ulong nameLen), nameLen);
        }

        /// Dynamically change the color of the zone
        public uint Color {
            set => TracyEmitZoneColor(context, value);
        }

        public void Dispose() => TracyEmitZoneEnd(context);
    }

    /// Creates a new profiling zone until the end of the current scope
    public static ZoneContext Zone(
        string? name = null,
        uint color = 0x000000,
        bool active = true,
        [CallerMemberName] string? function = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        var sourceLocation = TracyAllocSrclocName(
            (uint)line,
            GetCString(file, out ulong fileLen), fileLen,
            GetCString(function, out ulong functionLen), functionLen,
            GetCString(name, out ulong nameLen), nameLen,
            color);
        var context = TracyEmitZoneBeginAlloc(sourceLocation, active ? 1 : 0);

        return new ZoneContext(context);
    }

    #endregion
    #region Internal

    // Tracy expects most string to be of static lifetime, so we need to cache them
    private static readonly Dictionary<string, CString> stringCache = [];
    private static CString GetCString(string? str, out ulong len)
    {
        if (str == null) {
            len = 0;
            return CString.Null;
        }

        len = (ulong)str.Length;

        if (stringCache.TryGetValue(str, out var cString))
            return cString;

        cString = new CString(str);
        stringCache[str] = cString;
        return cString;
    }


    #endregion
}
