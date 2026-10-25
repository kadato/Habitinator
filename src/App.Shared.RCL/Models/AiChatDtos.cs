namespace App.Shared.RCL.Models;

public sealed class AiChatMessage
{
    public string Role { get; set; } = "user";

    public string? Content { get; set; }

    public string? Model { get; set; }

    public string? ToolCallId { get; set; }

    public string? Name { get; set; }

    public List<AiToolCall>? ToolCalls { get; set; }

    public static AiChatMessage User(string content) => new() { Role = "user", Content = content };

    public static AiChatMessage Assistant(string content, string? model = null) => new() { Role = "assistant", Content = content, Model = model };

    public static AiChatMessage System(string content) => new() { Role = "system", Content = content };

    public static AiChatMessage Tool(string toolCallId, string name, string content) => new()
    {
        Role = "tool",
        ToolCallId = toolCallId,
        Name = name,
        Content = content
    };
}

public sealed class AiToolCall
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = "function";

    public AiToolFunction Function { get; set; } = new();
}

public sealed class AiToolFunction
{
    public string Name { get; set; } = string.Empty;

    public string Arguments { get; set; } = "{}";
}

public sealed class AiToolExecution
{
    public string ToolName { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public bool IsSuccess { get; set; } = true;
}

public sealed class AiChatRequest
{
    public List<AiChatMessage> Messages { get; set; } = [];

    public string? EndpointOverride { get; set; }

    public string? ApiKeyOverride { get; set; }

    public string? ModelOverride { get; set; }

    public string? ClientTimeZoneId { get; set; }

    public string? ClientToday { get; set; }
}

public sealed class AiChatResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public AiChatMessage? Message { get; set; }

    public List<AiToolExecution> ToolExecutions { get; set; } = [];
}

public sealed class AiTestConnectionRequest
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

public sealed class AiTestConnectionResponse
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public long LatencyMs { get; set; }
}
