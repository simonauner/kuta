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

    // Latitud utanför ±90° kan inte lagras som geography och fällde tidigare hela Strava-importen.
    [Fact]
    public void Positions_outside_valid_latitude_are_treated_as_missing()
    {
        var parsed = FitImport.Parse(FitFile.Run(new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), 60, latitude: 100))!;
        Assert.Null(parsed.Lat);
        Assert.Null(parsed.Lon);
        Assert.Null(parsed.Route);
        Assert.Equal(61, parsed.T.Length);
    }
}
