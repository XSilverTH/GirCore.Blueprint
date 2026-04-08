using System.Reflection;
using Gio;

namespace XSTH.Blueprint.Helpers;

public static class GResourceHelper
{
    public static void RegisterAssemblyResources()
    {
        var assembly = Assembly.GetCallingAssembly();
        Console.WriteLine($"[DEBUG] Calling assembly: {assembly.FullName}");
        Console.WriteLine($"[DEBUG] Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        
        const string resourceName = "app.gresource";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            Console.WriteLine($"[WARNING] Embedded resource '{resourceName}' not found in assembly.");
            return;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        
        var bytes = ms.ToArray();
        
        var bytesRef = GLib.Bytes.New(bytes);
        var resource = Resource.NewFromData(bytesRef);
        
        Functions.ResourcesRegister(resource);
    }
}
