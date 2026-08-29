using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

/// <summary>Builds a <see cref="CtmsClient"/> wired to a stub handler and a controllable clock.</summary>
internal static class TestClient
{
    public static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public static CtmsClient Create(
        StubHttpMessageHandler handler,
        out MutableClock clock,
        IBundleStore? store = null,
        Action<CtmsClientOptions>? configure = null)
    {
        clock = new MutableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var options = new CtmsClientOptions
        {
            ProjectId = ProjectId,
            BaseAddress = new Uri("http://ctms.test/"),
            DefaultLocale = "en",
            RequestTimeout = TimeSpan.Zero,
        };
        configure?.Invoke(options);

        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        return new CtmsClient(options, http, store ?? new InMemoryBundleStore(), clock.Now);
    }
}

internal sealed class MutableClock(DateTimeOffset start)
{
    private DateTimeOffset _now = start;

    public Func<DateTimeOffset> Now => () => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
