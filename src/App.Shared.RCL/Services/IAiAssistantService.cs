using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IAiAssistantService
{
    Task<AiTestConnectionResponse> TestConnectionAsync(AiTestConnectionRequest request, CancellationToken cancellationToken = default);

    Task<AiChatResponse> SendChatAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}
