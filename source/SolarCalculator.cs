namespace Yuhui;

public static class SolarCalculator
{
    // NOAA solar-position approximation. Result is local time; adequate for a desktop ambience tool.
    public static (DateTime Sunrise, DateTime Sunset) GetSunTimes(DateTime date, double latitude, double longitude, TimeZoneInfo timeZone)
    {
        var day = date.DayOfYear;
        var gamma = 2 * Math.PI / 365 * (day - 1);
        var equationOfTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        var declination = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        var latitudeRadians = latitude * Math.PI / 180;
        var zenith = 90.833 * Math.PI / 180;
        var hourAngle = Math.Acos(Math.Clamp(
            Math.Cos(zenith) / (Math.Cos(latitudeRadians) * Math.Cos(declination)) - Math.Tan(latitudeRadians) * Math.Tan(declination), -1, 1));
        var utcOffsetMinutes = timeZone.GetUtcOffset(date).TotalMinutes;
        var solarNoon = 720 - 4 * longitude - equationOfTime + utcOffsetMinutes;
        var delta = hourAngle * 180 / Math.PI * 4;
        return (date.Date.AddMinutes(solarNoon - delta), date.Date.AddMinutes(solarNoon + delta));
    }
}
