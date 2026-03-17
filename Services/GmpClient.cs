using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ATLAS.Services;

/// <summary>
/// Low-level TLS TCP client for the Greenbone Management Protocol (GMP).
/// GMP is a stateful XML-over-TLS protocol: authenticate once, then send commands
/// and receive one complete XML document per response.
/// </summary>
public sealed class GmpClient : IAsyncDisposable
{
    private TcpClient? _tcp;
    private SslStream? _ssl;

    // ── Connection ────────────────────────────────────────────────────────────

    public async Task ConnectAsync(string host, int port, bool ignoreCertErrors = true,
        CancellationToken ct = default)
    {
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(host, port, ct);

        _ssl = new SslStream(
            _tcp.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: ignoreCertErrors ? (_, _, _, _) => true : null);

        await _ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = host },
            ct);
    }

    // ── Authentication ────────────────────────────────────────────────────────

    public async Task AuthenticateAsync(string username, string password,
        CancellationToken ct = default)
    {
        // Use XElement to ensure username/password are properly XML-escaped
        var xml = new XElement("authenticate",
            new XElement("credentials",
                new XElement("username", username),
                new XElement("password", password)))
            .ToString(SaveOptions.DisableFormatting);

        var response = await SendAsync(xml, ct);

        var status = response.Root?.Attribute("status")?.Value;
        if (status != "200")
        {
            var msg = response.Root?.Attribute("status_text")?.Value ?? status;
            throw new InvalidOperationException($"GMP authentication failed: {msg}");
        }
    }

    // ── Command / Response ────────────────────────────────────────────────────

    public async Task<XDocument> SendAsync(string xmlCommand, CancellationToken ct = default)
    {
        if (_ssl == null) throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var bytes = Encoding.UTF8.GetBytes(xmlCommand);
        await _ssl.WriteAsync(bytes, ct);
        await _ssl.FlushAsync(ct);

        return await ReadDocumentAsync(ct);
    }

    /// <summary>
    /// Reads bytes from the stream until a complete XML document is received.
    /// Detects completion by finding the root element's closing tag.
    /// </summary>
    private async Task<XDocument> ReadDocumentAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[65536];
        string? closingTag = null;

        while (true)
        {
            int n = await _ssl!.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("GMP: connection closed unexpectedly.");

            ms.Write(buffer, 0, n);

            // On first chunk, extract the root element name to build the expected closing tag.
            // This lets us stop reading as soon as we have the full document.
            if (closingTag == null && ms.Length >= 4)
            {
                var preview = Encoding.UTF8.GetString(ms.GetBuffer(), 0, Math.Min(512, (int)ms.Length));
                var m = Regex.Match(preview, @"<([a-z_][a-z0-9_]*)[\s>/]");
                if (m.Success)
                    closingTag = "</" + m.Groups[1].Value + ">";
            }

            if (closingTag == null) continue;

            // Scan the accumulated buffer for the closing tag (or self-closing root)
            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            var hasClose = text.Contains(closingTag) ||
                           (text.TrimEnd().EndsWith("/>") && ms.Length < 4096);

            if (!hasClose) continue;

            // Try to parse — if it succeeds we have the complete document
            try
            {
                ms.Position = 0;
                return XDocument.Load(ms);
            }
            catch (System.Xml.XmlException)
            {
                // Not yet a complete XML document — keep reading
            }
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_ssl != null) await _ssl.DisposeAsync();
        _tcp?.Dispose();
    }
}
