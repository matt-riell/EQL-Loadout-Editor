using System.Globalization;
using System.Collections.ObjectModel;
using EqlLoadouts.App.Models;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App;

public sealed partial class MainViewModel
{
    private SocialRow? _selectedSocial;
    private SocialSide? _selectedSocialSide;
    private SocialMacroLink? _activeSocialLink;
    private List<SocialSlot>? _sourceBaseline;
    private List<SocialSlot>? _destinationBaseline;
    private string _socialEditorCaption = "Select a macro to edit its name and lines.";

    public ObservableCollection<SocialPageGroup> SourceSocialPages { get; } = [];

    public ObservableCollection<SocialPageGroup> DestinationSocialPages { get; } = [];

    public ObservableCollection<QueuedSocialCopy> QueuedSocialCopies { get; } = [];

    public ObservableCollection<SocialChange> SocialChanges { get; } = [];

    public SocialRow? SelectedSocial
    {
        get => _selectedSocial;
        private set
        {
            if (ReferenceEquals(_selectedSocial, value))
            {
                return;
            }

            _selectedSocial = value;
            Raise(nameof(SelectedSocial));
            Raise(nameof(HasSocialSelection));
        }
    }

    public bool HasSocialSelection => SelectedSocial is not null;

    public bool HasQueuedSocialCopies => QueuedSocialCopies.Count > 0;

    public bool HasSocialChanges => SocialChanges.Count > 0;

    public bool CanEditSocial => _selectedSocialSide == SocialSide.Destination;

    public bool IsSocialReadOnly => !CanEditSocial;

    public string ApplySocialCopiesText => QueuedSocialCopies.Count switch
    {
        0 => "Apply copies",
        1 => "Apply 1 copy",
        _ => string.Create(CultureInfo.InvariantCulture, $"Apply {QueuedSocialCopies.Count} copies"),
    };

    public string SocialEditorCaption
    {
        get => _socialEditorCaption;
        private set => SetProperty(ref _socialEditorCaption, value);
    }

    public string SourceSocialHeading => SocialHeading("Source", _sourceLoadout);

    public string DestinationSocialHeading => SocialHeading("Destination", _destinationLoadout);

    public SocialMacroLink? ActiveSocialLink => _activeSocialLink;

    public event EventHandler? SocialLinkChanged;

    public void SelectSocialButton(SocialButton button)
    {
        var editingDestination = button.Side == SocialSide.Destination;
        if (!editingDestination && button.IsEmpty)
        {
            return;
        }

        var section = FindSocialSection(editingDestination);
        var copied = editingDestination ? SourceDetailsForCopy(button) : null;
        if (section is null && editingDestination)
        {
            section = EnsureSocialSection(editingDestination);
        }
        else if (section is null && (copied is not null || !button.IsEmpty))
        {
            section = EnsureSocialSection(editingDestination);
        }

        if (section is null)
        {
            _selectedSocialSide = null;
            SelectedSocial = null;
            SocialEditorCaption = "This source loadout has no macros.";
            RaiseSocialEditorMode();
            UpdateSocialFlags();
            return;
        }

        var row = new SocialRow(
            button.Page,
            button.Slot,
            section,
            () => OnSocialEdited(button.Side, button.Page, button.Slot),
            readOnly: !editingDestination,
            allowEdit: EnsureContentClasses);
        if (copied is not null)
        {
            row.Load(copied.Details);
        }
        else
        {
            var existing = SocialButtons.ForDisplay(section).FirstOrDefault(slot => slot.Page == button.Page && slot.Slot == button.Slot);
            if (existing is not null && !existing.IsEmpty)
            {
                row.Load(existing);
            }
        }

        _selectedSocialSide = button.Side;
        SelectedSocial = row;
        RaiseSocialEditorMode();
        UpdateSocialFlags();
    }

    public void ClearDestinationSocial(int page, int slot)
    {
        if (!EnsureContentClasses())
        {
            return;
        }

        var section = EnsureSocialSection();
        if (section is null)
        {
            Status = "The destination loadout has no file for macros.";
            return;
        }

        RemoveQueuesForSlot(page, slot);
        SocialButtons.ClearSlot(section, page, slot);
        if (!MarkEdited("clear 1 macro"))
        {
            return;
        }
        RefreshDestinationButton(page, slot);
        if (_selectedSocialSide == SocialSide.Destination && SelectedSocial is { } row && row.Page == page && row.Slot == slot)
        {
            row.Load(new SocialSlot(page, slot));
        }

        UpdateSocialFlags();
        Status = string.Create(CultureInfo.InvariantCulture, $"Cleared page {page}, button {slot}. Save to write the file.");
    }

    public void RemoveSocialChange(SocialChange change)
    {
        if (!EnsureContentClasses())
        {
            return;
        }

        var removedQueue = RemoveQueuesForSlot(change.Page, change.Slot);
        RestoreDestinationSlot(change.Page, change.Slot);
        if (removedQueue)
        {
            RaiseQueueProperties();
        }

        if (!MarkEdited($"revert {change.Caption}"))
        {
            return;
        }
        RefreshDestinationButton(change.Page, change.Slot);
        if (_selectedSocialSide == SocialSide.Destination && SelectedSocial is { } row && row.Page == change.Page && row.Slot == change.Slot)
        {
            var restored = SocialButtons.ForDisplay(FindSocialSection(destination: true))
                .FirstOrDefault(slot => slot.Page == change.Page && slot.Slot == change.Slot);
            row.Load(restored ?? new SocialSlot(change.Page, change.Slot));
        }

        UpdateSocialFlags();
    }

