using System.Text;

namespace Noogadev.SyncCache.ServiceBus;

internal static class SubscriptionNaming
{
    internal static string Resolve(string? configured)
    {
        var name = string.IsNullOrWhiteSpace(configured)
            ? null
            : configured.Trim();

        name ??= Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME");
        name ??= Environment.GetEnvironmentVariable("HOSTNAME");
        name ??= Environment.MachineName;

        return Sanitize($"sync-cache-{Guid.NewGuid():N}-{name}");
    }

    private const int MaxLength = 260;

    /// <summary>
    /// Produces a Service Bus–safe subscription segment: ASCII letters, digits, <c>.</c>, <c>-</c>, <c>_</c>, collapsed duplicate separators, trimmed length, with a fallback when the result would be empty.
    /// </summary>
    /// <param name="value">Candidate subscription name fragment.</param>
    /// <returns>Sanitized name suitable for subscription naming rules.</returns>
    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(Math.Min(value.Length, MaxLength));
        var lastWasSep = false;

        foreach (var c in value)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_';
            var use = ok ? c : '-';

            if (use == '-' && lastWasSep) continue;

            sb.Append(use);
            lastWasSep = use == '-';

            if (sb.Length >= MaxLength) break;
        }

        var s = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? $"sync-{Guid.NewGuid():N}" : s;
    }
}
