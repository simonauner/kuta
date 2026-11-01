using Xunit;

public class TermsVersionTests
{
    // The stored version must point to the published text.
    [Fact]
    public void Terms_version_matches_the_date_on_the_terms_page()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "web", "terms.html"))) dir = dir.Parent;
        var html = File.ReadAllText(Path.Combine(dir.FullName, "web", "terms.html"));
        Assert.Equal(2, html.Split($"<time datetime=\"{AuthEndpoints.TermsVersion}\">").Length - 1);
    }
}
