namespace VaultPreviewLambda;

public static class ProgressResetCalculator
{
    public static DateTimeOffset GetLastTuesday(DateTimeOffset now)
    {
        DateTimeOffset candidate = now.ToUniversalTime();
        if (candidate.DayOfWeek == DayOfWeek.Tuesday && candidate.TimeOfDay >= TimeSpan.FromHours(15))
        {
            return new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, 15, 0, 0, TimeSpan.Zero);
        }

        candidate = candidate.AddDays(-1);
        while (candidate.DayOfWeek != DayOfWeek.Tuesday)
            candidate = candidate.AddDays(-1);

        return new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, 15, 0, 0, TimeSpan.Zero);
    }
}
