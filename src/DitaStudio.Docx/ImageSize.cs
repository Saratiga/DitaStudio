namespace DitaStudio.Docx;

/// <summary>Определяет размер изображения в EMU (единицы OOXML, 914400 на дюйм) — без внешних
/// библиотек, разбором заголовков PNG/JPEG/GIF/BMP напрямую. Атрибуты DITA width/height (если
/// заданы) имеют приоритет над реальным размером файла.</summary>
internal static class ImageSize
{
    private const long EmuPerInch = 914400;
    private const long EmuPerPixelAt96Dpi = EmuPerInch / 96;
    private const long DefaultWidthEmu = 400L * EmuPerPixelAt96Dpi;
    private const long DefaultHeightEmu = 300L * EmuPerPixelAt96Dpi;
    private const long MaxWidthEmu = 6L * EmuPerInch; // не даём картинке быть шире печатной полосы

    public static (long Width, long Height) ReadEmuSize(string path, string? ditaWidth, string? ditaHeight)
    {
        var (pixelWidth, pixelHeight) = ReadPixelSize(path);

        long width = pixelWidth > 0 ? pixelWidth * EmuPerPixelAt96Dpi : DefaultWidthEmu;
        long height = pixelHeight > 0 ? pixelHeight * EmuPerPixelAt96Dpi : DefaultHeightEmu;

        var attrWidth = ParseMeasurement(ditaWidth);
        var attrHeight = ParseMeasurement(ditaHeight);
        if (attrWidth is not null && attrHeight is not null)
        {
            return (attrWidth.Value, attrHeight.Value);
        }

        if (attrWidth is not null && pixelWidth > 0 && pixelHeight > 0)
        {
            var scale = (double)attrWidth.Value / width;
            return (attrWidth.Value, (long)(height * scale));
        }

        if (attrHeight is not null && pixelWidth > 0 && pixelHeight > 0)
        {
            var scale = (double)attrHeight.Value / height;
            return ((long)(width * scale), attrHeight.Value);
        }

        if (width > MaxWidthEmu)
        {
            var scale = (double)MaxWidthEmu / width;
            width = MaxWidthEmu;
            height = (long)(height * scale);
        }

        return (width, height);
    }

    /// <summary>Атрибут DITA width/height: число (пиксели) либо число с суффиксом px/in/cm/pt.</summary>
    private static long? ParseMeasurement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        var numberLength = 0;
        while (numberLength < value.Length && (char.IsDigit(value[numberLength]) || value[numberLength] is '.' or '-'))
        {
            numberLength++;
        }

        if (numberLength == 0 || !double.TryParse(value[..numberLength], System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var unit = value[numberLength..].Trim().ToLowerInvariant();
        return unit switch
        {
            "" or "px" => (long)(number * EmuPerPixelAt96Dpi),
            "in" => (long)(number * EmuPerInch),
            "cm" => (long)(number * EmuPerInch / 2.54),
            "mm" => (long)(number * EmuPerInch / 25.4),
            "pt" => (long)(number * EmuPerInch / 72),
            _ => (long)(number * EmuPerPixelAt96Dpi)
        };
    }

    private static (int Width, int Height) ReadPixelSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            var read = stream.Read(header);
            if (read < 24)
            {
                return (0, 0);
            }

            // PNG: 8-byte signature, затем IHDR chunk с шириной/высотой (big-endian) на смещении 16.
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                var width = ReadInt32BigEndian(header[16..20]);
                var height = ReadInt32BigEndian(header[20..24]);
                return (width, height);
            }

            // GIF: сигнатура "GIF8", затем ширина/высота (little-endian) на смещении 6.
            if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
            {
                var width = header[6] | (header[7] << 8);
                var height = header[8] | (header[9] << 8);
                return (width, height);
            }

            // BMP: сигнатура "BM", ширина/высота (little-endian, signed) на смещении 18.
            if (header[0] == 'B' && header[1] == 'M')
            {
                var width = BitConverter.ToInt32(header[18..22]);
                var height = Math.Abs(BitConverter.ToInt32(header[22..26]));
                return (width, height);
            }

            // JPEG: ищем маркер SOFn (0xFFC0..0xFFCF, кроме C4/C8/CC) — высота/ширина в его теле.
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                return ReadJpegSize(stream, header);
            }
        }
        catch
        {
            // не удалось определить размер — используем размер по умолчанию
        }

        return (0, 0);
    }

    private static (int Width, int Height) ReadJpegSize(Stream stream, Span<byte> alreadyRead)
    {
        stream.Position = 2; // после SOI
        Span<byte> marker = stackalloc byte[4];
        while (true)
        {
            if (stream.Read(marker[..2]) < 2 || marker[0] != 0xFF)
            {
                return (0, 0);
            }

            var kind = marker[1];
            if (kind == 0xD8 || kind == 0x01 || (kind >= 0xD0 && kind <= 0xD7))
            {
                continue;
            }

            if (stream.Read(marker[..2]) < 2)
            {
                return (0, 0);
            }

            var segmentLength = (marker[0] << 8) | marker[1];
            var isSof = kind is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isSof)
            {
                Span<byte> sof = stackalloc byte[5];
                if (stream.Read(sof) < 5)
                {
                    return (0, 0);
                }

                var height = (sof[1] << 8) | sof[2];
                var width = (sof[3] << 8) | sof[4];
                return (width, height);
            }

            stream.Position += segmentLength - 2;
        }
    }

    private static int ReadInt32BigEndian(Span<byte> b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
}
