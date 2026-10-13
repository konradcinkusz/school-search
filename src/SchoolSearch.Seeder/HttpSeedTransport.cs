using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolSearch.Seeder;

/// <summary>Sends requests with an <see cref="HttpClient"/>. Network failures and timeouts become <see cref="ApiResponse.NoResponse"/>.</summary>
public sealed class HttpSeedTransport : ISeedTransport
{
    private const string RetryAfterHeader = "Retry-After";

    private readonly HttpClient _client;

    public HttpSeedTransport(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            string? headerValue = null;
            if (response.Headers.TryGetValues(RetryAfterHeader, out IEnumerable<string>? values))
            {
                headerValue = values?.FirstOrDefault();
            }

            return new ApiResponse((int)response.StatusCode, body, RetryAfterReader.Parse(headerValue, body));
        }
        catch (HttpRequestException)
        {
            return ApiResponse.NoResponse();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a TaskCanceledException; caller cancellation still propagates.
            return ApiResponse.NoResponse();
        }
        finally
        {
            request.Dispose();
        }
    }
}
