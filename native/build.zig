const std = @import("std");

/// On-demand profiling
const tracy_on_demand: bool = true;
/// Enforce callstack collection for tracy regions
const tracy_callstack: ?u8 = null;
/// Disable all callstack related functionality
const tracy_no_callstack: bool = false;
/// Disables the inline functions in callstacks
const tracy_no_callstack_inlines: bool = false;
/// Only listen on the localhost interface
const tracy_only_localhost = false;
/// Disable client discovery by broadcast to local network
const tracy_no_broadcast = false;
/// Tracy will only accept connections on IPv4 addresses (disable IPv6)
const tracy_only_ipv4 = false;
/// Disable collection of source code
const tracy_no_code_transfer = false;
/// Disable capture of context switches
const tracy_no_context_switch = false;
/// Client executable does not exit until all profile data is sent to server
const tracy_no_exit = false;
/// Disable call stack sampling
const tracy_no_sampling = false;
/// Disable zone validation for C API
const tracy_no_verify = false;
/// Disable capture of hardware Vsync events
const tracy_no_vsync_capture = false;
/// Disable the frame image support and its thread
const tracy_no_frame_image = false;
/// Disable systrace sampling
const tracy_no_system_tracing = true;
/// Enable delayed initialization of the library (init on first call)
const tracy_delayed_init = false;
/// Enable the manual lifetime management of the profile
const tracy_manual_lifetime = false;
/// Enable fibers support
const tracy_fibers = false;
/// Disable crash handling
const tracy_no_crash_handler = false;
/// Use lower resolution timers
const tracy_timer_fallback = false;

// Based on https://github.com/cipharius/zig-tracy/blob/master/build.zig
pub fn build(b: *std.Build) !void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const all_step = b.step("all", "Compiles Tracy for all targets");

    b.getInstallStep().dependOn(&installTracy(b, target, optimize).step);

    // Install all targets
    const targets: [4]std.Build.ResolvedTarget = .{
        b.resolveTargetQuery(try std.Target.Query.parse(.{ .arch_os_abi = "x86_64-linux-gnu" })),
        b.resolveTargetQuery(try std.Target.Query.parse(.{ .arch_os_abi = "x86_64-macos" })),
        b.resolveTargetQuery(try std.Target.Query.parse(.{ .arch_os_abi = "x86_64-windows-gnu" })),
        b.resolveTargetQuery(try std.Target.Query.parse(.{ .arch_os_abi = "x86-windows-gnu" })),
    };

    for (targets) |release_target| {
        all_step.dependOn(&installTracy(b, release_target, optimize).step);
    }
}

