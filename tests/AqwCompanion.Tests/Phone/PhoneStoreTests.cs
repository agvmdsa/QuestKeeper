using AqwCompanion.App;
using AqwCompanion.App.Phone;
using AqwCompanion.Phone.Devices;

namespace AqwCompanion.Tests.Phone;

public sealed class PhoneStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_loads_empty()
    {
        Assert.Equal(PhoneFile.Empty, new PhoneStore(_paths.PhonePath).Load());
    }

    [Fact]
    public void A_saved_file_with_one_device_round_trips()
    {
        var t = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var file = new PhoneFile(true, [new StoredDevice("id1", "Phone", "ABCD", t, t.AddMinutes(3))]);
        new PhoneStore(_paths.PhonePath).Save(file);

        var loaded = new PhoneStore(_paths.PhonePath).Load();

        Assert.True(loaded.Enabled);
        Assert.Equal(file.Devices, loaded.Devices);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("")]
    public void A_corrupt_or_empty_object_file_loads_empty(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.PhonePath, content);

        var loaded = new PhoneStore(_paths.PhonePath).Load();

        Assert.False(loaded.Enabled);
        Assert.Empty(loaded.Devices);
    }

    [Fact]
    public void Saving_and_deleting_never_throw_when_the_folder_cannot_be_written()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(Path.Combine(_paths.Root, "blocker"), "x");
        var store = new PhoneStore(Path.Combine(_paths.Root, "blocker", "phone.json"));

        Assert.Null(Record.Exception(() => store.Save(PhoneFile.Empty)));
        Assert.Null(Record.Exception(store.Delete));
        Assert.Equal(PhoneFile.Empty, store.Load());
    }

    [Fact]
    public void A_successful_save_leaves_no_temp_file()
    {
        new PhoneStore(_paths.PhonePath).Save(new PhoneFile(true, []));

        Assert.Equal(["phone.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void Delete_removes_the_file_and_is_fine_when_it_is_already_gone()
    {
        var store = new PhoneStore(_paths.PhonePath);
        store.Save(new PhoneFile(true, []));

        store.Delete();
        Assert.False(File.Exists(_paths.PhonePath));
        Assert.Null(Record.Exception(store.Delete));
    }

    [Fact]
    public void Delete_also_removes_a_leftover_temp_file()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.PhonePath, "{}");
        File.WriteAllText(_paths.PhonePath + ".tmp", "{}");

        new PhoneStore(_paths.PhonePath).Delete();

        Assert.Empty(Directory.GetFiles(_paths.Root));
    }
}
