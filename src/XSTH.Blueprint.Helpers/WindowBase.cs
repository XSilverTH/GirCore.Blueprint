using System.Diagnostics.CodeAnalysis;
using Gtk;

namespace XSTH.Blueprint.Helpers;

public abstract class WindowBase<T> where T : Gtk.Widget
{
    protected readonly Builder Builder;
    public T Widget { get; }

    protected virtual string ResourcePath => $"/{GetType().Namespace!.Replace('.', '/')}/{GetType().Name}.ui";
    protected virtual string BuilderId => GetType().Name;

    protected WindowBase()
    {
        Builder = Builder.NewFromResource(ResourcePath);
        Widget = (T)Builder.GetObject(BuilderId)!;
        ConfigureSignals(Builder);
    }

    protected virtual void ConfigureSignals(Builder builder)
    {
    }
}