using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AqwCompanion.App.Phone;

public partial class PairingWindow : Window
{
    private DispatcherTimer? _timer;
    private PairingViewModel? _vm;

    public PairingWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Activated += (_, _) => _vm?.Tick();
        Closed += (_, _) =>
        {
            var vm = _vm;
            Detach();
            vm?.Dispose();
            SetImage(null);
        };
    }

    private void Attach()
    {
        Detach();
        if (DataContext is not PairingViewModel vm)
        {
            return;
        }

        _vm = vm;
        vm.PropertyChanged += OnChanged;
        ShowQr();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => vm.Tick();
        _timer.Start();
    }

    private void Detach()
    {
        _timer?.Stop();
        _timer = null;
        if (_vm is { } vm)
        {
            vm.PropertyChanged -= OnChanged;
            _vm = null;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PairingViewModel.QrPng))
        {
            ShowQr();
        }
    }

    private void ShowQr()
    {
        SetImage(_vm is { QrPng.Length: > 0 } vm ? vm.QrPng : null);
    }

    private void SetImage(byte[]? png)
    {
        var image = this.FindControl<Image>("PairingQrImage")!;
        var old = image.Source as Bitmap;
        if (png is null)
        {
            image.Source = null;
        }
        else
        {
            using var stream = new MemoryStream(png);
            image.Source = new Bitmap(stream);
        }

        old?.Dispose();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
