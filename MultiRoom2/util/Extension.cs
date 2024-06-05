namespace MultiRoom2;

public static class Extension
{
    public static TimeSpan ToTimeSpan(this TimeSpanType c)
    {
        var t = TimeSpan.Zero;

        if (c != null)
        {
            if (c.SecondsSpecified)
                t += TimeSpan.FromSeconds(c.Seconds);
            if (c.MinutesSpecified)
                t += TimeSpan.FromMinutes(c.Minutes);
            if (c.HoursSpecified)
                t += TimeSpan.FromHours(c.Hours);
            if (c.DaysSpecified)
                t += TimeSpan.FromDays(c.Days);
        }

        return t;
    }
}