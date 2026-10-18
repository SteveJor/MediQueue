namespace MediQueue.Patient.Models;

public enum TicketStatus
{
    Waiting,          // WAITING
    InConsultation,   // IN_CONSULTATION
    Completed,        // COMPLETED
    Cancelled         // CANCELLED (annulation par le patient)
}
