using CommunityToolkit.Mvvm.ComponentModel;
using Jekyller.ViewModels;

namespace Jekyller.Models;

public partial class NavItem : ObservableObject
{
    public string Title { get; }
    public string Icon { get; }
    public ViewModelBase ViewModel { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public NavItem(string title, string icon, ViewModelBase viewModel)
    {
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
    }
}
