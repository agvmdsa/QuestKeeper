using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.Phone;

public class PhoneStreamBookTests
{
    private readonly PhoneStreamBook _book = new(maxTotal: 8, maxPerDevice: 3);

    private PhoneStreamBook.Entry Open(string? device, out CancellationTokenSource cts)
    {
        cts = new CancellationTokenSource();
        return Assert.IsType<PhoneStreamBook.Entry>(_book.TryOpen(device, cts));
    }

    [Fact]
    public void A_fourth_stream_of_one_device_cancels_that_devices_oldest_and_takes_its_slot()
    {
        Open("a", out var first);
        Open("a", out var second);
        Open("a", out var third);

        Open("a", out var fourth);

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested || third.IsCancellationRequested || fourth.IsCancellationRequested);
        Assert.Equal(3, _book.Open);
        Assert.Equal(3, _book.OpenFor("a"));
    }

    [Fact]
    public void Closing_an_evicted_stream_frees_nothing_twice()
    {
        var oldest = Open("a", out _);
        Open("a", out _);
        Open("a", out _);
        Open("a", out _);

        _book.Close(oldest);
        _book.Close(oldest);

        Assert.Equal(3, _book.Open);
    }

    [Fact]
    public void The_global_cap_refuses_a_device_below_its_own_cap_but_not_one_that_replaces_its_oldest()
    {
        for (var i = 0; i < 3; i++)
        {
            Open("a", out _);
            Open("b", out _);
        }

        Open("c", out _);
        Open("c", out _);
        Assert.Equal(8, _book.Open);

        Assert.Null(_book.TryOpen("c", new CancellationTokenSource()));
        Assert.Null(_book.TryOpen(null, new CancellationTokenSource()));
        Assert.NotNull(_book.TryOpen("a", new CancellationTokenSource()));
        Assert.Equal(8, _book.Open);
    }

    [Fact]
    public void A_closed_stream_frees_its_slot_for_any_device()
    {
        var entries = new List<PhoneStreamBook.Entry>();
        for (var i = 0; i < 8; i++)
        {
            entries.Add(Open($"d{i}", out _));
        }

        Assert.Null(_book.TryOpen("x", new CancellationTokenSource()));
        _book.Close(entries[0]);

        Assert.NotNull(_book.TryOpen("x", new CancellationTokenSource()));
        Assert.Equal(0, _book.OpenFor("d0"));
    }

    [Fact]
    public void Cancelling_a_device_ends_all_its_streams_and_no_others()
    {
        Open("a", out var a1);
        Open("a", out var a2);
        Open("b", out var b1);

        _book.CancelDevices(["a"]);

        Assert.True(a1.IsCancellationRequested && a2.IsCancellationRequested);
        Assert.False(b1.IsCancellationRequested);
    }

    [Fact]
    public void Streams_without_a_device_count_only_toward_the_global_cap()
    {
        for (var i = 0; i < 8; i++)
        {
            Assert.NotNull(_book.TryOpen(null, new CancellationTokenSource()));
        }

        Assert.Null(_book.TryOpen(null, new CancellationTokenSource()));
    }
}
