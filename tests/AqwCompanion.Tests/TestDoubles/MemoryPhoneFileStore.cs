using AqwCompanion.Phone.Devices;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class MemoryPhoneFileStore(PhoneFile? initial = null) : IPhoneFileStore
{
    public PhoneFile Saved { get; private set; } = initial ?? PhoneFile.Empty;
    public int Saves;
    public int Deletes;

    public PhoneFile Load() => Saved;

    public void Save(PhoneFile file)
    {
        Saved = file;
        Saves++;
    }

    public Exception? DeleteThrows { get; set; }

    public void Delete()
    {
        if (DeleteThrows is not null)
        {
            throw DeleteThrows;
        }

        Saved = PhoneFile.Empty;
        Deletes++;
    }
}
