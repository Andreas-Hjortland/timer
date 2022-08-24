using System.Globalization;

var roundingOpt = new Option<TimeSpan>(
    aliases: new[] { "--rounding", "-r" },
    getDefaultValue: () => TimeSpan.FromMinutes(5),
    description: "How much to round start and end times"
);

var workStartOpt = new Option<TimeSpan>(
    aliases: new[] { "--work-start", "--ws", "-w" },
    getDefaultValue: () => TimeSpan.FromHours(6),
    description: "When the working hours start"
);

var workEndOpt = new Option<TimeSpan>(
    aliases: new[] { "--work-end", "--we", "-e" },
    getDefaultValue: () => TimeSpan.FromHours(18),
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
    waitOpt,
};
rootCommand.SetHandler((rounding, workStart, workEnd, workIdle, afterIdle, summary, waitForKeypress) =>
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
            h.UniqueSessions.First().StartTime.TimeOfDay,
            h.UniqueSessions.Last().EndTime.TimeOfDay,
            h.Duration
        ))));
        Console.WriteLine("└────────────┴─────────────┴─────────────┴──────────┘");
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
            foreach (var session in day.UniqueSessions)
            {
                Console.WriteLine(formatString, i++, session.StartTime.TimeOfDay, session.EndTime.TimeOfDay, session.Duration);
            }
            Console.WriteLine("├────────────┼──────────┼──────────┼──────────┤");
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture, 
                formatString, 
                "Sum", 
                day.UniqueSessions.First().StartTime.TimeOfDay,
                day.UniqueSessions.Last().EndTime.TimeOfDay,
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
}, roundingOpt, workStartOpt, workEndOpt, workIdleOpt, afterIdleOpt, summaryOpt, waitOpt);

await rootCommand.InvokeAsync(args);
