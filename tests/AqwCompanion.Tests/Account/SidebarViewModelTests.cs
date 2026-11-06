using AqwCompanion.App.Account;

namespace AqwCompanion.Tests.Account;

public class SidebarViewModelTests
{
    [Fact]
    public void Starts_closed_and_empty()
    {
        var sidebar = new SidebarViewModel();

        Assert.False(sidebar.IsOpen);
        Assert.Null(sidebar.Content);
        Assert.False(sidebar.HasContent);
    }

    [Fact]
    public void Open_sets_title_and_content_and_opens()
    {
        var sidebar = new SidebarViewModel();
        var content = new object();

        sidebar.Open("Equipped", content);

        Assert.True(sidebar.IsOpen);
        Assert.Equal("Equipped", sidebar.Title);
        Assert.Same(content, sidebar.Content);
        Assert.True(sidebar.HasContent);
        Assert.Equal("Equipped details", sidebar.AutomationName);
    }

    [Fact]
    public void Open_raises_change_notifications_for_title_automation_name_and_content()
    {
        var sidebar = new SidebarViewModel();
        var changed = new List<string?>();
        sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sidebar.Open("Badges", new object());

        Assert.Contains(nameof(SidebarViewModel.Title), changed);
        Assert.Contains(nameof(SidebarViewModel.AutomationName), changed);
        Assert.Contains(nameof(SidebarViewModel.HasContent), changed);
        Assert.Contains(nameof(SidebarViewModel.Content), changed);
        Assert.Contains(nameof(SidebarViewModel.IsOpen), changed);
    }

    [Fact]
    public void Close_keeps_the_content_for_the_slide_out()
    {
        var sidebar = new SidebarViewModel();
        var content = new object();
        sidebar.Open("Badges", content);

        sidebar.CloseCommand.Execute(null);

        Assert.False(sidebar.IsOpen);
        Assert.Same(content, sidebar.Content);
        Assert.Equal("Badges", sidebar.Title);
    }

    [Fact]
    public void Close_when_already_closed_is_harmless()
    {
        var sidebar = new SidebarViewModel();

        sidebar.CloseCommand.Execute(null);

        Assert.False(sidebar.IsOpen);
    }

    [Fact]
    public void CloseIf_closes_only_when_the_content_is_the_same_object()
    {
        var sidebar = new SidebarViewModel();
        var content = new object();
        sidebar.Open("Equipped", content);

        sidebar.CloseIf(new object());
        Assert.True(sidebar.IsOpen);

        sidebar.CloseIf(content);
        Assert.False(sidebar.IsOpen);
        Assert.Same(content, sidebar.Content);
    }

    [Fact]
    public void Reset_closes_and_clears_title_and_content()
    {
        var sidebar = new SidebarViewModel();
        sidebar.Open("Badges", new object());

        sidebar.Reset();

        Assert.False(sidebar.IsOpen);
        Assert.Equal(string.Empty, sidebar.Title);
        Assert.Null(sidebar.Content);
        Assert.False(sidebar.HasContent);
    }
}
