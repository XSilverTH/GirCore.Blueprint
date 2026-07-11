# XSTH.Blueprint.Helpers

`XSTH.Blueprint.Helpers` is a small foundation for GTK 4 / Libadwaita applications that keep Blueprint views in independent `.blp` files. It compiles every Blueprint file in a project, bundles the resulting GTK Builder XML into one GResource, and uses a source generator to wire Blueprint signals directly to GirCore events.

Version **2.0.0** adds first-class non-window roots and a deliberately narrow `INotifyPropertyChanged` binding pattern. It is not an application framework: applications keep ownership of navigation, dependency composition, commands, and widget placement.

## Install and project setup

Reference the runtime package and the GirCore package version used by this repository:

```xml
<ItemGroup>
  <PackageReference Include="GirCore.Adw-1" Version="0.7.0" />
  <PackageReference Include="XSTH.Blueprint.Helpers" Version="2.0.0" />
</ItemGroup>

<ItemGroup>
  <Compile Update="**\*.cs">
    <DependentUpon>%(Filename).blp</DependentUpon>
  </Compile>
  <AdditionalFiles Include="**\*.blp" />
</ItemGroup>
```

The package supplies the runtime assembly, MSBuild targets, and source generator. The targets discover all `**/*.blp` files, compile them with `blueprint-compiler`, strip `<signal>` elements from the Builder XML used at runtime, generate one `app.gresource` with `glib-compile-resources`, and embed it into the consuming assembly. The generator reads the unstripped intermediate `.ui` XML, so static C# subscriptions still exist even though GTK itself does not attempt C symbol lookup.

At startup, register the bundle before creating any Blueprint-backed view. Create views from the GTK activation/UI thread after `RunWithSynchronizationContext` has installed its synchronization context:

```csharp
Adw.Module.Initialize();
GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

var app = new App();
return app.RunWithSynchronizationContext(args);
```

## Window roots and ordinary view roots

Use `WindowBase<TWindow>` only when the Blueprint root is a `Gtk.Window`:

```csharp
public partial class MainWindow : WindowBase<Adw.ApplicationWindow>
{
}
```

Use `ViewBase<TWidget>` for every non-window root, including pages, reusable components, and widgets to place in an `Adw.ViewStack`, `Gtk.Stack`, or other GTK container:

```csharp
using Gtk;
using XSTH.Blueprint.Helpers;

namespace MyApp.Views;

public partial class StatusPage : ViewBase<Box>
{
    private readonly Label _status;

    public StatusPage()
    {
        _status = GetRequiredObject<Label>("status_label");
    }
}
```

Both bases load the root from a GResource, expose it as the typed `Widget` property (and as `Root` when a `Gtk.Widget` is all a container needs), expose the `Gtk.Builder` to derived classes, and call the generated `ConfigureSignals(Gtk.Builder)` override. `GetRequiredObject<T>(id)` is the normal strongly typed way to retrieve named children. If the resource, root, or named object does not match the view contract, its exception identifies the requested ID/type, the root view type, and the resource path.

### Naming and resource convention

The default convention is intentional and applies to both windows and pages:

| C# backing type | Blueprint file/root | Resource loaded |
|---|---|---|
| `MyApp.Views.MainWindow` | `Views/MainWindow.blp`, root `MainWindow` | `/MyApp/Views/MainWindow.ui` |
| `MyApp.Views.StatusPage` | `Views/StatusPage.blp`, root `StatusPage` | `/MyApp/Views/StatusPage.ui` |

The root ID in Blueprint must match the partial C# type name. Its folder maps to the C# namespace below the project root namespace. Override the protected `ResourcePath` or `BuilderId` only for a real, deliberate naming exception.

A window can be a shell while each page owns its own resource and backing class:

```csharp
var shell = new MainWindow();
var page = new StatusPage();

shell.PageStack.AddTitled(page.Widget, "status", "Status");
shell.Widget.Application = this;
shell.Widget.Present();
```

The repository sample in `test_app/MyApp` demonstrates this exact shape: an `Adw.ApplicationWindow` shell loads `StatusPage` from a different `.blp` resource and inserts its `Gtk.Box` root into `Adw.ViewStack`.

## Blueprint signals for pages and components

Signal syntax is the same for all roots. A page is simply a partial class with a matching method:

```blp
Button change_message_button {
  label: "Change message";
  clicked => $OnChangeMessageButtonClicked();
}
```

