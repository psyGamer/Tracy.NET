using System;
using System.Runtime.CompilerServices;
using static Tracy.Native;

namespace Tracy;

public unsafe class Tracy
{
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
