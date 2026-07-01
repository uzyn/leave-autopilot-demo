using LeaveAutopilot.Web.Services;

namespace LeaveAutopilot.Tests.Services;

/// <summary>
/// S4-1 acceptance criteria: a single, centralized utility computing chargeable days
/// (working days only, half-day adjustments) so submission/approval/balance logic all
/// agree. Pure function, no I/O — every boundary case is covered here.
/// </summary>
public class WorkingDayCalculatorTests
{
    // Monday 2026-07-06 .. Sunday 2026-07-12, per the fixed "today" (2026-07-01) used
    // elsewhere in this sprint's tests.
    private static readonly DateOnly Mon = new(2026, 7, 6);
    private static readonly DateOnly Tue = new(2026, 7, 7);
    private static readonly DateOnly Wed = new(2026, 7, 8);
    private static readonly DateOnly Thu = new(2026, 7, 9);
    private static readonly DateOnly Fri = new(2026, 7, 10);
    private static readonly DateOnly Sat = new(2026, 7, 11);
    private static readonly DateOnly Sun = new(2026, 7, 12);
    private static readonly DateOnly NextMon = new(2026, 7, 13);

    [Fact]
    public void SingleFullDay_OnAWeekday_CountsAsOne()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Tue, Tue, startHalfDay: false, endHalfDay: false);

        Assert.Equal(1m, days);
    }

    [Fact]
    public void MultiDaySpan_IncludingAWeekend_ExcludesTheWeekendDays()
    {
        // Thu -> Mon (next week): Thu, Fri, Sat, Sun, Mon = 3 chargeable days (Sat/Sun excluded).
        var days = WorkingDayCalculator.CalculateChargeableDays(Thu, NextMon, startHalfDay: false, endHalfDay: false);

        Assert.Equal(3m, days);
    }

    [Fact]
    public void FridayToMondaySpan_CountsOnlyTheTwoWeekdays()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Fri, NextMon, startHalfDay: false, endHalfDay: false);

        Assert.Equal(2m, days);
    }

    [Fact]
    public void FullWorkweek_MondayToFriday_CountsFiveDays()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Mon, Fri, startHalfDay: false, endHalfDay: false);

        Assert.Equal(5m, days);
    }

    [Fact]
    public void HalfDayOnStart_SubtractsHalfADay()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Mon, Wed, startHalfDay: true, endHalfDay: false);

        Assert.Equal(2.5m, days);
    }

    [Fact]
    public void HalfDayOnEnd_SubtractsHalfADay()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Mon, Wed, startHalfDay: false, endHalfDay: true);

        Assert.Equal(2.5m, days);
    }

    [Fact]
    public void HalfDayOnBothStartAndEnd_SubtractsOneFullDayTotal()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Mon, Wed, startHalfDay: true, endHalfDay: true);

        Assert.Equal(2m, days);
    }

    [Fact]
    public void SingleDayHalfDay_CountsAsHalf()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Mon, Mon, startHalfDay: true, endHalfDay: false);

        Assert.Equal(0.5m, days);
    }

    [Fact]
    public void WeekendOnlyRange_YieldsZeroChargeableDays()
    {
        var days = WorkingDayCalculator.CalculateChargeableDays(Sat, Sun, startHalfDay: false, endHalfDay: false);

        Assert.Equal(0m, days);
    }

    [Fact]
    public void HalfDayFlagOnAWeekendDate_DoesNotGoNegative()
    {
        // A half-day flag on a weekend date shouldn't drive the day's contribution below
        // zero (it should already be 0 for that date since it's not a working day).
        var days = WorkingDayCalculator.CalculateChargeableDays(Sat, Sun, startHalfDay: true, endHalfDay: true);

        Assert.Equal(0m, days);
    }

    [Fact]
    public void EndDateBeforeStartDate_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WorkingDayCalculator.CalculateChargeableDays(Wed, Tue, startHalfDay: false, endHalfDay: false));
    }
}