    public void ClearSocialChanges()
    {
        if (SocialChanges.Count == 0 && QueuedSocialCopies.Count == 0)
        {
            return;
        }

        if (!EnsureContentClasses())
        {
            return;
        }

        var slots = SocialChanges.Select(change => (change.Page, change.Slot)).Distinct().ToList();
        QueuedSocialCopies.Clear();
        RaiseQueueProperties();
        foreach (var (page, slot) in slots)
        {
            RestoreDestinationSlot(page, slot);
        }

        if (!MarkEdited("revert macro changes"))
        {
            return;
        }
        BuildSocialBoard();
        Status = "Reverted the destination macro changes.";
    }

    public void SelectSocialChange(SocialChange change)
    {
        var button = FindSocialButton(SocialSide.Destination, change.Page, change.Slot);
        if (button is not null)
        {
            SelectSocialButton(button);
        }
    }

    public void SelectQueuedSocial(QueuedSocialCopy copy)
    {
        var button = FindSocialButton(SocialSide.Destination, copy.DestinationPage, copy.DestinationSlot);
        if (button is not null)
        {
            SelectSocialButton(button);
        }
    }

    public void QueueSocialCopy(int sourcePage, int sourceSlot, int destinationPage, int destinationSlot, string? destinationName = null)
    {
        TryQueueSocialCopy(sourcePage, sourceSlot, destinationPage, destinationSlot, destinationName, announce: true, commit: true, out _);
    }

    public void QueueSocialDrop(
        int sourcePage,
        int sourceSlot,
        int destinationPage,
        int destinationSlot,
        Func<IReadOnlyList<SocialButton>, SocialConflictChoice> ask,
        Func<string, string?, string?> askName)
    {
        var clashes = DestinationCommandClashes(sourcePage, sourceSlot);
        if (clashes.Count == 0)
        {
            QueueSocialCopy(sourcePage, sourceSlot, destinationPage, destinationSlot);
            return;
        }

        switch (ask(clashes))
        {
            case SocialConflictChoice.Overwrite:
                QueueSocialOverwrites(sourcePage, sourceSlot, clashes);
                return;
            case SocialConflictChoice.NewButton:
                break;
            default:
                Status = "Left the macro where it is.";
                return;
        }

        var source = FindSocialButton(SocialSide.Source, sourcePage, sourceSlot);
        var suggested = SuggestSocialName(source?.Name ?? string.Empty);
        string? error = null;
        while (true)
        {
            var name = askName(suggested, error);
            if (string.IsNullOrWhiteSpace(name))
            {
                Status = "Left the macro where it is.";
                return;
            }

            error = SocialRenameProblem(name, sourcePage, sourceSlot);
            if (error is not null)
            {
                suggested = name.Trim();
                continue;
            }

            var spot = PlaceForNewSocialButton(destinationPage, destinationSlot);
            QueueSocialCopy(sourcePage, sourceSlot, spot.Page, spot.Slot, name);
            return;
        }
    }

    public IReadOnlyList<SocialButton> DestinationCommandClashes(int sourcePage, int sourceSlot)
    {
        var source = FindSocialButton(SocialSide.Source, sourcePage, sourceSlot);
        if (source is null || source.IsEmpty || SocialBoardsShareSection || string.IsNullOrWhiteSpace(source.Name))
        {
            return [];
        }

        var slot = source.ToSlot();
        return DestinationSocialPages
            .SelectMany(group => group.Buttons)
            .Where(button => !button.IsEmpty
                && SocialLayout.SameName(button.Name, slot.Name)
                && !SocialLayout.SameCommands(button.ToSlot(), slot))
            .ToList();
    }

