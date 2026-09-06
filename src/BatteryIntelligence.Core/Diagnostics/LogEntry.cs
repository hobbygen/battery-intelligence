namespace BatteryIntelligence.Core.Diagnostics;

/// <summary>
/// One parsed line from the Serilog rolling file, for the Diagnostics page's log
/// viewer (specification section 26; docs/prd.md — "Diagnostics: … logs").
/// </summary>
/// <param name="TimestampLocal">The event time as written (local, with offset), or <see langword="null"/> for an unparsed line.</param>
/// <param name="Level">Normalised level: <c>TRACE</c>, <c>DEBUG</c>, <c>INFO</c>, <c>WARNING</c>, <c>ERROR</c>, <c>FATAL</c>.</param>
/// <param name="Text">The message, with any continuation (stack-trace) lines folded in.</param>
public sealed record LogEntry(DateTimeOffset? TimestampLocal, string Level, string Text);
