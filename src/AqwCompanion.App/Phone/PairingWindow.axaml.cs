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
        Closed += (_, _) =>
        {
            var vm = _vm;
            Detach();
            vm?.Dispose();
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
        var image = this.FindControl<Image>("PairingQrImage")!;
        var old = image.Source as Bitmap;
        if (_vm is { QrPng.Length: > 0 } vm)
        {
            using var stream = new MemoryStream(vm.QrPng);
            image.Source = new Bitmap(stream);
        }
        else
        {
            image.Source = null;
        }

        old?.Dispose();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
