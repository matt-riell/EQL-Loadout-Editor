using System.Text;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Tests;

public class IniDocumentTests
{
    private const string Original = "[BagInv1]\r\nXPos=10\r\nYPos=20\r\n[BagInv2]\r\nXPos=30\r\n";

    [Fact]
    public void ReplaceAll_RestoresEarlierTextWithoutSharingSections()
    {
        var document = IniDocument.Parse(Original);
        var snapshot = IniDocument.Parse(Original);
        document.Find("BagInv1")!.Set("XPos", "99");
        document.Sections.RemoveAt(1);

        document.ReplaceAll(snapshot);
        snapshot.Find("BagInv1")!.Set("XPos", "1");

        Assert.Equal(Original, document.Write());
    }

    [Fact]
    public void HasChanges_IgnoresBlankLinesTheWriterDrops()
    {
        const string spaced = "[BagInv1]\r\nXPos=10\r\n\r\n[BagInv2]\r\nXPos=30\r\n";
        var file = new LoadedIniFile("test.ini", spaced, Encoding.UTF8, IniDocument.Parse(spaced));

        Assert.False(file.HasChanges);
    }

    [Fact]
    public void MarkSaved_ClearsChangesUntilTheNextEdit()
    {
        var file = new LoadedIniFile("test.ini", Original, Encoding.UTF8, IniDocument.Parse(Original));
        file.Document.Find("BagInv2")!.Set("XPos", "40");
        Assert.True(file.HasChanges);

        file.MarkSaved(file.Document.Write());
        Assert.False(file.HasChanges);

        file.Document.Find("BagInv2")!.Set("XPos", "50");
        Assert.True(file.HasChanges);
    }
}
