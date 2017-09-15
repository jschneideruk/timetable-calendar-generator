﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace makecal
{
  internal class Settings
  {
    public IList<LessonTime> LessonTimes { get; set; }
    public IList<StudyLeave> StudyLeave { get; set; }
    public IList<Override> Overrides
    {
      set
      {
        OverrideDictionary = value.ToDictionary(o => (o.Date, o.Period), o => o.Title);
      }
    }
    public IList<Rename> Renames
    {
      set
      {
        RenameDictionary = value.ToDictionary(o => o.OriginalTitle, o => o.NewTitle);
      }
    }

    public string ServiceAccountKey { get; set; }
    public IDictionary<DateTime, string> DayTypes { get; set; }
    public IDictionary<(DateTime, int), string> OverrideDictionary { get; private set; }
    public IDictionary<string, string> RenameDictionary { get; private set; }

    public DaysOptions Days { get; set; } = new DaysOptions();

    internal class LessonTime
    {
      public string StartTime
      {
        set
        {
          var parts = value.Split(':');
          StartHour = int.Parse(parts[0]);
          StartMinute = int.Parse(parts[1]);
        }
      }
      public int StartHour { get; private set; }
      public int StartMinute { get; private set; }
      public int Duration { get; set; }
    }

    internal class DaysOptions
    {
      public int? Weeks { get; set; }
      public DateTime? Start { get; set; }
      public DateTime? End { get; set; }
      public string DaysOfWeek { get; set; }

      public static readonly DaysOptions Defaults = new DaysOptions()
      {
        Weeks = 5,
        Start = DateTime.Now,
        DaysOfWeek = DayOfWeekFlags.Weekdays.ToString()
      };

      public void UseDefaults()
      {
        Weeks = Weeks ?? Defaults.Weeks;
        Start = Start ?? Defaults.Start;
        DaysOfWeek = DaysOfWeek ?? Defaults.DaysOfWeek;
      }

      public bool IsSet => Weeks.HasValue || Start.HasValue || DaysOfWeek != null;
    }
  }

  internal class StudyLeave
  {
    public int? Year { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
  }

  internal class Override
  {
    public DateTime Date { get; set; }
    public int Period { get; set; }
    public string Title { get; set; }
  }

  internal class Rename
  {
    public string OriginalTitle { get; set; }
    public string NewTitle { get; set; }
  }
}