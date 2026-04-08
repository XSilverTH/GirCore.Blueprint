using Gtk;
using XSTH.Blueprint.Helpers;

namespace AppTemplate.Views;

public partial class MainWindow:WindowBase
{
    public MainWindow()
    {
        ConfigureSignals(Builder);
    }

    private void OnClickMeButton_Clicked(object? sender, EventArgs e)
    {
        Console.WriteLine("Button was clicked!");
        (sender as Button)!.Label = "Clicked!";
    }
}
