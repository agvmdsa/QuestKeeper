using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Account;

/// <summary>
/// The details sidebar. It does not know which cards exist: a card hands it a title and itself as content, and the view
/// renders that content through a data template.
/// </summary>
public partial class SidebarViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private object? _content;

    public string AutomationName => $"{Title} details";

    public bool HasContent => Content is not null;

    public void Open(string title, object content)
    {
        Title = title;
        Content = content;
        IsOpen = true;
    }

    /// <summary>Closes the sidebar; the content stays so the slide-out keeps showing it.</summary>
    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Closes the sidebar only when it is showing the given content.</summary>
    public void CloseIf(object content)
    {
        if (ReferenceEquals(Content, content))
        {
            IsOpen = false;
        }
    }

    /// <summary>Closes the sidebar and forgets what it showed.</summary>
    public void Reset()
    {
        IsOpen = false;
        Title = string.Empty;
        Content = null;
    }
}
