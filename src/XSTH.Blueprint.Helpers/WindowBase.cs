using Gtk;

namespace XSTH.Blueprint.Helpers;

/// <summary>
/// A Blueprint-backed GTK window root. Use <see cref="ViewBase{TWidget}"/> for pages and components.
/// </summary>
public abstract class WindowBase<TWindow> : ViewBase<TWindow>
    where TWindow : Window
{
}