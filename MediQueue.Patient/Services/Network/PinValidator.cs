using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MediQueue.Patient.Services.Network;

/// <summary>
/// Authentification du SERVEUR par empreinte de clé publique (« pin »).
///
/// Pin = SHA-256 du SubjectPublicKeyInfo (DER) du certificat, en base64url sans remplissage (43 caractères).
/// C'est le même calcul que le backend (app/tls/pin.py). Le pin est transmis hors réseau : par le QR code imprimé dans
/// le centre (Patient) ou par le fichier de confiance de l'application (Consultant).
///
/// Pourquoi ce n'est PAS « désactiver la validation TLS » : la chaîne d'autorités et le nom d'hôte ne sont pas
/// utilisables sur un réseau local (certificat privé, adresse IP qui change). On les remplace par une vérification
/// plus stricte : la clé présentée DOIT être celle attendue. Un faux serveur ne peut pas la présenter (il ne possède pas
/// la clé privée) ; tout autre certificat est rejeté, y compris un certificat « valide » émis par une autorité publique.
/// </summary>
public static class PinValidator
{
    public const int PinLength = 43;
    public const int MaxPins = 3;     // clé actuelle + clé de secours (renouvellement) + marge

    public static bool IsValidPinFormat(string? pin)
        => pin is { Length: PinLength } && pin.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string ToPin(byte[] sha256)
        => Convert.ToBase64String(sha256).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? FromPin(string pin)
    {
        if (!IsValidPinFormat(pin)) return null;
        try
        {
            var padded = pin.Replace('-', '+').Replace('_', '/') + "=";
            var bytes = Convert.FromBase64String(padded);
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string ComputePin(X509Certificate2 certificate)
        => ToPin(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    /// <summary>Indice public (16 caractères hexadécimaux) publié dans le TXT DNS-SD : sert seulement à repérer le bon serveur.</summary>
    public static string ToHint(string pin)
    {
        var bytes = FromPin(pin) ?? throw new ArgumentException("pin invalide", nameof(pin));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public static bool HintMatchesAny(IEnumerable<string> pins, string hintHex)
        => pins.Any(p => FromPin(p) is not null && string.Equals(ToHint(p), hintHex, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Décision de confiance, appelée pendant la poignée de main TLS (avant tout envoi de données applicatives).
    /// Refuse : pas de certificat, aucun pin, certificat hors période de validité, clé ≠ pin attendu.
    /// </summary>
    public static bool IsTrusted(X509Certificate? certificate, IReadOnlyCollection<string> pins, DateTime? utcNow = null)
    {
        if (certificate is null || pins.Count == 0) return false;
        try
        {
            using var cert = new X509Certificate2(certificate);
            var now = utcNow ?? DateTime.UtcNow;
            if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime()) return false;

            var actual = SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo());
            foreach (var pin in pins)
            {
                var expected = FromPin(pin);
                if (expected is not null && CryptographicOperations.FixedTimeEquals(actual, expected)) return true;
            }
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
