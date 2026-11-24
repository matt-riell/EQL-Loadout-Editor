using System.Windows.Media;
using EqlLoadouts.Core.Content;

namespace EqlLoadouts.App.Models;

public sealed class SuggestedGemFace : ISpellDetails
{
    public static SuggestedGemFace Empty(int number) => new()
    {
        Number = number,
        SpellId = -1,
        SpellName = "Empty",
        Name = "Empty",
        LoadoutLevels = string.Empty,
        UsableTip = "Empty",
        Description = string.Empty,
        EffectText = string.Empty,
        EffectLines = [],
        Details = [],
    };

    public int Number { get; init; }

    public int SpellId { get; init; }

    public string SpellName { get; init; } = string.Empty;

    public bool IsEmpty => SpellId < 0;

    public bool CanClear => false;

    public bool HasUpgrade => false;

    public ImageSource? Icon { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string EffectText { get; init; } = string.Empty;

    public IReadOnlyList<SpellDetailLine> EffectLines { get; init; } = [];

    public IReadOnlyList<SpellDetailLine> Details { get; init; } = [];

    public string LoadoutLevels { get; init; } = string.Empty;

    public bool HasLevels => LoadoutLevels.Length > 0;

    public string PanelLevel => LoadoutLevels;

    public bool IsUnusable { get; init; }

    public string UsableTip { get; init; } = string.Empty;
}

public sealed class SuggestedSpellPage : ObservableObject
{
    public SuggestedSpellPage(SpellSetKind kind, string name, IReadOnlyList<SuggestedGemFace> gems)
    {
        Kind = kind;
        Name = name;
        Gems = gems;
    }

    public SpellSetKind Kind { get; }

    public string Name { get; }

    public IReadOnlyList<SuggestedGemFace> Gems { get; private set; }

    public void Replace(IReadOnlyList<SuggestedGemFace> gems)
    {
        Gems = gems;
        Raise(nameof(Gems));
    }
}

public sealed class GemSuggestion
{
    public GemSuggestion(int gemNumber, int spellId, string text)
    {
        GemNumber = gemNumber;
        SpellId = spellId;
        Text = text;
    }

    public int GemNumber { get; }

    public int SpellId { get; }

    public string Text { get; }
}
