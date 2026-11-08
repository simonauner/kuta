using Dapper;
using Npgsql;

// Grade-adjusted average speed and time-weighted heart rate over the steady part of a run: the basis for
// "pace at the same heart rate" over time. Same rules as the heart rate drift on the activity page
// (web/src/lib/activity-analysis.ts): a 10-minute warm-up is skipped, ~30 s windows of continuous movement,
// at least 20 minutes left and a coefficient of variation in grade-adjusted speed of at most 0.15, so that
// intervals and fartlek don't count. Heart rate is health data: the values are cleared with the consent.
public sealed record AerobicSample(double SpeedMS, double Hr);

public sealed record AerobicPoint(Guid ActivityId, DateOnly Day, double SpeedMS, double Hr);

public static class AerobicEfficiency
{
    public const int WarmUpS = 600;
    public const int MinimumS = 20 * 60;
    public const double MaxVariation = 0.15;
    const int WindowS = 30;
    const int GapS = 10;
    const double MovingSpeed = 1.5;
    public const int ShownDays = 365;

    // Regular running activities only; multisport legs are rarely steady enough and mix sports' heart rate.
    public static AerobicSample? ForActivity(string sport, int[] t, float?[]? dist, float?[]? alt, short?[]? hr,
        ActivityPart[]? parts) =>
        parts is { Length: > 0 } || !ActivitySports.IsRunning(sport) ? null : Calculate(t, dist, alt, hr);

    public static AerobicSample? Calculate(int[] t, float?[]? dist, float?[]? alt, short?[]? hr)
    {
        if (dist is null || hr is null || dist.Length != t.Length || hr.Length != t.Length || t.Length == 0) return null;
        if (alt?.Length != t.Length) alt = null;
        var from = t[0] + WarmUpS;
        var windows = new List<(double Seconds, double Adjusted, double HrSeconds)>();
        int start = -1, previous = -1;
        double hrSeconds = 0;
        var hrMissing = false;

        void Close(int end)
        {
            if (start < 0 || end <= start || hrMissing || hrSeconds <= 0) return;
            double seconds = t[end] - t[start];
            double distance = dist[end]!.Value - dist[start]!.Value;
            if (seconds <= 0 || distance / seconds < MovingSpeed) return;
            var adjusted = alt?[start] is { } a && alt[end] is { } b && float.IsFinite(a) && float.IsFinite(b)
                ? distance * TrainingLoad.GradeFactor((b - a) / distance)
                : distance;
            windows.Add((seconds, adjusted, hrSeconds));
        }

        for (var i = 0; i < t.Length; i++)
        {
            var valid = t[i] >= from && dist[i] is { } d && float.IsFinite(d) && d >= 0;
            var broken = !valid || (previous >= 0 &&
                (t[i] <= t[previous] || dist[i]!.Value < dist[previous]!.Value || t[i] - t[previous] > GapS));
            if (broken)
            {
                Close(previous);
                start = previous = -1;
                if (!valid) continue;
            }
            if (start < 0)
            {
                start = previous = i;
                hrSeconds = 0;
                hrMissing = false;
                continue;
            }
            if (hr[i] is { } h && h > 0) hrSeconds += h * (t[i] - t[previous]);
            else hrMissing = true;
            previous = i;
            if (t[i] - t[start] >= WindowS)
            {
                Close(i);
                start = i;
                hrSeconds = 0;
                hrMissing = false;
            }
        }
        Close(previous);

        var total = windows.Sum(w => w.Seconds);
        if (total < MinimumS) return null;
        var mean = windows.Sum(w => w.Adjusted) / total;
        var variance = windows.Sum(w => w.Seconds * Math.Pow(w.Adjusted / w.Seconds - mean, 2)) / total;
        if (Math.Sqrt(variance) / mean > MaxVariation) return null;
        return new AerobicSample(mean, windows.Sum(w => w.HrSeconds) / total);
    }

    public static async Task<short?[]?> ReadHeartRateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id)
    {
        await using var command = new NpgsqlCommand("select hr from activity_streams where activity_id = @id", conn, tx);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() && !reader.IsDBNull(0) ? reader.GetFieldValue<short?[]>(0) : null;
    }

    // Own steady runs of the last year, oldest first; the web chooses the reference heart rate and the months.
    public static async Task<IEnumerable<AerobicPoint>> ReadAsync(NpgsqlConnection conn, Guid userId, string timeZone,
        DateTime now, CancellationToken ct) =>
        await conn.QueryAsync<AerobicPoint>(new CommandDefinition("""
            select id as activity_id, (started_at at time zone @timeZone)::date as day,
                aerobic_speed_m_s as speed_m_s, aerobic_hr as hr
            from activities
            where user_id = @userId and aerobic_hr is not null
                and started_at > @now::timestamptz - make_interval(days => @days) and started_at <= @now
            order by started_at, id
            """, new { userId, timeZone, now, days = ShownDays }, cancellationToken: ct));
}