    public string SuggestSocialName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "Macro";
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var button in DestinationSocialPages.SelectMany(group => group.Buttons))
        {
            var existing = button.Name.Trim();
            if (existing.Length > 0)
            {
                taken.Add(existing);
            }
        }

        if (!taken.Contains(trimmed))
        {
            return trimmed;
        }

        for (var index = 2; index < 100; index++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{trimmed} {index}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return trimmed;
    }

    public string? SocialRenameProblem(string name, int sourcePage, int sourceSlot)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "Enter a name for the new button.";
        }

        var source = FindSocialButton(SocialSide.Source, sourcePage, sourceSlot);
        if (source is null || source.IsEmpty)
        {
            return "The source macro is no longer there.";
        }

        var renamed = source.ToSlot();
        renamed.Name = trimmed;
        var clash = DestinationSocialPages
            .SelectMany(group => group.Buttons)
            .Any(button => !button.IsEmpty
                && SocialLayout.SameName(button.Name, trimmed)
                && !SocialLayout.SameCommands(button.ToSlot(), renamed));
        return clash
            ? "A destination macro already uses that name with different commands."
            : null;
    }

    private void QueueSocialOverwrites(int sourcePage, int sourceSlot, IReadOnlyList<SocialButton> targets)
    {
        var caption = string.Empty;
        var count = 0;
        foreach (var target in targets)
        {
            if (TryQueueSocialCopy(sourcePage, sourceSlot, target.Page, target.Slot, destinationName: null, announce: false, commit: false, out var queued))
            {
                caption = queued;
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        if (!MarkEdited(count == 1 ? "copy 1 macro" : string.Create(CultureInfo.InvariantCulture, $"copy {count} macros")))
        {
            return;
        }

        BuildSocialBoard();
        Status = count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"Copied {caption} over the destination button that uses the same name. Save to write the file.")
            : string.Create(CultureInfo.InvariantCulture, $"Copied {caption} over {count} destination buttons that use the same name. Save to write the file.");
    }

    private (int Page, int Slot) PlaceForNewSocialButton(int preferredPage, int preferredSlot)
    {
        var preferred = FindSocialButton(SocialSide.Destination, preferredPage, preferredSlot);
        if (IsDestinationOpen(preferred))
        {
            return (preferredPage, preferredSlot);
        }

        foreach (var group in DestinationSocialPages)
        {
            foreach (var button in group.Buttons)
            {
                if (IsDestinationOpen(button))
                {
                    return (button.Page, button.Slot);
                }
            }
        }

        var page = Math.Max(1, DestinationSocialPages.Count + 1);
        EnsureSocialPage(page);
        return (page, 1);
    }

    private bool TryQueueSocialCopy(
        int sourcePage,
        int sourceSlot,
        int destinationPage,
        int destinationSlot,
        string? destinationName,
        bool announce,
        bool commit,
        out string caption)
    {
        caption = string.Empty;
        if (!EnsureContentClasses())
        {
            return false;
        }

        var source = SocialButtons.ForDisplay(FindSocialSection(destination: false))
            .FirstOrDefault(slot => slot.Page == sourcePage && slot.Slot == sourceSlot);
        if (source is null || source.IsEmpty)
        {
            Status = "That macro is empty, so there is nothing to copy.";
            return false;
        }

        if (SocialBoardsShareSection && sourcePage == destinationPage && sourceSlot == destinationSlot)
        {
            Status = "Source and destination are the same loadout. Drop the macro on a different button.";
            return false;
        }

        var section = EnsureSocialSection();
        if (section is null)
        {
            Status = "The destination loadout has no file for macros.";
            return false;
        }

        destinationName = string.IsNullOrWhiteSpace(destinationName) ? null : destinationName.Trim();
        var replaced = FindSocialButton(SocialSide.Destination, destinationPage, destinationSlot);
        caption = destinationName
            ?? (string.IsNullOrWhiteSpace(source.Name)
                ? string.Create(CultureInfo.InvariantCulture, $"Page {sourcePage} button {sourceSlot}")
                : source.Name.Trim());
        var content = source.CopiedTo(destinationPage, destinationSlot);
        if (destinationName is not null)
        {
            content.Name = destinationName;
        }

        if (replaced is { IsEmpty: false } && SocialLayout.SameMacro(replaced.ToSlot(), content))
        {
            if (announce)
            {
                Status = string.Create(CultureInfo.InvariantCulture, $"{caption} is already on that button.");
            }

            return false;
        }

        RemoveQueuesForSlot(destinationPage, destinationSlot);
        SocialButtons.WriteSlot(section, content);
        if (commit)
        {
            if (!MarkEdited("copy 1 macro"))
            {
                return false;
            }

            BuildSocialBoard();
        }

        if (announce)
        {
            Status = replaced is { IsEmpty: false }
                ? string.Create(CultureInfo.InvariantCulture, $"Copied {caption} onto destination page {destinationPage}, button {destinationSlot}, replacing {replaced.Caption}. Save to write the file.")
                : string.Create(CultureInfo.InvariantCulture, $"Copied {caption} onto destination page {destinationPage}, button {destinationSlot}. Save to write the file.");
        }

        return true;
    }

    public void RemoveQueuedSocial(QueuedSocialCopy copy)
    {
        if (!QueuedSocialCopies.Remove(copy))
        {
            return;
        }

        RaiseQueueProperties();
        UpdateSocialFlags();
        Status = $"Removed {copy.Caption} from the copy list.";
    }

    public void ClearQueuedSocials()
    {
        if (QueuedSocialCopies.Count == 0)
        {
            return;
        }

        QueuedSocialCopies.Clear();
        RaiseQueueProperties();
        UpdateSocialFlags();
        Status = "Cleared the macros queued to copy.";
    }

    public void ApplyQueuedSocials()
    {
        if (QueuedSocialCopies.Count == 0)
        {
            return;
        }

        if (!EnsureContentClasses())
        {
            return;
        }

        var section = EnsureSocialSection();
        if (section is null)
        {
            Status = "The destination loadout has no file for macros.";
            return;
        }

        var copies = QueuedSocialCopies.ToList();
        foreach (var copy in copies)
        {
            SocialButtons.WriteSlot(section, copy.Content.CopiedTo(copy.DestinationPage, copy.DestinationSlot));
        }

        QueuedSocialCopies.Clear();
        RaiseQueueProperties();
        if (!MarkEdited(copies.Count == 1 ? "copy 1 macro" : string.Create(CultureInfo.InvariantCulture, $"copy {copies.Count} macros")))
        {
            return;
        }

        BuildSocialBoard();
        Status = copies.Count == 1
            ? "Copied 1 macro into the destination. Save to write the file."
            : string.Create(CultureInfo.InvariantCulture, $"Copied {copies.Count} macros into the destination. Save to write the file.");
    }

    private void ResetSocialComparison()
    {
        _sourceBaseline = null;
        _destinationBaseline = null;
    }

    private void ClearSocialBoard()
    {
        SourceSocialPages.Clear();
        DestinationSocialPages.Clear();
        QueuedSocialCopies.Clear();
        SocialChanges.Clear();
        _selectedSocialSide = null;
        SelectedSocial = null;
        RaiseSocialEditorMode();
        Raise(nameof(HasSocialChanges));
        SetActiveSocialLink(null);
        SocialEditorCaption = "Select a macro to edit its name and lines.";
        RaiseQueueProperties();
        Raise(nameof(SourceSocialHeading));
        Raise(nameof(DestinationSocialHeading));
    }

    private void BuildSocialBoard()
    {
        var side = _selectedSocialSide;
        var page = SelectedSocial?.Page ?? 0;
        var slot = SelectedSocial?.Slot ?? 0;
        var restore = SelectedSocial is not null && side is not null;

        var sourceSlots = ReadSocialSlots(destination: false);
        var destinationSlots = ReadSocialSlots(destination: true);
        _sourceBaseline ??= sourceSlots.Select(slot => slot.CopiedTo(slot.Page, slot.Slot)).ToList();
        _destinationBaseline ??= destinationSlots.Select(slot => slot.CopiedTo(slot.Page, slot.Slot)).ToList();
        var queuedSlots = QueuedSocialCopies
            .Select(copy => new SocialSlot(copy.DestinationPage, copy.DestinationSlot))
            .ToList();
        var maxPage = _sourceBaseline.Concat(destinationSlots).Concat(queuedSlots).Select(slot => slot.Page).DefaultIfEmpty(0).Max();
        var layoutSlots = destinationSlots.Concat(queuedSlots).Append(new SocialSlot(Math.Max(1, maxPage + 1), 1)).ToList();
        var pages = SocialLayout.Pages(_sourceBaseline, layoutSlots);
        SetActiveSocialLink(null);
        SourceSocialPages.Clear();
        DestinationSocialPages.Clear();
        foreach (var number in pages)
        {
            var count = SocialLayout.ButtonsOnPage(_sourceBaseline, layoutSlots, number);
            SourceSocialPages.Add(MakeSocialPage(SocialSide.Source, number, count, _sourceBaseline));
            DestinationSocialPages.Add(MakeSocialPage(SocialSide.Destination, number, count, destinationSlots));
        }

        Raise(nameof(SourceSocialHeading));
        Raise(nameof(DestinationSocialHeading));
        _selectedSocialSide = null;
        SelectedSocial = null;
        RaiseSocialEditorMode();
        if (restore && FindSocialButton(side!.Value, page, slot) is { } button)
        {
            SelectSocialButton(button);
            return;
        }

        SocialEditorCaption = "Select a macro to edit its name and lines.";
        UpdateSocialFlags();
    }

    private static SocialPageGroup MakeSocialPage(SocialSide side, int page, int count, IReadOnlyList<SocialSlot> slots)
    {
        var group = new SocialPageGroup(page);
        var bySlot = slots.Where(slot => slot.Page == page).ToDictionary(slot => slot.Slot);
        for (var slot = 1; slot <= count; slot++)
        {
            var button = new SocialButton(side, page, slot);
            bySlot.TryGetValue(slot, out var social);
            button.Load(social);
            group.Buttons.Add(button);
        }

        return group;
    }

    private void OnSocialEdited(SocialSide side, int page, int slot)
    {
        MarkEdited();
        var section = FindSocialSection(side == SocialSide.Destination);
        var stored = section is null
            ? null
            : SocialButtons.Read(section).FirstOrDefault(item => item.Page == page && item.Slot == slot);
        var social = SocialButtons.ForDisplay(section).FirstOrDefault(item => item.Page == page && item.Slot == slot);
        if (stored is null
            && social is not null
            && _selectedSocialSide == side
            && SelectedSocial is { } editor
            && editor.Page == page
            && editor.Slot == slot)
        {
            editor.Load(social);
        }

        FindSocialButton(side, page, slot)?.Load(social);
        if (side == SocialSide.Source)
        {
            RefreshQueuedSource(page, slot, social);
        }

        if (side == SocialSide.Destination)
        {
            UpdateQueuedFromDestination(page, slot, social);
        }

        UpdateSocialFlags();
    }

    private void UpdateQueuedFromDestination(int page, int slot, SocialSlot? social)
    {
        var matches = QueuedSocialCopies.Where(copy => copy.DestinationPage == page && copy.DestinationSlot == slot).ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var sourceSlots = ReadSocialSlots(destination: false);
        var changed = false;
        foreach (var match in matches)
        {
            var source = sourceSlots.FirstOrDefault(item => item.Page == match.SourcePage && item.Slot == match.SourceSlot);
            var stillLinked = source is not null && social is not null && SocialLayout.SameMacro(source, social);
            if (stillLinked)
            {
                continue;
            }

            QueuedSocialCopies.Remove(match);
            changed = true;
        }

        if (changed)
        {
            RaiseQueueProperties();
        }
    }

    private void RefreshQueuedSource(int page, int slot, SocialSlot? social)
    {
        var matches = QueuedSocialCopies.Where(copy => copy.SourcePage == page && copy.SourceSlot == slot).ToList();
        if (matches.Count == 0)
        {
            return;
        }

        if (social is null || social.IsEmpty)
        {
            foreach (var match in matches)
            {
                QueuedSocialCopies.Remove(match);
            }

            RaiseQueueProperties();
            Status = matches.Count == 1
                ? "Removed the queued copy because the source macro is now empty."
                : "Removed the queued copies because the source macro is now empty.";
            return;
        }

        foreach (var match in matches)
        {
            var index = QueuedSocialCopies.IndexOf(match);
            if (index < 0)
            {
                continue;
            }

            var content = social.CopiedTo(page, slot);
            var caption = match.DestinationName;
            if (!string.IsNullOrWhiteSpace(caption))
            {
                content.Name = caption;
            }
            else
            {
                caption = string.IsNullOrWhiteSpace(social.Name)
                    ? string.Create(CultureInfo.InvariantCulture, $"Page {page} button {slot}")
                    : social.Name.Trim();
            }

            QueuedSocialCopies[index] = new QueuedSocialCopy
            {
                SourcePage = match.SourcePage,
                SourceSlot = match.SourceSlot,
                DestinationPage = match.DestinationPage,
                DestinationSlot = match.DestinationSlot,
                Caption = caption,
                DestinationName = match.DestinationName,
                Content = content,
            };
        }
    }

    private void UpdateSocialFlags()
    {
        var sourceButtons = SourceSocialPages.SelectMany(group => group.Buttons).ToList();
        var destinationButtons = DestinationSocialPages.SelectMany(group => group.Buttons).ToList();
        var sourceSlots = sourceButtons.Select(button => button.ToSlot()).ToList();
        var destinationSlots = destinationButtons.Select(button => button.ToSlot()).ToList();
        RememberSocialLink(sourceButtons, destinationButtons);

        foreach (var button in sourceButtons)
        {
            var queued = QueuedSocialCopies
                .Where(copy => copy.SourcePage == button.Page && copy.SourceSlot == button.Slot)
                .ToList();
            var slot = button.ToSlot();
            var match = SocialLayout.Match(slot, destinationSlots);
            button.SetIncoming(null, null);
            button.SetFlags(match == SocialNameMatch.SameCommands, mismatch: false, queued.Count > 0, IsSocialSelected(button), isNew: false, SourceTip(button, match, queued, destinationButtons));
        }

        foreach (var button in destinationButtons)
        {
            var incoming = QueuedSocialCopies.FirstOrDefault(copy => copy.DestinationPage == button.Page && copy.DestinationSlot == button.Slot);
            var slot = button.ToSlot();
            var match = SocialLayout.Match(slot, sourceSlots);
            var isNew = SocialLayout.IsNew(slot, sourceSlots);
            button.SetIncoming(incoming?.Caption, incoming?.Content.Color);
            button.SetFlags(match == SocialNameMatch.SameCommands, mismatch: false, incoming is not null, IsSocialSelected(button), isNew, DestinationTip(button, match, isNew, incoming, sourceButtons));
        }

        RebuildSocialChanges();
    }

    private void RememberSocialLink(IReadOnlyList<SocialButton> sourceButtons, IReadOnlyList<SocialButton> destinationButtons)
    {
        SocialMacroLink? link = null;
        if (_selectedSocialSide == SocialSide.Destination && SelectedSocial is { } selectedDestination)
        {
            var destination = FindSocialButton(SocialSide.Destination, selectedDestination.Page, selectedDestination.Slot);
            var source = destination is null ? null : CopiedSource(destination, sourceButtons);
            if (destination is not null && source is not null)
            {
                link = new SocialMacroLink(source.Page, source.Slot, destination.Page, destination.Slot);
                SocialEditorCaption = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Editing destination · page {destination.Page}, button {destination.Slot}. Showing source page {source.Page}, button {source.Slot}. Changes apply only to the destination.");
            }
        }
        else if (_selectedSocialSide == SocialSide.Source && SelectedSocial is { } selectedSource)
        {
            var source = FindSocialButton(SocialSide.Source, selectedSource.Page, selectedSource.Slot);
            var destination = source is null ? null : CopiedDestination(source, destinationButtons);
            if (source is not null && destination is not null)
            {
                link = new SocialMacroLink(source.Page, source.Slot, destination.Page, destination.Slot);
                var queued = QueuedSocialCopies.Any(copy =>
                    copy.SourcePage == source.Page
                    && copy.SourceSlot == source.Slot
                    && copy.DestinationPage == destination.Page
                    && copy.DestinationSlot == destination.Slot);
                var because = queued ? "Queued to copy onto" : "Same macro on";
                SocialEditorCaption = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Source · page {source.Page}, button {source.Slot}. {because} destination page {destination.Page}, button {destination.Slot}. Source macros can't be edited.");
            }
        }

        if (link is null)
        {
            SocialEditorCaption = _selectedSocialSide switch
            {
                SocialSide.Source => "Source macros can't be edited.",
                SocialSide.Destination when SelectedSocial is { IsBlank: true } created =>
                    string.Create(CultureInfo.InvariantCulture, $"New macro · page {created.Page}, button {created.Slot}. Enter a name and commands."),
                SocialSide.Destination when SelectedSocial is { } editing =>
                    string.Create(CultureInfo.InvariantCulture, $"Editing destination · page {editing.Page}, button {editing.Slot}."),
                _ => SocialEditorCaption,
            };
        }

        SetActiveSocialLink(link);
    }

    private CopiedSourceDetails? SourceDetailsForCopy(SocialButton destination)
    {
        var incoming = QueuedSocialCopies.FirstOrDefault(copy =>
            copy.DestinationPage == destination.Page && copy.DestinationSlot == destination.Slot);
        if (incoming is not null)
        {
            var source = ReadSocialSlots(destination: false)
                .FirstOrDefault(slot => slot.Page == incoming.SourcePage && slot.Slot == incoming.SourceSlot);
            var details = source?.CopiedTo(destination.Page, destination.Slot) ?? incoming.Content.CopiedTo(destination.Page, destination.Slot);
            if (!string.IsNullOrWhiteSpace(incoming.DestinationName))
            {
                details.Name = incoming.DestinationName;
            }

            return new CopiedSourceDetails(incoming.SourcePage, incoming.SourceSlot, details);
        }

        if (destination.IsEmpty || string.IsNullOrWhiteSpace(destination.Name))
        {
            return null;
        }

        var current = destination.ToSlot();
        var match = ReadSocialSlots(destination: false).FirstOrDefault(slot => SocialLayout.SameMacro(slot, current));
        return match is null ? null : new CopiedSourceDetails(match.Page, match.Slot, match);
    }

    private SocialButton? CopiedSource(SocialButton destination, IReadOnlyList<SocialButton> sourceButtons)
    {
        var incoming = QueuedSocialCopies.FirstOrDefault(copy =>
            copy.DestinationPage == destination.Page && copy.DestinationSlot == destination.Slot);
        if (incoming is not null)
        {
            var queuedSource = sourceButtons.FirstOrDefault(button => button.Page == incoming.SourcePage && button.Slot == incoming.SourceSlot);
            var queuedSlot = incoming.Content.CopiedTo(destination.Page, destination.Slot);
            if (!string.IsNullOrWhiteSpace(incoming.DestinationName))
            {
                queuedSlot.Name = incoming.DestinationName;
            }

            return queuedSource is not null && SocialLayout.SameMacro(queuedSource.ToSlot(), queuedSlot)
                ? queuedSource
                : null;
        }

        if (destination.IsEmpty || string.IsNullOrWhiteSpace(destination.Name))
        {
            return null;
        }

        var slot = destination.ToSlot();
        return sourceButtons.FirstOrDefault(button => !button.IsEmpty && SocialLayout.SameMacro(button.ToSlot(), slot));
    }

    private SocialButton? CopiedDestination(SocialButton source, IReadOnlyList<SocialButton> destinationButtons)
    {
        var outgoing = QueuedSocialCopies.FirstOrDefault(copy =>
            copy.SourcePage == source.Page && copy.SourceSlot == source.Slot);
        if (outgoing is not null)
        {
            var queuedDestination = destinationButtons.FirstOrDefault(button => button.Page == outgoing.DestinationPage && button.Slot == outgoing.DestinationSlot);
            var queuedSlot = outgoing.Content.CopiedTo(outgoing.DestinationPage, outgoing.DestinationSlot);
            if (!string.IsNullOrWhiteSpace(outgoing.DestinationName))
            {
                queuedSlot.Name = outgoing.DestinationName;
            }

            return queuedDestination is not null && SocialLayout.SameMacro(source.ToSlot(), queuedSlot)
                ? queuedDestination
                : null;
        }

        if (source.IsEmpty || string.IsNullOrWhiteSpace(source.Name))
        {
            return null;
        }

        var slot = source.ToSlot();
        return destinationButtons.FirstOrDefault(button => !button.IsEmpty && SocialLayout.SameMacro(button.ToSlot(), slot));
    }

    private void SetActiveSocialLink(SocialMacroLink? link)
    {
        if (_activeSocialLink == link)
        {
            return;
        }

        _activeSocialLink = link;
        SocialLinkChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsDestinationOpen(SocialButton? button)
    {
        return button is { IsEmpty: true }
            && !QueuedSocialCopies.Any(copy => copy.DestinationPage == button.Page && copy.DestinationSlot == button.Slot);
    }

    private void EnsureSocialPage(int page)
    {
        EnsureSocialSide(SourceSocialPages, SocialSide.Source, page);
        EnsureSocialSide(DestinationSocialPages, SocialSide.Destination, page);
    }

    private static void EnsureSocialSide(ObservableCollection<SocialPageGroup> pages, SocialSide side, int page)
    {
        while (pages.Count < page)
        {
            var number = pages.Count + 1;
            var group = new SocialPageGroup(number);
            for (var slot = 1; slot <= SocialLayout.ButtonsPerPage; slot++)
            {
                group.Buttons.Add(new SocialButton(side, number, slot));
            }

            pages.Add(group);
        }
    }

    private bool IsSocialSelected(SocialButton button)
    {
        if (_selectedSocialSide == button.Side
            && SelectedSocial is { } row
            && row.Page == button.Page
            && row.Slot == button.Slot)
        {
            return true;
        }

        if (_activeSocialLink is not { } link)
        {
            return false;
        }

        return button.Side == SocialSide.Source
            ? button.Page == link.SourcePage && button.Slot == link.SourceSlot
            : button.Page == link.DestinationPage && button.Slot == link.DestinationSlot;
    }

    private static string SourceTip(SocialButton button, SocialNameMatch match, IReadOnlyList<QueuedSocialCopy> queued, IReadOnlyList<SocialButton> otherSide)
    {
        var lines = new List<string> { ButtonTitle(button) };
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Page {button.Page}, button {button.Slot}"));
        AddCommands(lines, button);

        if (match == SocialNameMatch.SameCommands)
        {
            lines.Add("Same macro on the destination: " + MatchingButtons(button, otherSide));
        }

        if (queued.Count > 0)
        {
            var targets = string.Join(", ", queued.Select(copy => string.Create(CultureInfo.InvariantCulture, $"page {copy.DestinationPage} button {copy.DestinationSlot}")));
            lines.Add("Queued to copy onto " + targets + ".");
        }
        else if (!button.IsEmpty)
        {
            lines.Add("Drag onto a destination button, or into Changes.");
        }

        return string.Join('\n', lines);
    }

    private static string DestinationTip(SocialButton button, SocialNameMatch match, bool isNew, QueuedSocialCopy? incoming, IReadOnlyList<SocialButton> otherSide)
    {
        var lines = new List<string> { incoming is null ? ButtonTitle(button) : incoming.Caption };
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Page {button.Page}, button {button.Slot}"));
        if (incoming is null)
        {
            AddCommands(lines, button);
        }

        if (isNew)
        {
            lines.Add("New macro. The name, color, and commands do not match a source macro.");
        }
        else if (match == SocialNameMatch.SameCommands)
        {
            lines.Add("Same macro on the source: " + MatchingButtons(button, otherSide));
        }

        if (incoming is not null)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Will be replaced by {incoming.Caption} from page {incoming.SourcePage} button {incoming.SourceSlot}."));
        }

        return string.Join('\n', lines);
    }

    private static void AddCommands(List<string> lines, SocialButton button)
    {
        var slot = button.ToSlot();
        foreach (var line in new[] { slot.Line1, slot.Line2, slot.Line3, slot.Line4, slot.Line5 })
        {
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
    }

    private static string ButtonTitle(SocialButton button)
    {
        return button.IsEmpty
            ? string.Create(CultureInfo.InvariantCulture, $"Empty · page {button.Page} button {button.Slot}")
            : button.Caption;
    }

    private static string MatchingButtons(SocialButton button, IReadOnlyList<SocialButton> buttons)
    {
        var slot = button.ToSlot();
        var matches = buttons
            .Where(other => !other.IsEmpty && SocialLayout.SameMacro(other.ToSlot(), slot))
            .Select(other => string.Create(CultureInfo.InvariantCulture, $"page {other.Page} button {other.Slot}"))
            .Take(4)
            .ToList();
        return matches.Count == 0 ? "the other loadout" : string.Join(", ", matches);
    }

    private SocialButton? FindSocialButton(SocialSide side, int page, int slot)
    {
        var pages = side == SocialSide.Source ? SourceSocialPages : DestinationSocialPages;
        return pages.FirstOrDefault(group => group.Page == page)?.Buttons.FirstOrDefault(button => button.Slot == slot);
    }

    private IReadOnlyList<SocialSlot> ReadSocialSlots(bool destination)
    {
        return SocialButtons.ForDisplay(FindSocialSection(destination));
    }

    private IniSection? FindSocialSection(bool destination)
    {
        return Document(editingDestination: destination, SocialFileKind)?.Find("Socials");
    }

    private IniSection? EnsureSocialSection(bool destination = true)
    {
        var document = Document(editingDestination: destination, SocialFileKind);
        if (document is null)
        {
            return null;
        }

        var section = document.Find("Socials");
        if (section is not null)
        {
            return section;
        }

        section = new IniSection("Socials");
        document.Sections.Add(section);
        return section;
    }

    private bool SocialBoardsShareSection
    {
        get
        {
            var source = FindSocialSection(destination: false);
            var destination = FindSocialSection(destination: true);
            return source is not null && ReferenceEquals(source, destination);
        }
    }

    private LoadoutFileKind SocialFileKind => _selectedSection?.Kind ?? LoadoutFileKind.Character;

    private void RaiseQueueProperties()
    {
        Raise(nameof(HasQueuedSocialCopies));
        Raise(nameof(ApplySocialCopiesText));
    }

    private void RaiseSocialEditorMode()
    {
        Raise(nameof(CanEditSocial));
        Raise(nameof(IsSocialReadOnly));
    }

    private void RebuildSocialChanges()
    {
        SocialChanges.Clear();
        if (_destinationBaseline is null)
        {
            Raise(nameof(HasSocialChanges));
            return;
        }

        var baseline = _destinationBaseline.ToDictionary(slot => (slot.Page, slot.Slot));
        var seen = new HashSet<(int Page, int Slot)>();
        foreach (var button in DestinationSocialPages.SelectMany(group => group.Buttons))
        {
            var queue = QueuedSocialCopies.FirstOrDefault(copy => copy.DestinationPage == button.Page && copy.DestinationSlot == button.Slot);
            var effective = queue is null ? button.ToSlot() : QueuedSlot(queue);
            ConsiderSocialChange(baseline, seen, button.Page, button.Slot, effective, queue);
        }

        foreach (var queue in QueuedSocialCopies)
        {
            if (seen.Contains((queue.DestinationPage, queue.DestinationSlot)))
            {
                continue;
            }

            ConsiderSocialChange(baseline, seen, queue.DestinationPage, queue.DestinationSlot, QueuedSlot(queue), queue);
        }

        foreach (var original in _destinationBaseline)
        {
            if (original.IsEmpty || seen.Contains((original.Page, original.Slot)))
            {
                continue;
            }

            ConsiderSocialChange(baseline, seen, original.Page, original.Slot, new SocialSlot(original.Page, original.Slot), null);
        }

        Raise(nameof(HasSocialChanges));
    }

    private void ConsiderSocialChange(
        Dictionary<(int Page, int Slot), SocialSlot> baseline,
        HashSet<(int Page, int Slot)> seen,
        int page,
        int slot,
        SocialSlot effective,
        QueuedSocialCopy? queue)
    {
        seen.Add((page, slot));
        baseline.TryGetValue((page, slot), out var original);
        var had = original is { IsEmpty: false };
        var has = !effective.IsEmpty;
        var sourceMatch = MatchingSourceButton(effective);
        var changed = had && has && !SocialLayout.SameMacro(original!, effective);
        SocialChangeKind? kind = (had, has, changed) switch
        {
            (false, true, _) when sourceMatch is not null => SocialChangeKind.Copied,
            (false, true, _) => SocialChangeKind.New,
            (true, false, _) => SocialChangeKind.Deleted,
            (true, true, true) when sourceMatch is not null => SocialChangeKind.Copied,
            (true, true, true) => SocialChangeKind.Modified,
            _ => null,
        };
        if (kind is null)
        {
            return;
        }

        var named = kind == SocialChangeKind.Deleted ? original!.Name : effective.Name;
        var caption = string.IsNullOrWhiteSpace(named)
            ? string.Create(CultureInfo.InvariantCulture, $"Page {page} button {slot}")
            : named.Trim();
        var fromPage = sourceMatch?.Page ?? queue?.SourcePage;
        var fromSlot = sourceMatch?.Slot ?? queue?.SourceSlot;
        var detail = kind == SocialChangeKind.Copied && fromPage is int copiedPage && fromSlot is int copiedSlot
            ? string.Create(CultureInfo.InvariantCulture, $"Page {page} · button {slot} · from page {copiedPage} button {copiedSlot}")
            : string.Create(CultureInfo.InvariantCulture, $"Page {page} · button {slot}");
        SocialChanges.Add(new SocialChange
        {
            Kind = kind.Value,
            Page = page,
            Slot = slot,
            Caption = caption,
            Detail = detail,
            Queue = queue,
        });
    }

    private SocialButton? MatchingSourceButton(SocialSlot effective)
    {
        if (effective.IsEmpty || string.IsNullOrWhiteSpace(effective.Name))
        {
            return null;
        }

        return SourceSocialPages
            .SelectMany(group => group.Buttons)
            .FirstOrDefault(button => !button.IsEmpty && SocialLayout.SameMacro(button.ToSlot(), effective));
    }

    private static SocialSlot QueuedSlot(QueuedSocialCopy queue)
    {
        var slot = queue.Content.CopiedTo(queue.DestinationPage, queue.DestinationSlot);
        if (!string.IsNullOrWhiteSpace(queue.DestinationName))
        {
            slot.Name = queue.DestinationName;
        }

        return slot;
    }

    private bool RemoveQueuesForSlot(int page, int slot)
    {
        var removed = false;
        for (var index = QueuedSocialCopies.Count - 1; index >= 0; index--)
        {
            var copy = QueuedSocialCopies[index];
            if (copy.DestinationPage != page || copy.DestinationSlot != slot)
            {
                continue;
            }

            QueuedSocialCopies.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            RaiseQueueProperties();
        }

        return removed;
    }

    private void RestoreDestinationSlot(int page, int slot)
    {
        var section = FindSocialSection(destination: true);
        if (section is null)
        {
            return;
        }

        var original = _destinationBaseline?.FirstOrDefault(item => item.Page == page && item.Slot == slot);
        if (original is null || original.IsEmpty || IsImplicitSystem(original))
        {
            SocialButtons.WriteSlot(section, new SocialSlot(page, slot));
            return;
        }

        SocialButtons.WriteSlot(section, original.CopiedTo(page, slot));
    }

    private static bool IsImplicitSystem(SocialSlot slot)
    {
        var system = SystemSocials.Slots().FirstOrDefault(item => item.Page == slot.Page && item.Slot == slot.Slot);
        return system is not null && SocialLayout.SameMacro(system, slot);
    }

    private void RefreshDestinationButton(int page, int slot)
    {
        var social = SocialButtons.ForDisplay(FindSocialSection(destination: true))
            .FirstOrDefault(item => item.Page == page && item.Slot == slot);
        FindSocialButton(SocialSide.Destination, page, slot)?.Load(social);
    }

    private static string SocialHeading(string side, LoadoutChoice? choice)
    {
        return choice is null || choice.Text.Length == 0 ? side : $"{side} · {choice.Text}";
    }

    private static string? BlockReasonFor(string sectionName)
    {
        return sectionName switch
        {
            "Socials" => "Macros can't be edited while ALL is selected as the destination. Choose a single destination loadout.",
            "SpellLoadouts" => "Spell loadouts can't be edited while ALL is selected as the destination. Choose a single destination loadout.",
            _ => null,
        };
    }
}

public readonly record struct SocialMacroLink(int SourcePage, int SourceSlot, int DestinationPage, int DestinationSlot);

internal sealed record CopiedSourceDetails(int Page, int Slot, SocialSlot Details);

public enum SocialConflictChoice
{
    Cancel,
    Overwrite,
    NewButton,
}
