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
    public void Rejects_unsafe_or_invalid_rules(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => RuleCatalog.Parse(stream));
    }
}
