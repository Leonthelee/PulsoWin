using System.Text;
using PulsoWin.Core.Cleaning;

namespace PulsoWin.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void Built_in_catalog_loads_with_unique_ids()
    {
        var rules = RuleCatalog.LoadBuiltIn();
        Assert.NotEmpty(rules);
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Built_in_browser_rules_require_the_browser_closed()
    {
        foreach (var rule in RuleCatalog.LoadBuiltIn().Where(r => r.Category == "Navegadores"))
            Assert.NotEmpty(rule.BlockingProcesses);
    }

    [Theory]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","subPaths":["..\\.."]}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","subPaths":["C:\\Windows"]}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","pattern":"..\\*"}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","profiles":"opera"}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%\\.."}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"\\\\servidor\\compartido"}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"a"},{"id":"X","name":"y","category":"y","base":"b"}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","pattern":null}]""")]
    [InlineData("""[{"id":"x","name":"x","category":"x","base":"%TEMP%","subPaths":null}]""")]
    [InlineData("""[null]""")]
    public void Rejects_unsafe_or_invalid_rules(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => RuleCatalog.Parse(stream));
    }

    [Fact]
    public void Omitted_fields_keep_their_safe_defaults()
    {
        const string json = """[{"id":"x","name":"x","category":"x","base":"%TEMP%"}]""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var rule = Assert.Single(RuleCatalog.Parse(stream));

        Assert.Equal("*", rule.Pattern);
        Assert.Equal(new[] { "" }, rule.SubPaths);
        Assert.True(rule.Recursive);
        Assert.True(rule.DefaultOn);
        Assert.Empty(rule.BlockingProcesses);
        Assert.Equal(0, rule.MinAgeHours);
        Assert.Null(rule.Profiles);
    }

    [Fact]
    public void Built_in_rules_keep_the_values_written_in_rules_json()
    {
        var rules = RuleCatalog.LoadBuiltIn().ToDictionary(r => r.Id);

        Assert.Equal(24, rules["win-temp"].MinAgeHours);
        Assert.False(rules["win-thumbs"].Recursive);
        Assert.Equal("thumbcache_*.db", rules["win-thumbs"].Pattern);
        Assert.False(rules["win-dx-shader"].DefaultOn);
        Assert.Equal(new[] { "chrome" }, rules["chrome-cache"].BlockingProcesses);
        Assert.Equal("chromium", rules["chrome-cache"].Profiles);
    }
}
