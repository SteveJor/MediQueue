namespace MediQueue.Patient.Services;

public enum QueueError
{
    Network,         // serveur injoignable / délai dépassé
    InvalidQr,       // QR inconnu, inactif ou illisible
    TicketNotFound,  // ticket supprimé / inconnu côté serveur
    Server           // erreur 5xx ou réponse inattendue
}

/// <summary>Erreur « métier » des services ; le message est destiné à l'utilisateur.</summary>
public sealed class QueueServiceException : Exception
{
    public QueueError Error { get; }

    public QueueServiceException(QueueError error, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
    }
}
