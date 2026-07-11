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
    public enum BlueprintDiagnosticKind
    {
        None,
        MalformedXml,
        MissingInterface,
        MissingRootElement,
        MissingRootIdentity
    }

    public record SignalModel(string SignalName, string Handler, string ObjectId, string ObjectClass);

    public record FileSignalsModel(
        string FilePath,
        string? RootId,
        string? FinalNamespace,
        ImmutableArray<SignalModel> Signals,
        string? ErrorMessage = null,
        BlueprintDiagnosticKind DiagnosticKind = BlueprintDiagnosticKind.None
    )
    {
        public virtual bool Equals(FileSignalsModel? other)
        {
            if (other is null) return false;
            return FilePath == other.FilePath &&
                   RootId == other.RootId &&
                   FinalNamespace == other.FinalNamespace &&
                   ErrorMessage == other.ErrorMessage &&
                   DiagnosticKind == other.DiagnosticKind &&
                   Signals.SequenceEqual(other.Signals);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FilePath.GetHashCode();
                hashCode = (hashCode * 397) ^ (RootId?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (FinalNamespace?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ErrorMessage?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (int)DiagnosticKind;
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
        private static readonly DiagnosticDescriptor MalformedXmlDiagnostic = new DiagnosticDescriptor(
            id: "BSG001",
            title: "Malformed Blueprint XML",
            messageFormat: "Blueprint file '{0}' is not well-formed XML: {1}",
            category: "BlueprintSignalGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingInterfaceDiagnostic = new DiagnosticDescriptor(
            id: "BSG002",
            title: "Blueprint interface is missing",
            messageFormat: "Blueprint file '{0}' must contain a top-level <interface> element",
            category: "BlueprintSignalGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingRootElementDiagnostic = new DiagnosticDescriptor(
            id: "BSG003",
            title: "Blueprint root view element is missing",
            messageFormat: "Blueprint file '{0}' must contain a root <object> or <template> element inside <interface>",
            category: "BlueprintSignalGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingRootIdentityDiagnostic = new DiagnosticDescriptor(
            id: "BSG004",
            title: "Blueprint root view identity is missing",
            messageFormat: "Blueprint file '{0}' has a root <{1}> without a non-blank '{2}' attribute; the root view ID/class is required",
            category: "BlueprintSignalGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all AdditionalFiles that end with .ui.
            var uiFiles = context.AdditionalTextsProvider
                .Where(file => file.Path.EndsWith(".ui", StringComparison.OrdinalIgnoreCase));

            // Get the namespace from MSBuild properties (RootNamespace).
            var rootNamespace = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespaceValue);
                    return string.IsNullOrWhiteSpace(rootNamespaceValue) ? "AppTemplate" : rootNamespaceValue;
                });

            // Get the IntermediateOutputPath used by the AdditionalFiles convention.
            var intermediateOutputPath = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var path);
                    return path ?? "";
                });

            var inputs = uiFiles
                .Combine(rootNamespace)
                .Combine(intermediateOutputPath);

            var models = inputs.Select((combined, ct) =>
            {
                var file = combined.Left.Left;
                var rootNs = combined.Left.Right;
                var intermediatePath = combined.Right;
                return ParseUiFile(file, rootNs!, intermediatePath, ct);
            });

            context.RegisterSourceOutput(models, (spc, model) =>
            {
                if (model.DiagnosticKind != BlueprintDiagnosticKind.None)
                {
                    var descriptor = GetDiagnosticDescriptor(model.DiagnosticKind);
                    if (model.DiagnosticKind == BlueprintDiagnosticKind.MalformedXml)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            descriptor,
                            Location.None,
                            model.FilePath,
                            model.ErrorMessage ?? "The document could not be parsed."));
                    }
                    else if (model.DiagnosticKind == BlueprintDiagnosticKind.MissingRootIdentity)
                    {
                        var rootElement = model.ErrorMessage?.StartsWith("template", StringComparison.OrdinalIgnoreCase) == true
                            ? "template"
                            : "object";
                        var identityAttribute = rootElement == "template" ? "class" : "id";
                        spc.ReportDiagnostic(Diagnostic.Create(
                            descriptor,
                            Location.None,
                            model.FilePath,
                            rootElement,
                            identityAttribute));
                    }
                    else
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, model.FilePath));
                    }

                    return;
                }

                // A valid root view with no signals does not need generated source.
                if (model.RootId == null || model.FinalNamespace == null || model.Signals.IsEmpty)
                    return;

                GenerateSource(spc, model);
            });
        }

        private static DiagnosticDescriptor GetDiagnosticDescriptor(BlueprintDiagnosticKind kind)
        {
            switch (kind)
            {
                case BlueprintDiagnosticKind.MalformedXml:
                    return MalformedXmlDiagnostic;
                case BlueprintDiagnosticKind.MissingInterface:
                    return MissingInterfaceDiagnostic;
                case BlueprintDiagnosticKind.MissingRootElement:
                    return MissingRootElementDiagnostic;
                case BlueprintDiagnosticKind.MissingRootIdentity:
                    return MissingRootIdentityDiagnostic;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private FileSignalsModel ParseUiFile(AdditionalText file, string rootNamespace, string intermediateOutputPath, CancellationToken ct)
        {
            var content = file.GetText(ct)?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return InvalidModel(
                    file.Path,
                    BlueprintDiagnosticKind.MalformedXml,
                    "The file is empty.");
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                return InvalidModel(file.Path, BlueprintDiagnosticKind.MalformedXml, ex.Message);
            }

            var interfaceNode = doc.Root != null && NameIs(doc.Root, "interface")
                ? doc.Root
                : null;
            if (interfaceNode == null)
            {
                return InvalidModel(file.Path, BlueprintDiagnosticKind.MissingInterface, null);
            }

            // GTK Builder permits either an object root or a template root.
            var rootElement = interfaceNode.Elements()
                .FirstOrDefault(element => NameIs(element, "object") || NameIs(element, "template"));
            if (rootElement == null)
            {
                return InvalidModel(file.Path, BlueprintDiagnosticKind.MissingRootElement, null);
            }

            var rootIsTemplate = NameIs(rootElement, "template");
            var rootIdentityAttribute = rootIsTemplate ? "class" : "id";
            var rootId = rootElement.Attribute(rootIdentityAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(rootId))
            {
                // The diagnostic payload identifies which root form and identity attribute failed.
                return InvalidModel(
                    file.Path,
                    BlueprintDiagnosticKind.MissingRootIdentity,
                    rootIsTemplate ? "template" : "object");
            }

            // Extract signals from the complete root view subtree, including nested objects.
            var signals = rootElement
                .Descendants()
                .Where(element => NameIs(element, "signal"))
                .Select(signal =>
                {
                    var parentObject = signal.Ancestors()
                        .FirstOrDefault(element => NameIs(element, "object") || NameIs(element, "template"));
                    if (parentObject == null)
                    {
                        return null;
                    }

                    var parentIsTemplate = NameIs(parentObject, "template");
                    var objectId = parentObject.Attribute(parentIsTemplate ? "class" : "id")?.Value;
                    var objectClass = parentObject.Attribute(parentIsTemplate ? "parent" : "class")?.Value;
                    return new SignalModel(
                        signal.Attribute("name")?.Value ?? "",
                        signal.Attribute("handler")?.Value ?? "",
                        objectId ?? "",
                        objectClass ?? "");
                })
                .Where(signal => signal != null &&
                                !string.IsNullOrWhiteSpace(signal.SignalName) &&
                                !string.IsNullOrWhiteSpace(signal.Handler) &&
                                !string.IsNullOrWhiteSpace(signal.ObjectId) &&
                                !string.IsNullOrWhiteSpace(signal.ObjectClass))
                .Select(signal => signal!)
                .ToImmutableArray();

            var finalNamespace = GetFinalNamespace(file.Path, rootNamespace, intermediateOutputPath);
            return new FileSignalsModel(file.Path, rootId, finalNamespace, signals);
        }

        private static FileSignalsModel InvalidModel(
            string filePath,
            BlueprintDiagnosticKind diagnosticKind,
            string? errorMessage)
        {
            return new FileSignalsModel(
                filePath,
                null,
                null,
                ImmutableArray<SignalModel>.Empty,
                errorMessage,
                diagnosticKind);
        }

        private static bool NameIs(XElement element, string localName)
        {
            return string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
        }

        private static string GetFinalNamespace(
            string filePath,
            string rootNamespace,
            string intermediateOutputPath)
        {
            var normalizedFilePath = filePath.Replace('\\', '/');
            var normalizedIntermediate = (intermediateOutputPath ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');
            var relativePath = string.Empty;

            if (!string.IsNullOrEmpty(normalizedIntermediate))
            {
                var marker = normalizedIntermediate + "/";
                var index = normalizedFilePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    relativePath = normalizedFilePath.Substring(index + marker.Length);
                }
                else
                {
                    // IntermediateOutputPath is commonly project-relative while AdditionalText.Path is absolute.
                    marker = "/" + marker;
                    index = normalizedFilePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        relativePath = normalizedFilePath.Substring(index + marker.Length);
                    }
                }
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                relativePath = System.IO.Path.GetFileName(filePath);
            }

            var relativeDir = System.IO.Path.GetDirectoryName(relativePath) ?? string.Empty;
            var normalizedDir = relativeDir.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalizedDir))
            {
                return rootNamespace;
            }

            var folders = normalizedDir
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return rootNamespace + "." + string.Join(".", folders);
        }

        private static void GenerateSource(SourceProductionContext context, FileSignalsModel model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using Gtk;");
            sb.AppendLine();
            sb.AppendLine($"namespace {model.FinalNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {model.RootId}");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Wires Blueprint root/view signals to statically typed GirCore handlers.");
            sb.AppendLine("        /// </summary>");
            AppendSignalMethod(sb, "ConfigureSignals", model.Signals, "+=");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Removes the exact subscriptions made by ConfigureSignals.");
            sb.AppendLine("        /// </summary>");
            AppendSignalMethod(sb, "DisposeSignals", model.Signals, "-=");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var safeNamespace = SanitizeIdentifier(model.FinalNamespace!);
            var safeRootId = SanitizeIdentifier(model.RootId!);
            var hintSeed = model.FinalNamespace + "|" + model.RootId;
            var sourceHint = $"{safeNamespace}_{safeRootId}_Signals_{StableHash(hintSeed)}.g.cs";
            context.AddSource(sourceHint, SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static void AppendSignalMethod(
            StringBuilder sb,
            string methodName,
            ImmutableArray<SignalModel> signals,
            string subscriptionOperator)
        {
            sb.AppendLine($"        protected override void {methodName}(Gtk.Builder builder)");
            sb.AppendLine("        {");

            // One typed local per builder object avoids duplicate local declarations when
            // several signals are attached to the same widget.
            var objectGroups = signals
                .GroupBy(signal => signal.ObjectId, StringComparer.Ordinal)
                .ToList();
            for (var objectIndex = 0; objectIndex < objectGroups.Count; objectIndex++)
            {
                var objectGroup = objectGroups[objectIndex];
                var firstSignal = objectGroup.First();
                var variableName = "__blueprintObject" + objectIndex;
                var objectType = MapToCSharpType(firstSignal.ObjectClass);
                sb.AppendLine($"            var {variableName} = builder.GetObject({QuoteCSharpString(firstSignal.ObjectId)}) as {objectType};");
                sb.AppendLine($"            if ({variableName} != null)");
                sb.AppendLine("            {");
                foreach (var signal in objectGroup)
                {
                    var eventName = $"On{ToPascalCase(signal.SignalName)}";
                    sb.AppendLine($"                {variableName}.{eventName} {subscriptionOperator} {signal.Handler};");
                }
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
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

            return gtkClassName;
        }

        private static string QuoteCSharpString(string value)
        {
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "\"";
        }

        private static string SanitizeIdentifier(string value)
        {
            var result = new StringBuilder();
            foreach (var character in value)
            {
                result.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }

            if (result.Length == 0)
            {
                result.Append('_');
            }
            else if (char.IsDigit(result[0]))
            {
                result.Insert(0, '_');
            }

            return result.ToString();
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in value)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return hash.ToString("X8");
            }
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}
