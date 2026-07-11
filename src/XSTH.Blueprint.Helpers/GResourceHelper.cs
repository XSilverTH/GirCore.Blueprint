using System.Reflection;
using Gio;

namespace XSTH.Blueprint.Helpers;

/// <summary>Registers the <c>app.gresource</c> embedded by the package MSBuild targets.</summary>
public static class GResourceHelper
{
    /// <summary>Registers the compiled Blueprint resource bundle embedded in <paramref name="assembly"/>.</summary>
    public static void RegisterAssemblyResources(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        const string resourceName = "app.gresource";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Assembly '{assembly.FullName}' does not contain '{resourceName}'. " +
                "Ensure the project references XSTH.Blueprint.Helpers and contains at least one .blp file.");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        var bytes = GLib.Bytes.New(memory.ToArray());
        var resource = Resource.NewFromData(bytes);
        Functions.ResourcesRegister(resource);
    }
}
