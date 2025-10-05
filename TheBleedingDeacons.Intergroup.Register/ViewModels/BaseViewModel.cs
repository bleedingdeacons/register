using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;


public partial class BaseViewModel : ObservableObject, IQueryAttributable
{
    [ObservableProperty]
    bool isBusy;

    [ObservableProperty]
    string title = string.Empty;

    public virtual void ApplyQueryAttributes(IDictionary<string, object> query)
    {

    }
}
