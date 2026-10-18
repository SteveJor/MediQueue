namespace MediQueue.Consultant.Services;

public enum ConsultantError
{
    Network,             // serveur injoignable / délai dépassé
    InvalidCredentials,  // identifiants refusés à la connexion
    Unauthorized,        // jeton absent, expiré ou refusé : il faut se reconnecter
    QueueEmpty,          // aucun patient à appeler
    QueueChanged,        // le « prochain patient » n'est plus celui que le consultant a confirmé
    Server               // erreur 5xx ou réponse inattendue
}

/// <summary>Erreur « métier » des services ; le message est destiné à l'utilisateur.</summary>
public sealed class ConsultantServiceException : Exception
{
    public ConsultantError Error { get; }

    public ConsultantServiceException(ConsultantError error, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
    }
}
