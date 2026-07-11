using System.ComponentModel;

namespace MyApp.Views;

public sealed class StatusPageViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}
