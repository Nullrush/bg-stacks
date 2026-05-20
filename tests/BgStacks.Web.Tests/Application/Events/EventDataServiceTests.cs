using System.Text.Json;
using BgStacks.Web.Domain.Events;
using FluentAssertions;

namespace BgStacks.Web.Tests.Application.Events;

public class EventDataSerializationTests
{
    [Fact]
    public void EventData_RoundTrips_ViaSystemTextJson()
    {
        var original = new EventData
        {
            SlugValue = "gw-2026-pnw",
            Title = "Geekway 2026 PnW",
            EditTimestamp = 1700000000L,
            GamesJson = "[{\"id\":1}]",
            MechanicsJson = "[\"Worker Placement\"]",
            CategoriesJson = "[\"Strategy\"]",
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<EventData>(json);

        restored.Should().NotBeNull();
        restored!.SlugValue.Should().Be("gw-2026-pnw");
        restored.Title.Should().Be("Geekway 2026 PnW");
        restored.EditTimestamp.Should().Be(1700000000L);
        restored.GamesJson.Should().Be("[{\"id\":1}]");
        restored.MechanicsJson.Should().Be("[\"Worker Placement\"]");
        restored.CategoriesJson.Should().Be("[\"Strategy\"]");
        restored.Slug.Value.Should().Be("gw-2026-pnw");
    }
}