```csharp
private void OnChangeMessageButtonClicked(object? sender, EventArgs eventArgs)
{
    // Application-specific action.
}
```

For a valid root containing signals, the generator emits direct, typed GirCore event subscriptions in `ConfigureSignals`, plus matching unsubscriptions in `DisposeSignals`. It has no reflective signal lookup. Invalid generated UI receives actionable `BSG001`–`BSG004` diagnostics for malformed XML, a missing `<interface>`, a missing root object/template, or a missing/blank root identity. Generated hint names include the namespace, so roots named alike in different namespaces do not collide.

## Typed view models and one-way bindings

For a view model that implements only `INotifyPropertyChanged`, derive from `ViewBase<TWidget, TViewModel>` and add bindings in `BindViewModel`:

```csharp
public sealed class StatusViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new(nameof(Status)));
        }
    }
}

public partial class StatusPage : ViewBase<Gtk.Box, StatusViewModel>
{
    private readonly Gtk.Label _statusLabel;

    public StatusPage()
    {
        _statusLabel = GetRequiredObject<Gtk.Label>("status_label");
    }

    protected override void BindViewModel(
        StatusViewModel viewModel,
        BindingScope<StatusViewModel> bindings)
    {
        bindings.Bind(
            nameof(StatusViewModel.Status),
            _statusLabel,
            static model => model.Status,
            static (label, value) => label.SetText(value));
    }
}
```

Attach or replace the model with the one `ViewModel` property:

```csharp
page.ViewModel = firstModel;   // creates a scope and performs initial synchronization
page.ViewModel = nextModel;    // disposes/unsubscribes the first scope, then binds nextModel
page.ViewModel = null;         // unbinds the current model
page.Dispose();                // unbinds and removes generated signal delegates
```

`BindingScope<TViewModel>.Bind` is one-way, accepts the property name (use `nameof`), typed model getter, target object, and typed setter. It performs initial synchronization, reacts to both a matching `PropertyChanged` name and an empty/null property name, and permits multiple bindings for one model property. The package intentionally does **not** add a command abstraction or a two-way binding convenience API; Blueprint signals and ordinary view-model methods are clearer for user actions.

### Threading and lifetime contract

The generic view base captures `SynchronizationContext.Current` when the view is constructed. With the documented `RunWithSynchronizationContext` startup, a `PropertyChanged` event raised on a worker thread is posted to the GTK UI context; the widget setter never runs on that worker. A change raised on the captured UI context updates immediately without a queue allocation.

Construct and dispose Blueprint-backed views on the GTK UI thread. `ViewModel` attachment requires the captured UI synchronization context and throws an actionable exception if the view was created before it existed. Disposing a view detaches the model event subscription, releases binding delegates/targets, removes generated signal delegates, and makes queued binding callbacks no-ops. Replacing a model does the same for the old scope before the new binding hook runs. A detached-but-still-owned GTK widget is safe to receive an update; once the managed view is disposed, it receives none.

Assigning `ViewModel` after a view is disposed throws `ObjectDisposedException`; create a replacement view instead of reusing a disposed wrapper.

`Dispose()` deliberately does **not** dispose `Widget`/`Root`. The GTK container or application owns the root widget lifetime. Dispose the managed wrapper when the application removes or no longer needs that page.

## Templates

Install templates locally with `test_and_install.sh`, then use:

```bash
dotnet new gircore-adw -n MyGtkApp
dotnet new gircore-adw-window -n SettingsWindow
dotnet new gircore-adw-view -n StatusPage --namespace MyGtkApp.Views
```

`gircore-adw-view` creates a non-window `Gtk.Box` Blueprint root, a matching partial `ViewBase<Box>` class, and a representative generated signal. Place the pair under the namespace/folder dictated by the resource convention above.

## Explicit non-goals

This package does **not** provide Blueprint markup `{Binding}`, reflection-driven binding, expression compilation, dynamic dispatch, a service locator, dependency injection container, navigation framework, command framework, or an application architecture. It provides the low-level compiled-resource, root-view, signal, and opt-in typed one-way binding primitives; consuming applications choose everything else.

## Requirements

- .NET SDK targeting `net10.0`
- `blueprint-compiler` on `PATH`
- `glib-compile-resources` on `PATH`
- GirCore GTK 4 / Libadwaita packages
