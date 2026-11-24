using System.Text;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Loadouts;

public enum DestinationScope
{
    Single,
    AllForCharacterAndServer,
    AllLoadouts,
}

public sealed class PendingFileWrite
{
    public PendingFileWrite(string path, string newText, Encoding encoding)
    {
        Path = path;
        NewText = newText;
        Encoding = encoding;
    }

    public string Path { get; }

    public string NewText { get; }

    public Encoding Encoding { get; }
}

public sealed class SectionApplyPlan
{
    private SectionApplyPlan(IReadOnlyList<PendingFileWrite> writes, IReadOnlyList<string> skipped, string? error)
    {
        Writes = writes;
        Skipped = skipped;
        Error = error;
    }

    public IReadOnlyList<PendingFileWrite> Writes { get; }

    public IReadOnlyList<string> Skipped { get; }

    public string? Error { get; }

    public static SectionApplyPlan Failed(string error) => new([], [], error);

    public static SectionApplyPlan Ready(IReadOnlyList<PendingFileWrite> writes, IReadOnlyList<string> skipped)
        => new(writes, skipped, null);
}

public static class DestinationSet
{
    public static IReadOnlyList<DiscoveredLoadout> Resolve(
        DiscoveredLoadout source,
        DestinationScope scope,
        DiscoveredLoadout? single,
        IReadOnlyList<DiscoveredLoadout> all,
        string? character = null,
        string? server = null)
    {
        var targetCharacter = string.IsNullOrWhiteSpace(character) ? source.Id.Character : character;
        var targetServer = string.IsNullOrWhiteSpace(server) ? source.Id.Server : server;
        return scope switch
        {
            DestinationScope.Single => single is null ? [] : [single],
            DestinationScope.AllForCharacterAndServer => all
                .Where(loadout => !SameLoadout(loadout, source)
                    && string.Equals(loadout.Id.Character, targetCharacter, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(loadout.Id.Server, targetServer, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            DestinationScope.AllLoadouts => all.Where(loadout => !SameLoadout(loadout, source)).ToList(),
            _ => [],
        };
    }

    public static bool SameLoadout(DiscoveredLoadout left, DiscoveredLoadout right)
    {
        return string.Equals(left.Id.Character, right.Id.Character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Id.Server, right.Id.Server, StringComparison.OrdinalIgnoreCase)
            && left.Id.Number == right.Id.Number;
    }

    /// <summary>
    /// Loadouts that receive window changes when saving to every destination.
    /// The loadout being edited is included. This list is every matching loadout file;
    /// class labels are not known here and must not remove entries.
    /// </summary>
    public static IReadOnlyList<DiscoveredLoadout> WindowSaveLoadouts(
        DiscoveredLoadout source,
        DestinationScope scope,
        IReadOnlyList<DiscoveredLoadout> all,
        string? character = null,
        string? server = null)
    {
        if (scope is not (DestinationScope.AllForCharacterAndServer or DestinationScope.AllLoadouts))
        {
            return [source];
        }

        var others = Resolve(source, scope, null, all, character, server);
        return others.Any(loadout => SameLoadout(loadout, source)) ? others : [source, .. others];
    }
}

public static class SectionApplier
{
    public static SectionApplyPlan Plan(
        IniSection section,
        LoadoutFileKind kind,
        IReadOnlyList<DiscoveredLoadout> destinations)
    {
        var writes = new List<PendingFileWrite>();
        var skipped = new List<string>();

        foreach (var destination in destinations)
        {
            var path = destination.FileFor(kind);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                skipped.Add($"{destination.Id.Title} has no {kind.ToString().ToLowerInvariant()} file.");
                continue;
            }

            var loaded = IniFile.Load(path);
            loaded.Document.ReplaceSection(section);
            var updated = loaded.Document.Write();
            if (updated == loaded.OriginalText)
            {
                continue;
            }

            writes.Add(new PendingFileWrite(path, updated, loaded.Encoding));
        }

        if (writes.Count == 0 && skipped.Count == destinations.Count && destinations.Count > 0)
        {
            return SectionApplyPlan.Failed(string.Join(Environment.NewLine, skipped));
        }

        return SectionApplyPlan.Ready(writes, skipped);
    }
}
