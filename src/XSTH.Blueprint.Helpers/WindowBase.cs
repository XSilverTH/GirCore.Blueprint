using System.Diagnostics.CodeAnalysis;
using Gtk;

namespace XSTH.Blueprint.Helpers;

public abstract class WindowBase<T> where T : Gtk.Widget
{
    protected readonly Builder Builder;
    public T Widget { get; }
    protected WindowBase()
    {
        Builder = Builder.NewFromResource($"/{GetType().Namespace!.Replace('.', '/')}/{GetType().Name}.ui");
        Widget = (T)Builder.GetObject(GetType().Name)!;
        ConfigureSignals(Builder);
    }

    protected virtual void ConfigureSignals(Builder builder)
    {
    }
}