using System.Buffers.Binary;

namespace AqwCompanion.Tests.Architecture;

/// <summary>
/// The app icon is the project logo. These tests keep the two asset files well formed: the .ico carries every standard size
/// (the Windows shell picks the one it needs) and the .png is the 256 px window and tray icon.
/// </summary>
public class AppIconTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void The_ico_holds_every_standard_size_as_a_png()
    {
        var bytes = File.ReadAllBytes(AssetPath("app.ico"));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + (i * 16), 16);
            sizes.Add(entry[0] == 0 ? 256 : entry[0]);
            Assert.Equal(sizes[^1], entry[1] == 0 ? 256 : entry[1]);
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.True(offset + length <= bytes.Length);
            Assert.True(bytes.AsSpan(offset, length)[..8].SequenceEqual(PngSignature), "Each entry is a PNG image.");
        }

        Assert.Equal([16, 32, 48, 64, 128, 256], sizes.Order());
    }

    [Fact]
    public void The_png_is_the_256_pixel_window_icon()
    {
        var bytes = File.ReadAllBytes(AssetPath("app.png"));

        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(PngSignature));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
    }

    private static string AssetPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AqwCompanion.App", "Assets", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(name + " not found above " + AppContext.BaseDirectory);
    }
}
