using NafMidi;

namespace NafReDaw;

public static class ArrangeSystem
{
    public const int BeatsPerPattern = 4;
    public const int TrackCount = 8;
    public const int StepCount = 64;
    public const int EmptyStep = -1;
    public const int MaxPatterns = 32;
    public const int StepPageCount = StepCount / LaunchpadLayout.GridColumns;

    private static CancellationTokenSource? _transportCts;

    public static int CurrentStep { get; private set; }

    public static bool IsPlaying => _transportCts is not null;

    public static Action? OnTransportTick { get; set; }

    public static int MsPerStep =>
        (int)(60_000.0 * BeatsPerPattern / (App.Project.Arrangement.Bpm * StepCount));

    public static void EnsureInitialized(Arrangement arrangement)
    {
        if (arrangement.Patterns.Count == 0)
        {
            arrangement.Patterns.Add(new Pattern());
        }

        if (arrangement.Patterns.Count > MaxPatterns)
        {
            arrangement.Patterns.RemoveRange(MaxPatterns, arrangement.Patterns.Count - MaxPatterns);
        }

        foreach (var pattern in arrangement.Patterns)
        {
            EnsurePatternInitialized(pattern);
        }

        if (App.ActivePatternIndex >= arrangement.Patterns.Count)
        {
            App.ActivePatternIndex = Math.Max(0, arrangement.Patterns.Count - 1);
        }
    }

    public static Pattern GetActivePattern(Arrangement arrangement)
    {
        EnsureInitialized(arrangement);
        if (arrangement.Patterns.Count == 0)
        {
            arrangement.Patterns.Add(new Pattern());
        }

        if (App.ActivePatternIndex < 0 || App.ActivePatternIndex >= arrangement.Patterns.Count)
        {
            App.ActivePatternIndex = 0;
        }

        return arrangement.Patterns[App.ActivePatternIndex];
    }

    public static int[][] CreateEmptySteps()
    {
        var steps = new int[TrackCount][];
        for (var track = 0; track < TrackCount; track++)
        {
            steps[track] = new int[StepCount];
            Array.Fill(steps[track], EmptyStep);
        }

        return steps;
    }

    public static void EnsurePatternInitialized(Pattern pattern)
    {
        if (pattern.Steps.Length != TrackCount)
        {
            pattern.Steps = CreateEmptySteps();
            return;
        }

        for (var track = 0; track < TrackCount; track++)
        {
            if (pattern.Steps[track] is null || pattern.Steps[track].Length != StepCount)
            {
                var resized = new int[StepCount];
                Array.Fill(resized, EmptyStep);
                if (pattern.Steps[track] is not null)
                {
                    var copyCount = Math.Min(pattern.Steps[track].Length, StepCount);
                    Array.Copy(pattern.Steps[track], resized, copyCount);
                }

                pattern.Steps[track] = resized;
            }
        }
    }

    public static void StartTransport()
    {
        if (IsPlaying)
        {
            return;
        }

        EnsureInitialized(App.Project.Arrangement);
        _transportCts = new CancellationTokenSource();
        var cts = _transportCts;
        CurrentStep = 0;
        _ = RunTransportAsync(cts.Token);
        App.Output($"Arrangement playing ({App.Project.Arrangement.Bpm} BPM, {MsPerStep} ms/step).");
        NotifyTransportTick();
    }

    public static void StopTransport()
    {
        if (!IsPlaying)
        {
            return;
        }

        _transportCts?.Cancel();
        _transportCts?.Dispose();
        _transportCts = null;
        CurrentStep = 0;
        App.AudioEngine.StopAllPlayback();
        App.CurrentlyPlayingSampleHandle = -1;
        App.CurrentlyPlayingNote = -1;
        App.Output("Arrangement stopped.");
        NotifyTransportTick();
    }

    public static void ToggleTransport()
    {
        if (IsPlaying)
        {
            StopTransport();
        }
        else
        {
            StartTransport();
        }
    }

    public static bool CanSelectNextPattern
    {
        get
        {
            EnsureInitialized(App.Project.Arrangement);
            return App.ActivePatternIndex < MaxPatterns - 1;
        }
    }

    public static bool CanSelectPreviousPattern
    {
        get
        {
            EnsureInitialized(App.Project.Arrangement);
            return App.ActivePatternIndex > 0;
        }
    }

    public static bool CanNextStepPage => App.ArrangeStepPage < StepPageCount - 1;

    public static bool CanPreviousStepPage => App.ArrangeStepPage > 0;

    public static void SelectNextPattern()
    {
        if (!CanSelectNextPattern)
        {
            return;
        }

        var patterns = App.Project.Arrangement.Patterns;
        if (App.ActivePatternIndex >= patterns.Count - 1)
        {
            if (patterns.Count >= MaxPatterns)
            {
                return;
            }

            patterns.Add(new Pattern { Name = $"Pattern {patterns.Count + 1}" });
            App.ChangesMade = true;
        }

        App.ActivePatternIndex++;
        App.Output($"Pattern {App.ActivePatternIndex + 1}/{patterns.Count}: {GetActivePattern(App.Project.Arrangement).Name}");
    }

