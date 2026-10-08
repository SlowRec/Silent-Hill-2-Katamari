using SilentKatamari.Converter;
using Xunit;

public class SheetsTests
{
    [Fact]
    public void GeneratedSheetsAreReachable()
    {
        Assert.Equal("2124490", Games.sh2.SteamAppid);
        Assert.Equal("1880620", Games.ouak.SteamAppid);
        Assert.NotNull(Extract.ById("sh2_james_mesh"));
        Assert.All(Extract.All, r => Assert.NotNull(Games.ById(r.Game)));
    }
}
