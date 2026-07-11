using XSTH.Blueprint.Helpers;

namespace MyApp.Views;

public partial class MainWindow : WindowBase<Adw.ApplicationWindow>
{
    public Adw.ViewStack PageStack { get; }

    public MainWindow()
    {
        PageStack = GetRequiredObject<Adw.ViewStack>("page_stack");
    }
}
