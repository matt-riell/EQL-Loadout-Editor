namespace EqlLoadouts.Core.Ini;

public sealed class IniEntry
{
    public IniEntry(string key, string value, bool hasEquals = true)
    {
        Key = key;
        Value = value;
        HasEquals = hasEquals;
    }

    public string Key { get; set; }

    public string Value { get; set; }

    public bool HasEquals { get; set; }

    public IniEntry Clone() => new(Key, Value, HasEquals);
}

public sealed class IniSection
{
    public IniSection(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<IniEntry> Entries { get; } = [];

    public string? Get(string key)
    {
        return Entries.FirstOrDefault(entry => entry.HasEquals && entry.Key == key)?.Value;
    }

    public void Set(string key, string value)
    {
        var existing = Entries.FirstOrDefault(entry => entry.HasEquals && entry.Key == key);
        if (existing is null)
        {
            Entries.Add(new IniEntry(key, value));
            return;
        }

        existing.Value = value;
    }

    public void Remove(string key)
    {
        Entries.RemoveAll(entry => entry.HasEquals && entry.Key == key);
    }

    public void ReplaceEntries(IEnumerable<IniEntry> entries)
    {
        Entries.Clear();
        Entries.AddRange(entries.Select(entry => entry.Clone()));
    }

    public IniSection Clone()
    {
        var copy = new IniSection(Name);
        copy.ReplaceEntries(Entries);
        return copy;
    }
}

public sealed class IniDocument
{
    public List<IniSection> Sections { get; } = [];

    public string NewLine { get; set; } = "\r\n";

    public IniSection? Find(string name)
    {
        return Sections.FirstOrDefault(section => section.Name == name);
    }

    public void ReplaceSection(IniSection replacement)
    {
        var clone = replacement.Clone();
        var index = Sections.FindIndex(section => section.Name == replacement.Name);
        if (index >= 0)
        {
            Sections[index] = clone;
            return;
        }

        Sections.Add(clone);
    }

    public void ReplaceAll(IniDocument other)
    {
        NewLine = other.NewLine;
        Sections.Clear();
        Sections.AddRange(other.Sections.Select(section => section.Clone()));
    }

    public string Write()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var section in Sections)
        {
            builder.Append('[').Append(section.Name).Append(']').Append(NewLine);
            foreach (var entry in section.Entries)
            {
                if (entry.HasEquals)
                {
                    builder.Append(entry.Key).Append('=').Append(entry.Value);
                }
                else
                {
                    builder.Append(entry.Key);
                }

                builder.Append(NewLine);
            }
        }

        if (builder.Length == 0)
        {
            return NewLine;
        }

        return builder.ToString();
    }

    public static IniDocument Parse(string text)
    {
        var document = new IniDocument
        {
            NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
        };

        IniSection? current = null;
        foreach (var line in SplitLines(text))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                current = new IniSection(line[1..^1]);
                document.Sections.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                current.Entries.Add(new IniEntry(line, string.Empty, hasEquals: false));
                continue;
            }

            current.Entries.Add(new IniEntry(line[..separator], line[(separator + 1)..]));
        }

        return document;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var end = i;
            if (end > start && text[end - 1] == '\r')
            {
                end--;
            }

            yield return text[start..end];
            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
