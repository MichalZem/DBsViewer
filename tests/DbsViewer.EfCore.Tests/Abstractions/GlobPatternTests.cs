namespace DbsViewer.Tests.Abstractions;

public class GlobPatternTests
{
    [Theory]
    [InlineData("Orders", "Orders")]
    [InlineData("Orders", "orders")]
    [InlineData("AspNetUsers", "AspNetUser*")]
    [InlineData("AspNetUser", "AspNetUser*")]
    [InlineData("audit.Changes", "audit.*")]
    [InlineData("Orders", "*")]
    [InlineData("Orders", "*s")]
    [InlineData("Orders", "O*s")]
    [InlineData("Orders", "Order?")]
    [InlineData("Orders", "??????")]
    [InlineData("abcabd", "a*b*d")]
    [InlineData("Orders", "**Orders**")]
    public void Shoduje_se(string value, string pattern) =>
        Assert.True(GlobPattern.IsMatch(value, pattern));

    [Theory]
    [InlineData("Orders", "Order")]
    [InlineData("Orders", "Customers")]
    [InlineData("Orders", "Order??")]
    [InlineData("AspNet", "AspNetUser*")]
    [InlineData("audit_Changes", "audit.*")]
    [InlineData("abcabc", "a*b*d")]
    [InlineData("Orders", "x*")]
    public void Neshoduje_se(string value, string pattern) =>
        Assert.False(GlobPattern.IsMatch(value, pattern));

    [Fact]
    public void Prazdny_vstup_se_nikdy_neshoduje()
    {
        Assert.False(GlobPattern.IsMatch("Orders", null));
        Assert.False(GlobPattern.IsMatch("Orders", ""));
        Assert.False(GlobPattern.IsMatch(null, "*"));
    }

    [Fact]
    public void Prazdny_retezec_odpovida_jen_hvezdicce()
    {
        Assert.True(GlobPattern.IsMatch("", "*"));
        Assert.False(GlobPattern.IsMatch("", "?"));
    }
}
