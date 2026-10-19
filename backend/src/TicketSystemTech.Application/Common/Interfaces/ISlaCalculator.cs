namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>Turns an SLA plan's resolution time into a concrete due date.</summary>
public interface ISlaCalculator
{
    /// <summary>The UTC instant by which a ticket started at <paramref name="startUtc"/> must be resolved,
    /// given a plan of <paramref name="resolutionHours"/> hours.</summary>
    DateTime ComputeDueDateUtc(DateTime startUtc, int resolutionHours);
}
