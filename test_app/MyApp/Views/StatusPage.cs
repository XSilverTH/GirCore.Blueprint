using Gtk;
using XSTH.Blueprint.Helpers;

namespace MyApp.Views;

public partial class StatusPage : ViewBase<Box, StatusPageViewModel>
{
    private readonly Label _statusLabel;
    private readonly Button _changeMessageButton;

    public StatusPage()
    {
        _statusLabel = GetRequiredObject<Label>("status_label");
        _changeMessageButton = GetRequiredObject<Button>("change_message_button");
    }

    public string DisplayedStatus => _statusLabel.GetText();
    public void TriggerChangeMessage() => _changeMessageButton.Activate();

    protected override void BindViewModel(StatusPageViewModel viewModel, BindingScope<StatusPageViewModel> bindings)
    {
        bindings.Bind(nameof(StatusPageViewModel.Status), _statusLabel,
            static model => model.Status,
            static (label, status) => label.SetText(status));
    }

    private void OnChangeMessageButtonClicked(object? sender, EventArgs eventArgs)
    {
        if (ViewModel is not null) ViewModel.Status = "Changed by Blueprint signal";
    }
}
