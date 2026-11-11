using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>An external client company whose employees submit tickets.</summary>
public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>False when the company does not want the text of its tickets to go to an outside AI provider: no ticket of the company is indexed for the
    /// knowledge base (nothing is embedded, no chunk is kept), and turning it off removes the chunks that already exist.</summary>
    public bool AiIndexingEnabled { get; set; } = true;
}
