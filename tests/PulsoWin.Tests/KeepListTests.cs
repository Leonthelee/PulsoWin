using PulsoWin.Core.Cleaning;

namespace PulsoWin.Tests;

public class KeepListTests
{
    [Theory]
    [InlineData("google.com", true)]
    [InlineData(".google.com", true)]
    [InlineData("mail.google.com", true)]
    [InlineData("MAIL.Google.COM", true)]
    [InlineData("notgoogle.com", false)]
    [InlineData("google.com.evil.net", false)]
    [InlineData("", false)]
    public void Keeps_domain_and_subdomains_only(string host, bool expected)
    {
        var keep = KeepList.Parse("google.com");
        Assert.Equal(expected, keep.Keeps(host));
    }

    [Fact]
    public void Parse_accepts_urls_wildcards_and_separators()
    {
        var keep = KeepList.Parse("https://github.com/login\r\n*.bancoestado.cl; outlook.live.com:443, ,");
        Assert.Equal(new[] { "github.com", "bancoestado.cl", "outlook.live.com" }, keep.Domains);
    }

    [Fact]
    public void Empty_list_keeps_nothing()
    {
        Assert.False(KeepList.Parse("").Keeps("google.com"));
    }
}
