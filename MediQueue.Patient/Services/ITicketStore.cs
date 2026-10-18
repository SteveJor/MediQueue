using MediQueue.Patient.Models;

namespace MediQueue.Patient.Services;

/// <summary>Persistance locale du ticket actif (retrouvé à la réouverture, sans compte).</summary>
public interface ITicketStore
{
    Task<Ticket?> LoadAsync();
    Task SaveAsync(Ticket ticket);
    Task ClearAsync();
}

public sealed class JsonTicketStore : ITicketStore
{
    private readonly JsonFileStore<Ticket> _file;

    public JsonTicketStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = new JsonFileStore<Ticket>(Path.Combine(dataDirectory, "active_ticket.json"));
    }

    public Task<Ticket?> LoadAsync() => _file.LoadAsync();
    public Task SaveAsync(Ticket ticket) => _file.SaveAsync(ticket);
    public Task ClearAsync() => _file.DeleteAsync();
}
