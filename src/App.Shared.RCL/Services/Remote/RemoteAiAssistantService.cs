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

    public async IAsyncEnumerable<AiChatStreamEvent> StreamChatAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AiChatStreamEvent>();
        _ = ProduceStreamEventsAsync(request, channel.Writer, cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    private async Task ProduceStreamEventsAsync(
        AiChatRequest request,
        System.Threading.Channels.ChannelWriter<AiChatStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var emittedAny = false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/ai/chat/stream");
            req.Content = JsonContent.Create(request, options: JsonDefaults.Api);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                writer.TryWrite(new AiChatStreamEvent
                {
                    Type = "error",
                    Error = string.IsNullOrWhiteSpace(errorText) ? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}" : errorText
                });
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[6..].Trim();
                if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
                {
                    continue;
                }

                try
                {
                    var evt = System.Text.Json.JsonSerializer.Deserialize<AiChatStreamEvent>(data, JsonDefaults.Api);
                    if (evt is not null)
                    {
                        emittedAny = true;
                        writer.TryWrite(evt);
                    }
                }
                catch
                {
                    // Ignore parse errors on partial frames
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            if (!emittedAny)
            {
                // Fallback to non-streaming endpoint
                var fallback = await SendChatAsync(request, cancellationToken);
                if (fallback.Success && fallback.Message is not null)
                {
                    foreach (var tool in fallback.ToolExecutions)
                    {
                        writer.TryWrite(new AiChatStreamEvent { Type = "tool_end", ToolExecution = tool });
                    }
                    writer.TryWrite(new AiChatStreamEvent { Type = "delta", Text = fallback.Message.Content });
                    writer.TryWrite(new AiChatStreamEvent { Type = "done", Model = fallback.Message.Model });
                    return;
                }

                writer.TryWrite(new AiChatStreamEvent { Type = "error", Error = fallback.Error ?? ex.Message });
            }
            else
            {
                writer.TryWrite(new AiChatStreamEvent { Type = "error", Error = ex.Message });
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }
}
