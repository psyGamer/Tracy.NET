using System.Diagnostics;
using System.Reflection;
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

    private MethodDefinition m_object_GetType = null!;
    private MethodReference m_Type_getFullName = null!;
    private MethodReference m_string_Concat3 = null!;

    private TypeReference t_Tracy = null!;
    private TypeReference t_TracyZoneContext = null!;
    private MethodReference m_Tracy_Zone = null!;
    private MethodReference m_TracyZoneContext_Dispose = null!;

    public override bool Execute()
    {
        Log.LogMessage($"Patching assembly '{AssemblyPath}'");

        ModuleDefinition? module = null;
        try
        {
            // Read the module
            var readerParams = new ReaderParameters(ReadingMode.Immediate) { ReadSymbols = false };
            module = ModuleDefinition.ReadModule(AssemblyPath, readerParams);

            // Apply patches
            PatchModule(module);

            // Can't overwrite the existing path, since Cecil doesn't like that
            string tmpPath = AssemblyPath + ".tmp";
            // Write the module
            module.Write(tmpPath, new WriterParameters { WriteSymbols = false });
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
        m_object_GetType = module.TypeSystem.Object.Resolve().Methods.First(method => method.Name == "GetType");
        m_Type_getFullName = module.ImportReference(typeof(Type).GetProperty("FullName")!.GetGetMethod());
        m_string_Concat3 = module.ImportReference(typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!);

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
                if (method.GetCustomAttribute("TracyNET.Tracy/ProfileMethod") is { } profileAttrib)
                {
                    Log.LogMessage($"Applying full-method profiling to {method.FullName}");

                    new ILContext(method).Invoke(il => ProfileMethod(il, profileAttrib));
                    method.CustomAttributes.Remove(profileAttrib);
                }
            }
        }
    }

    private void ProfileMethod(ILContext il, CustomAttribute profileAttrib)
    {
        // Parameters
        string? name = (string?)profileAttrib.ConstructorArguments[0].Value;
        uint color = (uint)profileAttrib.ConstructorArguments[1].Value;
        bool active = (bool)profileAttrib.ConstructorArguments[2].Value;
        string function = (string?)profileAttrib.ConstructorArguments[3].Value ?? il.Method.FullName;
        string file = (string?)profileAttrib.ConstructorArguments[4].Value ?? il.Method.DeclaringType.Name + ".cs";
        int line = (int)profileAttrib.ConstructorArguments[5].Value + 1; // The attribute is above the method

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
                if (il.Method.IsStatic || !il.Method.IsVirtual)
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

        // // Begin try-block
        exceptionHandler.TryStart = cur.Next;

        // Convert all "ret" into "leave" instructions
        var returnLabel = cur.DefineLabel();
        for (; cur.Index < il.Instrs.Count; cur.Index++)
        {
            if (cur.Next?.OpCode == OpCodes.Ret)
            {
                if (nonVoidReturnType)
                {
                    // Store return result
                    cur.EmitStloc(returnVar);
                }

                cur.Next.OpCode = OpCodes.Leave;
                cur.Next.Operand = returnLabel;
            }
        }

        // End try-block
        cur.Index = il.Instrs.Count - 1;
        if (nonVoidReturnType)
        {
            // Store return result
            cur.Next!.OpCode = OpCodes.Stloc;
            cur.Next!.Operand = returnVar;
        }
        else
        {
            // Avoid dealing with retargeting labels
            cur.Next!.OpCode = OpCodes.Nop;
        }

        cur.Index++;

        cur.EmitLeave(returnLabel);

        // End profiler zone
        cur.EmitLdloca(zoneVar);
        exceptionHandler.TryEnd = cur.Prev;
        exceptionHandler.HandlerStart = cur.Prev; // Begin finally-block
        cur.EmitCall(m_TracyZoneContext_Dispose);

        // End finally-block
        cur.EmitEndfinally();

        if (nonVoidReturnType)
        {
            // Retrieve return result
            cur.EmitLdloc(returnVar);
            exceptionHandler.HandlerEnd = cur.Prev;
            cur.EmitRet();
        }
        else
        {
            cur.EmitRet();
            exceptionHandler.HandlerEnd = cur.Prev;
        }

        returnLabel.Target = cur.Prev;
    }
}
