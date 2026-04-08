using AppTemplate.Views;

namespace AppTemplate;

public class App : Adw.Application
{
    public App()
    {
        ApplicationId = "org.AppTemplate.App";
        Flags = Gio.ApplicationFlags.FlagsNone;
        OnActivate += Activate;
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        var mainWindowWrapper = new MainWindow();
        var mainWindow = mainWindowWrapper.Window;
        
        mainWindow.Application = this;

        AddWindow(mainWindow);

        mainWindow.Present();
    }
}
