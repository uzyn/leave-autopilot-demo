namespace LeaveAutopilot.Web.Services;

/// <summary>
/// S4-1: the single, centralized utility for computing chargeable leave days between a
/// start and end date. Counts Monday-Friday only (no public-holiday calendar in v1); a
/// half-day flag on the start and/or end date subtracts 0.5 from that date's contribution.
/// Submission, approval, and balance logic all call this so day counts never disagree.
/// </summary>
public static class WorkingDayCalculator
{
    public static decimal CalculateChargeableDays(DateOnly startDate, DateOnly endDate, bool startHalfDay, bool endHalfDay)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("End date must not be before start date.", nameof(endDate));
        }

        var total = 0m;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayValue = IsWorkingDay(date) ? 1m : 0m;

            if (date == startDate && startHalfDay)
            {
                dayValue -= 0.5m;
            }

            if (date == endDate && endHalfDay)
            {
                dayValue -= 0.5m;
            }

            // A half-day flag can only reduce a day that was already chargeable (a
            // weekday); clamp so a flag applied to a weekend date can't drive the
            // running total negative.
            total += Math.Max(dayValue, 0m);
        }

        return total;
    }

    private static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
