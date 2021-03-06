﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace makecal
{
  public static class DayOfWeekFlagsExtensions
  {
    public static IList<DayOfWeek> ToDays(this DayOfWeekFlags flags)
    {
      var count = 0;

      for (var i = 0; i < 8; i++)
      {
        var mask = 1 << i;

        if (((int)flags & mask) == mask)
        {
          count++;
        }
      }

      var days = new DayOfWeek[count];

      for (int i = 0, bit = 0; i < count; i++, bit++)
      {
        var mask = 1 << bit;

        while (((int)flags & mask) != mask)
        {
          mask = 1 << ++bit;
        }

        var day = (DayOfWeekFlags)mask;

        switch (day)
        {
          case DayOfWeekFlags.Monday:
            days[i] = DayOfWeek.Monday;
            break;
          case DayOfWeekFlags.Tuesday:
            days[i] = DayOfWeek.Tuesday;
            break;
          case DayOfWeekFlags.Wednesday:
            days[i] = DayOfWeek.Wednesday;
            break;
          case DayOfWeekFlags.Thursday:
            days[i] = DayOfWeek.Thursday;
            break;
          case DayOfWeekFlags.Friday:
            days[i] = DayOfWeek.Friday;
            break;
          case DayOfWeekFlags.Saturday:
            days[i] = DayOfWeek.Saturday;
            break;
          case DayOfWeekFlags.Sunday:
            days[i] = DayOfWeek.Sunday;
            break;
          default:
            break;
        }
      }

      return days;
    }

    public static IDictionary<DayOfWeek, int> CalculateDistanceToNextDay(this DayOfWeekFlags flags)
    {
      var days = flags.ToDays();
      return Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToDictionary(d => d,
        d =>
        {
          var value = d;
          do
          {
            value = (int)value == 6 ? 0 : value + 1;
          } while (!days.Contains(value));
          return (value <= d ? (int)value + 7 : (int)value) - (int)d;
        });
    }
  }
}
