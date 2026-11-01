using Xunit;

public class FitImportTests
{
    // The cap protects memory: a data point can take a few bytes in the file but several hundred in the SDK's objects.
    [Fact]
    public void Parse_rejects_files_with_more_records_than_the_cap()
    {
        var start = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        Assert.Equal(FitImport.MaxRecords, FitImport.Parse(FitFile.Run(start, FitImport.MaxRecords - 1))!.T.Length);
        Assert.Null(FitImport.Parse(FitFile.Run(start, FitImport.MaxRecords)));
    }
}
