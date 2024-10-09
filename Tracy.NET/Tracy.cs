using System;
using System.Runtime.CompilerServices;
using static Tracy.Native;

namespace Tracy;

public unsafe class Tracy
{
    #region Frames

    /// Marks the current frame as completed
    /// Should be called right after the graphics buffers have been swapped
    public static void MarkFrame(string? name = null)
    {
        using var nameStr = new CString(name);
        TracyEmitFrameMark(nameStr);
    }

    /// Marks the beginning of a discontinuous frame
    public static void MarkFrameStart(string? name = null)
    {
        using var nameStr = new CString(name);
        TracyEmitFrameMarkStart(nameStr);
    }

    /// Marks the end of a discontinuous frame
    public static void MarkFrameEnd(string? name = null)
    {
        using var nameStr = new CString(name);
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
            set {
                using var textStr = new CString(value);
                TracyEmitZoneText(context, textStr, (ulong)value.Length);
            }
        }

        /// Additional numeric value to display along with the zone information
        public ulong Value {
            set => TracyEmitZoneValue(context, value);
        }

        /// Dynamically change the name of the zone
        public string Name {
            set {
                using var nameStr = new CString(value);
                TracyEmitZoneName(context, nameStr, (ulong)value.Length);
            }
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
        using var nameStr = new CString(name);
        using var functionStr = new CString(function);
        using var fileStr = new CString(file);

        var sourceLocation = new TracySourceLocationData
        {
            name = nameStr,
            function = functionStr,
            file = fileStr,
            line = (uint)line,
            color = color,
        };
        var context = TracyEmitZoneBegin(&sourceLocation, active ? 1 : 0);

        return new ZoneContext(context);
    }

    #endregion
}
