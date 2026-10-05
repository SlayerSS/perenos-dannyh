using System.Globalization;

namespace DataMigrator.Services;

internal static class ByteSize
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Format(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
            return $"{bytes.ToString("N0", Ru)} Б";
        return $"{value.ToString("0.#", Ru)} {units[unit]}";
    }

    public static string Files(int count, bool partial)
    {
        var noun = Plural(count, "файл", "файла", "файлов");
        var number = count.ToString("N0", Ru);
        return partial ? $"более {number} {noun}" : $"{number} {noun}";
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var nAbs = Math.Abs(n) % 100;
        var n1 = nAbs % 10;
        if (nAbs is > 10 and < 20)
            return many;
        if (n1 is > 1 and < 5)
            return few;
        if (n1 == 1)
            return one;
        return many;
    }
}
