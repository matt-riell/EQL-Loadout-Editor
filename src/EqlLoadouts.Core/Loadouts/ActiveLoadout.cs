namespace EqlLoadouts.Core.Loadouts;

/// <summary>One loadout file the game wrote. Character and UI files for the same loadout are separate entries.</summary>
public readonly record struct LoadoutFileWrite(LoadoutId Id, DateTime Written);

public static class ActiveLoadout
{
    /// <summary>
    /// The loadout whose newest file is strictly newer than every other loadout.
    /// A tie, or no files, is not a match.
    /// </summary>
    public static LoadoutId? Newest(IEnumerable<LoadoutFileWrite> writes)
    {
        var newest = new Dictionary<LoadoutId, DateTime>();
        foreach (var write in writes)
        {
            if (newest.TryGetValue(write.Id, out var known))
            {
                if (write.Written > known)
                {
                    newest[write.Id] = write.Written;
                }
            }
            else
            {
                newest[write.Id] = write.Written;
            }
        }

        LoadoutId? winner = null;
        DateTime? written = null;
        var tied = false;
        foreach (var pair in newest)
        {
            if (written is null || pair.Value > written)
            {
                winner = pair.Key;
                written = pair.Value;
                tied = false;
            }
            else if (pair.Value == written)
            {
                tied = true;
            }
        }

        return tied ? null : winner;
    }
}
