﻿using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Build.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using Task = Microsoft.Build.Utilities.Task;

namespace TracyNET.MSBuild;

public class InstrumentMethodsTask : Task
{
    [Required]
    public string AssemblyPath { get; set; } = null!;

    [Required]
    public ITaskItem[] PackageReference { get; set; } = null!;

    [Required]
    public ITaskItem[] ReferencePath { get; set; } = null!;

    // Config
    private bool Enabled = true;
    private bool ProfileAllMethods = false;

    private MethodReference m_object_GetType = null!;
    private MethodReference m_Type_getFullName = null!;
    private MethodReference m_string_Concat3 = null!;

    private TypeReference t_Tracy = null!;
    private TypeReference t_TracyZoneContext = null!;
    private MethodReference m_Tracy_Zone = null!;
    private MethodReference m_TracyZoneContext_Dispose = null!;

    public override bool Execute()
    {
        // Detect config
        var tracyPackage = PackageReference.First(item => item.TryGetMetadata("Identity", out var identity) && identity == "Tracy.NET");

        if (tracyPackage.TryGetMetadata(nameof(Enabled), out string? valueStr) && bool.TryParse(valueStr, out bool value))
            Enabled = value;
        if (tracyPackage.TryGetMetadata(nameof(ProfileAllMethods), out valueStr) && bool.TryParse(valueStr, out value))
            ProfileAllMethods = value;

        Log.LogMessage($"Patching assembly '{AssemblyPath}'");

        ModuleDefinition? module = null;
        try
        {
            // Read the module
            var helper = new MSBuildHelper(ReferencePath, Log);
            var readerParams = helper.WithProviders(new ReaderParameters(ReadingMode.Immediate) { ReadSymbols = true });
            try
            {
                module = ModuleDefinition.ReadModule(AssemblyPath, readerParams);
            }
            catch (SymbolsNotFoundException)
            {
                readerParams.ReadSymbols = false;
                module = ModuleDefinition.ReadModule(AssemblyPath, readerParams);
            }
            catch (SymbolsNotMatchingException)
            {
                readerParams.ReadSymbols = false;
                module = ModuleDefinition.ReadModule(AssemblyPath, readerParams);
            }

            // Apply patches
            PatchModule(module);

            // Can't overwrite the existing path, since Cecil doesn't like that
            string tmpPath = AssemblyPath + ".tmp";
            // Write the module
            module.Write(tmpPath, new WriterParameters { WriteSymbols = readerParams.ReadSymbols });
            module.Dispose();

            File.Delete(AssemblyPath);
            File.Move(tmpPath, AssemblyPath);

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true, showDetail: true, file: null);
            module?.Dispose();

            return false;
        }
    }

    private void PatchModule(ModuleDefinition module)
    {
        // Setup important types / methods
        m_object_GetType = module.ImportReference(module.TypeSystem.Object.Resolve().Methods.First(method =>
            method.Name == nameof(GetType)));
        m_Type_getFullName = module.ImportReference(typeof(Type).GetProperty("FullName")!.GetGetMethod());
        m_string_Concat3 = module.ImportReference(module.TypeSystem.String.Resolve().Methods.First(method =>
            method.Name == nameof(string.Concat) &&
            method.Parameters.Count == 3 && method.Parameters.All(param => param.ParameterType.FullName == "System.String")));

        // We are in "build/Tracy.NET.MSBuild.dll" and we want "lib/net7.0/Tracy.NET.dll"
        string tracyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "..", "lib", "net7.0", "Tracy.NET.dll");
        using var tracyModule = ModuleDefinition.ReadModule(tracyPath);
        t_Tracy = tracyModule.GetType("TracyNET.Tracy");
        t_TracyZoneContext = tracyModule.GetType("TracyNET.Tracy/ZoneContext");
        m_Tracy_Zone = module.ImportReference(t_Tracy.Resolve().Methods.First(method => method.Name == "Zone"));
        m_TracyZoneContext_Dispose = module.ImportReference(t_TracyZoneContext.Resolve().Methods.First(method => method.Name == "Dispose"));

        // Import types (needs to be done after being resolved for methods)
        t_Tracy = module.ImportReference(t_Tracy);
        t_TracyZoneContext = module.ImportReference(t_TracyZoneContext);

        // Apply full-method profiling to methods with [Tracy.ProfileMethod]
        foreach (var type in module.Types)
        {
            foreach (var method in type.Methods)
            {
                try
                {
                    // Skip non-patchable methods
                    if (method.IsAbstract)
                    {
                        continue;
                    }

                    // Full-method profiling
                    if (method.GetCustomAttribute("TracyNET.Tracy/ProfileMethod") is { } profileAttrib)
                    {
                        if (Enabled)
                        {
                            string? name = (string?)profileAttrib.ConstructorArguments[0].Value;
                            uint color = (uint)profileAttrib.ConstructorArguments[1].Value;
                            bool active = (bool)profileAttrib.ConstructorArguments[2].Value;
                            string file = (string?)profileAttrib.ConstructorArguments[3].Value ?? method.DeclaringType.Name + ".cs";
                            int line = (int)profileAttrib.ConstructorArguments[4].Value + 1; // The attribute is above the method

                            new ILContext(method).Invoke(il => ProfileMethod(il, name, color, active, function: $"{method.DeclaringType.FullName}::{method.Name}", file, line));
                        }
                        method.CustomAttributes.Remove(profileAttrib);
                    }
                    else if (Enabled && ProfileAllMethods)
                    {
                        string file;
                        int line;
                        if (method.DebugInformation.HasSequencePoints)
                        {
                            file = method.DebugInformation.SequencePoints[0].Document.Url;
                            line = method.DebugInformation.SequencePoints[0].StartLine;
                        }
                        else
                        {
                            file = method.DeclaringType.Name + ".cs";
                            line = 0;
                        }

                        new ILContext(method).Invoke(il => ProfileMethod(il, name: null, color: 0x000000, active: true, function: $"{method.DeclaringType.FullName}::{method.Name}", file, line));
                    }

                    // Zone removal
                    if (!Enabled)
                    {
                        new ILContext(method).Invoke(RemoveZones);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed patching method {method.FullName}\n{ex}");
                }
            }
        }
    }

    // Puts a "using var zone = Tracy.Zone(...)" at the top of the method
    private void ProfileMethod(ILContext il, string? name, uint color, bool active, string function, string file, int line)
    {
        Log.LogMessage($"Applying full-method profiling to! {il.Method.FullName}");

        var cur = new ILCursor(il);

        // Create a try-finally block to properly dispose the zone
        var exceptionHandler = new ExceptionHandler(ExceptionHandlerType.Finally);
        il.Body.ExceptionHandlers.Add(exceptionHandler);

        // Store zone in a local variable
        var zoneVar = new VariableDefinition(t_TracyZoneContext);
        il.Body.Variables.Add(zoneVar);

        // Store return value in a local variable (if needed)
        var returnVar = new VariableDefinition(il.Method.ReturnType);
        bool nonVoidReturnType = il.Method.ReturnType.FullName != "System.Void";

        if (nonVoidReturnType)
        {
            il.Body.Variables.Add(returnVar);
        }

        // Begin profiler zone
        {
            // Setup zone name
            if (name != null)
            {
                cur.EmitLdstr(name);
            }
            else
            {
                // Structs may still contain virtual methods, so avoid calling .GetType() on those
                if (il.Method.IsStatic || !il.Method.IsVirtual || il.Method.DeclaringType.IsValueType)
                {
                    cur.EmitLdnull();
                }
                else
                {
                    cur.EmitLdstr(function + " (");
                    cur.EmitLdarg0();
                    cur.EmitCallvirt(m_object_GetType);
                    cur.EmitCallvirt(m_Type_getFullName);
                    cur.EmitLdstr(")");
                    cur.EmitCall(m_string_Concat3);
                }
            }

            cur.EmitLdcI4(color);
            cur.EmitLdcI4(active ? 1 : 0);
            cur.EmitLdstr(function);
            cur.EmitLdstr(file);
            cur.EmitLdcI4(line);
            cur.EmitCall(m_Tracy_Zone);
            cur.EmitStloc(zoneVar);
        }

        // Begin try-block
        exceptionHandler.TryStart = cur.Next;

        // Convert all "ret" into "leave" instructions
        var returnLabel = cur.DefineLabel();
        for (cur.Index = 0; cur.Index < il.Instrs.Count; cur.Index++)
        {
            if (cur.Next?.OpCode == OpCodes.Ret)
            {
                if (nonVoidReturnType)
                {
                    // Store return result
                    cur.MoveAfterLabels();
                    cur.EmitStloc(returnVar);
                }

                cur.Next.OpCode = OpCodes.Leave;
                cur.Next.Operand = returnLabel;
            }
        }

        // End profiler zone
        cur.Index = il.Instrs.Count;
        cur.EmitLdloca(zoneVar);
        exceptionHandler.TryEnd = cur.Prev;
        exceptionHandler.HandlerStart = cur.Prev; // Begin finally-block
        cur.EmitCall(m_TracyZoneContext_Dispose);

        // These instructions are used for actual using-statements, however just a direct call should be more performant?
        //     cur.EmitConstrained(t_TracyZoneContext);
        //     cur.EmitCallvirt(il.Module.ImportReference(typeof(IDisposable).GetMethod("Dispose")!));

        // End finally-block
        cur.EmitEndfinally();

        if (nonVoidReturnType)
        {
            // Retrieve return result
            cur.EmitLdloc(returnVar);
            returnLabel.Target = exceptionHandler.HandlerEnd = cur.Prev;
            cur.EmitRet();
        }
        else
        {
            cur.EmitRet();
            returnLabel.Target = exceptionHandler.HandlerEnd = cur.Prev;
        }
    }

    private void RemoveZones(ILContext il)
    {
        Log.LogMessage($"Removing profiling zones from {il.Method.FullName}");

        var cur = new ILCursor(il);

        while (cur.TryGotoNext(MoveType.After, instr => instr.MatchCall("TracyNET.Tracy", "Zone")))
        {
            cur.Index++; // Exception handler starts after zone begin

            const int zoneBeginInstrCount = 6 + 1 + 1; // 6 parameters + 1 call + 1 store

            if (il.Body.ExceptionHandlers.FirstOrDefault(handler => handler.TryStart == cur.Next) is { HandlerType: ExceptionHandlerType.Finally } exceptionHandler)
            {
                // using / try-finally block

                // Remove zone begin
                cur.Index -= zoneBeginInstrCount;
                cur.RemoveRange(zoneBeginInstrCount);

                // Replace leave with br
                cur.Next = exceptionHandler.TryEnd;

                if (cur.Prev.OpCode == OpCodes.Leave)
                    cur.Prev.OpCode = OpCodes.Br;
                else if (cur.Prev.OpCode == OpCodes.Leave_S)
                    cur.Prev.OpCode = OpCodes.Br_S;

                // Remove finally-block
                cur.Next = exceptionHandler.HandlerStart;
                while (cur.Index < il.Instrs.Count && cur.Next != exceptionHandler.HandlerEnd) {
                    cur.Remove();
                }

                if (cur.Index < il.Instrs.Count)
                {
                    // Re-target incoming handlers
                    foreach (var handler in il.Body.ExceptionHandlers.Where(handler => handler.HandlerEnd == exceptionHandler.HandlerStart))
                        handler.HandlerEnd = cur.Next;
                }

                il.Body.ExceptionHandlers.Remove(exceptionHandler);
                cur.Index = 0;
            }
            else
            {
                // Manually disposed zone

                // Leave variable uninitialized
                cur.Index -= zoneBeginInstrCount;
                cur.RemoveRange(zoneBeginInstrCount);
            }
        }

        // Stub-out remaining calls to ZoneContext
        MethodReference? callTarget = null;
        for (cur.Index = 0; cur.TryGotoNext(instr => instr.MatchCall(out callTarget));)
        {
            if (callTarget == null || callTarget.DeclaringType.FullName != "TracyNET.Tracy/ZoneContext")
                continue;

            if (callTarget.Name == "get_Active")
            {
                // Remove load, otherwise pop
                if (cur.Prev.MatchLdloc(out _) || cur.Prev.MatchLdloca(out _) || cur.Prev.MatchLdarg(out _) || cur.Prev.MatchLdarga(out _))
                {
                    cur.Index--;
                    cur.Remove(); // Remove load
                    cur.Remove(); // Remove call
                }
                else
                {
                    il.Instrs[cur.Index] = cur.IL.Create(OpCodes.Pop); // Replace call
                }

                cur.EmitLdcI4(0 /*false*/);
            }
            else if (callTarget.Name is "set_Text" or "set_Value" or "set_Name" or "set_Color")
            {
                // Remove load, otherwise pop (parameter)
                if (cur.Prev.MatchLdcI4(out _) || cur.Prev.MatchLdstr(out _))
                {
                    cur.Index--;
                    cur.Remove();

                    // Remove load, otherwise pop (zone)
                    if (cur.Prev.MatchLdloc(out _) || cur.Prev.MatchLdloca(out _) || cur.Prev.MatchLdarg(out _) || cur.Prev.MatchLdarga(out _))
                    {
                        cur.Index--;
                        cur.Remove(); // Remove load
                        cur.Remove(); // Remove call
                    }
                    else
                    {
                        il.Instrs[cur.Index] = cur.IL.Create(OpCodes.Pop); // Replace call
                    }
                }
                else
                {
                    il.Instrs[cur.Index] = cur.IL.Create(OpCodes.Pop); // Replace call
                    cur.EmitPop(); // Could technically resolve dependency chain of parameter, but let's keep it simple
                }
            }
            else if (callTarget.Name == "Dispose")
            {
                // Remove load, otherwise pop
                if (cur.Prev.MatchLdloc(out _) || cur.Prev.MatchLdloca(out _) || cur.Prev.MatchLdarg(out _) || cur.Prev.MatchLdarga(out _))
                {
                    cur.Index--;
                    cur.Remove(); // Remove load
                    cur.Remove(); // Remove call
                }
                else
                {
                    il.Instrs[cur.Index] = cur.IL.Create(OpCodes.Pop); // Replace call
                }
            }
        }

        Console.WriteLine(il);

        // Cleanup
        OptimizeMethod(il);
    }

    /// Removes unused variables
    private void OptimizeMethod(ILContext il)
    {
        var cur = new ILCursor(il);

        // Remove unused variables / update references
        HashSet<int> usedVariables = [];

        for (cur.Index = 0; cur.Index < il.Instrs.Count; cur.Index++)
        {
            if (cur.Next!.MatchStloc(out int varIndex) ||
                cur.Next!.MatchLdloc(out varIndex) ||
                cur.Next!.MatchLdloca(out varIndex))
            {
                usedVariables.Add(varIndex);
            }
        }

        var variablesToRemove = Enumerable.Range(0, il.Body.Variables.Count).Except(usedVariables).ToArray();

        foreach (int index in variablesToRemove.OrderByDescending(idx => idx))
            il.Body.Variables.RemoveAt(index);

        int oldIndex = -1;

        for (cur.Index = 0; cur.TryGotoNext(instr => instr.MatchStloc(out oldIndex));)
        {
            int newIndex = oldIndex - variablesToRemove.Count(idx => oldIndex > idx);
            il.Instrs[cur.Index] = newIndex switch
            {
                0 => cur.IL.Create(OpCodes.Stloc_0),
                1 => cur.IL.Create(OpCodes.Stloc_1),
                2 => cur.IL.Create(OpCodes.Stloc_2),
                3 => cur.IL.Create(OpCodes.Stloc_3),
                <= byte.MaxValue => cur.IL.Create(OpCodes.Stloc_S, (byte)newIndex),
                _ => cur.IL.Create(OpCodes.Stloc, newIndex)
            };
        }

        for (cur.Index = 0; cur.TryGotoNext(instr => instr.MatchLdloc(out oldIndex));)
        {
            int newIndex = oldIndex - variablesToRemove.Count(idx => oldIndex > idx);
            il.Instrs[cur.Index] = newIndex switch
            {
                0 => cur.IL.Create(OpCodes.Ldloc_0),
                1 => cur.IL.Create(OpCodes.Ldloc_1),
                2 => cur.IL.Create(OpCodes.Ldloc_2),
                3 => cur.IL.Create(OpCodes.Ldloc_3),
                <= byte.MaxValue => cur.IL.Create(OpCodes.Ldloc_S, (byte)newIndex),
                _ => cur.IL.Create(OpCodes.Ldloc, newIndex)
            };
        }

        for (cur.Index = 0; cur.TryGotoNext(instr => instr.MatchLdloca(out oldIndex));)
        {
            int newIndex = oldIndex - variablesToRemove.Count(idx => oldIndex > idx);
            il.Instrs[cur.Index] = newIndex switch
            {
                <= byte.MaxValue => cur.IL.Create(OpCodes.Ldloca_S, (byte)newIndex),
                _ => cur.IL.Create(OpCodes.Ldloca, newIndex)
            };
        }
    }
}

// Taken from https://github.com/BepInEx/BepInEx.AssemblyPublicizer/blob/master/BepInEx.AssemblyPublicizer.MSBuild/Extensions.cs
internal static class Extensions {
    public static bool HasMetadata(this ITaskItem taskItem, string metadataName) {
        var metadataNames = (ICollection<string>)taskItem.MetadataNames;
        return metadataNames.Contains(metadataName);
    }

    public static bool TryGetMetadata(this ITaskItem taskItem, string metadataName, out string? metadata) {
        if (taskItem.HasMetadata(metadataName)) {
            metadata = taskItem.GetMetadata(metadataName);
            return true;
        }

        metadata = null;
        return false;
    }
}
