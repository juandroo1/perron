using Microsoft.Extensions.Options;

namespace Perron.Transport;

/// <summary>Configuration of the transport.opendata.ch client. Defaults follow plan.md, section 5.</summary>
public sealed class TransportApiOptions
{
    public const string BaseAddressVariable = "TRANSPORT_API_BASE";
    public const string DefaultBaseAddress = "https://transport.opendata.ch/v1/";

    /// <summary>Absolute URL ending with <c>/</c>.</summary>
    public string BaseAddress { get; set; } = DefaultBaseAddress;

    public TimeSpan LocationsCacheDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>For <c>/connections</c> and <c>/stationboard</c> (real-time data changes about every 30 s).</summary>
    public TimeSpan TimetableCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of cached responses.</summary>
    public int CacheSizeLimit { get; set; } = 2000;

    /// <summary>1 to 5 (the resilience pipeline needs at least one).</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(25);
}

public sealed class TransportApiOptionsValidator : IValidateOptions<TransportApiOptions>
{
    public ValidateOptionsResult Validate(string? name, TransportApiOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !options.BaseAddress.EndsWith('/'))
        {
            failures.Add(
                $"{TransportApiOptions.BaseAddressVariable} must be an absolute http(s) URL ending with '/', got '{options.BaseAddress}'.");
        }

        if (options.LocationsCacheDuration <= TimeSpan.Zero || options.TimetableCacheDuration <= TimeSpan.Zero)
        {
            failures.Add("Cache durations must be positive.");
        }

        if (options.CacheSizeLimit <= 0)
        {
            failures.Add("CacheSizeLimit must be positive.");
        }

        if (options.MaxRetryAttempts is < 1 or > 5)
        {
            failures.Add("MaxRetryAttempts must be between 1 and 5.");
        }

        if (options.RetryBaseDelay < TimeSpan.Zero)
        {
            failures.Add("RetryBaseDelay must not be negative.");
        }

        if (options.AttemptTimeout <= TimeSpan.Zero || options.TotalTimeout <= options.AttemptTimeout)
        {
            failures.Add("AttemptTimeout must be positive and TotalTimeout greater than AttemptTimeout.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
