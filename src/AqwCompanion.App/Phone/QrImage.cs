using QRCoder;

namespace AqwCompanion.App.Phone;

/// <summary>Draws a QR code as PNG bytes: black modules on white with the quiet zone a scanner needs. The same text always gives the same bytes.</summary>
public static class QrImage
{
    public static byte[] Render(string text, int pixelsPerModule)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerModule, 1);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule, [0, 0, 0], [255, 255, 255], drawQuietZones: true);
    }
}
