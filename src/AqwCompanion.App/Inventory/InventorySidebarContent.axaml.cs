using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.VisualTree;

namespace AqwCompanion.App.Inventory;

/// <summary>The filter box and the virtualized item list shown in the details sidebar.</summary>
public partial class InventorySidebarContent : UserControl
{
    // The sidebar hosts its content inside a ScrollViewer, which measures with unlimited height; a list there would
    // realize every row. So the layout takes the height of that viewer's viewport and the list scrolls on its own.
    private const double SidebarMargin = 32;
    private const double MinimumHeight = 160;

    /// <summary>A row shows "×Count" only when the item is held more than once.</summary>
    public static readonly IValueConverter ShowCount = new FuncValueConverter<int, bool>(count => count > 1);

    private ScrollViewer? _host;

    public InventorySidebarContent() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = this.FindAncestorOfType<ScrollViewer>();
        if (_host is not null)
        {
            _host.PropertyChanged += OnHostPropertyChanged;
            Resize();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty)
        {
            Resize();
        }
    }

    private void Resize()
    {
        if (_host is { Viewport.Height: > 0 } host)
        {
            InventoryLayout.Height = System.Math.Max(MinimumHeight, host.Viewport.Height - SidebarMargin);
        }
    }
}
