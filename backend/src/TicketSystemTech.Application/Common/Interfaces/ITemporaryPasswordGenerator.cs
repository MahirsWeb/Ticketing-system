namespace TicketSystemTech.Application.Common.Interfaces;

public interface ITemporaryPasswordGenerator
{
    /// <summary>Generates a random, human-typeable temporary password that satisfies the Identity password policy.</summary>
    string Generate();
}
