using System.ComponentModel;
using Xunit;
using XSTH.Blueprint.Helpers;

namespace XSTH.Blueprint.Helpers.Tests;

public sealed class BindingScopeTests
{
    [Fact]
    public void BindSynchronizesInitiallyAndUpdatesEveryBindingForNamedOrEmptyNotifications()
    {
        var context = new QueuedSynchronizationContext();
        var model = new TestViewModel("initial");
        var firstTarget = new TextTarget();
        var secondTarget = new TextTarget();
        using var scope = new BindingScope<TestViewModel>(model, context);

        scope.Bind(nameof(TestViewModel.Text), firstTarget, static source => source.Text, static (target, value) => target.Value = value);
        scope.Bind(nameof(TestViewModel.Text), secondTarget, static source => source.Text, static (target, value) => target.Value = value);
        context.Drain();

        Assert.Equal("initial", firstTarget.Value);
        Assert.Equal("initial", secondTarget.Value);

        model.Text = "named update";
        context.Drain();
        Assert.Equal("named update", firstTarget.Value);
        Assert.Equal("named update", secondTarget.Value);

        model.Text = "all update";
        model.RaiseAllPropertiesChanged();
        context.Drain();
        Assert.Equal("all update", firstTarget.Value);
        Assert.Equal("all update", secondTarget.Value);
    }

    [Fact]
    public void DisposeCancelsQueuedUpdatesAndDetachesTheModel()
    {
        var context = new QueuedSynchronizationContext();
        var model = new TestViewModel("initial");
        var target = new TextTarget();
        var scope = new BindingScope<TestViewModel>(model, context);

        scope.Bind(nameof(TestViewModel.Text), target, static source => source.Text, static (destination, value) => destination.Value = value);
        context.Drain();
        Assert.Equal("initial", target.Value);

        model.Text = "queued before disposal";
        scope.Dispose();
        context.Drain();
        Assert.Equal("initial", target.Value);

        model.Text = "after disposal";
        context.Drain();
        Assert.Equal("initial", target.Value);
    }

    private sealed class TextTarget
    {
        public string? Value { get; set; }
    }

    private sealed class TestViewModel : INotifyPropertyChanged
    {
        private string _text;

        public TestViewModel(string text)
        {
            _text = text;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value)
                {
                    return;
                }

                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public void RaiseAllPropertiesChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_callbacks)
            {
                _callbacks.Enqueue((callback, state));
            }
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) callback;
                lock (_callbacks)
                {
                    if (_callbacks.Count == 0)
                    {
                        return;
                    }

                    callback = _callbacks.Dequeue();
                }

                callback.Callback(callback.State);
            }
        }
    }
}
