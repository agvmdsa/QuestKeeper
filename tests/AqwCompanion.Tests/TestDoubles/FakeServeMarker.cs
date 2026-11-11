using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeServeMarker(bool exists = false) : IServeMarker
{
    public bool Present { get; set; } = exists;

    public int Creates { get; private set; }

    public int Deletes { get; private set; }

    public bool Exists() => Present;

    public void Create()
    {
        Creates++;
        Present = true;
    }

    public void Delete()
    {
        Deletes++;
        Present = false;
    }
}
