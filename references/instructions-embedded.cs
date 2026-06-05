// references/instructions-embedded.cs
//
// Pattern for loading agent instructions from embedded markdown resources.
//
// In the .csproj that owns the .md files, mark them as embedded:
//
//   <ItemGroup>
//     <EmbeddedResource Include="Instructions\**\*.md" />
//   </ItemGroup>
//
// Then reference by the resource name suffix. The marker type just anchors
// the assembly lookup so callers don't need to know the namespace.

using System.Reflection;

public static class InstructionsLoader
{
    /// <summary>
    /// Loads an embedded markdown file by manifest-name suffix.
    /// Example: LoadFromResource&lt;AssemblyMarker&gt;("Instructions.PlanningAgent.md")
    /// </summary>
    public static string LoadFromResource<TAssemblyMarker>(string resourceNameSuffix)
    {
        var asm = typeof(TAssemblyMarker).Assembly;
        var name = asm.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(resourceNameSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceNameSuffix}' not found in {asm.GetName().Name}. " +
                $"Did you forget <EmbeddedResource> in the csproj?");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

// Marker type lives next to your Instructions/ folder.
public sealed class AssemblyMarker { }

// Use site:
// var instructions = InstructionsLoader.LoadFromResource<AssemblyMarker>(
//     "Instructions.PlanningAgent.md");
// var options = new ChatClientAgentOptions { Instructions = instructions, ... };
