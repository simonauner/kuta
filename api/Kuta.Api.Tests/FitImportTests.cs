using Xunit;

public class FitImportTests
{
    // Taket skyddar minnet: en mätpunkt kan ta några byte i filen men flera hundra i SDK:ns objekt.
    [Fact]
    public void Parse_rejects_files_with_more_records_than_the_cap()
    {
        var start = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        Assert.Equal(FitImport.MaxRecords, FitImport.Parse(FitFile.Run(start, FitImport.MaxRecords - 1))!.T.Length);
        Assert.Null(FitImport.Parse(FitFile.Run(start, FitImport.MaxRecords)));
    }
}
