using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Services;

public static class SessionReplayService
{
    private const double MinimumSpeed = 0.1;
    private const double MaximumSpeed = 16;
    private const double SeekSeconds = 10;
    private const int PollMilliseconds = 25;

    public static async Task Replay(IEnumerable<SessionEvent> events, double speed = 1, TextWriter? output = null,
        bool interactive = false, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(speed) || speed is < MinimumSpeed or > MaximumSpeed) throw new ArgumentException("Replay speed must be between 0.1 and 16.");
        output ??= System.Console.Out;
        if (!interactive)
        {
            var previous = 0d;
            var playback = Stopwatch.StartNew();
            foreach (var entry in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!double.IsFinite(entry.Time) || entry.Time < previous) throw new InvalidDataException("Recording events must have chronological timestamps.");
                // Schedule against one playback clock so writing output does not accumulate drift.
                var remaining = entry.Time / speed - playback.Elapsed.TotalSeconds;
                while (remaining > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(remaining, 1)), cancellationToken);
                    remaining = entry.Time / speed - playback.Elapsed.TotalSeconds;
                }
                await output.WriteAsync(entry.Data);
                await output.FlushAsync();
                previous = entry.Time;
            }
            return;
        }

        var timeline = events.ToList();
        var stopwatch = Stopwatch.StartNew();
        var lastTick = stopwatch.Elapsed.TotalSeconds;
        var cursor = 0d;
        var index = 0;
        var paused = false;
        System.Console.Error.WriteLine("Replay controls: Space pause, arrows seek +/-10s or change speed, Q quit.");
        while (index < timeline.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tick = stopwatch.Elapsed.TotalSeconds;
            if (!paused) cursor += (tick - lastTick) * speed;
            lastTick = tick;
            while (System.Console.KeyAvailable)
            {
                var key = System.Console.ReadKey(intercept: true).Key;
                switch (key)
                {
                    case ConsoleKey.Q: return;
                    case ConsoleKey.Spacebar: paused = !paused; break;
                    case ConsoleKey.UpArrow: speed = Math.Min(speed * 2, MaximumSpeed); break;
                    case ConsoleKey.DownArrow: speed = Math.Max(speed / 2, MinimumSpeed); break;
                    case ConsoleKey.RightArrow: cursor += SeekSeconds; break;
                    case ConsoleKey.LeftArrow:
                        cursor = Math.Max(cursor - SeekSeconds, 0); index = 0;
                        System.Console.Clear(); break;
                }
            }
            while (index < timeline.Count && timeline[index].Time <= cursor)
                await output.WriteAsync(timeline[index++].Data);
            await output.FlushAsync();
            if (index < timeline.Count) await Task.Delay(PollMilliseconds, cancellationToken);
        }
    }
}
