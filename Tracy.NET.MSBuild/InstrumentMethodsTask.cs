using System.Reflection;
using Microsoft.Build.Framework;
using Mono.Cecil;
using MonoMod.Cil;
using Task = Microsoft.Build.Utilities.Task;

namespace TracyNET.MSBuild;

public class InstrumentMethodsTask : Task
{
    [Required]
    public string AssemblyPath { get; set; } = null!;

    public override bool Execute()
    {
        Log.LogWarning($"Got assembly path: {AssemblyPath}");

        // Read assembly
        var asm = AssemblyDefinition.ReadAssembly(AssemblyPath);
        foreach (var type in asm.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                PatchMethod(method);
            }
        }

        // Write assembly
        asm.Write(AssemblyPath);

        return true;
    }

    private void PatchMethod(MethodDefinition method)
    {
        var ctx = new ILContext(method);
        var cur = new ILCursor(ctx);

        while (cur.TryGotoNext(instr => instr.MatchLdstr(out _)))
        {
            cur.Next!.Operand = $"Hello World from {method.Name}";
        }
    }
}
