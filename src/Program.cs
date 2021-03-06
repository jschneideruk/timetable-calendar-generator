﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using KBCsv;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace makecal
{
  public static class Program
  {
    private static readonly string settingsFileName = @"inputs\settings.json";
    private static readonly string keyFileName = @"inputs\key.json";
    private static readonly string daysFileName = @"inputs\days.csv";
    private static readonly string studentsFileName = @"inputs\students.csv";
    private static readonly string teachersFileName = @"inputs\teachers.csv";

    private static readonly string blankingCode = "Blanking Code";

    private static readonly int headerHeight = 10;

    private static readonly int simultaneousRequests = 40;
    private static readonly int maxAttempts = 4;
    private static readonly int retryFirst = 5000;
    private static readonly int retryExponent = 4;

    private const char REPLACEMENT_CHARACTER = '\ufffd';

    private static bool waitForInput = false;

    static void Main(string[] args)
    {
      waitForInput = args.Any(a => a == "/k" || a == "-k");
      MainAsync().GetAwaiter().GetResult();
    }

    private static async Task MainAsync()
    {
#if !DEBUG
      try
      {
#endif
      Console.Clear();
      Console.CursorVisible = false;

      Console.WriteLine("TIMETABLE CALENDAR GENERATOR\n");

      var settings = await LoadSettingsAsync();
      var students = await LoadStudentsAsync();
      var teachers = await LoadTeachersAsync();

      GoogleCalendarService.Configure(settings.ServiceAccountKey);

      Console.WriteLine("\nSetting up calendars:");

      var tasks = new List<Task>();
      var throttler = new SemaphoreSlim(initialCount: simultaneousRequests);

      var people = students.Concat(teachers).ToList();

      Console.SetBufferSize(Console.BufferWidth, Math.Max(headerHeight + people.Count + 1, Console.BufferHeight));

      for (var i = 0; i < people.Count; i++)
      {
        var countLocal = i;
        await Task.Delay(10);
        await throttler.WaitAsync();
        var person = people[countLocal];
        tasks.Add(Task.Run(async () =>
        {
          try
          {
            var line = countLocal + headerHeight;
            ConsoleHelper.WriteDescription(line, $"({countLocal + 1}/{people.Count}) {person.Email}");
            ConsoleHelper.WriteProgress(line, 0);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
              try
              {
                await WriteTimetableAsync(person, settings, line);
                break;
              }
              catch (Google.GoogleApiException) when (attempt < maxAttempts)
              {
                var backoff = retryFirst * (int)Math.Pow(retryExponent, attempt - 1);
                ConsoleHelper.WriteStatus(line, $"Error. Retrying ({attempt} of {maxAttempts - 1})...", ConsoleColor.DarkYellow);
                await Task.Delay(backoff);
              }
              catch (Exception exc)
              {
                ConsoleHelper.WriteStatus(line, $"Failed. {exc.Message}", ConsoleColor.Red);
                break;
              }
            }
          }
          finally
          {
            throttler.Release();
          }
        }));
      }
      await Task.WhenAll(tasks);
      Console.SetCursorPosition(0, headerHeight + people.Count);
      Console.WriteLine("\nCalendar generation complete.\n");
#if !DEBUG
      }
      catch (Exception exc)
      {
        ConsoleHelper.WriteError(exc.Message);
      }
#endif
      if (waitForInput)
      {
        Console.ReadKey();
      }
    }

    private static ICollection<DateTime> GenerateDays(DateTime? start = null, DateTime? end = null, int? weeks = 5,
      DayOfWeekFlags daysOfWeek = DayOfWeekFlags.Weekdays)
    {
      start = start ?? DateTime.Today;
      end = end ?? DateTime.MaxValue;

      if (start < DateTime.Now)
      {
        start = DateTime.Today;
      }

      var next = daysOfWeek.CalculateDistanceToNextDay();

      var date = start.Value;
      var dates = new List<DateTime>();

      while ((date - start.Value).TotalDays / 7 < weeks && date <= end)
      {
        dates.Add(date);
        var step = next[date.DayOfWeek];
        date = date.AddDays(step);
      }

      return dates;
    }

    private static async Task<Settings> LoadSettingsAsync()
    {
      Console.WriteLine($"Reading {settingsFileName}");
      var settingsText = await File.ReadAllTextAsync(settingsFileName);
      var settings = JsonConvert.DeserializeObject<Settings>(settingsText, new IsoDateTimeConverter { DateTimeFormat = "dd-MMM-yy" });

      Console.WriteLine($"Reading {keyFileName}");
      settings.ServiceAccountKey = await File.ReadAllTextAsync(keyFileName);

      if (settings.EventTitle == null)
      {
        settings.EventTitle = new List<string>() { "P{{period}}.", "{{class}}", "({{teacher}})" };
      }

      if (settings.Days.IsSet)
      {
        settings.Days.UseDefaults();

        Console.WriteLine($"Skipping reading {daysFileName} and generating days instead");
        DayOfWeekFlags daysOfWeek = DayOfWeekFlags.None;
        if (!String.IsNullOrWhiteSpace(settings.Days.DaysOfWeek))
        {
          if (!Enum.TryParse(settings.Days.DaysOfWeek, out daysOfWeek))
          {
            daysOfWeek = DayOfWeekFlags.Weekdays;
          }
        }
        var days = GenerateDays(settings.Days.Start, settings.Days.End, settings.Days.Weeks, daysOfWeek);
        settings.DayTypes = days.ToDictionary(day => day, day => day.ToString("ddd"));
      }
      else
      {
        Console.WriteLine($"Reading {daysFileName}");
        settings.DayTypes = new Dictionary<DateTime, string>();

        using (var fs = File.Open(daysFileName, FileMode.Open))
        using (var reader = new CsvReader(fs))
        {
          while (reader.HasMoreRecords)
          {
            var record = await reader.ReadDataRecordAsync();

            if (string.IsNullOrEmpty(record[0]))
            {
              continue;
            }

            var date = DateTime.ParseExact(record[0], "dd-MMM-yy", null);
            var dayOfWeek = date.ToString("ddd");

            if (record.Count > 1)
            {
              settings.DayTypes.Add(date, record[1] + dayOfWeek);
            }
            else
            {
              settings.DayTypes.Add(date, dayOfWeek);
            }
          }
        }
      }

      return settings;
    }

    private static async Task<IEnumerable<Person>> LoadStudentsAsync()
    {
      Console.WriteLine($"Reading {studentsFileName}");

      var students = new List<Person>();

      using (var fs = File.Open(studentsFileName, FileMode.Open))
      using (var reader = new CsvReader(fs))
      {
        Person currentStudent = null;
        string currentSubject = null;

        while (reader.HasMoreRecords)
        {

          var record = await reader.ReadDataRecordAsync();

          if (record[StudentFields.Email] == nameof(StudentFields.Email))
          {
            continue;
          }

          if (!string.IsNullOrEmpty(record[StudentFields.Email]))
          {
            var newEmail = record[StudentFields.Email].ToLower();
            if (currentStudent?.Email != newEmail)
            {
              currentStudent = new Person
              {
                Email = newEmail,
                YearGroup = Int32.Parse(record[StudentFields.Year]),
                Lessons = new List<Lesson>()
              };
              currentSubject = null;
              students.Add(currentStudent);
            }
          }

          if (!string.IsNullOrEmpty(record[StudentFields.Subject]))
          {
            currentSubject = record[StudentFields.Subject];
          }

          if (currentStudent == null || currentSubject == null)
          {
            throw new InvalidOperationException("Incorrectly formatted timetable.");
          }

          currentStudent.Lessons.Add(new Lesson
          {
            PeriodCode = record[StudentFields.Period],
            Class = currentSubject,
            Room = record[StudentFields.Room],
            Teacher = record[StudentFields.Teacher]
          });
        }
      }
      return students;
    }

    private static async Task<IEnumerable<Person>> LoadTeachersAsync()
    {
      Console.WriteLine($"Reading {teachersFileName}");

      var teachers = new List<Person>();

      using (var fs = File.Open(teachersFileName, FileMode.Open))
      using (var reader = new CsvReader(fs))
      {
        var periodCodes = await reader.ReadDataRecordAsync();

        var first = periodCodes[0];

        if (periodCodes[0] == "Work Email" && periodCodes[1] == "Class")
        {
          // SIMS report method
          Person teacher = null;
          String @class = null;

          while (reader.HasMoreRecords)
          {
            var record = await reader.ReadDataRecordAsync();

            if (teacher == null || (record[0] != "" && teacher?.Email != record[0].ToLower()))
            {
              teacher = new Person { Email = record[0].ToLower(), Lessons = new List<Lesson>() };
            }

            if (record[1] != "")
            {
              @class = record[1];
            }

            var period = record[2].Trim(new[] { REPLACEMENT_CHARACTER });
            var room = record[3].Trim(new[] { REPLACEMENT_CHARACTER });

            if (String.IsNullOrWhiteSpace(period) || String.IsNullOrWhiteSpace(room))
            {
              continue;
            }

            teacher.Lessons.Add(new Lesson()
            {
              Class = @class,
              PeriodCode = period,
              Room = room
            });

            if (!teachers.Contains(teacher))
            {
              teachers.Add(teacher);
            }
          }
        }
        else
        {
          // All staff timetable method
          while (reader.HasMoreRecords)
          {
            var timetable = await reader.ReadDataRecordAsync();
            var rooms = await reader.ReadDataRecordAsync();
            var currentTeacher = new Person { Email = timetable[0].ToLower(), Lessons = new List<Lesson>() };

          for (var i = 1; i < timetable.Count; i++)
          {
            if (string.IsNullOrEmpty(timetable[i]))
            {
              continue;
            }
            currentTeacher.Lessons.Add(new Lesson {
              PeriodCode = periodCodes[i],
              Class = timetable[i].Trim().TrimEnd(new[] { REPLACEMENT_CHARACTER }),
              Room = rooms[i].Trim().TrimEnd(new[] { REPLACEMENT_CHARACTER })
            });
          }

            teachers.Add(currentTeacher);
          }
        }
      }

      return teachers;
    }

    private static async Task WriteTimetableAsync(Person person, Settings settings, int line)
    {
      var service = new GoogleCalendarService(person.Email);

      var calendar = await service.GetTimetableCalendarIdAsync() ?? await service.CreateTimetableCalendarAsync();

      ConsoleHelper.WriteProgress(line, 1);

      var fields = "id,summary,location,start(dateTime),end(dateTime)";
      var existing = await service.GetFutureEvents(calendar, DateTime.Today, fields);
      var expected = CreateExpectedEvents(person, settings);

      ConsoleHelper.WriteProgress(line, 2);

      var comparer = new EventComparer();

      var obsolete = existing.Except(expected, comparer);
      var missing = expected.Except(existing, comparer);

      await service.InsertEventsAsync(calendar, missing);
      await service.DeleteEventsAsync(calendar, obsolete);

      ConsoleHelper.WriteProgress(line, 3);
    }

    private static IList<Event> CreateExpectedEvents(Person person, Settings settings)
    {
      IEnumerable<StudyLeave> studyLeaves;
      if (person.YearGroup == null)
      {
        studyLeaves = new List<StudyLeave>();
      }
      else
      {
        studyLeaves = settings.StudyLeave.Where(o => o.Year == null || o.Year == person.YearGroup);
      }

      var lessons = person.Lessons.GroupBy(o => o.PeriodCode).ToDictionary(o => o.Key, o => o.First());
      var events = new List<Event>();
      foreach (var dayOfCalendar in settings.DayTypes.Where(o => o.Key >= DateTime.Today))
      {
        var date = dayOfCalendar.Key;
        var dayCode = dayOfCalendar.Value;
        if (studyLeaves.Any(o => o.StartDate <= date && o.EndDate >= date))
        {
          continue;
        }

        for (var period = 1; period <= settings.LessonTimes.Count; period++)
        {
          var @event = CreateEvent(period, dayCode, lessons, settings, date, person);

          if (@event != null)
          {
            events.Add(@event);
          }
        }
      }

      return events;
    }

    private static string GetEventTitle(Settings settings, int period, string @class, string teacher, string room)
    {
      var components = settings.EventTitle.ToList();

      for (var i = 0; i < components.Count; i++)
      {
        if (components[i].Contains("{{period}}"))
        {
          components[i] = components[i].Replace("{{period}}", period.ToString());
        }
        if (components[i].Contains("{{class}}"))
        {
          if (String.IsNullOrWhiteSpace(@class))
          {
            components[i] = null;
            continue;
          }
          else
          {
            components[i] = components[i].Replace("{{class}}", @class);
          }
        }
        if (components[i].Contains("{{teacher}}"))
        {
          if (String.IsNullOrWhiteSpace(teacher))
          {
            components[i] = null;
            continue;
          }
          else
          {
            components[i] = components[i].Replace("{{teacher}}", teacher);
          }
        }
        if (components[i].Contains("{{room}}"))
        {
          if (String.IsNullOrWhiteSpace(room))
          {
            components[i] = null;
            continue;
          }
          else
          {
            components[i] = components[i].Replace("{{room}}", room);
          }
        }
      }

      return string.Join(' ', components);
    }

    private static Event CreateEvent(int period, string dayCode, IDictionary<string, Lesson> myLessons, Settings settings, DateTime date, Person person)
    {
      string title = $"P{period}. ";
      string room;

      if (myLessons.TryGetValue($"{dayCode}:{period}", out var lesson) && lesson.Class == blankingCode)
      {
        return null;
      }

      string @class = null, teacher = null;

      if (settings.OverrideDictionary.TryGetValue((date, period), out @class))
      {
        if (string.IsNullOrEmpty(@class))
        {
          return null;
        }
        room = null;
      }
      else if (lesson != null)
      {
        if (person.YearGroup == null)
        {
          var classYearGroup = lesson.YearGroup;
          if (classYearGroup != null && settings.StudyLeave.Any(o => o.Year == classYearGroup && o.StartDate <= date && o.EndDate >= date))
          {
            return null;
          }
        }
        @class = lesson.Class;
        if (settings.RenameDictionary.TryGetValue(@class, out var newTitle))
        {
          if (string.IsNullOrEmpty(newTitle))
          {
            return null;
          }
          @class = newTitle;
        }
        if (@class == blankingCode)
        {
          return null;
        }
        teacher = lesson.Teacher;
        room = lesson.Room;
      }
      else
      {
        return null;
      }

      title = GetEventTitle(settings, period, @class, teacher, room);

      var lessonTime = settings.LessonTimes[period - 1];
      var start = new DateTime(date.Year, date.Month, date.Day, lessonTime.StartHour, lessonTime.StartMinute, 0);
      var end = start.AddMinutes(lessonTime.Duration);

      return new Event
      {
        Summary = title,
        Location = room,
        Start = new EventDateTime { DateTime = start },
        End = new EventDateTime { DateTime = end }
      };
    }

  }
}