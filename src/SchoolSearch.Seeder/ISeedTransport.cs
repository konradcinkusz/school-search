using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolSearch.Seeder;

/// <summary>
/// The only way the seeder reaches a service. Production uses <see cref="HttpSeedTransport"/>; tests substitute a
/// fake HttpMessageHandler underneath it. Implementations dispose the request.
/// </summary>
public interface ISeedTransport
{
    Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
