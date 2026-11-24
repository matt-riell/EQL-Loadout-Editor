using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Tests;

public class SocialLayoutTests
{
    [Fact]
    public void Pages_FillGapsAndDefaultToOne()
    {
        var source = new[] { new SocialSlot(2, 1) { Name = "Heal" } };
        var destination = new[] { new SocialSlot(4, 3) { Name = "Mez" } };

        Assert.Equal([1, 2, 3, 4], SocialLayout.Pages(source, destination));
        Assert.Equal([1], SocialLayout.Pages([], []));
    }

    [Fact]
    public void ButtonsOnPage_KeepsTwelveUnlessASideHasMore()
    {
        var source = new[] { new SocialSlot(2, 14) { Name = "Wide" } };
        var destination = new[] { new SocialSlot(1, 2) { Name = "Other" } };

        Assert.Equal(14, SocialLayout.ButtonsOnPage(source, destination, 2));
        Assert.Equal(12, SocialLayout.ButtonsOnPage(source, destination, 1));
    }

    [Fact]
    public void DuplicateNames_MatchAcrossSidesAndIgnoreBlanks()
    {
        var source = new[]
        {
            new SocialSlot(2, 1) { Name = " Heal " },
            new SocialSlot(2, 2) { Name = "Only source" },
            new SocialSlot(2, 3) { Name = "   " },
        };
        var destination = new[]
        {
            new SocialSlot(4, 3) { Name = "heal" },
            new SocialSlot(4, 4) { Name = "Only dest" },
        };

        var duplicates = SocialLayout.DuplicateNames(source, destination, sameDocument: false);

        var duplicate = Assert.Single(duplicates);
        Assert.Equal("heal", duplicate, ignoreCase: true);
        Assert.True(SocialLayout.IsDuplicate("HEAL", duplicates));
        Assert.False(SocialLayout.IsDuplicate("Only source", duplicates));
        Assert.False(SocialLayout.IsDuplicate(" ", duplicates));
        Assert.Empty(SocialLayout.DuplicateNames(source, destination, sameDocument: true));
    }

    [Fact]
    public void Match_RequiresTheWholeMacroAndMarksTheRestNew()
    {
        var heal = new SocialSlot(1, 1) { Name = " Heal ", Color = "0", Line1 = "/cast 1", Line2 = " /echo hi " };
        var same = new SocialSlot(2, 2) { Name = "heal", Line1 = "/cast 1", Line2 = "/echo hi" };
        var recolored = new SocialSlot(2, 2) { Name = "heal", Color = "4", Line1 = "/cast 1", Line2 = "/echo hi" };
        var different = new SocialSlot(2, 3) { Name = "HEAL", Line1 = "/cast 2", Line5 = "/sit" };
        var other = new SocialSlot(2, 4) { Name = "Mez", Line1 = "/cast 1" };
        var typed = new SocialSlot(1, 5) { Name = "  ", Line1 = "/cast 1" };
        var blank = new SocialSlot(1, 6);

        Assert.Equal(SocialNameMatch.SameCommands, SocialLayout.Match(heal, [same, other]));
        Assert.Equal(SocialNameMatch.None, SocialLayout.Match(heal, [different, other]));
        Assert.Equal(SocialNameMatch.None, SocialLayout.Match(recolored, [heal]));
        Assert.Equal(SocialNameMatch.None, SocialLayout.Match(other, [heal]));
        Assert.Equal(SocialNameMatch.None, SocialLayout.Match(typed, [typed]));
        Assert.Equal(SocialNameMatch.SameCommands, SocialLayout.Match(heal, [same]));
        Assert.True(SocialLayout.SameMacro(heal, same));
        Assert.False(SocialLayout.SameMacro(heal, different));
        Assert.False(SocialLayout.SameMacro(heal, recolored));
        Assert.False(SocialLayout.IsNew(heal, [same]));
        Assert.True(SocialLayout.IsNew(different, [heal]));
        Assert.True(SocialLayout.IsNew(recolored, [heal]));
        Assert.True(SocialLayout.IsNew(typed, [heal]));
        Assert.False(SocialLayout.IsNew(blank, [heal]));
    }

    [Fact]
    public void ClearSlot_HidesTheSystemMacroBehindABlankButton()
    {
        var section = new IniSection("Socials");

        SocialButtons.ClearSlot(section, 1, 1);
        var shown = SocialButtons.ForDisplay(section);
        var cleared = Assert.Single(shown, slot => slot.Page == 1 && slot.Slot == 1);

        Assert.True(cleared.IsEmpty);
        Assert.DoesNotContain(shown, slot => slot.Name == "Assist Main");
    }

    [Fact]
    public void CopiedSocial_WritesTheDestinationSlot()
    {
        var section = new IniSection("Socials");
        var copy = new SocialSlot(1, 2)
        {
            Name = "Mez",
            Color = "1",
            Line1 = "/cast 1",
            Line2 = "/echo hi",
        }.CopiedTo(3, 4);

        SocialButtons.WriteSlot(section, copy);
        var read = Assert.Single(SocialButtons.Read(section));

        Assert.Equal(3, read.Page);
        Assert.Equal(4, read.Slot);
        Assert.Equal("Mez", read.Name);
        Assert.Equal("1", read.Color);
        Assert.Equal("/cast 1", read.Line1);
        Assert.Equal("/echo hi", read.Line2);
        Assert.Equal(string.Empty, read.Line3);
    }

    [Fact]
    public void SystemSocials_AppearUntilTheFileDefinesThatButton()
    {
        var shown = SocialButtons.ForDisplay(null);

        Assert.Equal(
            ["Assist Main", "Auto Fire", "Auto Skill", "Bug", "Consider", "Corpse", "Hail", "Open", "Pick Zone", "Time", "Use Target", "Who"],
            shown.Select(slot => slot.Name).ToArray());
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), shown.Select(slot => slot.Slot).ToArray());
        Assert.All(shown, slot =>
        {
            Assert.Equal(1, slot.Page);
            Assert.Equal("0", slot.Color);
        });
        Assert.Equal(
            ["/assist main", "/autofire", "/autoskill", "/bug", "/con", "/corpse", "/hail", "/open", "/pickzone", "/time", "/usetarget", "/who"],
            shown.Select(slot => slot.Line1).ToArray());

        var section = new IniSection("Socials");
        section.Set("Page1Button1Name", "Custom");
        section.Set("Page1Button1Color", "4");
        section.Set("Page1Button1Line1", "/say hi");
        section.Set("Page2Button1Name", "Later");
        section.Set("Page2Button1Line1", "/sit");

        var mixed = SocialButtons.ForDisplay(section);
        var custom = Assert.Single(mixed, slot => slot.Page == 1 && slot.Slot == 1);
        Assert.Equal("Custom", custom.Name);
        Assert.Equal("4", custom.Color);
        Assert.Equal("/say hi", custom.Line1);
        Assert.Contains(mixed, slot => slot is { Page: 1, Slot: 2, Name: "Auto Fire", Color: "0", Line1: "/autofire" });
        Assert.Contains(mixed, slot => slot is { Page: 1, Slot: 5, Name: "Consider", Line1: "/con" });
        Assert.Contains(mixed, slot => slot is { Page: 1, Slot: 6, Name: "Corpse", Line1: "/corpse" });
        Assert.Contains(mixed, slot => slot is { Page: 1, Slot: 9, Name: "Pick Zone", Line1: "/pickzone" });
        Assert.Contains(mixed, slot => slot is { Page: 2, Slot: 1, Name: "Later", Line1: "/sit" });
        Assert.Equal(2, SocialButtons.Read(section).Count);
    }
}
