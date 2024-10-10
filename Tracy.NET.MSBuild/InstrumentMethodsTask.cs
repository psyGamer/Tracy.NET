using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace TracyNET.MSBuild;

public class InstrumentMethodsTask : Task
{
    [Required]
    public string AssemblyPath { get; set; } = null!;

    public override bool Execute()
    {
        Log.LogWarning($"Got assembly path: {AssemblyPath}");
        return true;
    }
}
