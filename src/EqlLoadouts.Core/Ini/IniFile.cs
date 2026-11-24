using System.Text;

namespace EqlLoadouts.Core.Ini;

public sealed class LoadedIniFile
{
    public LoadedIniFile(string path, string originalText, Encoding encoding, IniDocument document)
    {
        Path = path;
        OriginalText = originalText;
        Encoding = encoding;
        Document = document;
        _savedText = document.Write();
    }

    private string _savedText;

    public string Path { get; }

    public string OriginalText { get; private set; }

    public Encoding Encoding { get; }

    public IniDocument Document { get; }

    public bool HasChanges => Document.Write() != _savedText;

    public void MarkSaved(string writtenText)
    {
        OriginalText = writtenText;
        _savedText = writtenText;
    }
}

public static class IniFile
{
    public static LoadedIniFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (text, encoding) = Decode(bytes);
        return new LoadedIniFile(path, text, encoding, IniDocument.Parse(text));
    }

    public static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (DecoderFallbackException)
        {
            var ansi = Encoding.GetEncoding(1252);
            return (ansi.GetString(bytes), ansi);
        }
    }
}
