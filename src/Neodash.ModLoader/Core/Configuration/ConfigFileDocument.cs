using System.Text;

namespace Neodash_Mod_Loader;

/// <summary>Represents a configuration file together with its original byte representation.</summary>
public sealed class ConfigFileDocument
{
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private ConfigFileDocument(string path, byte[] originalBytes, string text, Encoding encoding, byte[] preamble)
    {
        Path = path;
        OriginalBytes = originalBytes;
        Text = text;
        _encoding = encoding;
        _preamble = preamble;
    }

    public string Path { get; }
    public byte[] OriginalBytes { get; }
    public string Text { get; }

    public static ConfigFileDocument Load(string path, byte[] originalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(originalBytes);

        byte[] bytes = originalBytes.ToArray();
        (Encoding encoding, int preambleLength) = DetectEncoding(bytes);
        try
        {
            string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new ConfigFileDocument(path, bytes, text, encoding, bytes[..preambleLength]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException("This configuration has invalid text bytes. Open its folder to edit it externally.", exception);
        }
    }

    /// <summary>Encodes edited text with the original encoding and BOM, rejecting data loss.</summary>
    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            byte[] payload = _encoding.GetBytes(text);
            byte[] result = GC.AllocateUninitializedArray<byte>(_preamble.Length + payload.Length);
            _preamble.CopyTo(result, 0);
            payload.CopyTo(result, _preamble.Length);
            return result;
        }
        catch (EncoderFallbackException exception)
        {
            throw new IOException($"This configuration uses {_encoding.WebName}, which cannot represent one or more edits. Open its folder to edit it with a compatible editor.", exception);
        }
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return (new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return (new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), 2);

        Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            _ = utf8.GetString(bytes);
            return (utf8, 0);
        }
        catch (DecoderFallbackException)
        {
            return (GetCurrentAnsiEncoding(), 0);
        }
    }

    private static Encoding GetCurrentAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            int codePage = Encoding.GetEncoding(0).CodePage;
            if (codePage <= 0 || codePage == Encoding.UTF8.CodePage)
                throw new NotSupportedException("The current Windows ANSI code page is unavailable.");

            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new IOException("This configuration is not valid UTF-8 and its legacy Windows text encoding is unavailable. Open its folder to edit it externally.", exception);
        }
    }

}
