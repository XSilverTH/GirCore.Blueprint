using MyApp.Views;

namespace MyApp;

public class App : Adw.Application
{
    private readonly bool _smokeMode = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke") >= 0;
    private MainWindow? _shell;
    private StatusPage? _page;

    public App()
    {
        ApplicationId = "org.MyApp.App";
        Flags = Gio.ApplicationFlags.FlagsNone;
        OnActivate += Activate;
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        _shell = new MainWindow();
        var viewModel = new StatusPageViewModel();
        _page = new StatusPage { ViewModel = viewModel };
        _shell.PageStack.AddTitled(_page.Widget, "status", "Status");
        _shell.Widget.Application = this;
        AddWindow(_shell.Widget);
        _shell.Widget.Present();

        if (_smokeMode) RunSmokeAsync(_page, viewModel);
        else _ = Task.Run(() => viewModel.Status = "Updated from a worker thread");
    }

    private async void RunSmokeAsync(StatusPage page, StatusPageViewModel viewModel)
    {
        try
        {
            page.TriggerChangeMessage();
            if (viewModel.Status != "Changed by Blueprint signal" || page.DisplayedStatus != "Changed by Blueprint signal")
                throw new InvalidOperationException("The generated Blueprint signal was not wired to the page handler.");

            await Task.Run(() => viewModel.Status = "Updated from a worker thread");
            if (page.DisplayedStatus != "Updated from a worker thread")
                throw new InvalidOperationException("The worker-thread view-model update was not dispatched to the page.");

            page.Dispose();
            viewModel.Status = "Ignored after page disposal";
            if (page.DisplayedStatus != "Updated from a worker thread")
                throw new InvalidOperationException("A disposed page accepted a view-model update.");

            Quit();
        }
        catch
        {
            Environment.ExitCode = 1;
            Quit();
            throw;
        }
    }
}
