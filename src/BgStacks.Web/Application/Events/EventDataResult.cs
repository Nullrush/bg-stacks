using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

/// <summary>Wraps the outcome of a geeklist data lookup.</summary>
/// <param name="Data">The resolved event data, or null when the geeklist is not found.</param>
/// <param name="IsLoading">
/// True when a background fetch has been kicked off but hasn't finished yet.
/// Callers should respond with 202 and let the client retry.
/// </param>
public sealed record EventDataResult(EventData? Data, bool IsLoading = false);
