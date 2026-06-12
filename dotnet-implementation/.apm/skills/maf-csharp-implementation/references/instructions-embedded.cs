// references/instructions-embedded.cs
//
// Pattern for loading agent instructions from embedded markdown resources.
//
// In the .csproj that owns the .md files, mark them as embedded.
// The glob covers the entire Agents/ subtree so every slice's instructions
// are picked up automatically when a new agent folder is added:
//
//   <ItemGroup>
//     <EmbeddedResource Include="Agents\**\*.md" />
//   </ItemGroup>
//
// The manifest resource name is built from the project's root namespace plus
// the folder path (dots replace backslashes). For a file at:
//
//   Agents/WeatherAgent/Instructions/WeatherAgent.md
//
// the manifest name becomes:
//
//   <RootNamespace>.Agents.WeatherAgent.Instructions.WeatherAgent.md
//
// Load by passing the suffix that uniquely identifies the file.
// The marker type anchors the assembly lookup so callers don't need to
// know the full namespace.

using System.Reflection;

public static class InstructionsLoader
{
    /// <summary>
    /// Loads an embedded markdown file by manifest-name suffix.
    /// Example: LoadFromResource&lt;AssemblyMarker&gt;("Agents.WeatherAgent.Instructions.WeatherAgent.md")
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

// AssemblyMarker lives at the project root, next to ServiceCollectionExtensions.cs.
// One per project is enough — the suffix path distinguishes slices.
public sealed class AssemblyMarker { }

// Use site (inside a slice's DI extension):
// var instructions = InstructionsLoader.LoadFromResource<AssemblyMarker>(
//     "Agents.WeatherAgent.Instructions.WeatherAgent.md");
