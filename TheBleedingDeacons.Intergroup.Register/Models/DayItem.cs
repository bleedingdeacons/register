using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Register.Models;

public partial class DayItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool isSelected;

    public DayItem(string name)
    {
        Name = name;
        IsSelected = false;
    }
}