using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace XSTH.Blueprint.Generators
{
    public record SignalModel(string SignalName, string Handler, string ObjectId, string ObjectClass);

    public record FileSignalsModel(
        string FilePath,
        string? WindowId,
        string? FinalNamespace,
        ImmutableArray<SignalModel> Signals,
        string? ErrorMessage = null
    )
    {
        public virtual bool Equals(FileSignalsModel? other)
        {
            if (other is null) return false;
            return FilePath == other.FilePath &&
                   WindowId == other.WindowId &&
                   FinalNamespace == other.FinalNamespace &&
                   ErrorMessage == other.ErrorMessage &&
                   Signals.SequenceEqual(other.Signals);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FilePath.GetHashCode();
                hashCode = (hashCode * 397) ^ (WindowId?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (FinalNamespace?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ErrorMessage?.GetHashCode() ?? 0);
                foreach (var signal in Signals)
                {
                    hashCode = (hashCode * 397) ^ signal.GetHashCode();
                }
                return hashCode;
            }
        }
    }

    [Generator]
    public class BlueprintSignalGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor GeneratorError = new DiagnosticDescriptor(
            id: "BSG001",
            title: "Blueprint Signal Generator Error",
            messageFormat: "Blueprint Signal Generator failed to generate code for file '{0}': {1}",
            category: "BlueprintSignalGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all AdditionalFiles that end with .ui
            var uiFiles = context.AdditionalTextsProvider
                .Where(file => file.Path.EndsWith(".ui", StringComparison.OrdinalIgnoreCase));

            // Get the namespace from MSBuild properties (RootNamespace)
            var rootNamespace = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespaceValue);
                    // Try to infer from path if not found, but we require a matching convention
                    return string.IsNullOrWhiteSpace(rootNamespaceValue) ? "AppTemplate" : rootNamespaceValue;
                });

            // Get the IntermediateOutputPath from MSBuild properties
            var intermediateOutputPath = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var path);
                    return path ?? "";
                });

            // Combine UI files with root namespace and intermediate output path
            var inputs = uiFiles
                .Combine(rootNamespace)
                .Combine(intermediateOutputPath);

            // Parse UI files into models
            var models = inputs.Select((combined, ct) =>
            {
                var file = combined.Left.Left;
                var rootNs = combined.Left.Right;
                var intermediatePath = combined.Right;
                return ParseUiFile(file, rootNs!, intermediatePath, ct);
            });

            // Generate source code from models
            context.RegisterSourceOutput(models, (spc, model) =>
            {
                if (model.ErrorMessage != null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(GeneratorError, Location.None, model.FilePath, model.ErrorMessage));
                    return;
                }

                if (model.WindowId == null || model.FinalNamespace == null || model.Signals.IsEmpty)
                    return;

                GenerateSource(spc, model);
            });
        }

        private FileSignalsModel ParseUiFile(AdditionalText file, string rootNamespace, string intermediateOutputPath, CancellationToken ct)
        {
            var content = file.GetText(ct)?.ToString();
            if (string.IsNullOrWhiteSpace(content)) 
                return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty);

            try
            {
                var doc = XDocument.Parse(content);
                var interfaceNode = doc.Element("interface");
                if (interfaceNode == null) 
                    return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty);

                // In GTK builder XML, the root object is typically our window/widget class
                var rootObject = interfaceNode.Element("object") ?? interfaceNode.Element("template");
                if (rootObject == null) 
                    return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty);

                var windowId = rootObject.Attribute(rootObject.Name == "template" ? "class" : "id")?.Value;
                if (string.IsNullOrWhiteSpace(windowId)) 
                    return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty);

                // Extract all <signal> elements anywhere in the tree under this object
                var signals = rootObject.Descendants("signal")
                    .Select(s => {
                        var parentObj = s.Ancestors().First(a => a.Name == "object" || a.Name == "template");
                        var isTemplate = parentObj.Name == "template";
                        return new SignalModel(
                            s.Attribute("name")?.Value ?? "",
                            s.Attribute("handler")?.Value ?? "",
                            parentObj.Attribute(isTemplate ? "class" : "id")?.Value ?? "",
                            parentObj.Attribute(isTemplate ? "parent" : "class")?.Value ?? ""
                        );
                    })
                    .Where(s => !string.IsNullOrEmpty(s.SignalName) && !string.IsNullOrEmpty(s.Handler) && !string.IsNullOrEmpty(s.ObjectId) && !string.IsNullOrEmpty(s.ObjectClass))
                    .ToImmutableArray();

                if (signals.IsEmpty) 
                    return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty);

                // Dynamically evaluate sub-namespace based on MSBuild metadata passed directory
                var relativeDir = "";
                if (!string.IsNullOrEmpty(intermediateOutputPath))
                {
                    var searchPath = intermediateOutputPath.Replace('\\', '/').TrimEnd('/') + "/";
                    var filePath = file.Path.Replace('\\', '/');
                    
                    var idx = filePath.IndexOf(searchPath, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var relativePath = filePath.Substring(idx + searchPath.Length);
                        relativeDir = System.IO.Path.GetDirectoryName(relativePath) ?? "";
                    }
                }

                var subNamespace = "";
                if (!string.IsNullOrEmpty(relativeDir))
                {
                    var normalizedDir = relativeDir.Replace('\\', '/').Trim('/');
                    if (!string.IsNullOrEmpty(normalizedDir))
                    {
                        subNamespace = "." + string.Join(".", normalizedDir.Split('/'));
                    }
                }
                var finalNamespace = $"{rootNamespace}{subNamespace}";

                return new FileSignalsModel(file.Path, windowId, finalNamespace, signals);
            }
            catch (Exception ex)
            {
                return new FileSignalsModel(file.Path, null, null, ImmutableArray<SignalModel>.Empty, ex.Message);
            }
        }

        private void GenerateSource(SourceProductionContext context, FileSignalsModel model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using Gtk;");
            sb.AppendLine();
            sb.AppendLine($"namespace {model.FinalNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {model.WindowId}");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Automatically wires up GTK signals defined in the Blueprint/UI file to the C# handlers.");
            sb.AppendLine("        /// Call this method after retrieving your widgets from the Gtk.Builder.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        protected override void ConfigureSignals(Gtk.Builder builder)");
            sb.AppendLine("        {");

            foreach (var sig in model.Signals)
            {
                // Convert GTK signal name to C# Gir.Core Event Name (e.g. "clicked" -> "OnClicked")
                // Gir.Core maps signal "clicked" to event "OnClicked"
                var eventName = $"On{ToPascalCase(sig.SignalName)}";
                var typeName = MapToCSharpType(sig.ObjectClass);

                // We assume builder.GetObject works because the user already loaded the UI
                sb.AppendLine($"            var {sig.ObjectId}_obj = builder.GetObject(\"{sig.ObjectId}\") as {typeName};");
                sb.AppendLine($"            if ({sig.ObjectId}_obj != null)");
                sb.AppendLine("            {");
                sb.AppendLine($"                {sig.ObjectId}_obj.{eventName} += {sig.Handler};");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource($"{model.WindowId}.Signals.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var parts = input.Split(new[] { '-', '_', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();
            foreach (var part in parts)
            {
                result.Append(char.ToUpper(part[0]));
                if (part.Length > 1)
                {
                    result.Append(part.Substring(1).ToLower());
                }
            }
            return result.ToString();
        }

        private static string MapToCSharpType(string gtkClassName)
        {
            if (string.IsNullOrEmpty(gtkClassName)) return "Gtk.Widget";

            if (gtkClassName.StartsWith("Gtk", StringComparison.Ordinal)) return "Gtk." + gtkClassName.Substring(3);
            if (gtkClassName.StartsWith("Adw", StringComparison.Ordinal)) return "Adw." + gtkClassName.Substring(3);
            if (gtkClassName.StartsWith("Gio", StringComparison.Ordinal)) return "Gio." + gtkClassName.Substring(3);
            if (gtkClassName.StartsWith("Gdk", StringComparison.Ordinal)) return "Gdk." + gtkClassName.Substring(3);
            if (gtkClassName.StartsWith("GObject", StringComparison.Ordinal)) return "GObject." + gtkClassName.Substring(7);
            if (gtkClassName.StartsWith("GLib", StringComparison.Ordinal)) return "GLib." + gtkClassName.Substring(4);
            if (gtkClassName.StartsWith("Gsk", StringComparison.Ordinal)) return "Gsk." + gtkClassName.Substring(3);
            if (gtkClassName.StartsWith("Pango", StringComparison.Ordinal)) return "Pango." + gtkClassName.Substring(5);

            return gtkClassName; // Fallback for custom components
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}
