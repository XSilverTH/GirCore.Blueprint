using Gtk;
using XSTH.Blueprint.Helpers;

namespace AppTemplate.Views;

public partial class TemplateView : ViewBase<Box>
{
    private void OnActionButtonClicked(object? sender, EventArgs eventArgs)
    {
        ((Button)sender!).Label = "Done";
    }
}
