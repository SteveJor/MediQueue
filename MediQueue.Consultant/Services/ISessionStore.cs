using Microsoft.Maui.Storage;

using System.Text.Json;
using MediQueue.Consultant.Models;

namespace MediQueue.Consultant.Services;

/// <summary>Stockage chiffré d'une valeur texte (Android Keystore via SecureStorage en production).</summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}

/// <summary>Persistance locale : session (chiffrée) + derniers identifiants saisis (en clair, sans mot de passe).</summary>
public interface ISessionStore
{
    Task<Session?> LoadSessionAsync();
    Task SaveSessionAsync(Session session);
    Task ClearSessionAsync();

    Task<LoginHints?> LoadHintsAsync();
    Task SaveHintsAsync(LoginHints hints);
}

public sealed class SessionStore : ISessionStore
{
    private const string SessionKey = "mediqueue.consultant.session";

    private readonly ISecretStore _secrets;
    private readonly JsonFileStore<LoginHints> _hints;

    public SessionStore(string dataDirectory, ISecretStore secrets)
    {
        Directory.CreateDirectory(dataDirectory);
        _secrets = secrets;
        _hints = new JsonFileStore<LoginHints>(Path.Combine(dataDirectory, "login_hints.json"));
    }

    public async Task<Session?> LoadSessionAsync()
    {
        try
        {
            var json = await _secrets.GetAsync(SessionKey);
            if (string.IsNullOrEmpty(json)) return null;

            var session = JsonSerializer.Deserialize<Session>(json);
            if (session is null || session.IsExpired())
            {
                _secrets.Remove(SessionKey);
                return null;
            }
            return session;
        }
        catch (Exception)
        {
            // Magasin chiffré illisible (réinstallation, restauration…) : on repart sans session.
            try { _secrets.Remove(SessionKey); } catch (Exception) { /* sans importance */ }
            return null;
        }
    }

    public Task SaveSessionAsync(Session session)
        => _secrets.SetAsync(SessionKey, JsonSerializer.Serialize(session));

    public Task ClearSessionAsync()
    {
        try { _secrets.Remove(SessionKey); } catch (Exception) { /* déjà absent */ }
        return Task.CompletedTask;
    }

    public Task<LoginHints?> LoadHintsAsync() => _hints.LoadAsync();
    public Task SaveHintsAsync(LoginHints hints) => _hints.SaveAsync(hints);
}

/// <summary>Implémentation MAUI : SecureStorage (chiffré par le Keystore Android).</summary>
public sealed class MauiSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
