# ACA Migration — Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the BgStacks.Web ASP.NET Core app — DDD domain/application/infrastructure/presentation layers, OAuth auth, event routing, tags API, events API, static files, and Dockerfile — runnable locally against Cosmos DB emulator and Azurite.

**Architecture:** Single ASP.NET Core Minimal API project with DDD layering (Domain → Application → Infrastructure → Presentation). Cookie-based OAuth authentication (Google, Facebook, Discord) implements the SWA `/.auth/*` contract. Per-event game data is fetched from Azure Blob Storage by subdomain slug; user tags are stored in Cosmos DB NoSQL with eTag optimistic concurrency.

**Tech Stack:** .NET 9, ASP.NET Core Minimal API, Microsoft.Azure.Cosmos, Azure.Storage.Blobs, Azure.Identity, Microsoft.AspNetCore.Authentication.Google/Facebook, AspNet.Security.OAuth.Discord, xUnit, NSubstitute, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing

**Spec:** `docs/superpowers/specs/2026-05-13-aca-migration-design.md`

---

## File Map

```
src/BgStacks.Web/
  Domain/
    Tags/
      UserId.cs
      Tags.cs
      UserTags.cs
      ITagsRepository.cs
      ConflictException.cs
    Events/
      EventSlug.cs
      Event.cs
      EventData.cs
      IEventRepository.cs
      IEventDataRepository.cs
  Application/
    Tags/
      TagsService.cs
    Events/
      EventService.cs
      EventDataService.cs
  Infrastructure/
    Tags/
      CosmosTagsRepository.cs
    Events/
      CosmosEventRepository.cs
      BlobEventDataRepository.cs
    Auth/
      OAuthUserIdFactory.cs
  Presentation/
    Auth/
      AuthEndpoints.cs
    Api/
      TagsEndpoints.cs
      EventsEndpoints.cs
    Events/
      EventMiddleware.cs
      GamesJsonEndpoint.cs
  wwwroot/
    home.html
    home.js
    index.html          (copied from public/)
    app.js              (copied from public/)
    styles.css          (copied from public/)
    sw.js               (copied from public/)
    manifest.json       (copied from public/, updated to platform identity)
    categories.json     (copied from public/)
    mechanics.json      (NOT copied — served dynamically per event)
    games.json          (NOT copied — served dynamically per event)
    icon.svg, icon-192.png, icon-512.png  (copied from public/)
  Program.cs
  BgStacks.Web.csproj
  appsettings.json
  appsettings.Development.json
  Dockerfile

tests/BgStacks.Web.Tests/
  Domain/
    Tags/
      UserIdTests.cs
      TagsTests.cs
    Events/
      EventSlugTests.cs
      EventTests.cs
  Application/
    Tags/
      TagsServiceTests.cs
    Events/
      EventServiceTests.cs
      EventDataServiceTests.cs
  Infrastructure/
    Auth/
      OAuthUserIdFactoryTests.cs
  Presentation/
    Helpers/
      CustomWebApplicationFactory.cs
      TestAuthHandler.cs
      InMemoryTagsRepository.cs
      InMemoryEventRepository.cs
      InMemoryEventDataRepository.cs
    Auth/
      AuthEndpointsTests.cs
    Api/
      TagsEndpointsTests.cs
      EventsEndpointsTests.cs
    Events/
      EventMiddlewareTests.cs
      GamesJsonEndpointTests.cs
  BgStacks.Web.Tests.csproj
```

---

### Task 1: Project scaffold

**Files:**
- Create: `src/BgStacks.Web/BgStacks.Web.csproj`
- Create: `tests/BgStacks.Web.Tests/BgStacks.Web.Tests.csproj`
- Create: `bg-stacks.sln`

- [ ] **Step 1: Create solution and projects**

```powershell
cd C:\Projects\bg-stacks
mkdir src, tests
dotnet new webapi -n BgStacks.Web -o src/BgStacks.Web --no-openapi
dotnet new xunit -n BgStacks.Web.Tests -o tests/BgStacks.Web.Tests
dotnet new sln -n bg-stacks
dotnet sln add src/BgStacks.Web/BgStacks.Web.csproj
dotnet sln add tests/BgStacks.Web.Tests/BgStacks.Web.Tests.csproj
dotnet add tests/BgStacks.Web.Tests/BgStacks.Web.Tests.csproj reference src/BgStacks.Web/BgStacks.Web.csproj
```

- [ ] **Step 2: Add NuGet packages to the web project**

```powershell
cd src/BgStacks.Web
dotnet add package Microsoft.AspNetCore.Authentication.Google
dotnet add package Microsoft.AspNetCore.Authentication.Facebook
dotnet add package AspNet.Security.OAuth.Discord
dotnet add package Microsoft.Azure.Cosmos
dotnet add package Azure.Storage.Blobs
dotnet add package Azure.Identity
```

- [ ] **Step 3: Add NuGet packages to the test project**

```powershell
cd ../../tests/BgStacks.Web.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package NSubstitute
dotnet add package FluentAssertions
dotnet add package Microsoft.NET.Test.Sdk
```

- [ ] **Step 4: Create the DDD folder structure**

```powershell
cd ../../src/BgStacks.Web
mkdir Domain/Tags, Domain/Events
mkdir Application/Tags, Application/Events
mkdir Infrastructure/Tags, Infrastructure/Events, Infrastructure/Auth
mkdir Presentation/Auth, Presentation/Api, Presentation/Events
mkdir wwwroot
```

- [ ] **Step 5: Delete the webapi template boilerplate**

Delete `src/BgStacks.Web/Controllers/` if it exists. Delete `WeatherForecast.cs` if it exists. Clear `Program.cs` to an empty file (leave only `var builder = WebApplication.CreateBuilder(args);` and `var app = builder.Build(); app.Run();`).

- [ ] **Step 6: Add `public partial class Program { }` to the bottom of Program.cs**

This makes `Program` accessible from the test project via `WebApplicationFactory<Program>`.

```csharp
// src/BgStacks.Web/Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

public partial class Program { }
```

- [ ] **Step 7: Verify the solution builds**

```powershell
cd C:\Projects\bg-stacks
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```powershell
git add src/ tests/ bg-stacks.sln
git commit -m "feat: scaffold BgStacks.Web project and test project"
```

---

### Task 2: Domain — Tags

**Files:**
- Create: `src/BgStacks.Web/Domain/Tags/UserId.cs`
- Create: `src/BgStacks.Web/Domain/Tags/Tags.cs`
- Create: `src/BgStacks.Web/Domain/Tags/UserTags.cs`
- Create: `src/BgStacks.Web/Domain/Tags/ITagsRepository.cs`
- Create: `src/BgStacks.Web/Domain/Tags/ConflictException.cs`
- Create: `tests/BgStacks.Web.Tests/Domain/Tags/UserIdTests.cs`
- Create: `tests/BgStacks.Web.Tests/Domain/Tags/TagsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Domain/Tags/UserIdTests.cs
using BgStacks.Web.Domain.Tags;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Tags;

public class UserIdTests
{
    [Fact]
    public void From_SameProviderAndSub_ReturnsSameValue()
    {
        var a = UserId.From("google", "12345");
        var b = UserId.From("google", "12345");
        a.Value.Should().Be(b.Value);
    }

    [Fact]
    public void From_DifferentProviders_ReturnsDifferentValues()
    {
        var a = UserId.From("google", "12345");
        var b = UserId.From("facebook", "12345");
        a.Value.Should().NotBe(b.Value);
    }

    [Fact]
    public void From_ProducesLowercaseHex64Chars()
    {
        var id = UserId.From("google", "abc");
        id.Value.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("google", "")]
    [InlineData(null, "abc")]
    [InlineData("google", null)]
    public void From_EmptyOrNull_Throws(string? provider, string? sub)
    {
        var act = () => UserId.From(provider!, sub!);
        act.Should().Throw<ArgumentException>();
    }
}
```

```csharp
// tests/BgStacks.Web.Tests/Domain/Tags/TagsTests.cs
using BgStacks.Web.Domain.Tags;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Tags;

public class TagsTests
{
    [Fact]
    public void Empty_HasNoWantOrPlayed()
    {
        var tags = Tags.Empty;
        tags.Want.Should().BeEmpty();
        tags.Played.Should().BeEmpty();
    }

