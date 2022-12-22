namespace Timer;

public enum SessionEventType
{
    Activate,
    Deactivate,
}

public record WorkingDay(DateOnly Day, IEnumerable<MergedSession> MergedSessions)
{
    public TimeSpan Duration => MergedSessions.Aggregate(TimeSpan.Zero, (l, s) => l + s.Duration);
    public override string ToString()
    {
        return $"{Day} - {Duration}{Environment.NewLine}{string.Join(Environment.NewLine, MergedSessions)}";
    }
}
public record MergedSession(IReadOnlyList<Session> Sessions)
{
    public DateTime StartTime => Sessions[0].StartTime;
    public DateTime EndTime => Sessions[^1].EndTime;

    public TimeSpan Duration => EndTime - StartTime;
}
public record Session(DateTime StartTime, DateTime EndTime)
{
    public TimeSpan Duration => EndTime - StartTime;

    public bool IsWorkingHours(TimeOnly workStart, TimeOnly workEnd) => TimeOnly.FromDateTime(StartTime) <= workEnd &&
                                                                        TimeOnly.FromDateTime(EndTime) >= workStart &&
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
        TimeOnly workStart,
        TimeOnly workEnd,
        TimeSpan workIdle,
        TimeSpan afterIdle
    )
    {
        var blocks = new List<List<Session>> { new List<Session>() };
        DateOnly? currDay = null;
        foreach (var session in sessions)
        {
            var sessionDay = DateOnly.FromDateTime(session.StartTime);
            currDay ??= sessionDay;
            if (currDay == sessionDay)
            {
                if(blocks[^1].Count > 0)
                {
                    var idleTimeout = session.IsWorkingHours(workStart, workEnd) ? workIdle : afterIdle;
                    var currEndTime = blocks[^1][^1].EndTime;
                    if ((session.StartTime - currEndTime) > idleTimeout)
                    {
                        blocks.Add(new List<Session>());
                    }
                }
            }
            else
            {
                yield return new WorkingDay(currDay.Value, blocks.Select(b => new MergedSession(b)));
                currDay = sessionDay;
                blocks = new List<List<Session>> { new List<Session>() };
            }
            blocks[^1].Add(session);
        }

        if(currDay.HasValue && blocks.Count > 0)
        {
            yield return new WorkingDay(currDay.Value, blocks.Select(b => new MergedSession(b)));
        }
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
                    System[EventID=4624] and
                    EventData/Data[@Name='TargetUserSid']='{sid}' and
                    EventData/Data[@Name='LogonType']!='4' <!-- filter scheduled task events -->
                ) or (
                    <!-- Local session logout and lock/unlock -->
                    System[
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
