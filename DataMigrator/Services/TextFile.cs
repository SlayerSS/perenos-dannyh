using System.Runtime.InteropServices;
using System.Text;

namespace DataMigrator.Services;

internal static class TextFile
{
    public static string ReadAuto(string path)
    {
        var data = File.ReadAllBytes(path);
        return Decode(data);
    }

    // 1C writes ibases.v8i as UTF-8, UTF-16 (usually with BOM), or ANSI Windows-1251 without a BOM.
    public static string Decode(byte[] data)
    {
        if (data.Length == 0)
            return "";
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

        var sample = Math.Min(data.Length, 256);
        if (sample >= 8)
        {
            var zeros = 0;
            var pairs = 0;
            for (var i = 1; i < sample; i += 2)
            {
                pairs++;
                if (data[i] == 0)
                    zeros++;
            }
            if (pairs > 0 && zeros * 2 > pairs)
                return Encoding.Unicode.GetString(data);
        }

        if (IsUtf8(data))
            return Encoding.UTF8.GetString(data);
        return Ansi1251(data);
    }

    private static bool IsUtf8(byte[] data)
    {
        try
        {
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            enc.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string Ansi1251(byte[] data)
    {
        var chars = new char[data.Length];
        var n = MultiByteToWideChar(1251, 0, data, data.Length, chars, chars.Length);
        if (n <= 0)
            return Encoding.Latin1.GetString(data);
        return new string(chars, 0, n);
    }

    [DllImport("kernel32.dll")]
    private static extern int MultiByteToWideChar(
        uint codePage,
        uint dwFlags,
        byte[] lpMultiByteStr,
        int cbMultiByte,
        [Out] char[] lpWideCharStr,
        int cchWideChar);
}
