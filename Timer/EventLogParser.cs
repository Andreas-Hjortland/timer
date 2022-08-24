namespace Timer;

public enum SessionEventType
{
    Activate,
    Deactivate,
}

public record WorkingDay(DateOnly Day, IEnumerable<Session> UniqueSessions)
{
    public TimeSpan Duration => UniqueSessions.Aggregate(TimeSpan.Zero, (l, s) => l + s.Duration);
    public override string ToString()
    {
        return $"{Day} - {Duration}{Environment.NewLine}{string.Join(Environment.NewLine, UniqueSessions)}";
    }
}
public record Session(DateTime StartTime, DateTime EndTime)
{
    public TimeSpan Duration => EndTime - StartTime;

    public bool IsWorkingHours(TimeSpan workStart, TimeSpan workEnd) => StartTime.TimeOfDay <= workEnd &&
                                                                        EndTime.TimeOfDay >= workStart &&
                                                                        !StartTime.IsWeekend();
}

public record Event(DateTime EventTime, SessionEventType Type);

public static class EventLogParser
{
    public static bool IsWeekend(this DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => true,
        DayOfWeek.Sunday => true,
        _ => false
    };
    
    private static DateTime Floor(this DateTime dateTime, TimeSpan interval)
    {
        var truncated = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, 0, dateTime.Kind);
        // TODO: Truncate seconds
        return truncated.AddTicks(-(truncated.Ticks % interval.Ticks));
    }

    private static DateTime Ceiling(this DateTime dateTime, TimeSpan interval)
    {
        var truncated = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, 0, dateTime.Kind);
        var overflow = truncated.Ticks % interval.Ticks;

        return overflow == 0 ? truncated : truncated.AddTicks(interval.Ticks - overflow);
    }

    public static IEnumerable<Session> ToSessions(this IEnumerable<Event> events, TimeSpan rounding)
    {
        DateTime? firstEvtDate = null;

        DateTime? currFromTime = null;
        DateTime? currToTime = null;
        foreach (var evt in events)
        {
            // Filter events from the first day since we cannot know that we have all the history from that day anymore
            if(evt.EventTime.Date == (firstEvtDate ??= evt.EventTime.Date)) continue;
            if(evt.Type == SessionEventType.Deactivate && currFromTime == null) continue; // Want to start at the first activate event

            switch (evt.Type)
            {
                case SessionEventType.Activate:
                    var rounded = evt.EventTime.Floor(rounding);
                    if (currFromTime != null && currToTime != null)
                    {
                        if (currToTime >= rounded)
                        {
                            currToTime = null;
                            continue;
                        }

                        yield return new Session(currFromTime.Value, currToTime.Value);
                        currToTime = null;
                    }

                    currFromTime = rounded;
                    break;
                case SessionEventType.Deactivate:
                    currToTime = evt.EventTime.Ceiling(rounding);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(events), $"Unhandled event type {evt.Type}");
            }
        }

        if (currFromTime != null && currToTime == null)
        {
            yield return new Session(currFromTime.Value, DateTime.Now.Ceiling(rounding));
        }
    }

    public static IEnumerable<WorkingDay> CalculateWorkingHours(
        this IEnumerable<Session> sessions, 
        TimeSpan workStart, 
        TimeSpan workEnd, 
        TimeSpan workIdle, 
        TimeSpan afterIdle
    )
    {
        var blocks = new List<Session>();
        DateOnly? currDay = null;
        DateTime? currStartTime = null;
        DateTime? currEndTime = null;
        foreach (var session in sessions)
        {
            var sessionDay = DateOnly.FromDateTime(session.StartTime);
            currDay ??= sessionDay;
            if (currDay != sessionDay)
            {
                if (currStartTime != null && currEndTime != null)
                {
                    blocks.Add(new Session(currStartTime.Value, currEndTime.Value));
                }

                yield return new WorkingDay(currDay.Value, blocks);
                currDay = sessionDay;
                currStartTime = session.StartTime;
                currEndTime = session.EndTime;
                blocks = new List<Session>();
            }
            else if (currStartTime != null && currEndTime != null)
            {
                var idleTimeout = session.IsWorkingHours(workStart, workEnd) ? workIdle : afterIdle;
                if ((session.StartTime - currEndTime) > idleTimeout)
                {
                    blocks.Add(new Session(currStartTime.Value, currEndTime.Value));
                    currStartTime = session.StartTime;
                }

                currEndTime = session.EndTime;
            }
            else
            {
                currStartTime = session.StartTime;
                currEndTime = session.EndTime;
            }
        }

        if (!currDay.HasValue) yield break;
        if (currStartTime.HasValue && currEndTime.HasValue)
        {
            blocks.Add(new Session(currStartTime.Value, currEndTime.Value));
        }
        yield return new WorkingDay(currDay.Value, blocks);
    }

    public static IEnumerable<Event> Parse(WindowsIdentity id)
    {
        var sid = id.User!.Value;
        var username = id.Name.Split('\\', 2);
        var q = new EventLogQuery("Security", PathType.LogName, $@"<QueryList>
          <Query Id='0'>
            <Select Path='Security'>
                *[(
                    <!-- Local session -->
                    System[
                        EventID=4624 or
                        EventID=4647 or
                        EventID=4800 or
                        EventID=4801
                    ] and
                    EventData/Data[@Name='TargetUserSid']='{sid}'
                ) or (
                    <!-- Remote session -->
                    System[
                        EventID=4778 or
                        EventID=4779
                    ] and
                    EventData/Data[@Name='AccountName']='{username[1]}' and
                    EventData/Data[@Name='AccountDomain']='{username[0]}'
                )]
            </Select>
          </Query>
        </QueryList>");

        using var reader = new EventLogReader(q);
        SessionEventType? lastType = null;
        while (reader.ReadEvent() is { } rec)
        {
            if (rec.TimeCreated.HasValue)
            {
                var type = rec.Id switch
                {
                    4624 => SessionEventType.Activate, // LogOn
                    4778 => SessionEventType.Activate, // Remote Session Connect
                    4801 => SessionEventType.Activate, // SessionUnlock
                    4647 => SessionEventType.Deactivate, // LogOut
                    4779 => SessionEventType.Deactivate, // Remote Session Disconnect
                    4800 => SessionEventType.Deactivate, // SessionLock
                    _ => throw new ArgumentException($"Invalid event id {rec.Id}")
                };
                if (type != lastType)
                {
                    yield return new Event(rec.TimeCreated.Value, type);
                }
                lastType = type;
            }
        }
    }
}