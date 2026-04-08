using System.Diagnostics.CodeAnalysis;
using Gtk;

namespace XSTH.Blueprint.Helpers;

public abstract class WindowBase
{
    protected readonly Builder Builder;
    public Adw.ApplicationWindow Window { get; }
    protected WindowBase()
    {
        Builder = Builder.NewFromResource($"/{GetType().Namespace!.Replace('.', '/')}/{GetType().Name}.ui");
        Window = (Adw.ApplicationWindow)Builder.GetObject(GetType().Name)!;
        ConfigureSignals(Builder);
    }

    protected virtual void ConfigureSignals(Builder builder)
    {
    }
}