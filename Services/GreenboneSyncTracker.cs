namespace ATLAS.Services;

/// <summary>
/// Singleton that holds live Greenbone sync state. Updated by GreenboneController's
/// background task, read by the polling endpoints on Assets and Vulnerabilities pages.
/// </summary>
public class GreenboneSyncTracker
{
    private readonly object _lock = new();

    public bool      IsRunning       { get; private set; }
    public bool      IsDone          { get; private set; }
    public string    Phase           { get; private set; } = "Idle";
    public int       ResultsFetched  { get; private set; }
    public int       AssetsCreated   { get; private set; }
    public int       AssetsUpdated   { get; private set; }
    public int       FindingsAdded   { get; private set; }
    public int       FindingsUpdated { get; private set; }
    public string?   Error           { get; private set; }
    public DateTime? StartedAt       { get; private set; }

    /// <summary>
    /// Set when a sync completes successfully. Persists in memory so delta
    /// queries can filter to findings seen in the most recent sync.
    /// </summary>
    public DateTime? LastSyncAt { get; private set; }

    public int ElapsedSeconds =>
        StartedAt.HasValue ? (int)(DateTime.UtcNow - StartedAt.Value).TotalSeconds : 0;

    // ── Mutation methods (all lock-protected) ─────────────────────────────────

    public void Start()
    {
        lock (_lock)
        {
            IsRunning       = true;
            IsDone          = false;
            Phase           = "Connecting";
            ResultsFetched  = 0;
            AssetsCreated   = 0;
            AssetsUpdated   = 0;
            FindingsAdded   = 0;
            FindingsUpdated = 0;
            Error           = null;
            StartedAt       = DateTime.UtcNow;
        }
    }

    public void SetPhase(string phase)
    {
        lock (_lock) { Phase = phase; }
    }

    public void UpdateFetch(int fetched)
    {
        lock (_lock) { ResultsFetched = fetched; }
    }

    public void UpdateSaved(int assetsCreated, int assetsUpdated, int findingsAdded, int findingsUpdated)
    {
        lock (_lock)
        {
            AssetsCreated   = assetsCreated;
            AssetsUpdated   = assetsUpdated;
            FindingsAdded   = findingsAdded;
            FindingsUpdated = findingsUpdated;
        }
    }

    public void Complete(string? error = null)
    {
        lock (_lock)
        {
            IsRunning = false;
            IsDone    = true;
            Phase     = error == null ? "Done" : "Failed";
            Error     = error;
            if (error == null) LastSyncAt = DateTime.UtcNow;
        }
    }

    /// <summary>Returns an anonymous object safe to serialize as JSON for the polling endpoint.</summary>
    public object GetStatus() => new
    {
        isRunning       = IsRunning,
        isDone          = IsDone,
        phase           = Phase,
        resultsFetched  = ResultsFetched,
        assetsCreated   = AssetsCreated,
        assetsUpdated   = AssetsUpdated,
        findingsAdded   = FindingsAdded,
        findingsUpdated = FindingsUpdated,
        error           = Error,
        elapsedSeconds  = ElapsedSeconds,
        lastSyncAt      = LastSyncAt?.ToString("O")
    };
}
