using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ATLAS.Data;
using ATLAS.Models;
using ATLAS.Services;

namespace ATLAS.Controllers;

[Route("Greenbone")]
public class GreenboneController(
    IServiceScopeFactory scopeFactory,
    GreenboneSyncTracker tracker,
    GreenboneService gbService,
    ILogger<GreenboneController> logger) : Controller
{
    // ── Trigger sync ──────────────────────────────────────────────────────────

    [HttpPost("Sync"), ValidateAntiForgeryToken]
    public IActionResult Sync()
    {
        if (tracker.IsRunning)
            return Json(new { started = false, message = "A Greenbone sync is already running." });

        tracker.Start();

        // Fire-and-forget on a thread-pool thread with its own DI scope
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db      = scope.ServiceProvider.GetRequiredService<AtlasContext>();
                var service = scope.ServiceProvider.GetRequiredService<GreenboneService>();
                await RunSyncAsync(db, service);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Greenbone sync failed with unhandled exception");
                tracker.Complete(ex.Message);
            }
        });

        return Json(new { started = true });
    }

    // ── Polling endpoint ──────────────────────────────────────────────────────

    [HttpGet("Status")]
    public IActionResult Status() => Json(tracker.GetStatus());

    // ── Test connection ───────────────────────────────────────────────────────

    [HttpGet("TestConnection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var msg = await gbService.TestConnectionAsync(HttpContext.RequestAborted);
            return Json(new { ok = true, message = msg });
        }
        catch (Exception ex)
        {
            var details = new System.Text.StringBuilder();
            details.AppendLine($"[{ex.GetType().Name}] {ex.Message}");
            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null)
            {
                details.AppendLine($"  Caused by [{inner.GetType().Name}] {inner.Message}");
                inner = inner.InnerException;
                if (++depth > 5) break;
            }
            logger.LogError(ex, "Greenbone TestConnection failed");
            return Json(new { ok = false, message = details.ToString().Trim() });
        }
    }

    // ── Delta endpoints (called by JS after sync completes) ───────────────────

    /// <summary>
    /// Returns assets that gained new or updated Greenbone findings in the most recent sync,
    /// ordered by worst CVSS score descending.
    /// </summary>
    [HttpGet("AssetDelta")]
    public async Task<IActionResult> AssetDelta()
    {
        if (tracker.LastSyncAt == null)
            return Json(new { items = Array.Empty<object>() });

        using var scope = scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AtlasContext>();
        var since = tracker.LastSyncAt.Value.AddMinutes(-2);

        // Load client-side so we can use pattern matching on Asset subtypes
        var avs = await db.AssetVulnerabilities
            .Where(av => av.Source == "Greenbone" && av.LastSeenAt >= since)
            .Include(av => av.Asset)
            .Include(av => av.Vulnerability)
            .ToListAsync();

        var items = avs
            .GroupBy(av => av.AssetId)
            .Select(g =>
            {
                var worst = g.OrderByDescending(av => av.Vulnerability.CvssScore ?? 0).First();
                return new
                {
                    assetId       = g.Key,
                    assetName     = g.First().Asset.Name,
                    ip            = (g.First().Asset as Computer)?.IpAddress,
                    findingsTotal = g.Count(),
                    worstCvss     = worst.Vulnerability.CvssScore,
                    worstSeverity = worst.Vulnerability.Severity.ToString(),
                    worstCveId    = worst.Vulnerability.CveId
                };
            })
            .OrderByDescending(x => x.worstCvss)
            .ToList();

        return Json(new { items });
    }

    /// <summary>
    /// Returns AssetVulnerability records created/updated in the most recent Greenbone sync,
    /// ordered by CVSS score descending (Critical first).
    /// </summary>
    [HttpGet("FindingDelta")]
    public async Task<IActionResult> FindingDelta()
    {
        if (tracker.LastSyncAt == null)
            return Json(new { items = Array.Empty<object>() });

        using var scope = scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AtlasContext>();
        var since = tracker.LastSyncAt.Value.AddMinutes(-2);

        var items = await db.AssetVulnerabilities
            .Where(av => av.Source == "Greenbone" && av.LastSeenAt >= since)
            .Include(av => av.Asset)
            .Include(av => av.Vulnerability)
            .OrderByDescending(av => av.Vulnerability.CvssScore)
            .Select(av => new
            {
                cveId      = av.Vulnerability.CveId,
                title      = av.Vulnerability.Title,
                severity   = av.Vulnerability.Severity.ToString(),
                cvss       = av.Vulnerability.CvssScore,
                assetName  = av.Asset.Name,
                assetId    = av.AssetId,
                detectedAt = av.DetectedAt.ToString("yyyy-MM-dd")
            })
            .ToListAsync();

        return Json(new { items });
    }

    // ── Core sync logic ───────────────────────────────────────────────────────

    private async Task RunSyncAsync(AtlasContext db, GreenboneService service)
    {
        var syncTime = DateTime.UtcNow;

        tracker.SetPhase("Loading");

        // Build in-memory lookups for O(1) dedup — avoids per-row DB queries in the hot loop
        var ipToAssetId = await db.Assets.OfType<Computer>()
            .Where(c => c.IpAddress != null)
            .ToDictionaryAsync(c => c.IpAddress!, c => c.Id);

        var cveToVulnId = await db.Vulnerabilities
            .Where(v => v.CveId != null)
            .ToDictionaryAsync(v => v.CveId!, v => v.Id);

        // Load all AssetVulnerabilities into memory so EF can track updates
        var allAvs = await db.AssetVulnerabilities.ToListAsync();
        var avByKey = allAvs.ToDictionary(av => (av.AssetId, av.VulnerabilityId));

        tracker.SetPhase("Fetching");

        int fetched = 0, assetsCreated = 0, assetsUpdated = 0, findingsAdded = 0, findingsUpdated = 0;
        int batch = 0;

        await foreach (var f in service.GetFindingsAsync())
        {
            fetched++;
            if (fetched % 50 == 0) tracker.UpdateFetch(fetched);

            // ── Resolve asset ─────────────────────────────────────────────────
            if (!ipToAssetId.TryGetValue(f.HostIp, out var assetId))
            {
                var asset = new Computer
                {
                    Name         = f.HostName ?? f.HostIp,
                    IpAddress    = f.HostIp,
                    Status       = AssetStatus.Active,
                    ComputerType = ComputerType.Server,
                    CreatedAt    = syncTime,
                    LastUpdated  = syncTime
                };
                db.Assets.Add(asset);
                await db.SaveChangesAsync();  // need the Id immediately
                assetId = asset.Id;
                ipToAssetId[f.HostIp] = assetId;
                assetsCreated++;
            }

            // ── Resolve vulnerability ─────────────────────────────────────────
            var vulnKey = f.CveId ?? $"nvt:{f.NvtOid}";
            if (!cveToVulnId.TryGetValue(vulnKey, out var vulnId))
            {
                var sev = f.SeverityLabel switch
                {
                    "Critical"      => Severity.Critical,
                    "High"          => Severity.High,
                    "Medium"        => Severity.Medium,
                    "Low"           => Severity.Low,
                    _               => Severity.Informational
                };
                var vuln = new Vulnerability
                {
                    CveId            = f.CveId,
                    Title            = f.NvtName,
                    Severity         = sev,
                    CvssScore        = (double)f.Severity,
                    DiscoveredAt     = f.DetectedAt,
                    AffectedSoftware = f.NvtName
                };
                db.Vulnerabilities.Add(vuln);
                await db.SaveChangesAsync();
                vulnId = vuln.Id;
                cveToVulnId[vulnKey] = vulnId;
            }

            // ── Create or refresh AssetVulnerability ──────────────────────────
            var key = (assetId, vulnId);
            if (!avByKey.TryGetValue(key, out var existing))
            {
                var newAv = new AssetVulnerability
                {
                    AssetId         = assetId,
                    VulnerabilityId = vulnId,
                    Status          = RemediationStatus.Open,
                    DetectedAt      = f.DetectedAt,
                    Source          = "Greenbone",
                    LastSeenAt      = syncTime
                };
                db.AssetVulnerabilities.Add(newAv);
                avByKey[key] = newAv;
                findingsAdded++;
            }
            else
            {
                // Already tracked by EF — just update LastSeenAt
                existing.LastSeenAt = syncTime;
                findingsUpdated++;
            }

            // ── Periodic commit ───────────────────────────────────────────────
            batch++;
            if (batch >= 200)
            {
                tracker.SetPhase("Saving");
                await db.SaveChangesAsync();
                tracker.UpdateSaved(assetsCreated, assetsUpdated, findingsAdded, findingsUpdated);
                tracker.SetPhase("Fetching");
                batch = 0;
            }
        }

        // Final commit
        if (batch > 0)
        {
            await db.SaveChangesAsync();
            tracker.UpdateSaved(assetsCreated, assetsUpdated, findingsAdded, findingsUpdated);
        }

        tracker.Complete();
        logger.LogInformation(
            "Greenbone sync complete — assets created: {Ac}, findings added: {Fa}, updated: {Fu}",
            assetsCreated, findingsAdded, findingsUpdated);
    }
}
