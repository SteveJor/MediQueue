using MediQueue.Consultant.Models;

namespace MediQueue.Consultant.Services;

/// <summary>Session courante en mémoire (singleton). ApiConsultantService y lit le jeton à chaque requête.</summary>
public sealed class SessionContext
{
    public Session? Current { get; private set; }

    public bool IsAuthenticated => Current is { } s && !s.IsExpired();

    public void Set(Session session) => Current = session;

    public void Clear() => Current = null;
}
