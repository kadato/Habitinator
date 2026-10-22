using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteAiAssistantService(IHttpClientFactory http) : IAiAssistantService
{
    private HttpClient Client => http.CreateClient("api");

    public async Task<AiTestConnectionResponse> TestConnectionAsync(
        AiTestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("api/ai/test", request, JsonDefaults.Api, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                return new AiTestConnectionResponse
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(errorText) ? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}" : errorText
                };
            }

            var result = await response.Content.ReadFromJsonAsync<AiTestConnectionResponse>(JsonDefaults.Api, cancellationToken);
            return result ?? new AiTestConnectionResponse { Success = false, Message = "Empty response from server." };
        }
        catch (Exception ex)
        {
            return new AiTestConnectionResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<AiChatResponse> SendChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("api/ai/chat", request, JsonDefaults.Api, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                return new AiChatResponse
                {
                    Success = false,
                    Error = string.IsNullOrWhiteSpace(errorText) ? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}" : errorText
                };
            }

            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>(JsonDefaults.Api, cancellationToken);
            return result ?? new AiChatResponse { Success = false, Error = "Empty response from server." };
        }
        catch (Exception ex)
        {
            return new AiChatResponse { Success = false, Error = ex.Message };
        }
    }
}