fn installTracy(b: *std.Build, target: std.Build.ResolvedTarget, optimize: std.builtin.OptimizeMode) *std.Build.Step.WriteFile {
    const tracy_dep = b.dependency("tracy", .{});

    // Compile client library
    const tracy_client = b.addSharedLibrary(.{
        .name = "tracy",
        .target = target,
        .optimize = optimize,
    });
    tracy_client.linkLibCpp();
    tracy_client.addCSourceFile(.{
        .file = tracy_dep.path("public/TracyClient.cpp"),
        .flags = &.{},
    });
    tracy_client.addCSourceFile(.{
        .file = b.path("wrapper.cpp"),
        .flags = &.{},
    });
    tracy_client.defineCMacro("TRACY_ENABLE", null);

    // See https://github.com/wolfpld/tracy/blob/v0.11.1/public/TracyClient.cpp#L54-L57
    if (target.result.os.tag == .windows) {
        tracy_client.linkSystemLibrary("ws2_32");
        tracy_client.linkSystemLibrary("dbghelp");
        tracy_client.linkSystemLibrary("advapi32");
        tracy_client.linkSystemLibrary("user32");
    }

    if (tracy_on_demand)
        tracy_client.defineCMacro("TRACY_ON_DEMAND", null);
    if (tracy_callstack) |depth| {
        tracy_client.defineCMacro("TRACY_CALLSTACK", "\"" ++ digits2(depth) ++ "\"");
    }
    if (tracy_no_callstack) {
        tracy_client.defineCMacro("TRACY_NO_CALLSTACK", null);
    }
    if (tracy_no_callstack_inlines) {
        tracy_client.defineCMacro("TRACY_NO_CALLSTACK_INLINES", null);
    }
    if (tracy_only_localhost) {
        tracy_client.defineCMacro("TRACY_ONLY_LOCALHOST", null);
    }
    if (tracy_no_broadcast) {
        tracy_client.defineCMacro("TRACY_NO_BROADCAST", null);
    }
    if (tracy_only_ipv4) {
        tracy_client.defineCMacro("TRACY_ONLY_IPV4", null);
    }
    if (tracy_no_code_transfer) {
        tracy_client.defineCMacro("TRACY_NO_CODE_TRANSFER", null);
    }
    if (tracy_no_context_switch) {
        tracy_client.defineCMacro("TRACY_NO_CONTEXT_SWITCH", null);
    }
    if (tracy_no_exit) {
        tracy_client.defineCMacro("TRACY_NO_EXIT", null);
    }
    if (tracy_no_sampling) {
        tracy_client.defineCMacro("TRACY_NO_SAMPLING", null);
    }
    if (tracy_no_verify) {
        tracy_client.defineCMacro("TRACY_NO_VERIFY", null);
    }
    if (tracy_no_vsync_capture) {
        tracy_client.defineCMacro("TRACY_NO_VSYNC_CAPTURE", null);
    }
    if (tracy_no_frame_image) {
        tracy_client.defineCMacro("TRACY_NO_FRAME_IMAGE", null);
    }
    if (tracy_no_system_tracing) {
        tracy_client.defineCMacro("TRACY_NO_SYSTEM_TRACING", null);
    }
    if (tracy_delayed_init) {
        tracy_client.defineCMacro("TRACY_DELAYED_INIT", null);
    }
    if (tracy_manual_lifetime) {
        tracy_client.defineCMacro("TRACY_MANUAL_LIFETIME", null);
    }
    if (tracy_fibers) {
        tracy_client.defineCMacro("TRACY_FIBERS", null);
    }
    if (tracy_no_crash_handler) {
        tracy_client.defineCMacro("TRACY_NO_CRASH_HANDLER", null);
    }
    if (tracy_timer_fallback) {
        tracy_client.defineCMacro("TRACY_TIMER_FALLBACK", null);
    }

    // Copy into runtimes directory for NuGet
    var wf = b.addWriteFiles();

    if (target.result.os.tag == .linux) {
        if (target.result.cpu.arch == .x86_64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/linux-x64/libTracyClient.so");
        } else if (target.result.cpu.arch == .aarch64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/linux-arm64/libTracyClient.so");
        }
    } else if (target.result.os.tag == .macos) {
        if (target.result.cpu.arch == .x86_64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/osx-x64/libTracyClient.dylib");
        } else if (target.result.cpu.arch == .aarch64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/osx-arm64/libTracyClient.dylib");
        }
    } else if (target.result.os.tag == .windows) {
        if (target.result.cpu.arch == .x86) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/win-x86/TracyClient.dll");
        } else if (target.result.cpu.arch == .x86_64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/win-x64/TracyClient.dll");
        } else if (target.result.cpu.arch == .aarch64) {
            wf.addCopyFileToSource(tracy_client.getEmittedBin(), "runtimes/win-arm64/TracyClient.dll");
        }
    }

    return wf;
}

fn digits2(value: usize) [2]u8 {
    return ("0001020304050607080910111213141516171819" ++
        "2021222324252627282930313233343536373839" ++
        "4041424344454647484950515253545556575859" ++
        "6061626364656667686970717273747576777879" ++
        "8081828384858687888990919293949596979899")[value * 2 ..][0..2].*;
}
