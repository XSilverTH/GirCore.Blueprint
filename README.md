# Gir.Core Blueprint Template

A set of .NET templates and build utilities for creating GTK4 / Libadwaita applications using Gir.Core and Blueprint UI files.

This project streamlines the development of Gir.Core applications by automating the compilation of Blueprint UI files, managing GResource bundling, and generating C# signal bindings at compile time.

## Features

* **Automated Blueprint Compilation**: MSBuild targets automatically discover and compile `.blp` files to GTK `.ui` files during the build process.
* **GResource Bundling and Embedding**: Compiled UI files are automatically bundled into an `app.gresource` binary and embedded into the .NET assembly. A runtime helper registers the resource stream with GIO automatically.
* **Compile-Time Signal Binding**: A Roslyn Source Generator parses the generated UI XML and automatically creates strongly-typed C# code to wire GTK signals to your event handlers. This entirely eliminates the need for manual event subscription boilerplate and completely avoids Reflection, making the codebase Native AOT and Trimmer safe.
* **Included Templates**:
  * `gircore-adw`: Full application template with boilerplate for Adw.Application.
  * `gircore-adw-window`: Item template for adding new GTK Windows to an active project.

## Installation

To install the templates locally, execute `test_and_install.sh`

## Usage

### Creating a New Project

To create a new application, use the `gircore-adw` template:

```bash
dotnet new gircore-adw -n MyGtkApp
cd MyGtkApp
dotnet run
```

### Adding a New Window

To add a new Window (including its backing C# class and `.blp` UI file) to an active project:

```bash
dotnet new gircore-adw-window -n SettingsWindow
```

## How It Works

### The Build Pipeline (XSTH.Blueprint.Helpers)

When you build the project, custom MSBuild targets execute the following workflow:

1. Discovers all `**/*.blp` files in the project.
2. Compiles them to standard GTK `.ui` files in the intermediate output directory using `blueprint-compiler`.
3. Strips all `<signal>` tags from the UI XML that is intended for GTK. This allows us to define signals in Blueprint without crashing GTK's `Builder`, which natively attempts to resolve signals to exported C functions (`g_module_symbol`).
4. Generates a `resources.xml` manifest and runs `glib-compile-resources` to produce an `app.gresource` bundle.
5. Embeds the generated `app.gresource` into the final assembly as an `<EmbeddedResource>`.

At runtime, the `WindowBase` class and `GResourceLoader` extract and register the embedded resource stream with GIO before GTK attempts to load the UI files.

### Roslyn Source Generator (XSTH.Blueprint.Generators)

The Roslyn generator analyzes the original un-stripped `.ui` files to handle the signals. 

For each Blueprint file that defines a signal:
1. The generator identifies the target widget IDs, their GTK class types (e.g., `GtkButton`), signal names, and the requested C# handler names.
2. It generates a partial class containing a strongly-typed `ConfigureSignals(Gtk.Builder builder)` method.
3. This method uses the parsed class to cast the builder object statically (`as Gtk.Button`) and natively binds the event (`obj.OnClicked += this.MyHandler`), entirely bypassing Reflection. This guarantees optimal performance and strict compatibility with the .NET Trimmer and Native AOT compilers.

To use this functionality, simply inherit from `XSTH.Blueprint.Helpers.WindowBase`, declare your class as `partial`, and call `ConfigureSignals(Builder)` in the constructor. The included templates are already configured to utilize this.

## Requirements

* .NET SDK (defaults to net10.0)
* `blueprint-compiler` available in your system PATH
* `glib-compile-resources` available in your system PATH
* Gir.Core GTK4 and Libadwaita ecosystem packages

## Credits

* https://github.com/TenderOwl/gircore-blueprint-template
While i didn't take anything from them their project still helped me fix some problems
* https://gircore.github.io/ 
Obviously
