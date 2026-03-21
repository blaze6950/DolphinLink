namespace FlipperZero.NET.Tests.Infrastructure;

/// <summary>
/// Lightweight step runner for manual-assisted tests.
///
/// Provides two step types:
/// <list type="bullet">
///   <item><see cref="Step"/> — an automated step that executes an action and
///         records pass/fail with elapsed time.</item>
///   <item><see cref="ManualStep"/> — pauses the test, prints an instruction
///         to the console, and waits for the operator to press Enter (or for
///         the optional timeout to expire).</item>
/// </list>
///
/// After all steps are done, call <see cref="PrintReport"/> to emit a summary
/// table to the console.
///
/// Usage:
/// <code>
/// var runner = new ManualTestRunner(output);
/// await runner.Step("Open stream", async () => { ... });
/// await runner.ManualStep("Point the IR remote at the Flipper and press a button",
///     timeout: TimeSpan.FromSeconds(30));
/// await runner.Step("Verify event received", async () => { ... });
/// runner.PrintReport();
/// </code>
/// </summary>
public sealed class ManualTestRunner
{
    private readonly ITestOutputHelper? _output;
    private readonly List<StepResult> _results = [];

    public ManualTestRunner(ITestOutputHelper? output = null)
    {
        _output = output;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes an automated step.  Catches exceptions, records them as a
    /// failure, and re-throws so xUnit marks the test as failed.
    /// </summary>
    /// <param name="description">Short human-readable description of what this step does.</param>
    /// <param name="action">The step body to execute.</param>
    public async Task Step(string description, Func<Task> action)
    {
        Log($"  [ RUN ] {description}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await action().ConfigureAwait(false);
            sw.Stop();
            var result = new StepResult(description, StepStatus.Passed, sw.Elapsed, null);
            _results.Add(result);
            Log($"  [ OK  ] {description} ({sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            var result = new StepResult(description, StepStatus.Failed, sw.Elapsed, ex.Message);
            _results.Add(result);
            Log($"  [FAIL ] {description} ({sw.ElapsedMilliseconds} ms): {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Pauses the test and waits for the operator to press Enter.
    ///
    /// Prints the instruction to both the console and the xUnit output helper
    /// so it appears in test runner output.  If <paramref name="timeout"/> is
    /// given and the operator does not respond in time the step is recorded as
    /// <see cref="StepStatus.TimedOut"/> and the method returns (does not
    /// throw) — the caller's subsequent automated steps decide whether the test
    /// should fail.
    /// </summary>
    /// <param name="instruction">What the operator must physically do.</param>
    /// <param name="timeout">How long to wait for the operator. <c>null</c> = wait indefinitely.</param>
    public async Task ManualStep(string instruction, TimeSpan? timeout = null)
    {
        var label = $"  [WAIT ] MANUAL: {instruction}";
        Log(label);
        Console.WriteLine();
        Console.WriteLine(label);

        if (timeout.HasValue)
        {
            Console.WriteLine($"         (press Enter within {timeout.Value.TotalSeconds:0} s, or wait for timeout)");
        }
        else
        {
            Console.WriteLine("         (press Enter to continue)");
        }

        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool confirmed;

        if (timeout.HasValue)
        {
            confirmed = await WaitForEnterWithTimeout(timeout.Value).ConfigureAwait(false);
        }
        else
        {
            // No timeout — block until Enter is pressed.
            await Task.Run(() => Console.ReadLine()).ConfigureAwait(false);
            confirmed = true;
        }

        sw.Stop();
        var status = confirmed ? StepStatus.ManualConfirmed : StepStatus.TimedOut;
        var statusLabel = confirmed ? "CONFIRMED" : "TIMED OUT";
        _results.Add(new StepResult($"MANUAL: {instruction}", status, sw.Elapsed, null));
        Log($"  [{statusLabel,9}] {instruction} ({sw.ElapsedMilliseconds} ms)");
    }

    /// <summary>
    /// Prints a formatted summary of all step results to the console and the
    /// xUnit output helper.
    /// </summary>
    public void PrintReport()
    {
        Log(string.Empty);
        Log("  ---- Manual Test Report ----");

        int passed = 0, failed = 0, confirmed = 0, timedOut = 0;

        foreach (var r in _results)
        {
            var icon = r.Status switch
            {
                StepStatus.Passed => "OK  ",
                StepStatus.Failed => "FAIL",
                StepStatus.ManualConfirmed => "CONF",
                StepStatus.TimedOut => "TIME",
                _ => "????"
            };

            Log($"  [{icon}] {r.Description,-60} {r.Elapsed.TotalMilliseconds,6:0} ms" +
                (r.ErrorMessage is not null ? $"  => {r.ErrorMessage}" : string.Empty));

            switch (r.Status)
            {
                case StepStatus.Passed: passed++; break;
                case StepStatus.Failed: failed++; break;
                case StepStatus.ManualConfirmed: confirmed++; break;
                case StepStatus.TimedOut: timedOut++; break;
            }
        }

        Log($"  ----------------------------");
        Log($"  Passed: {passed}  Failed: {failed}  " +
            $"Confirmed: {confirmed}  Timed-out: {timedOut}");
        Log(string.Empty);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void Log(string message)
    {
        _output?.WriteLine(message);
        Console.WriteLine(message);
    }

    /// <summary>
    /// Waits for Enter on stdin or for <paramref name="timeout"/> to elapse,
    /// whichever comes first.
    /// </summary>
    /// <returns><c>true</c> if Enter was pressed; <c>false</c> if timed out.</returns>
    private static async Task<bool> WaitForEnterWithTimeout(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            await Task.Run(() =>
            {
                // Poll stdin with a short sleep so we can check the
                // cancellation token between reads.
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            return;
                        }
                    }
                    Thread.Sleep(100);
                }
            }, cts.Token).ConfigureAwait(false);

            return true; // Enter was pressed
        }
        catch (OperationCanceledException)
        {
            return false; // Timed out
        }
    }

    // -------------------------------------------------------------------------
    // Supporting types
    // -------------------------------------------------------------------------

    private enum StepStatus { Passed, Failed, ManualConfirmed, TimedOut }

    private sealed record StepResult(
        string Description,
        StepStatus Status,
        TimeSpan Elapsed,
        string? ErrorMessage);
}
