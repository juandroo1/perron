using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;

namespace Perron.Transport;

public static class TransportServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITransportApiClient"/> with resilience and caching (plan.md, section 5).
    /// The base address comes from <c>TRANSPORT_API_BASE</c>; options are validated at start.
    /// </summary>
    /// <returns>The HTTP client builder, e.g. to replace the primary handler in tests.</returns>
    public static IHttpClientBuilder AddTransportApiClient(
        this IServiceCollection services,
        Action<TransportApiOptions>? configure = null)
    {
        services.AddOptions<TransportApiOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var baseAddress = configuration[TransportApiOptions.BaseAddressVariable];
                if (!string.IsNullOrWhiteSpace(baseAddress))
                {
                    options.BaseAddress = baseAddress.Trim();
                }
            })
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TransportApiOptions>, TransportApiOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TransportCache>();

        var builder = services.AddHttpClient<ITransportApiClient, TransportApiClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<TransportApiOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Perron/{typeof(TransportApiClient).Assembly.GetName().Version?.ToString(3) ?? "0"}");
        });

        var resilience = builder.AddStandardResilienceHandler();
        services.AddOptions<HttpStandardResilienceOptions>(resilience.PipelineName)
            .Configure<IOptions<TransportApiOptions>>((resilienceOptions, transportOptions) =>
            {
                var options = transportOptions.Value;
                resilienceOptions.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
                resilienceOptions.Retry.Delay = options.RetryBaseDelay;
                resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
                resilienceOptions.Retry.UseJitter = true;
                resilienceOptions.Retry.ShouldRetryAfterHeader = true;
                resilienceOptions.AttemptTimeout.Timeout = options.AttemptTimeout;
                resilienceOptions.TotalRequestTimeout.Timeout = options.TotalTimeout;

                // The circuit breaker's sampling window must be at least twice the attempt timeout.
                var minimumSampling = options.AttemptTimeout * 2;
                if (resilienceOptions.CircuitBreaker.SamplingDuration < minimumSampling)
                {
                    resilienceOptions.CircuitBreaker.SamplingDuration = minimumSampling;
                }
            });

        return builder;
    }
}