    [Fact]
    public void Tags_StoresWantAndPlayed()
    {
        var tags = new Tags([1, 2], [3]);
        tags.Want.Should().BeEquivalentTo(new[] { 1, 2 });
        tags.Played.Should().BeEquivalentTo(new[] { 3 });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
cd C:\Projects\bg-stacks
dotnet test tests/BgStacks.Web.Tests --filter "Domain.Tags"
```

Expected: Build error — types not defined yet.

- [ ] **Step 3: Implement the domain types**

```csharp
// src/BgStacks.Web/Domain/Tags/UserId.cs
using System.Security.Cryptography;
using System.Text;

namespace BgStacks.Web.Domain.Tags;

public readonly record struct UserId
{
    public string Value { get; }

    private UserId(string value) => Value = value;

    public static UserId From(string identityProvider, string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        var input = $"{identityProvider.ToLowerInvariant()}:{sub}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new UserId(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
```

```csharp
// src/BgStacks.Web/Domain/Tags/Tags.cs
namespace BgStacks.Web.Domain.Tags;

public sealed record Tags(int[] Want, int[] Played)
{
    public static Tags Empty => new([], []);
}
```

```csharp
// src/BgStacks.Web/Domain/Tags/UserTags.cs
namespace BgStacks.Web.Domain.Tags;

public sealed class UserTags
{
    public UserId UserId { get; }
    public Tags Tags { get; }

    public UserTags(UserId userId, Tags tags)
    {
        UserId = userId;
        Tags = tags;
    }
}
```

```csharp
// src/BgStacks.Web/Domain/Tags/ITagsRepository.cs
namespace BgStacks.Web.Domain.Tags;

public interface ITagsRepository
{
    Task<(Tags? tags, string? etag)> GetAsync(UserId userId, CancellationToken ct = default);
    Task<string> SaveAsync(UserTags userTags, string? clientEtag, CancellationToken ct = default);
}
```

```csharp
// src/BgStacks.Web/Domain/Tags/ConflictException.cs
namespace BgStacks.Web.Domain.Tags;

public sealed class ConflictException : Exception
{
    public ConflictException() : base("eTag conflict: the resource has been updated by another client.") { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Domain.Tags"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Domain/Tags/ tests/BgStacks.Web.Tests/Domain/Tags/
git commit -m "feat: add Tags domain — UserId, Tags, UserTags, ITagsRepository, ConflictException"
```

---

### Task 3: Domain — Events

**Files:**
- Create: `src/BgStacks.Web/Domain/Events/EventSlug.cs`
- Create: `src/BgStacks.Web/Domain/Events/Event.cs`
- Create: `src/BgStacks.Web/Domain/Events/EventData.cs`
- Create: `src/BgStacks.Web/Domain/Events/IEventRepository.cs`
- Create: `src/BgStacks.Web/Domain/Events/IEventDataRepository.cs`
- Create: `tests/BgStacks.Web.Tests/Domain/Events/EventSlugTests.cs`
- Create: `tests/BgStacks.Web.Tests/Domain/Events/EventTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Domain/Events/EventSlugTests.cs
using BgStacks.Web.Domain.Events;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Events;

public class EventSlugTests
{
    [Theory]
    [InlineData("gw-2026-pnw")]
    [InlineData("geekwaywest2026")]
    [InlineData("ab")]
    public void From_ValidSlug_Succeeds(string value)
    {
        var slug = EventSlug.From(value);
        slug.Value.Should().Be(value);
    }

    [Fact]
    public void From_UpperCase_NormalizesToLower()
    {
        var slug = EventSlug.From("GW-2026-PNW");
        slug.Value.Should().Be("gw-2026-pnw");
    }

    [Theory]
    [InlineData("")]
    [InlineData("-startswithdash")]
    [InlineData("endswith-")]
    [InlineData("has spaces")]
    [InlineData("has!special")]
    public void From_InvalidSlug_Throws(string value)
    {
        var act = () => EventSlug.From(value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_ValidSlug_ReturnsTrueAndSlug()
    {
        var result = EventSlug.TryFrom("gw-2026-pnw", out var slug);
        result.Should().BeTrue();
        slug.Value.Should().Be("gw-2026-pnw");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-bad")]
    public void TryFrom_InvalidSlug_ReturnsFalse(string? value)
    {
        var result = EventSlug.TryFrom(value, out _);
        result.Should().BeFalse();
    }
}
```

```csharp
// tests/BgStacks.Web.Tests/Domain/Events/EventTests.cs
using BgStacks.Web.Domain.Events;
using FluentAssertions;

namespace BgStacks.Web.Tests.Domain.Events;

public class EventTests
{
    private static readonly EventSlug Slug = EventSlug.From("gw-2026-pnw");

    [Fact]
    public void IsUpcoming_EventDateTodayOrFuture_ReturnsTrue()
    {
        var today = new DateOnly(2026, 6, 1);
        var ev = new Event(Slug, "Test Event", today, true);
        ev.IsUpcoming(today).Should().BeTrue();
        ev.IsUpcoming(today.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsUpcoming_EventDateInPast_ReturnsFalse()
    {
        var today = new DateOnly(2026, 6, 1);
        var ev = new Event(Slug, "Test Event", new DateOnly(2026, 5, 1), true);
        ev.IsUpcoming(today).Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        var act = () => new Event(Slug, "", new DateOnly(2026, 6, 1), true);
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Domain.Events"
```

Expected: Build error — types not defined yet.

- [ ] **Step 3: Implement the domain types**

```csharp
// src/BgStacks.Web/Domain/Events/EventSlug.cs
using System.Text.RegularExpressions;

namespace BgStacks.Web.Domain.Events;

public readonly record struct EventSlug
{
    private static readonly Regex ValidPattern =
        new(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$", RegexOptions.Compiled);

    public string Value { get; }

    private EventSlug(string value) => Value = value;

    public static EventSlug From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var lower = value.ToLowerInvariant();
        if (!ValidPattern.IsMatch(lower))
            throw new ArgumentException($"Invalid event slug: '{value}'", nameof(value));
        return new EventSlug(lower);
    }

    public static bool TryFrom(string? value, out EventSlug slug)
    {
        slug = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lower = value.ToLowerInvariant();
        if (!ValidPattern.IsMatch(lower)) return false;
        slug = new EventSlug(lower);
        return true;
    }

    public override string ToString() => Value;
}
```

```csharp
// src/BgStacks.Web/Domain/Events/Event.cs
namespace BgStacks.Web.Domain.Events;

public sealed class Event
{
    public EventSlug Slug { get; }
    public string Name { get; }
    public DateOnly EventDate { get; }
    public bool IsPublic { get; }

    public Event(EventSlug slug, string name, DateOnly eventDate, bool isPublic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Slug = slug;
        Name = name;
        EventDate = eventDate;
        IsPublic = isPublic;
    }

    public bool IsUpcoming(DateOnly today) => EventDate >= today;
}
```

```csharp
// src/BgStacks.Web/Domain/Events/EventData.cs
namespace BgStacks.Web.Domain.Events;

public sealed class EventData
{
    public EventSlug Slug { get; }
    public string GamesJson { get; }
    public string MechanicsJson { get; }
    public string CategoriesJson { get; }

    public EventData(EventSlug slug, string gamesJson, string mechanicsJson, string categoriesJson)
    {
        Slug = slug;
        GamesJson = gamesJson;
        MechanicsJson = mechanicsJson;
        CategoriesJson = categoriesJson;
    }
}
```

```csharp
// src/BgStacks.Web/Domain/Events/IEventRepository.cs
namespace BgStacks.Web.Domain.Events;

public interface IEventRepository
{
    Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default);
    Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default);
    Task SaveAsync(Event @event, CancellationToken ct = default);
}
```

```csharp
// src/BgStacks.Web/Domain/Events/IEventDataRepository.cs
namespace BgStacks.Web.Domain.Events;

public interface IEventDataRepository
{
    Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Domain.Events"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Domain/Events/ tests/BgStacks.Web.Tests/Domain/Events/
git commit -m "feat: add Events domain — EventSlug, Event, EventData, repository interfaces"
```

---

### Task 4: Application — TagsService

**Files:**
- Create: `src/BgStacks.Web/Application/Tags/TagsService.cs`
- Create: `tests/BgStacks.Web.Tests/Application/Tags/TagsServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Application/Tags/TagsServiceTests.cs
using BgStacks.Web.Application.Tags;
using BgStacks.Web.Domain.Tags;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BgStacks.Web.Tests.Application.Tags;

public class TagsServiceTests
{
    private readonly ITagsRepository _repo = Substitute.For<ITagsRepository>();
    private readonly TagsService _sut;
    private readonly UserId _userId = UserId.From("google", "user1");

    public TagsServiceTests() => _sut = new TagsService(_repo);

    [Fact]
    public async Task GetTagsAsync_RepositoryReturnsData_ReturnsIt()
    {
        var tags = new Tags([1, 2], [3]);
        _repo.GetAsync(_userId).Returns((tags, "etag123"));

        var (result, etag) = await _sut.GetTagsAsync(_userId);

        result.Should().Be(tags);
        etag.Should().Be("etag123");
    }

    [Fact]
    public async Task GetTagsAsync_NoData_ReturnsNull()
    {
        _repo.GetAsync(_userId).Returns(((Tags?)null, (string?)null));

        var (result, etag) = await _sut.GetTagsAsync(_userId);

        result.Should().BeNull();
        etag.Should().BeNull();
    }

    [Fact]
    public async Task SaveTagsAsync_DelegatesToRepository()
    {
        var tags = new Tags([1], [2]);
        _repo.SaveAsync(Arg.Any<UserTags>(), "etag1").Returns("etag2");

        var result = await _sut.SaveTagsAsync(_userId, tags, "etag1");

        result.Should().Be("etag2");
        await _repo.Received(1).SaveAsync(
            Arg.Is<UserTags>(ut => ut.UserId == _userId && ut.Tags == tags), "etag1");
    }

    [Fact]
    public async Task SaveTagsAsync_ConflictException_Propagates()
    {
        _repo.SaveAsync(Arg.Any<UserTags>(), Arg.Any<string?>()).ThrowsAsync(new ConflictException());

        var act = async () => await _sut.SaveTagsAsync(_userId, Tags.Empty, "stale");

        await act.Should().ThrowAsync<ConflictException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Application.Tags"
```

Expected: Build error — TagsService not defined.

- [ ] **Step 3: Implement TagsService**

```csharp
// src/BgStacks.Web/Application/Tags/TagsService.cs
using BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Application.Tags;

public sealed class TagsService
{
    private readonly ITagsRepository _repository;

    public TagsService(ITagsRepository repository) => _repository = repository;

    public Task<(Tags? tags, string? etag)> GetTagsAsync(UserId userId, CancellationToken ct = default)
        => _repository.GetAsync(userId, ct);

    public Task<string> SaveTagsAsync(UserId userId, Tags tags, string? clientEtag, CancellationToken ct = default)
        => _repository.SaveAsync(new UserTags(userId, tags), clientEtag, ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Application.Tags"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Application/Tags/ tests/BgStacks.Web.Tests/Application/Tags/
git commit -m "feat: add TagsService application service"
```

---

### Task 5: Application — EventService + EventDataService

**Files:**
- Create: `src/BgStacks.Web/Application/Events/EventService.cs`
- Create: `src/BgStacks.Web/Application/Events/EventDataService.cs`
- Create: `tests/BgStacks.Web.Tests/Application/Events/EventServiceTests.cs`
- Create: `tests/BgStacks.Web.Tests/Application/Events/EventDataServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Application/Events/EventServiceTests.cs
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class EventServiceTests
{
    private readonly IEventRepository _repo = Substitute.For<IEventRepository>();
    private readonly EventService _sut;

    public EventServiceTests() => _sut = new EventService(_repo);

    [Fact]
    public async Task ListPublicEventsAsync_DelegatesToRepository()
    {
        var events = new List<Event>
        {
            new(EventSlug.From("gw-2026-pnw"), "GW 2026", new DateOnly(2026, 6, 1), true)
        };
        _repo.ListPublicAsync().Returns(events);

        var result = await _sut.ListPublicEventsAsync();

        result.Should().BeEquivalentTo(events);
    }

    [Fact]
    public async Task GetEventAsync_DelegatesToRepository()
    {
        var slug = EventSlug.From("gw-2026-pnw");
        var ev = new Event(slug, "GW 2026", new DateOnly(2026, 6, 1), true);
        _repo.GetAsync(slug).Returns(ev);

        var result = await _sut.GetEventAsync(slug);

        result.Should().Be(ev);
    }
}
```

```csharp
// tests/BgStacks.Web.Tests/Application/Events/EventDataServiceTests.cs
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace BgStacks.Web.Tests.Application.Events;

public class EventDataServiceTests
{
    private readonly IEventDataRepository _repo = Substitute.For<IEventDataRepository>();
    private readonly EventDataService _sut;
    private static readonly EventSlug Slug = EventSlug.From("gw-2026-pnw");
    private static readonly EventData Data = new(Slug, "{}", "[]", "[]");

    public EventDataServiceTests()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new EventDataService(_repo, cache);
    }

    [Fact]
    public async Task GetEventDataAsync_CacheMiss_FetchesFromRepo()
    {
        _repo.GetAsync(Slug).Returns(Data);

        var result = await _sut.GetEventDataAsync(Slug);

        result.Should().Be(Data);
        await _repo.Received(1).GetAsync(Slug);
    }

    [Fact]
    public async Task GetEventDataAsync_CacheHit_DoesNotCallRepo()
    {
        _repo.GetAsync(Slug).Returns(Data);
        await _sut.GetEventDataAsync(Slug); // prime cache

        await _sut.GetEventDataAsync(Slug); // should hit cache

        await _repo.Received(1).GetAsync(Slug); // only called once
    }

    [Fact]
    public async Task GetEventDataAsync_NotFound_ReturnsNull()
    {
        _repo.GetAsync(Slug).Returns((EventData?)null);

        var result = await _sut.GetEventDataAsync(Slug);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Application.Events"
```

Expected: Build error — services not defined.

- [ ] **Step 3: Implement EventService and EventDataService**

```csharp
// src/BgStacks.Web/Application/Events/EventService.cs
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Application.Events;

public sealed class EventService
{
    private readonly IEventRepository _repository;

    public EventService(IEventRepository repository) => _repository = repository;

    public Task<IReadOnlyList<Event>> ListPublicEventsAsync(CancellationToken ct = default)
        => _repository.ListPublicAsync(ct);

    public Task<Event?> GetEventAsync(EventSlug slug, CancellationToken ct = default)
        => _repository.GetAsync(slug, ct);
}
```

```csharp
// src/BgStacks.Web/Application/Events/EventDataService.cs
using BgStacks.Web.Domain.Events;
using Microsoft.Extensions.Caching.Memory;

namespace BgStacks.Web.Application.Events;

public sealed class EventDataService
{
    private readonly IEventDataRepository _repository;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public EventDataService(IEventDataRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<EventData?> GetEventDataAsync(EventSlug slug, CancellationToken ct = default)
    {
        var cacheKey = $"event-data:{slug.Value}";
        if (_cache.TryGetValue(cacheKey, out EventData? cached))
            return cached;

        var data = await _repository.GetAsync(slug, ct);
        if (data is not null)
            _cache.Set(cacheKey, data, CacheTtl);
        return data;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Application.Events"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Application/Events/ tests/BgStacks.Web.Tests/Application/Events/
git commit -m "feat: add EventService and EventDataService application services"
```

---

### Task 6: Infrastructure — OAuthUserIdFactory

**Files:**
- Create: `src/BgStacks.Web/Infrastructure/Auth/OAuthUserIdFactory.cs`
- Create: `tests/BgStacks.Web.Tests/Infrastructure/Auth/OAuthUserIdFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Infrastructure/Auth/OAuthUserIdFactoryTests.cs
using System.Security.Claims;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Auth;
using FluentAssertions;

namespace BgStacks.Web.Tests.Infrastructure.Auth;

public class OAuthUserIdFactoryTests
{
    [Fact]
    public void FromPrincipal_GoogleUser_DerivesUserIdFromProviderAndSub()
    {
        var principal = MakePrincipal("google", "google-sub-123", "user@example.com");

        var userId = OAuthUserIdFactory.FromPrincipal(principal);

        userId.Should().Be(UserId.From("google", "google-sub-123"));
    }

    [Fact]
    public void GetUserDetails_EmailAvailable_ReturnsEmail()
    {
        var principal = MakePrincipal("google", "sub", "user@example.com");

        var details = OAuthUserIdFactory.GetUserDetails(principal);

        details.Should().Be("user@example.com");
    }

    [Fact]
    public void GetUserDetails_NoEmail_ReturnsName()
    {
        var principal = MakePrincipal("discord", "sub", name: "CoolUser#1234");

        var details = OAuthUserIdFactory.GetUserDetails(principal);

        details.Should().Be("CoolUser#1234");
    }

    [Fact]
    public void GetPictureUrl_Google_ReturnsPictureClaim()
    {
        var claims = new[]
        {
            new Claim("idp", "google"),
            new Claim(ClaimTypes.NameIdentifier, "sub"),
            new Claim("picture", "https://lh3.googleusercontent.com/photo.jpg"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "google");

        url.Should().Be("https://lh3.googleusercontent.com/photo.jpg");
    }

    [Fact]
    public void GetPictureUrl_Facebook_ConstructsGraphUrl()
    {
        var principal = MakePrincipal("facebook", "fb-user-id-999", "user@example.com");

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "facebook");

        url.Should().Be("https://graph.facebook.com/fb-user-id-999/picture?type=square");
    }

    [Fact]
    public void GetPictureUrl_Discord_ConstructsCdnUrl()
    {
        var claims = new[]
        {
            new Claim("idp", "discord"),
            new Claim(ClaimTypes.NameIdentifier, "discord-id-456"),
            new Claim("avatar", "abc123hash"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var url = OAuthUserIdFactory.GetPictureUrl(principal, "discord");

        url.Should().Be("https://cdn.discordapp.com/avatars/discord-id-456/abc123hash.png");
    }

    private static ClaimsPrincipal MakePrincipal(string provider, string sub,
        string? email = null, string? name = null)
    {
        var claims = new List<Claim>
        {
            new("idp", provider),
            new(ClaimTypes.NameIdentifier, sub),
        };
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Infrastructure.Auth"
```

Expected: Build error — OAuthUserIdFactory not defined.

- [ ] **Step 3: Implement OAuthUserIdFactory**

```csharp
// src/BgStacks.Web/Infrastructure/Auth/OAuthUserIdFactory.cs
using System.Security.Claims;
using BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Infrastructure.Auth;

public static class OAuthUserIdFactory
{
    public static UserId FromPrincipal(ClaimsPrincipal principal)
    {
        var provider = principal.FindFirstValue("idp")
            ?? throw new InvalidOperationException("Identity provider claim 'idp' not found.");
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("NameIdentifier claim not found.");
        return UserId.From(provider, sub);
    }

    public static string? GetUserDetails(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Email)
           ?? principal.FindFirstValue(ClaimTypes.Name);

    public static string? GetPictureUrl(ClaimsPrincipal principal, string provider) =>
        provider switch
        {
            "google" => principal.FindFirstValue("picture"),
            "facebook" => principal.FindFirstValue(ClaimTypes.NameIdentifier) is { } fbId
                ? $"https://graph.facebook.com/{fbId}/picture?type=square"
                : null,
            "discord" => GetDiscordAvatarUrl(principal),
            _ => null,
        };

    private static string? GetDiscordAvatarUrl(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var avatar = principal.FindFirstValue("avatar");
        return id is not null && avatar is not null
            ? $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png"
            : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Infrastructure.Auth"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Infrastructure/Auth/ tests/BgStacks.Web.Tests/Infrastructure/Auth/
git commit -m "feat: add OAuthUserIdFactory — derives userId, userDetails, picture from ClaimsPrincipal"
```

---

### Task 7: Infrastructure — CosmosTagsRepository

**Files:**
- Create: `src/BgStacks.Web/Infrastructure/Tags/CosmosTagsRepository.cs`

No unit tests for this task — the repository is a thin wrapper around the Cosmos SDK. It is covered by integration tests in Task 11.

- [ ] **Step 1: Implement CosmosTagsRepository**

```csharp
// src/BgStacks.Web/Infrastructure/Tags/CosmosTagsRepository.cs
using System.Net;
using System.Text.Json.Serialization;
using BgStacks.Web.Domain.Tags;
using Microsoft.Azure.Cosmos;

namespace BgStacks.Web.Infrastructure.Tags;

public sealed class CosmosTagsRepository : ITagsRepository
{
    private readonly Container _container;

    public CosmosTagsRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "usertags");

    public async Task<(Tags? tags, string? etag)> GetAsync(UserId userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<TagsDocument>(
                "tags", new PartitionKey(userId.Value), cancellationToken: ct);
            var doc = response.Resource;
            return (new Tags(doc.Want, doc.Played), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    public async Task<string> SaveAsync(UserTags userTags, string? clientEtag, CancellationToken ct = default)
    {
        var doc = new TagsDocument
        {
            Id = "tags",
            UserId = userTags.UserId.Value,
            Want = userTags.Tags.Want,
            Played = userTags.Tags.Played,
        };

        try
        {
            ItemResponse<TagsDocument> response;
            if (clientEtag is null)
            {
                response = await _container.CreateItemAsync(
                    doc, new PartitionKey(userTags.UserId.Value), cancellationToken: ct);
            }
            else
            {
                response = await _container.ReplaceItemAsync(
                    doc, "tags", new PartitionKey(userTags.UserId.Value),
                    new ItemRequestOptions { IfMatchEtag = clientEtag },
                    cancellationToken: ct);
            }
            return response.ETag;
        }
        catch (CosmosException ex) when (
            ex.StatusCode == HttpStatusCode.PreconditionFailed ||
            ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ConflictException();
        }
    }

    private sealed class TagsDocument
    {
        [JsonPropertyName("id")]   public string Id { get; set; } = "tags";
        [JsonPropertyName("userId")] public string UserId { get; set; } = "";
        [JsonPropertyName("want")]   public int[] Want { get; set; } = [];
        [JsonPropertyName("played")] public int[] Played { get; set; } = [];
    }
}
```

- [ ] **Step 2: Verify the project builds**

```powershell
cd C:\Projects\bg-stacks
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add src/BgStacks.Web/Infrastructure/Tags/
git commit -m "feat: add CosmosTagsRepository"
```

---

### Task 8: Infrastructure — CosmosEventRepository + BlobEventDataRepository

**Files:**
- Create: `src/BgStacks.Web/Infrastructure/Events/CosmosEventRepository.cs`
- Create: `src/BgStacks.Web/Infrastructure/Events/BlobEventDataRepository.cs`

- [ ] **Step 1: Implement CosmosEventRepository**

```csharp
// src/BgStacks.Web/Infrastructure/Events/CosmosEventRepository.cs
using System.Net;
using System.Text.Json.Serialization;
using BgStacks.Web.Domain.Events;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class CosmosEventRepository : IEventRepository
{
    private readonly Container _container;

    public CosmosEventRepository(CosmosClient client, string databaseId)
        => _container = client.GetContainer(databaseId, "events");

    public async Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<EventDocument>(
                slug.Value, new PartitionKey(slug.Value), cancellationToken: ct);
            return ToEvent(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.isPublic = true ORDER BY c.eventDate DESC");
        using var feed = _container.GetItemQueryIterator<EventDocument>(query);
        var results = new List<Event>();
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            results.AddRange(page.Select(ToEvent));
        }
        return results;
    }

    public async Task SaveAsync(Event @event, CancellationToken ct = default)
    {
        var doc = new EventDocument
        {
            Id = @event.Slug.Value,
            Slug = @event.Slug.Value,
            Name = @event.Name,
            EventDate = @event.EventDate.ToString("yyyy-MM-dd"),
            IsPublic = @event.IsPublic,
        };
        await _container.UpsertItemAsync(doc, new PartitionKey(@event.Slug.Value), cancellationToken: ct);
    }

    private static Event ToEvent(EventDocument doc) => new(
        EventSlug.From(doc.Slug),
        doc.Name,
        DateOnly.Parse(doc.EventDate),
        doc.IsPublic);

    private sealed class EventDocument
    {
        [JsonPropertyName("id")]        public string Id { get; set; } = "";
        [JsonPropertyName("slug")]      public string Slug { get; set; } = "";
        [JsonPropertyName("name")]      public string Name { get; set; } = "";
        [JsonPropertyName("eventDate")] public string EventDate { get; set; } = "";
        [JsonPropertyName("isPublic")]  public bool IsPublic { get; set; }
    }
}
```

- [ ] **Step 2: Implement BlobEventDataRepository**

```csharp
// src/BgStacks.Web/Infrastructure/Events/BlobEventDataRepository.cs
using Azure;
using Azure.Storage.Blobs;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Infrastructure.Events;

public sealed class BlobEventDataRepository : IEventDataRepository
{
    private readonly BlobServiceClient _blobClient;
    private const string ContainerName = "events";

    public BlobEventDataRepository(BlobServiceClient blobClient) => _blobClient = blobClient;

    public async Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default)
    {
        var container = _blobClient.GetBlobContainerClient(ContainerName);
        try
        {
            var games = await DownloadTextAsync(container, $"{slug.Value}/games.json", ct);
            var mechanics = await DownloadTextAsync(container, $"{slug.Value}/mechanics.json", ct);
            var categories = await DownloadTextAsync(container, $"{slug.Value}/categories.json", ct);
            return new EventData(slug, games, mechanics, categories);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<string> DownloadTextAsync(
        BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var blob = container.GetBlobClient(blobName);
        var response = await blob.DownloadContentAsync(ct);
        return response.Value.Content.ToString();
    }
}
```

- [ ] **Step 3: Verify the project builds**

```powershell
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add src/BgStacks.Web/Infrastructure/Events/
git commit -m "feat: add CosmosEventRepository and BlobEventDataRepository"
```

---

### Task 9: Presentation test infrastructure

**Files:**
- Create: `tests/BgStacks.Web.Tests/Presentation/Helpers/TestAuthHandler.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryTagsRepository.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryEventRepository.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryEventDataRepository.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Helpers/CustomWebApplicationFactory.cs`

These helpers are shared by all presentation integration tests (Tasks 10–13).

- [ ] **Step 1: Implement TestAuthHandler**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Helpers/TestAuthHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    [ThreadStatic]
    public static ClaimsPrincipal? CurrentUser;

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (CurrentUser is null)
            return Task.FromResult(AuthenticateResult.NoResult());
        var ticket = new AuthenticationTicket(CurrentUser, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 2: Implement in-memory repository fakes**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryTagsRepository.cs
using BgStacks.Web.Domain.Tags;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryTagsRepository : ITagsRepository
{
    private readonly Dictionary<string, (Tags tags, string etag)> _store = new();

    public Task<(Tags? tags, string? etag)> GetAsync(UserId userId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(userId.Value, out var entry))
            return Task.FromResult<(Tags?, string?)>((entry.tags, entry.etag));
        return Task.FromResult<(Tags?, string?)>((null, null));
    }

    public Task<string> SaveAsync(UserTags userTags, string? clientEtag, CancellationToken ct = default)
    {
        var key = userTags.UserId.Value;
        if (clientEtag is not null &&
            (!_store.TryGetValue(key, out var existing) || existing.etag != clientEtag))
            throw new ConflictException();

        var newEtag = Guid.NewGuid().ToString("N");
        _store[key] = (userTags.Tags, newEtag);
        return Task.FromResult(newEtag);
    }
}
```

```csharp
// tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryEventRepository.cs
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryEventRepository : IEventRepository
{
    private readonly Dictionary<string, Event> _store = new();

    public void Seed(Event @event) => _store[@event.Slug.Value] = @event;

    public Task<Event?> GetAsync(EventSlug slug, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(slug.Value));

    public Task<IReadOnlyList<Event>> ListPublicAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Event> results = _store.Values
            .Where(e => e.IsPublic)
            .OrderByDescending(e => e.EventDate)
            .ToList();
        return Task.FromResult(results);
    }

    public Task SaveAsync(Event @event, CancellationToken ct = default)
    {
        _store[@event.Slug.Value] = @event;
        return Task.CompletedTask;
    }
}
```

```csharp
// tests/BgStacks.Web.Tests/Presentation/Helpers/InMemoryEventDataRepository.cs
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class InMemoryEventDataRepository : IEventDataRepository
{
    private readonly Dictionary<string, EventData> _store = new();

    public void Seed(EventData data) => _store[data.Slug.Value] = data;

    public Task<EventData?> GetAsync(EventSlug slug, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(slug.Value));
}
```

- [ ] **Step 3: Implement CustomWebApplicationFactory**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Helpers/CustomWebApplicationFactory.cs
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BgStacks.Web.Tests.Presentation.Helpers;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryTagsRepository TagsRepository { get; } = new();
    public InMemoryEventRepository EventRepository { get; } = new();
    public InMemoryEventDataRepository EventDataRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITagsRepository>();
            services.RemoveAll<IEventRepository>();
            services.RemoveAll<IEventDataRepository>();
            services.RemoveAll<EventDataService>();

            services.AddScoped<ITagsRepository>(_ => TagsRepository);
            services.AddScoped<IEventRepository>(_ => EventRepository);
            services.AddSingleton<IEventDataRepository>(_ => EventDataRepository);
            services.AddSingleton<EventDataService>();

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // No real Azure credentials needed in tests
        });
    }
}
```

- [ ] **Step 4: Verify the test project builds**

```powershell
dotnet build tests/BgStacks.Web.Tests
```

Expected: Build succeeded (Program not yet complete — build may fail until Task 15 wires Program.cs; if so, stub the factory and revisit after Task 15).

- [ ] **Step 5: Commit**

```powershell
git add tests/BgStacks.Web.Tests/Presentation/Helpers/
git commit -m "test: add WebApplicationFactory, auth handler, and in-memory repository fakes"
```

---

### Task 10: Presentation — EventMiddleware

**Files:**
- Create: `src/BgStacks.Web/Presentation/Events/EventMiddleware.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Events/EventMiddlewareTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Events/EventMiddlewareTests.cs
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Presentation.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BgStacks.Web.Tests.Presentation.Events;

public class EventMiddlewareTests
{
    private static EventMiddleware MakeMiddleware(
        string baseDomain = "bgstacks.com", string? devSlug = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Events:BaseDomain"] = baseDomain,
                ["Events:DevFallbackSlug"] = devSlug,
            })
            .Build();
        return new EventMiddleware(_ => Task.CompletedTask, config);
    }

    private static HttpContext MakeContext(string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        return ctx;
    }

    [Fact]
    public async Task EventSubdomain_SetsSlugInItems()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("gw-2026-pnw.bgstacks.com");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeEquivalentTo(EventSlug.From("gw-2026-pnw"));
    }

    [Fact]
    public async Task RootDomain_SetsNullSlug()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("bgstacks.com");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeNull();
    }

    [Fact]
    public async Task Localhost_WithDevFallback_SetsDevSlug()
    {
        var middleware = MakeMiddleware(devSlug: "dev");
        var ctx = MakeContext("localhost:5000");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeEquivalentTo(EventSlug.From("dev"));
    }

    [Fact]
    public async Task Localhost_NoDevFallback_SetsNullSlug()
    {
        var middleware = MakeMiddleware();
        var ctx = MakeContext("localhost:5000");

        await middleware.InvokeAsync(ctx);

        ctx.Items[EventMiddleware.SlugKey].Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Presentation.Events.EventMiddlewareTests"
```

Expected: Build error — EventMiddleware not defined.

- [ ] **Step 3: Implement EventMiddleware**

```csharp
// src/BgStacks.Web/Presentation/Events/EventMiddleware.cs
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
        var host = context.Request.Host.Host;
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/BgStacks.Web.Tests --filter "Presentation.Events.EventMiddlewareTests"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Presentation/Events/EventMiddleware.cs tests/BgStacks.Web.Tests/Presentation/Events/
git commit -m "feat: add EventMiddleware — resolves event slug from Host header"
```

---

### Task 11: Presentation — AuthEndpoints + GamesJsonEndpoint

**Files:**
- Create: `src/BgStacks.Web/Presentation/Auth/AuthEndpoints.cs`
- Create: `src/BgStacks.Web/Presentation/Events/GamesJsonEndpoint.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Auth/AuthEndpointsTests.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Events/GamesJsonEndpointTests.cs`

Note: These integration tests require `Program.cs` to be complete (Task 15). Write the tests now, but run them after Task 15.

- [ ] **Step 1: Implement AuthEndpoints**

```csharp
// src/BgStacks.Web/Presentation/Auth/AuthEndpoints.cs
using System.Security.Claims;
using BgStacks.Web.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BgStacks.Web.Presentation.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/.auth");

        group.MapGet("/login/{provider}", (string provider, string? post_login_redirect_uri, HttpContext ctx) =>
        {
            var redirectUrl = post_login_redirect_uri ?? "/";
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Results.Challenge(properties, [provider]);
        });

        group.MapGet("/logout", async (string? post_logout_redirect_uri, HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(post_logout_redirect_uri ?? "/");
        });

        group.MapGet("/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new { clientPrincipal = (object?)null });

            var provider = ctx.User.FindFirstValue("idp") ?? "unknown";
            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var userDetails = OAuthUserIdFactory.GetUserDetails(ctx.User);
            var picture = OAuthUserIdFactory.GetPictureUrl(ctx.User, provider);

            var claims = picture is not null
                ? new[] { new { typ = "picture", val = picture } }
                : [];

            return Results.Ok(new
            {
                clientPrincipal = new
                {
                    userId = userId.Value,
                    userDetails,
                    identityProvider = provider,
                    claims,
                }
            });
        });

        return app;
    }
}
```

- [ ] **Step 2: Implement GamesJsonEndpoint**

```csharp
// src/BgStacks.Web/Presentation/Events/GamesJsonEndpoint.cs
using BgStacks.Web.Application.Events;
using BgStacks.Web.Domain.Events;

namespace BgStacks.Web.Presentation.Events;

public static class GamesJsonEndpoint
{
    public static IEndpointRouteBuilder MapGameDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/games.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.GamesJson));

        app.MapGet("/mechanics.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.MechanicsJson));

        app.MapGet("/categories.json", (HttpContext ctx, EventDataService service) =>
            ServeEventFile(ctx, service, data => data.CategoriesJson));

        return app;
    }

    private static async Task<IResult> ServeEventFile(
        HttpContext ctx, EventDataService service, Func<EventData, string> selector)
    {
        if (ctx.Items[EventMiddleware.SlugKey] is not EventSlug slug)
            return Results.NotFound();

        var data = await service.GetEventDataAsync(slug);
        return data is null
            ? Results.NotFound()
            : Results.Content(selector(data), "application/json");
    }
}
```

- [ ] **Step 3: Write integration tests for auth and game data endpoints**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Auth/AuthEndpointsTests.cs
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Auth;

public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetMe_Unauthenticated_ReturnsNullPrincipal()
    {
        TestAuthHandler.CurrentUser = null;

        var response = await _client.GetAsync("/.auth/me");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("clientPrincipal").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMe_Authenticated_ReturnsClientPrincipal()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("google-sub-123", "user@example.com");

        var response = await _client.GetAsync("/.auth/me");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = doc.RootElement.GetProperty("clientPrincipal");
        principal.GetProperty("identityProvider").GetString().Should().Be("google");
        principal.GetProperty("userDetails").GetString().Should().Be("user@example.com");
        principal.GetProperty("userId").GetString().Should().HaveLength(64);
    }

    private static ClaimsPrincipal MakeGoogleUser(string sub, string email) =>
        new(new ClaimsIdentity(
        [
            new Claim("idp", "google"),
            new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, sub),
            new Claim(System.Security.Claims.ClaimTypes.Email, email),
        ], TestAuthHandler.SchemeName));
}
```

```csharp
// tests/BgStacks.Web.Tests/Presentation/Events/GamesJsonEndpointTests.cs
using System.Net;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Events;

public class GamesJsonEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GamesJsonEndpointTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetGamesJson_KnownEventSlug_ReturnsJson()
    {
        var slug = EventSlug.From("gw-2026-pnw");
        _factory.EventDataRepository.Seed(
            new EventData(slug, "[{\"id\":1}]", "[]", "[]"));

        // Simulate request from event subdomain via Host header
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "gw-2026-pnw.bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[{\"id\":1}]");
    }

    [Fact]
    public async Task GetGamesJson_RootDomain_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = "bgstacks.com";

        var response = await client.GetAsync("/games.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 4: Commit**

```powershell
git add src/BgStacks.Web/Presentation/ tests/BgStacks.Web.Tests/Presentation/Auth/ tests/BgStacks.Web.Tests/Presentation/Events/GamesJsonEndpointTests.cs
git commit -m "feat: add AuthEndpoints and GamesJsonEndpoint; add integration tests"
```

---

### Task 12: Presentation — TagsEndpoints + EventsEndpoints

**Files:**
- Create: `src/BgStacks.Web/Presentation/Api/TagsEndpoints.cs`
- Create: `src/BgStacks.Web/Presentation/Api/EventsEndpoints.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Api/TagsEndpointsTests.cs`
- Create: `tests/BgStacks.Web.Tests/Presentation/Api/EventsEndpointsTests.cs`

- [ ] **Step 1: Implement TagsEndpoints**

```csharp
// src/BgStacks.Web/Presentation/Api/TagsEndpoints.cs
using BgStacks.Web.Application.Tags;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Auth;

namespace BgStacks.Web.Presentation.Api;

public static class TagsEndpoints
{
    public static IEndpointRouteBuilder MapTagsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/tags", async (HttpContext ctx, TagsService tagsService) =>
        {
            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var (tags, etag) = await tagsService.GetTagsAsync(userId);
            if (tags is null) return Results.NoContent();
            return Results.Ok(new { tags = new { want = tags.Want, played = tags.Played }, etag });
        });

        group.MapPut("/tags", async (HttpContext ctx, TagsService tagsService, TagsPutRequest? body) =>
        {
            if (body?.Tags is null ||
                body.Tags.Want is null || body.Tags.Played is null)
                return Results.BadRequest(new { error = "invalid body" });

            var userId = OAuthUserIdFactory.FromPrincipal(ctx.User);
            var tags = new Tags(body.Tags.Want, body.Tags.Played);
            try
            {
                var etag = await tagsService.SaveTagsAsync(userId, tags, body.Etag);
                return Results.Ok(new { etag });
            }
            catch (ConflictException)
            {
                return Results.Conflict(new { error = "conflict" });
            }
        });

        return app;
    }
}

public sealed record TagsPutRequest(TagsPayload? Tags, string? Etag);
public sealed record TagsPayload(int[]? Want, int[]? Played);
```

- [ ] **Step 2: Implement EventsEndpoints**

```csharp
// src/BgStacks.Web/Presentation/Api/EventsEndpoints.cs
using BgStacks.Web.Application.Events;
using Microsoft.Extensions.Configuration;

namespace BgStacks.Web.Presentation.Api;

public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (EventService eventService, IConfiguration config) =>
        {
            var baseDomain = config["Events:BaseDomain"] ?? "bgstacks.com";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var events = await eventService.ListPublicEventsAsync();
            var result = events.Select(e => new
            {
                slug = e.Slug.Value,
                name = e.Name,
                eventDate = e.EventDate.ToString("yyyy-MM-dd"),
                isUpcoming = e.IsUpcoming(today),
                url = $"https://{e.Slug.Value}.{baseDomain}/",
            });
            return Results.Ok(result);
        });

        return app;
    }
}
```

- [ ] **Step 3: Write integration tests**

```csharp
// tests/BgStacks.Web.Tests/Presentation/Api/TagsEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Api;

public class TagsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TagsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTags_Unauthenticated_Returns401()
    {
        TestAuthHandler.CurrentUser = null;
        var response = await _client.GetAsync("/api/tags");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTags_AuthenticatedNoData_Returns204()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-no-tags");
        var response = await _client.GetAsync("/api/tags");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PutTags_ValidBody_Returns200WithEtag()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-put-test");
        var body = new { tags = new { want = new[] { 1, 2 }, played = new[] { 3 } }, etag = (string?)null };

        var response = await _client.PutAsJsonAsync("/api/tags", body);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("etag").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PutTags_StaleEtag_Returns409()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-conflict-test");
        var body = new { tags = new { want = new[] { 1 }, played = Array.Empty<int>() }, etag = "stale-etag" };

        var response = await _client.PutAsJsonAsync("/api/tags", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutTags_MissingBody_Returns400()
    {
        TestAuthHandler.CurrentUser = MakeGoogleUser("user-bad-body");
        var response = await _client.PutAsJsonAsync("/api/tags", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static ClaimsPrincipal MakeGoogleUser(string sub) =>
        new(new ClaimsIdentity(
        [
            new Claim("idp", "google"),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim(ClaimTypes.Email, $"{sub}@example.com"),
        ], TestAuthHandler.SchemeName));
}
```

```csharp
// tests/BgStacks.Web.Tests/Presentation/Api/EventsEndpointsTests.cs
using System.Net;
using System.Text.Json;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Tests.Presentation.Helpers;
using FluentAssertions;

namespace BgStacks.Web.Tests.Presentation.Api;

public class EventsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EventsEndpointsTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetEvents_ReturnsPublicEventsOnly()
    {
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("gw-2026-pnw"), "Geekway 2026 PnW",
            new DateOnly(2026, 6, 12), isPublic: true));
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("private-event"), "Private Event",
            new DateOnly(2026, 7, 1), isPublic: false));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/events");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = doc.RootElement.EnumerateArray().ToList();
        events.Should().HaveCount(1);
        events[0].GetProperty("slug").GetString().Should().Be("gw-2026-pnw");
    }

    [Fact]
    public async Task GetEvents_SetsIsUpcomingCorrectly()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        _factory.EventRepository.Seed(new Event(
            EventSlug.From("past-event"), "Past Event", pastDate, isPublic: true));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/events");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var ev = doc.RootElement.EnumerateArray().First(e =>
            e.GetProperty("slug").GetString() == "past-event");
        ev.GetProperty("isUpcoming").GetBoolean().Should().BeFalse();
    }
}
```

- [ ] **Step 4: Commit**

```powershell
git add src/BgStacks.Web/Presentation/Api/ tests/BgStacks.Web.Tests/Presentation/Api/
git commit -m "feat: add TagsEndpoints and EventsEndpoints with integration tests"
```

---

### Task 13: Program.cs + appsettings

**Files:**
- Modify: `src/BgStacks.Web/Program.cs`
- Create: `src/BgStacks.Web/appsettings.json`
- Create: `src/BgStacks.Web/appsettings.Development.json`

- [ ] **Step 1: Write the complete Program.cs**

```csharp
// src/BgStacks.Web/Program.cs
using Azure.Identity;
using Azure.Storage.Blobs;
using BgStacks.Web.Application.Events;
using BgStacks.Web.Application.Tags;
using BgStacks.Web.Domain.Events;
using BgStacks.Web.Domain.Tags;
using BgStacks.Web.Infrastructure.Auth;
using BgStacks.Web.Infrastructure.Events;
using BgStacks.Web.Infrastructure.Tags;
using BgStacks.Web.Presentation.Api;
using BgStacks.Web.Presentation.Auth;
using BgStacks.Web.Presentation.Events;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// ── Cosmos DB ──────────────────────────────────────────────────────────────
var cosmosDatabaseId = builder.Configuration["Cosmos:DatabaseId"] ?? "bgstacks";
var cosmosConnStr = builder.Configuration["Cosmos:ConnectionString"];
var cosmosClient = cosmosConnStr is not null
    ? new CosmosClient(cosmosConnStr)
    : new CosmosClient(builder.Configuration["Cosmos:Endpoint"]!, new DefaultAzureCredential());
builder.Services.AddSingleton(cosmosClient);

// ── Blob Storage ───────────────────────────────────────────────────────────
var blobConnStr = builder.Configuration["Blob:ConnectionString"];
var blobClient = blobConnStr is not null
    ? new BlobServiceClient(blobConnStr)
    : new BlobServiceClient(new Uri(builder.Configuration["Blob:ServiceUri"]!), new DefaultAzureCredential());
builder.Services.AddSingleton(blobClient);

// ── Domain Repositories ────────────────────────────────────────────────────
builder.Services.AddScoped<ITagsRepository>(sp =>
    new CosmosTagsRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
builder.Services.AddScoped<IEventRepository>(sp =>
    new CosmosEventRepository(sp.GetRequiredService<CosmosClient>(), cosmosDatabaseId));
builder.Services.AddSingleton<IEventDataRepository>(sp =>
    new BlobEventDataRepository(sp.GetRequiredService<BlobServiceClient>()));

// ── Application Services ───────────────────────────────────────────────────
builder.Services.AddScoped<TagsService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddSingleton<EventDataService>();
builder.Services.AddMemoryCache();

// ── Authentication ─────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/.auth/login/google";
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Auth:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"]!;
    options.ClaimActions.MapJsonKey("picture", "picture");
    options.Events.OnCreatingTicket = ctx =>
    {
        ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "google"));
        return Task.CompletedTask;
    };
})
.AddFacebook(options =>
{
    options.ClientId = builder.Configuration["Auth:Facebook:ClientId"]!;
    options.ClientSecret = builder.Configuration["Auth:Facebook:ClientSecret"]!;
    options.Events.OnCreatingTicket = ctx =>
    {
        ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "facebook"));
        return Task.CompletedTask;
    };
})
.AddDiscord(options =>
{
    options.ClientId = builder.Configuration["Auth:Discord:ClientId"]!;
    options.ClientSecret = builder.Configuration["Auth:Discord:ClientSecret"]!;
    options.ClaimActions.MapJsonKey("avatar", "avatar");
    options.Scope.Add("identify");
    options.Scope.Add("email");
    options.Events.OnCreatingTicket = ctx =>
    {
        ctx.Identity!.AddClaim(new System.Security.Claims.Claim("idp", "discord"));
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<EventMiddleware>();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTagsEndpoints();
app.MapEventsEndpoints();
app.MapGameDataEndpoints();

// SPA fallback: event subdomain → index.html, root domain → home.html
app.MapFallback(async (HttpContext ctx) =>
{
    var slug = ctx.Items[EventMiddleware.SlugKey];
    var file = slug is not null ? "index.html" : "home.html";
    var path = Path.Combine(app.Environment.WebRootPath, file);
    if (!File.Exists(path)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "text/html";
    await ctx.Response.SendFileAsync(path);
});

app.Run();

public partial class Program { }
```

- [ ] **Step 2: Write appsettings.json**

```json
// src/BgStacks.Web/appsettings.json
{
  "Cosmos": {
    "Endpoint": "https://bgstacks.documents.azure.com:443/",
    "DatabaseId": "bgstacks"
  },
  "Blob": {
    "ServiceUri": "https://bgstacksstorage.blob.core.windows.net"
  },
  "Events": {
    "BaseDomain": "bgstacks.com",
    "DevFallbackSlug": null
  },
  "Auth": {
    "Google": { "ClientId": "", "ClientSecret": "" },
    "Facebook": { "ClientId": "", "ClientSecret": "" },
    "Discord": { "ClientId": "", "ClientSecret": "" }
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  }
}
```

- [ ] **Step 3: Write appsettings.Development.json**

```json
// src/BgStacks.Web/appsettings.Development.json
{
  "Cosmos": {
    "ConnectionString": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b5n/rCSlFy37cMOjggEAAAAAAAAAAAA=="
  },
  "Blob": {
    "ConnectionString": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
  },
  "Events": {
    "BaseDomain": "localhost",
    "DevFallbackSlug": "dev"
  },
  "Auth": {
    "Google": { "ClientId": "dev-placeholder", "ClientSecret": "dev-placeholder" },
    "Facebook": { "ClientId": "dev-placeholder", "ClientSecret": "dev-placeholder" },
    "Discord": { "ClientId": "dev-placeholder", "ClientSecret": "dev-placeholder" }
  }
}
```

- [ ] **Step 4: Verify the solution builds and tests pass**

```powershell
cd C:\Projects\bg-stacks
dotnet build
dotnet test tests/BgStacks.Web.Tests
```

Expected: Build succeeded. Unit tests pass. Integration tests may fail until static files and wwwroot are in place (Task 14) — that is expected.

- [ ] **Step 5: Commit**

```powershell
git add src/BgStacks.Web/Program.cs src/BgStacks.Web/appsettings.json src/BgStacks.Web/appsettings.Development.json
git commit -m "feat: wire Program.cs — DI, middleware pipeline, auth, route registration"
```

---

### Task 14: Static files + home page

**Files:**
- Copy: `public/` → `src/BgStacks.Web/wwwroot/` (excluding `games.json`, `mechanics.json`, `categories.json`)
- Create: `src/BgStacks.Web/wwwroot/home.html`
- Create: `src/BgStacks.Web/wwwroot/home.js`
- Modify: `src/BgStacks.Web/wwwroot/manifest.json` (update to platform identity)

- [ ] **Step 1: Copy static files to wwwroot**

```powershell
$src = "C:\Projects\bg-stacks\public"
$dst = "C:\Projects\bg-stacks\src\BgStacks.Web\wwwroot"
$exclude = @("games.json", "mechanics.json", "categories.json", "staticwebapp.config.json")
Get-ChildItem $src | Where-Object { $_.Name -notin $exclude } | Copy-Item -Destination $dst
```

- [ ] **Step 2: Update manifest.json to platform identity**

Open `src/BgStacks.Web/wwwroot/manifest.json` and replace event-specific metadata with:

```json
{
  "name": "BG Stacks",
  "short_name": "BG Stacks",
  "description": "Sortable, filterable board game lists for conventions.",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#1a1a2e",
  "theme_color": "#1a1a2e",
  "icons": [
    { "src": "/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

- [ ] **Step 3: Create home.html**

```html
<!-- src/BgStacks.Web/wwwroot/home.html -->
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>BG Stacks</title>
  <link rel="stylesheet" href="/styles.css" />
  <link rel="manifest" href="/manifest.json" />
</head>
<body>
  <header class="site-header">
    <span class="site-title">BG Stacks</span>
  </header>
  <main class="home-main">
    <section id="upcoming" class="event-section">
      <h2>Upcoming Events</h2>
      <ul id="upcomingList" class="event-list"></ul>
      <p id="upcomingEmpty" class="dim" hidden>No upcoming events.</p>
    </section>
    <section id="archive" class="event-section">
      <h2>Past Events</h2>
      <ul id="archiveList" class="event-list"></ul>
      <p id="archiveEmpty" class="dim" hidden>No past events.</p>
    </section>
  </main>
  <script src="/home.js"></script>
</body>
</html>
```

- [ ] **Step 4: Create home.js**

```javascript
// src/BgStacks.Web/wwwroot/home.js
(async () => {
  const upcomingList  = document.getElementById('upcomingList');
  const archiveList   = document.getElementById('archiveList');
  const upcomingEmpty = document.getElementById('upcomingEmpty');
  const archiveEmpty  = document.getElementById('archiveEmpty');

  let events = [];
  try {
    const res = await fetch('/api/events');
    if (res.ok) events = await res.json();
  } catch { /* render empty gracefully */ }

  const upcoming = events.filter(e => e.isUpcoming);
  const archive  = events.filter(e => !e.isUpcoming);

  function renderEvent(e) {
    const li = document.createElement('li');
    li.className = 'event-card';
    li.innerHTML = `<a href="${e.url}" class="event-link">
      <span class="event-name">${e.name}</span>
      <span class="event-date dim">${e.eventDate}</span>
    </a>`;
    return li;
  }

  if (upcoming.length > 0) {
    upcoming.forEach(e => upcomingList.appendChild(renderEvent(e)));
  } else {
    upcomingEmpty.hidden = false;
  }

  if (archive.length > 0) {
    archive.forEach(e => archiveList.appendChild(renderEvent(e)));
  } else {
    archiveEmpty.hidden = false;
  }
})();
```

- [ ] **Step 5: Run the full test suite**

```powershell
dotnet test tests/BgStacks.Web.Tests
```

Expected: All tests pass.

- [ ] **Step 6: Smoke-test locally (optional but recommended)**

Start the Cosmos DB emulator and Azurite, then:

```powershell
cd src/BgStacks.Web
dotnet run
```

Navigate to `http://localhost:5000/home.html` — should render the home page. Navigate to `http://localhost:5000` — should serve `home.html` (dev fallback slug is `dev`). Navigate to `http://localhost:5000/games.json` — should return 404 or JSON depending on whether blob data is present.

- [ ] **Step 7: Commit**

```powershell
git add src/BgStacks.Web/wwwroot/
git commit -m "feat: add static files to wwwroot; add home.html and home.js"
```

---

### Task 15: Dockerfile

**Files:**
- Create: `src/BgStacks.Web/Dockerfile`
- Create: `.dockerignore`

- [ ] **Step 1: Write the Dockerfile**

```dockerfile
# src/BgStacks.Web/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/BgStacks.Web/BgStacks.Web.csproj ./
RUN dotnet restore
COPY src/BgStacks.Web/ ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BgStacks.Web.dll"]
```

- [ ] **Step 2: Write .dockerignore**

```
# .dockerignore
**/.git
**/node_modules
**/bin
**/obj
tests/
docs/
scripts/
api/
public/
*.md
.github/
```

- [ ] **Step 3: Build and verify the Docker image**

Run this from the repo root (the Dockerfile uses paths relative to repo root):

```powershell
docker build -f src/BgStacks.Web/Dockerfile -t bgstacks:local .
```

Expected: Image builds successfully. If Docker is not available locally, skip this step and verify via CI.

- [ ] **Step 4: Commit**

```powershell
git add src/BgStacks.Web/Dockerfile .dockerignore
git commit -m "feat: add multi-stage Dockerfile for BgStacks.Web"
```

---

### Task 16: CLAUDE.md update

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md to document the new C# project**

Add the following section to `CLAUDE.md` after the existing content (before or replacing the Deploy section is fine):

```markdown
## C# Application (`src/BgStacks.Web/`)

ASP.NET Core 9 Minimal API. DDD structure: Domain → Application → Infrastructure → Presentation.

```bash
cd src/BgStacks.Web
dotnet run              # requires Cosmos emulator + Azurite running locally
dotnet test ../../tests/BgStacks.Web.Tests
docker build -f Dockerfile -t bgstacks:local ../..
```

### Local dev prerequisites
- [Cosmos DB emulator](https://aka.ms/cosmosdb-emulator) running on https://localhost:8081/
- [Azurite](https://github.com/Azure/Azurite) running: `npx azurite --silent`
- OAuth client IDs/secrets are not needed for local dev (auth flows won't work without them, but the rest of the app does)

### Key config (appsettings.Development.json)
- `Cosmos:ConnectionString` — emulator connection string (hardcoded default in the file)
- `Blob:ConnectionString` — Azurite connection string (hardcoded default in the file)
- `Events:DevFallbackSlug` — slug used when running on localhost (default: `"dev"`)
```

- [ ] **Step 2: Commit**

```powershell
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with C# project commands and local dev setup"
```

---

## Self-Review

**Spec coverage check:**

| Spec section | Covered by task |
|---|---|
| DDD project structure | Task 1 (scaffold), Tasks 2–8 (layers) |
| Domain: UserId, Tags, UserTags, ConflictException | Task 2 |
| Domain: EventSlug, Event, EventData, repository interfaces | Task 3 |
| Application: TagsService | Task 4 |
| Application: EventService, EventDataService | Task 5 |
| Infrastructure: CosmosTagsRepository | Task 7 |
| Infrastructure: CosmosEventRepository, BlobEventDataRepository | Task 8 |
| Infrastructure: OAuthUserIdFactory | Task 6 |
| Presentation: EventMiddleware (null slug for root domain) | Task 10 |
| Presentation: /.auth/* endpoints (login, logout, me) | Task 11 |
| Presentation: /api/tags (GET/PUT, auth, eTag, 409) | Task 12 |
| Presentation: /api/events (public events, isUpcoming, url) | Task 12 |
| Presentation: /games.json, /mechanics.json, /categories.json | Task 11 |
| SPA fallback (home.html vs index.html by slug) | Task 13 |
| Static files in wwwroot | Task 14 |
| home.html + home.js | Task 14 |
| manifest.json platform identity | Task 14 |
| Cookie auth with Google, Facebook, Discord | Task 13 |
| `idp` claim added via OnCreatingTicket | Task 13 |
| clientPrincipal shape (userId, userDetails, identityProvider, claims/picture) | Task 11 |
| Program.cs wiring (DI, middleware, routes) | Task 13 |
| Dockerfile | Task 15 |
| appsettings.json / appsettings.Development.json | Task 13 |

**Placeholder scan:** None found.

**Type consistency check:**
- `UserId.From(provider, sub)` — defined Task 2, used Tasks 6, 11, 12, 13 ✓
- `EventSlug.From(value)` / `TryFrom(value, out slug)` — defined Task 3, used Tasks 8, 10, 11 ✓
- `ITagsRepository.SaveAsync(UserTags, string?)` — defined Task 2, implemented Task 7, called Task 12 ✓
- `ConflictException` — defined Task 2, thrown Task 7, caught Task 12 ✓
- `EventMiddleware.SlugKey` — defined Task 10, used Tasks 11, 13 ✓
- `OAuthUserIdFactory.FromPrincipal(principal)` — defined Task 6, used Tasks 11, 12 ✓
- `TagsService.SaveTagsAsync(userId, tags, clientEtag)` — defined Task 4, called Task 12 ✓
- `EventDataService.GetEventDataAsync(slug)` — defined Task 5, called Task 11 ✓
- `EventService.ListPublicEventsAsync()` — defined Task 5, called Task 12 ✓
