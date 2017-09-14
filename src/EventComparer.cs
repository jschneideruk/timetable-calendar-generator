using Google.Apis.Calendar.v3.Data;
using System;
using System.Collections.Generic;

namespace makecal
{
  public class EventComparer : IEqualityComparer<Event>
  {
    public bool Equals(Event x, Event y)
    {
      return x.Start.DateTime == y.Start.DateTime &&
            x.End.DateTime == y.End.DateTime &&
            x.Summary == y.Summary &&
            (x.Location == y.Location ||
            (String.IsNullOrWhiteSpace(x.Location) &&
            String.IsNullOrWhiteSpace(y.Location)));
    }

    public int GetHashCode(Event obj)
    {
      return
        (obj.Start?.DateTime?.GetHashCode() ?? 0) +
        (obj.End?.DateTime?.GetHashCode() ?? 0) +
        (obj.Summary?.GetHashCode() ?? 0) +
        (obj.Location ?? "").GetHashCode();
    }
  }
}
