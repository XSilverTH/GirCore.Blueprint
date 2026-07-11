using Gtk;

namespace XSTH.Blueprint.Helpers;

/// <summary>
/// Loads a GTK widget root from this view's compiled Blueprint resource.
/// </summary>
/// <typeparam name="TWidget">The concrete type of the Blueprint root widget.</typeparam>
public abstract class ViewBase<TWidget> : IDisposable
    where TWidget : Widget
{
    private bool _disposed;

    /// <summary>
    /// Initializes a view and loads its compiled Blueprint root.
    /// Create views on the GTK UI thread after <c>RunWithSynchronizationContext</c> has installed its context.
    /// </summary>
    protected ViewBase()
    {
        try
        {
            Builder = Builder.NewFromResource(ResourcePath);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not load Blueprint resource '{ResourcePath}' for root view '{GetType().FullName}'. " +
                "Ensure GResourceHelper.RegisterAssemblyResources is called before constructing the view and that the .blp file follows the namespace/type naming convention.",
                exception);
        }

        Widget = GetRequiredObject<TWidget>(BuilderId);
        UiContext = SynchronizationContext.Current;
        ConfigureSignals(Builder);
    }

    /// <summary>Gets the builder used to create this view's root and named child objects.</summary>
    protected Builder Builder { get; }

    /// <summary>Gets the concrete root widget loaded from the GResource.</summary>
    public TWidget Widget { get; }

    /// <summary>Gets the root as a GTK widget for insertion into an arbitrary GTK or Libadwaita container.</summary>
    public Widget Root => Widget;

    /// <summary>
    /// Gets the UI synchronization context captured when the view was created. It is used by view-model bindings.
    /// </summary>
    protected SynchronizationContext? UiContext { get; }

    /// <summary>Gets whether this managed view has released its subscriptions.</summary>
    protected bool IsDisposed => _disposed;

    /// <summary>Gets the GResource path to the compiled UI. Override for an intentional naming exception.</summary>
    protected virtual string ResourcePath => GetDefaultResourcePath();

    /// <summary>Gets the GTK builder ID of the root object. Override for an intentional naming exception.</summary>
    protected virtual string BuilderId => GetType().Name;

    /// <summary>
    /// Retrieves a named builder object or throws an exception that identifies the failed Blueprint contract.
    /// </summary>
    protected T GetRequiredObject<T>(string id)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        object? value;
        try
        {
            value = Builder.GetObject(id);
        }
        catch (Exception exception)
        {
            throw CreateObjectResolutionException(id, typeof(T), exception);
        }

        if (value is T typed)
        {
            return typed;
        }

        throw CreateObjectResolutionException(id, typeof(T), null, value?.GetType());
    }

    /// <summary>Wires statically generated Blueprint signal handlers after the root has been loaded.</summary>
    protected virtual void ConfigureSignals(Builder builder)
    {
    }

    /// <summary>Removes statically generated Blueprint signal handlers when this managed view is disposed.</summary>
    protected virtual void DisposeSignals(Builder builder)
    {
    }

    /// <summary>
    /// Disposes managed subscriptions owned by the view. It deliberately does not dispose <see cref="Widget"/>;
    /// GTK container/application ownership determines the root widget lifetime.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Allows derived views to release their managed resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeSignals(Builder);
        }
    }

    private string GetDefaultResourcePath()
    {
        var viewType = GetType();
        var @namespace = viewType.Namespace;
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new InvalidOperationException(
                $"Blueprint root view '{viewType.FullName}' must have a namespace or override {nameof(ResourcePath)}.");
        }

        return $"/{@namespace.Replace('.', '/')}/{viewType.Name}.ui";
    }

    private InvalidOperationException CreateObjectResolutionException(
        string id,
        Type requestedType,
        Exception? innerException,
        Type? actualType = null)
    {
        var actualTypeDescription = actualType is null ? "was not found" : $"was '{actualType.FullName}'";
        return new InvalidOperationException(
            $"Blueprint object '{id}' requested as '{requestedType.FullName}' for root view '{GetType().FullName}' " +
            $"from resource '{ResourcePath}' {actualTypeDescription}. Check the root ID, Blueprint class, and view generic type.",
            innerException);
    }
}
