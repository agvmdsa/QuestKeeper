using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.Phone;

public class QrImageTests
{
    private const string Link = "https://pc.tail1.ts.net/#t=abcdEFGH0123456789-_abcdEFGH0123456789-_abc";

    [Fact]
    public void A_typical_link_gives_a_png()
    {
        var png = QrImage.Render(Link, 8);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png.Take(8));
    }

    [Fact]
    public void Equal_input_gives_equal_bytes_and_different_input_differs()
    {
        Assert.Equal(QrImage.Render(Link, 8), QrImage.Render(Link, 8));
        Assert.NotEqual(QrImage.Render(Link, 8), QrImage.Render(Link + "x", 8));
    }
}
