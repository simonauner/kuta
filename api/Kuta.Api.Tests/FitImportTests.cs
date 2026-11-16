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

    // A latitude outside ±90° cannot be stored as geography and used to fail the whole Strava import.
    [Fact]
    public void Positions_outside_valid_latitude_are_treated_as_missing()
    {
        var parsed = FitImport.Parse(FitFile.Run(new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), 60, latitude: 100))!;
        Assert.Null(parsed.Lat);
        Assert.Null(parsed.Lon);
        Assert.Null(parsed.Route);
        Assert.Equal(61, parsed.T.Length);
    }

    // The watch writes Training Effect as a byte scaled by 10, so a broken value can reach 25.5 and would fail the import.
    [Fact]
    public void Training_effect_outside_zero_to_five_is_treated_as_missing()
    {
        var file = FitFile.Run(new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), 60, trainingEffect: 25f);
        var parsed = FitImport.Parse(file)!;
        Assert.Equal((null, 0f), (parsed.AerobicTrainingEffect, parsed.AnaerobicTrainingEffect));
    }

    // The zones come from the watch; boundaries that do not increase cannot describe zones and are dropped.
    [Fact]
    public void Heart_rate_zones_are_read_from_the_session_and_follow_consent()
    {
        var start = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        var parsed = FitImport.Parse(FitFile.Run(start, 60))!;
        Assert.Equal(new short[] { 100, 120, 140, 160, 175 }, parsed.HrZones);
        Assert.Null(parsed.WithoutHeartRate().HrZones);
        Assert.Null(FitImport.Parse(FitFile.Run(start, 60, hrZones: [100, 140, 120, 160, 175]))!.HrZones);
    }
}
