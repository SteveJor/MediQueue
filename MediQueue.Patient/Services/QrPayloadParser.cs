using MediQueue.Patient.Config;

namespace MediQueue.Patient.Services;

/// <summary>
/// Format d'un QR MediQueue : mediqueue://queue/{jeton}
/// Cette vérification est seulement un filtre rapide côté mobile ;
/// la validité réelle du jeton est décidée par le backend.
/// </summary>
public static class QrPayloadParser
{
    public static bool TryExtractToken(string? raw, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, AppConfig.QrScheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, AppConfig.QrHost, StringComparison.OrdinalIgnoreCase)) return false;

        var candidate = uri.AbsolutePath.Trim('/');
        if (candidate.Length is < 6 or > 64) return false;
        if (!candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;

        token = candidate;
        return true;
    }
}
