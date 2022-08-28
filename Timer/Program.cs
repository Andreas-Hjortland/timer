using System.Globalization;

var roundingOpt = new Option<TimeSpan>(
    aliases: new[] { "--rounding", "-r" },
    getDefaultValue: () => TimeSpan.FromMinutes(5),
    description: "How much to round start and end times"
);

var workStartOpt = new Option<TimeOnly>(
    aliases: new[] { "--work-start", "--ws", "-w" },
    getDefaultValue: () => new TimeOnly(6, 0),
    description: "When the working hours start"
);

var workEndOpt = new Option<TimeOnly>(
    aliases: new[] { "--work-end", "--we", "-e" },
    getDefaultValue: () => new TimeOnly(18, 0),
    description: "When the working hours end"
);

var workIdleOpt = new Option<TimeSpan>(
    aliases: new[] { "--work-idle", "--wi", "-i" },
    getDefaultValue: () => TimeSpan.FromHours(4),
    description: "How long to be idle before starting a new session if the current was started within the working hours"
);

var afterIdleOpt = new Option<TimeSpan>(
    aliases: new[] { "--non-work-idle", "-n" },
    getDefaultValue: () => TimeSpan.FromMinutes(15),
    description: "How long to be idle before starting a new session if the current was started outside the working hours"
);

var summaryOpt = new Option<bool>(
    aliases: new [] { "--summary", "-s" },
    description: "Only show calculated times for each day instead of a table of all the sessions"
);
var verboseOpt = new Option<bool>(
    aliases: new [] { "--verbose", "-v" },
    description: "Show every session with a marker for which is merged"
);
var waitOpt = new Option<bool>(
    aliases: new [] { "--wait" },
    description: "Wait for keypress before exiting application. Useful if the console host window will close on exit"
);

var rootCommand = new RootCommand("Session length calculator.")
{
    roundingOpt,
    workStartOpt,
    workEndOpt,
    workIdleOpt,
    afterIdleOpt,
    summaryOpt,
    verboseOpt,
    waitOpt,
};
rootCommand.SetHandler((rounding, workStart, workEnd, workIdle, afterIdle, summary, waitForKeypress, verbose) =>
{
    var parsed = EventLogParser.Parse(WindowsIdentity.GetCurrent());
    var sessions = parsed.ToSessions(rounding).ToList();
    var workingHours = sessions.CalculateWorkingHours(workStart, workEnd, workIdle, afterIdle);
    if (summary)
    {
        const string formatString = "│ {0,-10} │ {1,11} │ {2,11} │ {3,8} │";
        Console.WriteLine("┌────────────┬─────────────┬─────────────┬──────────┐");
        Console.WriteLine(formatString, "Day", "First login", "Last logout", "Duration");
        Console.WriteLine("├────────────┼─────────────┼─────────────┼──────────┤");
        Console.WriteLine(string.Join(Environment.NewLine, workingHours.Select(h => string.Format(
            CultureInfo.InvariantCulture,
            formatString,
            h.Day.ToString("o"),
            h.MergedSessions.First().StartTime.TimeOfDay,
            h.MergedSessions.Last().EndTime.TimeOfDay,
            h.Duration
        ))));
        Console.WriteLine("└────────────┴─────────────┴─────────────┴──────────┘");
    }
    else if(verbose)
    {
        foreach (var day in workingHours)
        {
            const string formatString = "│ {0,10} │ {1,8} │ {2,8} │ {3,8} │";
            Console.WriteLine("┌────────────┬──────────┬──────────┬──────────┐");
            Console.WriteLine(formatString, day.Day.ToString("o"), "Login", "Logout", "Duration");

            int i = 1;
            foreach (var mergedSession in day.MergedSessions)
            {
                int j = 1;
                Console.WriteLine("├────────────┼──────────┼──────────┼──────────┤");
                if(mergedSession.Sessions.Count > 1)
                {
                    foreach(var session in mergedSession.Sessions)
                    {
                        Console.WriteLine(formatString, $"{i}.{j++}", session.StartTime.TimeOfDay, session.EndTime.TimeOfDay, session.Duration);
                    }
                }
                Console.WriteLine(formatString, $"Sum {i++}", mergedSession.StartTime.TimeOfDay, mergedSession.EndTime.TimeOfDay, mergedSession.Duration);
            }
            Console.WriteLine("╞════════════╪══════════╪══════════╪══════════┤");
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                formatString,
                "Sum",
                day.MergedSessions.First().StartTime.TimeOfDay,
                day.MergedSessions.Last().EndTime.TimeOfDay,
                day.Duration
            ));
            Console.WriteLine("└────────────┴──────────┴──────────┴──────────┘" + Environment.NewLine);
        }
    }
    else
    {
        foreach (var day in workingHours)
        {
            const string formatString = "│ {0,10} │ {1,8} │ {2,8} │ {3,8} │";
            Console.WriteLine("┌────────────┬──────────┬──────────┬──────────┐");
            Console.WriteLine(formatString, day.Day.ToString("o"), "Login", "Logout", "Duration");
            Console.WriteLine("├────────────┼──────────┼──────────┼──────────┤");

            int i = 1;
            foreach (var session in day.MergedSessions)
            {
                Console.WriteLine(formatString, i++, session.StartTime.TimeOfDay, session.EndTime.TimeOfDay, session.Duration);
            }
            Console.WriteLine("├────────────┼──────────┼──────────┼──────────┤");
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                formatString,
                "Sum",
                day.MergedSessions.First().StartTime.TimeOfDay,
                day.MergedSessions.Last().EndTime.TimeOfDay,
                day.Duration
            ));
            Console.WriteLine("└────────────┴──────────┴──────────┴──────────┘" + Environment.NewLine);
        }
    }

    if (waitForKeypress)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}, roundingOpt, workStartOpt, workEndOpt, workIdleOpt, afterIdleOpt, summaryOpt, waitOpt, verboseOpt);

await rootCommand.InvokeAsync(args);
