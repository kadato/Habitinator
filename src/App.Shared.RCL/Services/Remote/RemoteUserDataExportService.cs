using System.Net;
using System.Text.Json;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserDataExportService : IUserDataExportService
{
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;

    public RemoteUserDataExportService(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync("api/account/export", cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Sign in required. Open Log in and try again.");
        }

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<UserDataExportDto>(Serializer, cancellationToken)
               ?? throw new InvalidOperationException("Empty response from the export API.");
    }
}
