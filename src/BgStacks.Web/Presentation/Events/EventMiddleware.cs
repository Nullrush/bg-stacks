using BgStacks.Web.Domain.Events;
using Microsoft.Extensions.Configuration;

namespace BgStacks.Web.Presentation.Events;

public sealed class EventMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _baseDomain;
    private readonly string? _devSlug;

    public const string SlugKey = "EventSlug";

    public EventMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _baseDomain = config["Events:BaseDomain"] ?? "bgstacks.com";
        _devSlug = config["Events:DevFallbackSlug"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Headers["X-Event-Host"].FirstOrDefault()
                   ?? context.Request.Host.Host;
        EventSlug? slug = null;

        if (host.EndsWith(_baseDomain, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = host[..^_baseDomain.Length].TrimEnd('.');
            if (!string.IsNullOrEmpty(prefix) && EventSlug.TryFrom(prefix, out var s))
                slug = s;
        }
        else if (_devSlug is not null && EventSlug.TryFrom(_devSlug, out var devSlug))
        {
            slug = devSlug;
        }

        context.Items[SlugKey] = slug;
        await _next(context);
    }
}
