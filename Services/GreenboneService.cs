using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace ATLAS.Services;

/// <summary>
/// A single Greenbone scan finding — one host + one CVE (or NVT OID when no CVE ref exists).
/// </summary>
public record GreenboneFinding(
    string   HostIp,
    string?  HostName,
    string?  CveId,           // null when NVT has no CVE reference
    string   NvtName,
    string   NvtOid,
    decimal  Severity,        // CVSS numeric score
    string   SeverityLabel,   // Critical / High / Medium / Low / Informational
    DateTime DetectedAt);

/// <summary>
/// Authenticates to Greenbone via GMP and streams findings page-by-page.
/// </summary>
public class GreenboneService(IConfiguration config, ILogger<GreenboneService> logger)
{
    private string Host      => config["Greenbone:Host"]     ?? "10.40.10.88";
    private int    Port      => int.TryParse(config["Greenbone:Port"], out var p) ? p : 9390;
    private string Username  => config["Greenbone:Username"] ?? "admin";
    private string Password  => config["GREENBONE_PASSWORD"] ?? "";
    private bool   IgnoreTls => !bool.TryParse(config["Greenbone:IgnoreCertErrors"], out var b) || b;

    // ── Connection test ───────────────────────────────────────────────────────

    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        await using var client = new GmpClient();
        await client.ConnectAsync(Host, Port, IgnoreTls, ct);
        await client.AuthenticateAsync(Username, Password, ct);
        return $"Connected as {Username} to {Host}:{Port}";
    }

    // ── Finding stream ────────────────────────────────────────────────────────

    /// <summary>
    /// Streams all scan findings from Greenbone, 500 per page, filtering to results
    /// with quality-of-detection >= 70. Emits one <see cref="GreenboneFinding"/> per
    /// CVE reference per result; results with no CVE refs are emitted with CveId = null.
    /// </summary>
    public async IAsyncEnumerable<GreenboneFinding> GetFindingsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var client = new GmpClient();

        logger.LogInformation("Connecting to Greenbone at {Host}:{Port}", Host, Port);
        await client.ConnectAsync(Host, Port, IgnoreTls, ct);
        await client.AuthenticateAsync(Username, Password, ct);
        logger.LogInformation("GMP authenticated as {User}", Username);

        const int pageSize = 500;
        int first = 1;

        while (!ct.IsCancellationRequested)
        {
            var xml = $"""<get_results details="1" filter="apply_overrides=0 min_qod=70 first={first} rows={pageSize}"/>""";
            var doc = await client.SendAsync(xml, ct);

            var status = doc.Root?.Attribute("status")?.Value;
            if (status != "200")
            {
                logger.LogError("GMP get_results failed: status={Status}, text={Text}",
                    status, doc.Root?.Attribute("status_text")?.Value);
                yield break;
            }

            var results = doc.Root!.Elements("result").ToList();
            if (results.Count == 0)
            {
                logger.LogInformation("GMP: no more results at first={First}", first);
                yield break;
            }

            logger.LogDebug("GMP: received {Count} results (first={First})", results.Count, first);

            foreach (var result in results)
            {
                if (ct.IsCancellationRequested) yield break;

                // ── Host ─────────────────────────────────────────────────────
                var hostEl = result.Element("host");
                // Newer GMP: <host><ip>...</ip><hostname>...</hostname></host>
                // Older GMP: <host>192.168.1.1</host>
                var ip = hostEl?.Element("ip")?.Value?.Trim()
                      ?? hostEl?.Value?.Trim()
                      ?? string.Empty;
                if (string.IsNullOrEmpty(ip)) continue;

                var hostname = hostEl?.Element("hostname")?.Value?.Trim();
                if (string.IsNullOrEmpty(hostname)) hostname = null;

                // ── NVT ──────────────────────────────────────────────────────
                var nvt    = result.Element("nvt");
                var oid    = nvt?.Attribute("oid")?.Value ?? string.Empty;
                var name   = nvt?.Element("name")?.Value?.Trim() ?? "Unknown NVT";

                // ── Severity ─────────────────────────────────────────────────
                var sevText  = result.Element("severity")?.Value ?? "0";
                var severity = decimal.TryParse(sevText,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0m;

                var label = severity >= 9.0m ? "Critical"
                          : severity >= 7.0m ? "High"
                          : severity >= 4.0m ? "Medium"
                          : severity >  0.0m ? "Low"
                          : "Informational";

                // ── Detection time ────────────────────────────────────────────
                var timeText   = result.Element("creation_time")?.Value;
                var detectedAt = DateTime.TryParse(timeText, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    ? dt.ToUniversalTime()
                    : DateTime.UtcNow;

                // ── CVE refs ──────────────────────────────────────────────────
                var cveRefs = nvt?.Element("refs")
                    ?.Elements("ref")
                    .Where(r => string.Equals(r.Attribute("type")?.Value, "cve",
                                              StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Attribute("id")?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList() ?? new List<string?>();

                if (cveRefs.Count > 0)
                {
                    foreach (var cveId in cveRefs)
                        yield return new GreenboneFinding(ip, hostname, cveId, name, oid, severity, label, detectedAt);
                }
                else
                {
                    // No CVE ref — emit with null CveId; controller will use NVT OID as key
                    yield return new GreenboneFinding(ip, hostname, null, name, oid, severity, label, detectedAt);
                }
            }

            // ── Pagination ────────────────────────────────────────────────────
            var filteredStr = doc.Root
                .Element("result_count")?.Element("filtered")?.Value
                ?? "0";
            int.TryParse(filteredStr, out var total);

            first += results.Count;
            if (results.Count < pageSize || first > total) yield break;
        }
    }
}
