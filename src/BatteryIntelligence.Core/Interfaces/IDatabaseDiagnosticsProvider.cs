using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>Supplies the database facts the Diagnostics page shows (specification section 47).</summary>
public interface IDatabaseDiagnosticsProvider
{
    Task<DatabaseDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
