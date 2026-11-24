using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Loadouts;

public enum SectionWriteMode
{
    Replace,
    MergeNoDeletion,
}

public sealed class SectionDelta
{
    public SectionDelta(string name, IniSection? edited)
    {
        Name = name;
        Edited = edited;
    }

    public string Name { get; }

    /// <summary>The editor's section, or null when the editor removed it.</summary>
    public IniSection? Edited { get; }
}

public sealed class SectionWrite
{
    public SectionWrite(string name, IniSection? section, SectionWriteMode mode)
    {
        Name = name;
        Section = section;
        Mode = mode;
    }

    public string Name { get; }

    public IniSection? Section { get; }

    public SectionWriteMode Mode { get; }

    public static SectionWrite Replace(SectionDelta delta)
        => new(delta.Name, delta.Edited, SectionWriteMode.Replace);

    public static SectionWrite Merge(SectionDelta delta)
        => new(delta.Name, delta.Edited, SectionWriteMode.MergeNoDeletion);
}

public static class SectionSave
{
    public static bool IsCharacterContent(string name)
        => name is "Socials" or "SpellLoadouts";

    /// <summary>
    /// An all-destination save copies window sections. Spell sets stay on the loadout that was edited.
    /// </summary>
    public static bool CopiesToEveryLoadout(LoadoutFileKind kind, string sectionName)
        => kind == LoadoutFileKind.Ui && sectionName != "SpellLoadouts";

    /// <summary>Spell sets are written only when that section changed, and only on the loadout that was edited when the destination is every loadout.</summary>
    public static bool IncludeSpellLoadouts(bool destinationIsAll, bool isEditedLoadout)
        => !destinationIsAll || isEditedLoadout;

    public static IReadOnlyList<SectionDelta> ChangedSections(IniDocument baseline, IniDocument edited)
    {
        var changes = new List<SectionDelta>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in edited.Sections)
        {
            if (!seen.Add(section.Name))
            {
                continue;
            }

            var previous = baseline.Find(section.Name);
            if (previous is null || !SameEntries(previous, section))
            {
                changes.Add(new SectionDelta(section.Name, section.Clone()));
            }
        }

        foreach (var section in baseline.Sections)
        {
            if (seen.Contains(section.Name) || edited.Find(section.Name) is not null)
            {
                continue;
            }

            if (seen.Add(section.Name))
            {
                changes.Add(new SectionDelta(section.Name, null));
            }
        }

        return changes;
    }

    /// <summary>
    /// Replaces the named sections on <paramref name="destination"/> with the same sections from <paramref name="source"/>.
    /// A name that the source does not have is removed. Returns whether the destination changed.
    /// </summary>
    public static bool CopySections(IniDocument source, IniDocument destination, IEnumerable<string> names)
    {
        var changed = false;
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var section = source.Find(name);
            if (section is null)
            {
                if (destination.Find(name) is not null)
                {
                    destination.Sections.RemoveAll(item => item.Name == name);
                    changed = true;
                }

                continue;
            }

            var current = destination.Find(name);
            if (current is not null && SameEntries(current, section))
            {
                continue;
            }

            destination.ReplaceSection(section);
            changed = true;
        }

        return changed;
    }

    public static bool SameEntries(IniSection left, IniSection right)
    {
        if (left.Entries.Count != right.Entries.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Entries.Count; i++)
        {
            var a = left.Entries[i];
            var b = right.Entries[i];
            if (a.HasEquals != b.HasEquals || a.Key != b.Key || a.Value != b.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies section writes to a character or UI file that was just read from disk.
    /// Sections that are not listed stay as they are in <paramref name="diskText"/>.
    /// </summary>
    public static string Apply(string diskText, IReadOnlyList<SectionWrite> writes)
    {
        var document = IniDocument.Parse(diskText);
        foreach (var write in writes)
        {
            if (write.Mode == SectionWriteMode.MergeNoDeletion)
            {
                if (write.Section is not null)
                {
                    MergeSection(document, write.Section);
                }

                continue;
            }

            if (write.Section is null)
            {
                document.Sections.RemoveAll(section => section.Name == write.Name);
                continue;
            }

            document.ReplaceSection(write.Section);
        }

        return document.Write();
    }

    /// <summary>
    /// Copies disk updates for sections the snapshot had not edited, and keeps sections the snapshot had edited.
    /// </summary>
    public static string RebaseUnchanged(string snapshotText, string beforeSaveText, string afterSaveText)
    {
        var snapshot = IniDocument.Parse(snapshotText);
        var before = IniDocument.Parse(beforeSaveText);
        var after = IniDocument.Parse(afterSaveText);
        foreach (var section in snapshot.Sections.ToList())
        {
            var prior = before.Find(section.Name);
            if (prior is null || !SameEntries(prior, section))
            {
                continue;
            }

            var saved = after.Find(section.Name);
            if (saved is null)
            {
                snapshot.Sections.Remove(section);
            }
            else if (!SameEntries(section, saved))
            {
                section.ReplaceEntries(saved.Entries);
            }
        }

        foreach (var section in after.Sections)
        {
            if (snapshot.Find(section.Name) is not null || before.Find(section.Name) is not null)
            {
                continue;
            }

            snapshot.ReplaceSection(section);
        }

        snapshot.NewLine = after.NewLine;
        return snapshot.Write();
    }

    /// <summary>
    /// Text for the oldest undo step: sections edited since <paramref name="originalText"/> go back to that text,
    /// and every other section stays as it is in <paramref name="afterSaveText"/>.
    /// </summary>
    public static string UndoBaseline(string originalText, string afterSaveText, IniDocument edited)
    {
        var original = IniDocument.Parse(originalText);
        var result = IniDocument.Parse(afterSaveText);
        foreach (var change in ChangedSections(original, edited))
        {
            var prior = original.Find(change.Name);
            if (prior is null)
            {
                result.Sections.RemoveAll(section => section.Name == change.Name);
            }
            else
            {
                result.ReplaceSection(prior);
            }
        }

        return result.Write();
    }

    public static string CopyFileName(string role, LoadoutFileKind kind, string originalFileName)
    {
        var which = kind == LoadoutFileKind.Character ? "Character" : "UI";
        return $"{role} {which} - {originalFileName}";
    }

    private static void MergeSection(IniDocument document, IniSection edited)
    {
        var existing = document.Find(edited.Name);
        if (existing is null)
        {
            document.ReplaceSection(edited);
            return;
        }

        foreach (var entry in edited.Entries)
        {
            if (entry.HasEquals)
            {
                existing.Set(entry.Key, entry.Value);
                continue;
            }

            if (!existing.Entries.Any(item => !item.HasEquals && item.Key == entry.Key))
            {
                existing.Entries.Add(entry.Clone());
            }
        }
    }
}
