using Dapper;
using Npgsql;

public sealed record TrainingLoadSummary(
    double? ThresholdSpeedMS, string? ThresholdSource, double? PredictedThresholdSpeedMS,
    IReadOnlyList<TrainingLoadDay> Days, double? RampPerWeek, bool Warning);

public sealed record ThresholdSpeedRequest(double? ThresholdSpeedMS);

public static class TrainingLoads
{
    public const int ShownDays = 182;

    public static async Task<TrainingLoadSummary> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTime now, CancellationToken ct)
    {
        var manual = await conn.ExecuteScalarAsync<double?>(new CommandDefinition(
            "select threshold_speed_m_s from users where id = @userId", new { userId }, cancellationToken: ct));
        var prediction = await RecordTrends.PredictAsync(conn, userId, now, ct);
        double? predicted = prediction.Vdot is { } vdot ? RacePredictor.ThresholdSpeed(vdot) : null;
        var threshold = manual ?? predicted;
        if (threshold is not { } speed)
            return new TrainingLoadSummary(null, null, null, [], null, false);

        // Lagrad intensitet ur tidsserierna; annars snittfarten för löppass och löpdelar (manuella och äldre pass).
        var daily = await conn.QueryAsync<(DateOnly Day, double Intensity)>(new CommandDefinition($"""
            with {SportGroups.Cte}
            select day, sum(intensity) from (
                select (a.started_at at time zone @timeZone)::date as day,
                    coalesce(a.run_intensity,
                        case when coalesce(m.sport_group, a.sport) = 'running' and a.duration_s > 0
                            then a.distance_m ^ 2 / a.duration_s end,
                        (select sum(p.distance_m ^ 2 / (p.end_s - p.start_s)) from activity_parts p
                            left join sport_groups pm on pm.sport = p.sport
                            where p.activity_id = a.id and p.original = not a.parts_edited
                                and coalesce(pm.sport_group, p.sport) = 'running')) as intensity
                from activities a left join sport_groups m on m.sport = a.sport
                where a.user_id = @userId and a.started_at <= @now
            ) runs
            where intensity > 0
            group by day
            """, new { userId, timeZone, now, groupedSports = SportGroups.GroupedSports,
                sportGroupKeys = SportGroups.GroupKeys }, cancellationToken: ct));
        var today = await conn.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "select (@now::timestamptz at time zone @timeZone)::date", new { now, timeZone }, cancellationToken: ct));
        var days = TrainingLoad.Series(daily.ToDictionary(d => d.Day, d => TrainingLoad.Load(d.Intensity, speed)), today);
        var ramp = TrainingLoad.RampPerWeek(days);
        return new TrainingLoadSummary(speed, manual is null ? "prediction" : "manual", predicted,
            days.TakeLast(ShownDays).ToList(), ramp, ramp > TrainingLoad.RampWarningPerWeek);
    }
}
