using System;
using System.Collections.Generic;

namespace makecal
{
  public static class DateTimeExtensions
  {
    public static DateTime GetDayOfWeek(this DateTime date, DayOfWeek day)
    {
      var dayOfWeek = (date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek) - 1;
      var targetDay = (day == 0 ? 7 : (int)day) - 1;
      var diff = targetDay - dayOfWeek;
      return date.AddDays(diff).Date;
    }

    public static IList<DateTime> GetDaysOfWeek(this DateTime date, DayOfWeekFlags days)
    {
      var daysOfWeek = days.ToDays();

      var dates = new DateTime[daysOfWeek.Count];
      for (int i = 0; i < dates.Length; i++)
      {
        dates[i] = date.GetDayOfWeek(daysOfWeek[i]);
      }

      return dates;
    }
  }
}