    public static void SelectPreviousPattern()
    {
        if (!CanSelectPreviousPattern)
        {
            return;
        }

        App.ActivePatternIndex--;
        var count = App.Project.Arrangement.Patterns.Count;
        App.Output($"Pattern {App.ActivePatternIndex + 1}/{count}: {GetActivePattern(App.Project.Arrangement).Name}");
    }

    public static void SelectFirstPattern()
    {
        EnsureInitialized(App.Project.Arrangement);
        if (App.ActivePatternIndex == 0)
        {
            return;
        }

        App.ActivePatternIndex = 0;
        var count = App.Project.Arrangement.Patterns.Count;
        App.Output($"Pattern {App.ActivePatternIndex + 1}/{count}: {GetActivePattern(App.Project.Arrangement).Name}");
    }

    public static void NextStepPage()
    {
        if (!CanNextStepPage)
        {
            return;
        }

        App.ArrangeStepPage++;
        App.Output($"Steps {GetVisibleStep(0) + 1}-{GetVisibleStep(LaunchpadLayout.GridColumns - 1) + 1}");
    }

    public static void PreviousStepPage()
    {
        if (!CanPreviousStepPage)
        {
            return;
        }

        App.ArrangeStepPage--;
        App.Output($"Steps {GetVisibleStep(0) + 1}-{GetVisibleStep(LaunchpadLayout.GridColumns - 1) + 1}");
    }

    public static void FirstStepPage()
    {
        if (App.ArrangeStepPage == 0)
        {
            return;
        }

        App.ArrangeStepPage = 0;
        App.Output($"Steps {GetVisibleStep(0) + 1}-{GetVisibleStep(LaunchpadLayout.GridColumns - 1) + 1}");
    }

    public static int GetVisibleStep(int column) =>
        App.ArrangeStepPage * LaunchpadLayout.GridColumns + column;

    public static int GetCellNote(int track, int column)
    {
        var pattern = GetActivePattern(App.Project.Arrangement);
        EnsurePatternInitialized(pattern);
        if (track < 0 || track >= TrackCount || column < 0 || column >= LaunchpadLayout.GridColumns)
        {
            return EmptyStep;
        }

        var step = GetVisibleStep(column);
        return pattern.Steps[track][step];
    }

    public static bool IsCellAssigned(int track, int column) =>
        GetCellNote(track, column) != EmptyStep;

    public static bool IsPlayheadColumn(int column) =>
        IsPlaying && GetVisibleStep(column) == CurrentStep;

    private static async Task RunTransportAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pattern = GetActivePattern(App.Project.Arrangement);
                EnsurePatternInitialized(pattern);

                if (CurrentStep >= StepCount || !HasAssignedStepsFrom(pattern, CurrentStep))
                {
                    if (!AdvanceToNextPattern())
                    {
                        return;
                    }

                    continue;
                }

                PlayCurrentStep(pattern);
                NotifyTransportTick();
                await Task.Delay(MsPerStep, cancellationToken);
                CurrentStep++;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool HasAssignedStepsFrom(Pattern pattern, int fromStep)
    {
        for (var step = fromStep; step < StepCount; step++)
        {
            for (var track = 0; track < TrackCount; track++)
            {
                if (pattern.Steps[track][step] != EmptyStep)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <returns>False if transport was stopped (no next pattern).</returns>
    private static bool AdvanceToNextPattern()
    {
        var nextIndex = App.ActivePatternIndex + 1;
        if (nextIndex >= App.Project.Arrangement.Patterns.Count)
        {
            StopTransport();
            return false;
        }

        App.ActivePatternIndex = nextIndex;
        CurrentStep = 0;
        App.Output($"Playing pattern {App.ActivePatternIndex + 1}.");
        return true;
    }

    private static void NotifyTransportTick() => OnTransportTick?.Invoke();

    private static void PlayCurrentStep(Pattern pattern)
    {
        var step = CurrentStep;
        if (step < 0 || step >= StepCount)
        {
            return;
        }

        for (var track = 0; track < TrackCount; track++)
        {
            var note = pattern.Steps[track][step];
            if (note == EmptyStep)
            {
                continue;
            }

            var loadedSample = App.Project.LoadedSamples.FirstOrDefault(s => s.Note == note);
            if (loadedSample?.InMemorySample is null)
            {
                continue;
            }

            // Mix all notes on this step; do not cut other track voices.
            AudioSystem.PlayLoadedSample(loadedSample, () => { }, replaceCurrent: false);
        }
    }
}
