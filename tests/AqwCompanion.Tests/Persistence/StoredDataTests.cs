using AqwCompanion.Core.Persistence;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Persistence;

public sealed class StoredDataTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly StoredKey<string> Text = new("test.text", 1, "Test");
    private static readonly StoredKey<string> TextV2 = new("test.text", 2, "Test");
    private static readonly StoredKey<string> Other = new("test.other", 1, "Test");

    private readonly InMemoryStoredDataStore _inner = new();
    private readonly List<(string Operation, string Type)> _failures = [];

    private StoredData Build(params string[] obsolete) =>
        new(_inner, obsolete, (operation, type) => _failures.Add((operation, type)));

    [Fact]
    public void Save_and_load_round_trip_with_the_read_time()
    {
        var data = Build();

        Assert.True(data.Save(Text, "hello", At));
        var loaded = data.Load(Text);

        Assert.NotNull(loaded);
        Assert.Equal("hello", loaded.Value);
        Assert.Equal(At, loaded.ReadAtUtc);
        Assert.Empty(_failures);
    }

    [Fact]
    public void Load_returns_null_when_the_store_throws_and_reports_the_type()
    {
        _inner.ThrowOnLoad = true;

        Assert.Null(Build().Load(Text));

        Assert.Equal([("Load", "IOException")], _failures);
    }

    [Fact]
    public void Save_returns_false_when_the_store_throws()
    {
        _inner.ThrowOnSave = true;

        Assert.False(Build().Save(Text, "x", At));

        Assert.Equal([("Save", "IOException")], _failures);
    }

    [Fact]
    public void Remove_and_clear_report_failure_as_false()
    {
        var data = Build();
        _inner.FailNext = true;
        Assert.False(data.Remove(Text));
        _inner.FailNext = true;
        Assert.False(data.Clear());

        Assert.Equal([("Remove", "IOException"), ("Clear", "IOException")], _failures);
    }

    [Fact]
    public void Clear_passes_on_the_result_of_the_store()
    {
        _inner.ClearResult = false;

        Assert.False(Build().Clear());
        Assert.Empty(_failures);
    }

    [Fact]
    public void A_record_saved_under_version_1_is_not_returned_as_version_2_while_its_neighbour_still_loads()
    {
        var data = Build();
        data.Save(Text, "old", At);
        data.Save(Other, "neighbour", At);

        Assert.Null(data.Load(TextV2));
        Assert.Equal("neighbour", data.Load(Other)!.Value);
        Assert.Equal("old", data.Load(Text)!.Value);
    }

    [Fact]
    public void The_first_successful_save_removes_each_obsolete_key_once()
    {
        _inner.Items["old.one"] = ("x", At);
        _inner.Items["old.two"] = ("y", At);
        var data = Build("old.one", "old.two");

        data.Save(Text, "a", At);
        Assert.False(_inner.Items.ContainsKey("old.one"));
        Assert.False(_inner.Items.ContainsKey("old.two"));

        // A key that reappears is not touched by the second save.
        _inner.Items["old.one"] = ("again", At);
        data.Save(Text, "b", At);
        Assert.True(_inner.Items.ContainsKey("old.one"));
    }

    [Fact]
    public void A_failed_save_does_not_remove_the_obsolete_keys()
    {
        _inner.Items["old.one"] = ("x", At);
        _inner.ThrowOnSave = true;

        Build("old.one").Save(Text, "a", At);

        Assert.True(_inner.Items.ContainsKey("old.one"));
    }

    [Fact]
    public void A_failing_obsolete_removal_is_reported_and_does_not_fail_the_save()
    {
        var store = new RemoveThrowsStore();
        var failures = new List<(string, string)>();
        var data = new StoredData(store, ["old.one"], (operation, type) => failures.Add((operation, type)));

        Assert.True(data.Save(Text, "a", At));

        Assert.Equal([("RemoveObsolete", "IOException")], failures);
    }

    [Fact]
    public void Out_of_memory_is_not_swallowed()
    {
        var data = new StoredData(new OutOfMemoryStore(), []);

        Assert.Throws<OutOfMemoryException>(() => data.Load(Text));
        Assert.Throws<OutOfMemoryException>(() => data.Save(Text, "a", At));
        Assert.Throws<OutOfMemoryException>(() => data.Remove(Text));
        Assert.Throws<OutOfMemoryException>(() => data.Clear());
    }

    [Fact]
    public void The_failure_callback_never_receives_the_message_of_the_exception()
    {
        var failures = new List<(string Operation, string Type)>();
        var data = new StoredData(new MessageStore(), [], (operation, type) => failures.Add((operation, type)));

        data.Load(Text);
        data.Save(Text, "a", At);
        data.Remove(Text);
        data.Clear();

        Assert.Equal(4, failures.Count);
        Assert.All(failures, f =>
        {
            Assert.DoesNotContain("SECRET", f.Operation + f.Type, StringComparison.Ordinal);
            Assert.Equal("InvalidOperationException", f.Type);
        });
    }

    [Fact]
    public void A_failing_callback_does_not_make_the_module_throw()
    {
        var data = new StoredData(new MessageStore(), [], (_, _) => throw new InvalidOperationException("callback"));

        Assert.Null(data.Load(Text));
    }

    private sealed class RemoveThrowsStore : IStoredDataStore
    {
        public StoredValue<T>? Load<T>(string key, int version) => null;

        public void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc)
        {
        }

        public void Remove(string key) => throw new IOException("SECRET");

        public bool Clear() => true;
    }

    private sealed class OutOfMemoryStore : IStoredDataStore
    {
        public StoredValue<T>? Load<T>(string key, int version) => throw new OutOfMemoryException();

        public void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc) => throw new OutOfMemoryException();

        public void Remove(string key) => throw new OutOfMemoryException();

        public bool Clear() => throw new OutOfMemoryException();
    }

    private sealed class MessageStore : IStoredDataStore
    {
        public StoredValue<T>? Load<T>(string key, int version) => throw new InvalidOperationException("SECRET");

        public void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc) => throw new InvalidOperationException("SECRET");

        public void Remove(string key) => throw new InvalidOperationException("SECRET");

        public bool Clear() => throw new InvalidOperationException("SECRET");
    }
}
