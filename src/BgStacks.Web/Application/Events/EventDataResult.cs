using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

/// <summary>Wraps the outcome of a geeklist data lookup.</summary>
/// <param name="Data">The resolved event data, or null when the geeklist is not found.</param>
/// <param name="IsLoading">
/// True when a background fetch has been kicked off but hasn't finished yet.
/// Callers should respond with 202 and let the client retry.
/// </param>
/// <param name="EventName">The human-readable event name (e.g. "Geekway 2026 / Prime"), sourced from
/// the named <see cref="Domain.Events.Event"/> record — null when routing by numeric geeklist ID.</param>
public sealed record EventDataResult(EventData? Data, bool IsLoading = false, string? EventName = null);
