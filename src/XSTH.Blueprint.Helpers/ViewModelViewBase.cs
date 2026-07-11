using System.ComponentModel;

namespace XSTH.Blueprint.Helpers;

/// <summary>
/// A Blueprint view with a single replaceable <see cref="ViewModel"/> and deterministic binding lifetime.
/// </summary>
/// <typeparam name="TWidget">The concrete Blueprint root widget type.</typeparam>
/// <typeparam name="TViewModel">The view-model type.</typeparam>
public abstract class ViewBase<TWidget, TViewModel> : ViewBase<TWidget>
    where TWidget : Gtk.Widget
    where TViewModel : class, INotifyPropertyChanged
{
    private BindingScope<TViewModel>? _bindings;
    private TViewModel? _viewModel;

    /// <summary>Gets or replaces the current model. Replacing it first disposes every binding for the previous model.</summary>
    public TViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            var uiContext = value is null ? null : RequireUiContext();

            var previousModel = _viewModel;
            var previousBindings = _bindings;
            _bindings = null;
            _viewModel = value;
            previousBindings?.Dispose();

            if (value is null)
            {
                OnViewModelChanged(previousModel, null);
                return;
            }

            var bindings = new BindingScope<TViewModel>(value, uiContext!);
            _bindings = bindings;
            OnViewModelChanged(previousModel, value);
            BindViewModel(value, bindings);
        }
    }

    /// <summary>
    /// Creates bindings for an attached model. Bindings are disposed automatically when the model is replaced or the view is disposed.
    /// </summary>
    protected virtual void BindViewModel(TViewModel viewModel, BindingScope<TViewModel> bindings)
    {
    }

    /// <summary>Observes attachment and removal after the previous binding scope has been disposed.</summary>
    protected virtual void OnViewModelChanged(TViewModel? previousViewModel, TViewModel? currentViewModel)
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var bindings = _bindings;
            _bindings = null;
            _viewModel = null;
            bindings?.Dispose();
        }

        base.Dispose(disposing);
    }

    private SynchronizationContext RequireUiContext()
    {
        return UiContext ?? throw new InvalidOperationException(
            $"Cannot bind view model for '{GetType().FullName}' because no UI SynchronizationContext was captured. " +
            "Construct the view on the GTK thread after calling RunWithSynchronizationContext.");
    }
}

/// <summary>
/// Tracks one-way bindings for a single <see cref="INotifyPropertyChanged"/> view model.
/// A scope owns its event subscription and is normally disposed by <see cref="ViewBase{TWidget, TViewModel}"/>.
/// </summary>
/// <typeparam name="TViewModel">The view-model type.</typeparam>
public sealed class BindingScope<TViewModel> : IDisposable
    where TViewModel : class, INotifyPropertyChanged
{
    private readonly object _gate = new();
    private readonly List<IViewBinding> _bindings = [];
    private readonly SynchronizationContext _uiContext;
    private TViewModel? _viewModel;
    private bool _disposed;

    internal BindingScope(TViewModel viewModel, SynchronizationContext uiContext)
    {
        _viewModel = viewModel;
        _uiContext = uiContext;
        viewModel.PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Adds a one-way view-model-to-target binding and synchronizes the target immediately.
    /// Pass the property with <c>nameof(TViewModel.Property)</c> to keep the contract refactor-safe.
    /// </summary>
    public void Bind<TTarget, TValue>(
        string propertyName,
        TTarget target,
        Func<TViewModel, TValue> getValue,
        Action<TTarget, TValue> setValue)
        where TTarget : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(getValue);
        ArgumentNullException.ThrowIfNull(setValue);

        var binding = new OneWayBinding<TViewModel, TTarget, TValue>(this, propertyName, target, getValue, setValue);
        lock (_gate)
        {
            ThrowIfDisposed();
            _bindings.Add(binding);
        }

        binding.Update();
    }

    /// <summary>Disposes all bindings and detaches from the current view model.</summary>
    public void Dispose()
    {
        IViewBinding[] bindings;
        TViewModel? viewModel;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            viewModel = _viewModel;
            _viewModel = null;
            bindings = _bindings.ToArray();
            _bindings.Clear();
        }

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnPropertyChanged;
        }

        foreach (var binding in bindings)
        {
            binding.Dispose();
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _viewModel))
            {
                return;
            }

            foreach (var binding in _bindings)
            {
                if (binding.Matches(eventArgs.PropertyName))
                {
                    binding.Update();
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BindingScope<TViewModel>));
        }
    }

    private interface IViewBinding : IDisposable
    {
        bool Matches(string? propertyName);
        void Update();
    }

    private sealed class OneWayBinding<TModel, TTarget, TValue> : IViewBinding
        where TModel : class, INotifyPropertyChanged
        where TTarget : class
    {
        private static readonly SendOrPostCallback ApplyOnUiContext = static state => ((OneWayBinding<TModel, TTarget, TValue>)state!).Apply();

        private readonly object _gate = new();
        private BindingScope<TModel>? _scope;
        private readonly string _propertyName;
        private TTarget? _target;
        private Func<TModel, TValue>? _getValue;
        private Action<TTarget, TValue>? _setValue;
        private bool _disposed;

        public OneWayBinding(
            BindingScope<TModel> scope,
            string propertyName,
            TTarget target,
            Func<TModel, TValue> getValue,
            Action<TTarget, TValue> setValue)
        {
            _scope = scope;
            _propertyName = propertyName;
            _target = target;
            _getValue = getValue;
            _setValue = setValue;
        }

        public bool Matches(string? propertyName) => string.IsNullOrEmpty(propertyName) || propertyName == _propertyName;

        public void Update()
        {
            BindingScope<TModel>? scope;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                scope = _scope;
            }

            if (scope is null)
            {
                return;
            }

            if (ReferenceEquals(SynchronizationContext.Current, scope._uiContext))
            {
                Apply();
                return;
            }

            scope._uiContext.Post(ApplyOnUiContext, this);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _scope = null;
                _target = null;
                _getValue = null;
                _setValue = null;
            }
        }

        private void Apply()
        {
            lock (_gate)
            {
                if (_disposed || _scope?._viewModel is not TModel viewModel || _target is not TTarget target ||
                    _getValue is not { } getValue || _setValue is not { } setValue)
                {
                    return;
                }

                setValue(target, getValue(viewModel));
            }
        }
    }
}
