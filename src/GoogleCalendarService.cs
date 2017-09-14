using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Calendar.v3.Data;

namespace makecal
{
  public class GoogleCalendarService
  {
    private static readonly string ApplicationName = "makecal";
    private static readonly string CalendarName = "My timetable";
    private static readonly string CalendarColour = "#fbe983";

    private CalendarService Service { get; set; }

    public GoogleCalendarService(string user)
    {
      if (Credential == null)
      {
        throw new Exception("CalendarService must be configured before instantiation.");
      }

      var credential = Credential
        .CreateScoped(CalendarService.Scope.Calendar)
        .CreateWithUser(user);

      var initializer = new BaseClientService.Initializer
      {
        HttpClientInitializer = credential,
        ApplicationName = ApplicationName
      };

      Service = new CalendarService(initializer);
    }

    public async Task<string> GetTimetableCalendarIdAsync()
    {
      var request = Service.CalendarList.List();
      request.Fields = "items(id,summary)";
      var calendars = await request.ExecuteAsync();
      return calendars.Items?.FirstOrDefault(c => c.Summary == CalendarName)?.Id;
    }

    public async Task<string> CreateTimetableCalendarAsync()
    {
      var calendar = new Calendar { Summary = CalendarName };
      var id = (await Service.Calendars.Insert(calendar).ExecuteAsync()).Id;
      await Service.CalendarList.SetColor(id, CalendarColour).ExecuteAsync();
      return id;
    }

    public async Task<IList<Event>> GetFutureEvents(string calendar, DateTime after, string fields)
    {
      var request = Service.Events.List(calendar);
      request.Fields = $"nextPageToken,items({fields})";
      return await request.FetchAllAsync(after: after);
    }

    public async Task DeleteEventsAsync(string calendar, IEnumerable<string> events)
    {
      var batch = new UnlimitedBatch(Service);
      foreach (var @event in events)
      {
        batch.Queue(Service.Events.Delete(calendar, @event));
      }
      await batch.ExecuteAsync();
    }

    public async Task DeleteEventsAsync(string calendar, IEnumerable<Event> events)
    {
      await DeleteEventsAsync(calendar, events.Select(e => e.Id));
    }

    public async Task InsertEventsAsync(string calendar, IEnumerable<Event> events)
    {
      var batch = new UnlimitedBatch(Service);
      foreach (var @event in events)
      {
        batch.Queue(Service.Events.Insert(@event, calendar));
      }
      await batch.ExecuteAsync();
    }

    private static GoogleCredential Credential;

    public static void Configure(string key)
    {
      Credential = GoogleCredential.FromJson(key);
    }
  }
}
