using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using App.Shared.RCL.Models;

namespace App.Web.Services;

public sealed class AiAssistantService(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpClientFactory httpFactory,
    ILogger<AiAssistantService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxToolIterations = 5;

    public async Task<AiTestConnectionResponse> TestConnectionAsync(
        AiTestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return new AiTestConnectionResponse { Success = false, Message = "Endpoint URL is required." };
        }

        var model = NormalizeModelName(request.Model);
        var endpoint = ResolveEndpoint(request.Endpoint, model);
        var apiKey = ResolveApiKey(request.ApiKey, null);
        var isResponses = IsResponsesProtocol(model, endpoint);

        JsonNode payload;
        if (isResponses)
        {
            payload = new JsonObject
            {
                ["model"] = model,
                ["input"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "user",
                        ["content"] = "Hello! Please reply with 'OK'."
                    }
                }
            };
        }
        else
        {
            payload = new JsonObject
            {
                ["model"] = model,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = "Hello! Please reply with 'OK'."
                    }
                },
                ["max_tokens"] = 60
            };
        }

        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Habitinator/1.0");
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://habitinator.app");
        httpRequest.Headers.TryAddWithoutValidation("X-Title", "Habitinator");
        httpRequest.Headers.TryAddWithoutValidation("x-opencode-session", $"ses_habitinator_{Guid.NewGuid():N}");

        var sw = Stopwatch.StartNew();
        try
        {
            using var httpResponse = await client.SendAsync(httpRequest, cancellationToken);
            sw.Stop();

            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorMsg = ExtractErrorMessage(responseBody) ?? $"HTTP {(int)httpResponse.StatusCode} ({httpResponse.ReasonPhrase})";
                return new AiTestConnectionResponse
                {
                    Success = false,
                    Message = $"Connection failed: {errorMsg}",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }

            return new AiTestConnectionResponse
            {
                Success = true,
                Message = $"Connected successfully to {model} ({sw.ElapsedMilliseconds}ms)",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiTestConnectionResponse
            {
                Success = false,
                Message = $"Request failed: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<AiChatResponse> ChatAsync(
        Guid userId,
        AiPreferences preferences,
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = NormalizeModelName(!string.IsNullOrWhiteSpace(request.ModelOverride) ? request.ModelOverride : preferences.Model);
        var endpoint = ResolveEndpoint(
            !string.IsNullOrWhiteSpace(request.EndpointOverride) ? request.EndpointOverride : preferences.Endpoint,
            model);
        var apiKey = ResolveApiKey(request.ApiKeyOverride, preferences.ApiKey);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new AiChatResponse
            {
                Success = false,
                Error = "AI Assistant endpoint is not configured. Please open Settings -> AI Assistant."
            };
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiChatResponse
            {
                Success = false,
                Error = "AI Assistant API key is not configured. Please open Settings -> AI Assistant to provide your API key or configure OPENCODE_API_KEY in the environment."
            };
        }

        var today = await boards.TodayAsync(userId, cancellationToken);
        var systemInstructions = BuildSystemPrompt(today, preferences.CustomInstructions);

        if (IsResponsesProtocol(model, endpoint))
        {
            return await ChatResponsesProtocolAsync(userId, model, endpoint, apiKey, systemInstructions, request, cancellationToken);
        }

        return await ChatCompletionsProtocolAsync(userId, model, endpoint, apiKey, systemInstructions, request, cancellationToken);
    }

    private async Task<AiChatResponse> ChatResponsesProtocolAsync(
        Guid userId,
        string model,
        string endpoint,
        string apiKey,
        string systemInstructions,
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var toolExecutions = new List<AiToolExecution>();
        var toolsSchema = GetFlatToolsSchema();

        var inputArray = new JsonArray();
        foreach (var msg in request.Messages)
        {
            if (string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                inputArray.Add(new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = msg.Content ?? string.Empty
                });
            }
            else if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                if (msg.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        inputArray.Add(new JsonObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = tc.Id,
                            ["name"] = tc.Function.Name,
                            ["arguments"] = tc.Function.Arguments
                        });
                    }
                }
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    inputArray.Add(new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = msg.Content
                    });
                }
            }
            else if (string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(msg.ToolCallId))
            {
                inputArray.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = msg.ToolCallId,
                    ["output"] = msg.Content ?? string.Empty
                });
            }
        }

        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = model,
                ["instructions"] = systemInstructions,
                ["input"] = inputArray.DeepClone(),
                ["tools"] = toolsSchema.DeepClone()
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Habitinator/1.0");
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://habitinator.app");
            httpRequest.Headers.TryAddWithoutValidation("X-Title", "Habitinator");
            httpRequest.Headers.TryAddWithoutValidation("x-opencode-session", $"ses_habitinator_{Guid.NewGuid():N}");

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await client.SendAsync(httpRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to call AI provider at {Endpoint}", endpoint);
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"AI request failed: {ex.Message}",
                    ToolExecutions = toolExecutions
                };
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorMsg = ExtractErrorMessage(responseJson) ?? $"HTTP {(int)httpResponse.StatusCode} ({httpResponse.ReasonPhrase})";
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"AI provider returned error: {errorMsg}",
                    ToolExecutions = toolExecutions
                };
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(responseJson);
            }
            catch (Exception ex)
            {
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"Failed to parse AI response: {ex.Message}",
                    ToolExecutions = toolExecutions
                };
            }

            var outputArray = root?["output"]?.AsArray();
            if (outputArray is null || outputArray.Count == 0)
            {
                return new AiChatResponse
                {
                    Success = false,
                    Error = "No output returned by the AI provider.",
                    ToolExecutions = toolExecutions
                };
            }

            var functionCalls = new List<(string CallId, string Name, string Arguments)>();
            var messageTexts = new List<string>();

            foreach (var item in outputArray)
            {
                var type = item?["type"]?.GetValue<string>();
                if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
                {
                    var callId = item?["call_id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    var name = item?["name"]?.GetValue<string>() ?? string.Empty;
                    var args = item?["arguments"]?.GetValue<string>() ?? "{}";
                    functionCalls.Add((callId, name, args));
                }
                else if (string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
                {
                    var contentNode = item?["content"];
                    if (contentNode is JsonArray arr)
                    {
                        foreach (var c in arr)
                        {
                            var text = c?["text"]?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                messageTexts.Add(text);
                            }
                        }
                    }
                    else if (contentNode is not null)
                    {
                        var text = contentNode.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messageTexts.Add(text);
                        }
                    }
                }
            }

            if (functionCalls.Count == 0)
            {
                var assistantText = string.Join("\n\n", messageTexts);
                return new AiChatResponse
                {
                    Success = true,
                    Message = new AiChatMessage
                    {
                        Role = "assistant",
                        Content = assistantText,
                        Model = model
                    },
                    ToolExecutions = toolExecutions
                };
            }

            var assistantMsgDto = new AiChatMessage
            {
                Role = "assistant",
                Content = string.Join("\n\n", messageTexts),
                ToolCalls = []
            };

            foreach (var (callId, fnName, fnArgs) in functionCalls)
            {
                assistantMsgDto.ToolCalls.Add(new AiToolCall
                {
                    Id = callId,
                    Type = "function",
                    Function = new AiToolFunction
                    {
                        Name = fnName,
                        Arguments = fnArgs
                    }
                });

                inputArray.Add(new JsonObject
                {
                    ["type"] = "function_call",
                    ["call_id"] = callId,
                    ["name"] = fnName,
                    ["arguments"] = fnArgs
                });

                var execution = await ExecuteToolAsync(userId, fnName, fnArgs, cancellationToken);
                toolExecutions.Add(execution);

                inputArray.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = callId,
                    ["output"] = execution.Result
                });
            }
        }

        return new AiChatResponse
        {
            Success = true,
            Message = new AiChatMessage
            {
                Role = "assistant",
                Content = "I completed the requested actions on your board.",
                Model = model
            },
            ToolExecutions = toolExecutions
        };
    }

    private async Task<AiChatResponse> ChatCompletionsProtocolAsync(
        Guid userId,
        string model,
        string endpoint,
        string apiKey,
        string systemInstructions,
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var toolExecutions = new List<AiToolExecution>();
        var toolsSchema = GetToolsSchema();

        var messagesArray = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemInstructions
            }
        };

        foreach (var msg in request.Messages)
        {
            var node = new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(msg.ToolCallId))
            {
                node["tool_call_id"] = msg.ToolCallId;
            }
            if (!string.IsNullOrWhiteSpace(msg.Name))
            {
                node["name"] = msg.Name;
            }
            if (msg.ToolCalls is { Count: > 0 })
            {
                var tcArray = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    tcArray.Add(new JsonObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = tc.Type,
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.Function.Name,
                            ["arguments"] = tc.Function.Arguments
                        }
                    });
                }
                node["tool_calls"] = tcArray;
            }
            messagesArray.Add(node);
        }

        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messagesArray.DeepClone(),
                ["tools"] = toolsSchema.DeepClone(),
                ["temperature"] = 0.3
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Habitinator/1.0");
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://habitinator.app");
            httpRequest.Headers.TryAddWithoutValidation("X-Title", "Habitinator");
            httpRequest.Headers.TryAddWithoutValidation("x-opencode-session", $"ses_habitinator_{Guid.NewGuid():N}");

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await client.SendAsync(httpRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to call AI provider at {Endpoint}", endpoint);
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"AI request failed: {ex.Message}",
                    ToolExecutions = toolExecutions
                };
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorMsg = ExtractErrorMessage(responseJson) ?? $"HTTP {(int)httpResponse.StatusCode} ({httpResponse.ReasonPhrase})";
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"AI provider returned error: {errorMsg}",
                    ToolExecutions = toolExecutions
                };
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(responseJson);
            }
            catch (Exception ex)
            {
                return new AiChatResponse
                {
                    Success = false,
                    Error = $"Failed to parse AI response: {ex.Message}",
                    ToolExecutions = toolExecutions
                };
            }

            var choice = root?["choices"]?[0];
            var choiceMessage = choice?["message"];
            if (choiceMessage is null)
            {
                return new AiChatResponse
                {
                    Success = false,
                    Error = "No choices returned by the AI provider.",
                    ToolExecutions = toolExecutions
                };
            }

            var assistantRole = choiceMessage["role"]?.GetValue<string>() ?? "assistant";
            var assistantContent = choiceMessage["content"]?.GetValue<string>() ?? string.Empty;
            var toolCallsNode = choiceMessage["tool_calls"]?.AsArray();

            if (toolCallsNode is null || toolCallsNode.Count == 0)
            {
                return new AiChatResponse
                {
                    Success = true,
                    Message = new AiChatMessage
                    {
                        Role = assistantRole,
                        Content = assistantContent,
                        Model = model
                    },
                    ToolExecutions = toolExecutions
                };
            }

            var assistantMsgDto = new AiChatMessage
            {
                Role = assistantRole,
                Content = assistantContent,
                ToolCalls = []
            };

            var assistantJsonObj = new JsonObject
            {
                ["role"] = assistantRole,
                ["content"] = string.IsNullOrWhiteSpace(assistantContent) ? null : assistantContent,
                ["tool_calls"] = toolCallsNode.DeepClone()
            };
            messagesArray.Add(assistantJsonObj);

            foreach (var tcNode in toolCallsNode)
            {
                var callId = tcNode?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var fnName = tcNode?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var fnArgs = tcNode?["function"]?["arguments"]?.GetValue<string>() ?? "{}";

                assistantMsgDto.ToolCalls.Add(new AiToolCall
                {
                    Id = callId,
                    Type = "function",
                    Function = new AiToolFunction
                    {
                        Name = fnName,
                        Arguments = fnArgs
                    }
                });

                var execution = await ExecuteToolAsync(userId, fnName, fnArgs, cancellationToken);
                toolExecutions.Add(execution);

                messagesArray.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = callId,
                    ["name"] = fnName,
                    ["content"] = execution.Result
                });
            }
        }

        return new AiChatResponse
        {
            Success = true,
            Message = new AiChatMessage
            {
                Role = "assistant",
                Content = "I completed the requested actions on your board.",
                Model = model
            },
            ToolExecutions = toolExecutions
        };
    }

    private async Task<AiToolExecution> ExecuteToolAsync(
        Guid userId,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var exec = new AiToolExecution
        {
            ToolName = toolName,
            Arguments = argumentsJson
        };

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "get_board_summary":
                    {
                        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
                        var today = await boards.TodayAsync(userId, cancellationToken);
                        var format = root.TryGetProperty("format", out var f) ? f.GetString() : "markdown";

                        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                        {
                            var summaryObj = new
                            {
                                Date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                HabitsCount = snapshot.Habits.Count,
                                DailiesCount = snapshot.Dailies.Count,
                                DailiesDone = snapshot.Dailies.Count(d => d.IsCompleted),
                                DailiesPending = snapshot.Dailies.Count(d => !d.IsCompleted),
                                TodosActive = snapshot.Todos.Count(t => !t.IsCompleted),
                                TodosOverdue = snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today)
                            };
                            exec.Result = JsonSerializer.Serialize(summaryObj, Json);
                        }
                        else
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(CultureInfo.InvariantCulture, $"# Board Summary ({today:yyyy-MM-dd})");
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Dailies:** {snapshot.Dailies.Count(d => d.IsCompleted)}/{snapshot.Dailies.Count} done");
                            foreach (var d in snapshot.Dailies.Where(d => !d.IsCompleted).Take(10))
                            {
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {d.Title} (streak: {d.Counter}) [ID: {d.Id}]");
                            }
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Todos:** {snapshot.Todos.Count(t => !t.IsCompleted)} active");
                            foreach (var t in snapshot.Todos.Where(t => !t.IsCompleted).Take(10))
                            {
                                var due = t.TodoDueDate.HasValue ? $" (due {t.TodoDueDate.Value:yyyy-MM-dd})" : "";
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {t.Title}{due} [ID: {t.Id}]");
                            }
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Habits:** {snapshot.Habits.Count} tracked");
                            foreach (var h in snapshot.Habits.Take(8))
                            {
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- {h.Title}: +{h.Counter}/-{h.NegativeCounter} [ID: {h.Id}]");
                            }
                            exec.Result = sb.ToString();
                        }
                        exec.IsSuccess = true;
                        break;
                    }

                case "get_snapshot":
                    {
                        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
                        var items = new
                        {
                            Habits = snapshot.Habits.Select(h => new { h.Id, h.Title, h.Counter, h.NegativeCounter, h.Tags, h.Notes }),
                            Dailies = snapshot.Dailies.Select(d => new { d.Id, d.Title, d.IsCompleted, d.Counter, d.Tags, d.Notes }),
                            Todos = snapshot.Todos.Select(t => new { t.Id, t.Title, t.IsCompleted, DueDate = t.TodoDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.Tags, t.Notes })
                        };
                        exec.Result = JsonSerializer.Serialize(items, Json);
                        exec.IsSuccess = true;
                        break;
                    }

                case "create_item":
                    {
                        var sectionStr = root.TryGetProperty("section", out var s) ? s.GetString() : "Todo";
                        var title = root.TryGetProperty("title", out var t) ? t.GetString() : string.Empty;
                        var notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
                        var tags = root.TryGetProperty("tags", out var tg) ? tg.GetString() : null;
                        var dueDateStr = root.TryGetProperty("dueDate", out var d) ? d.GetString() : null;

                        if (string.IsNullOrWhiteSpace(title))
                        {
                            exec.Result = "Error: title is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        if (!Enum.TryParse<BoardSection>(sectionStr, true, out var section))
                        {
                            section = BoardSection.Todo;
                        }

                        DateOnly? parsedDate = null;
                        if (!string.IsNullOrWhiteSpace(dueDateStr) && DateOnly.TryParseExact(dueDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        {
                            parsedDate = dt;
                        }

                        var created = await boards.CreateItemDetailedAsync(
                            userId,
                            section,
                            title.Trim(),
                            notes,
                            tags,
                            startOrDueDate: parsedDate,
                            itemId: null,
                            cancellationToken: cancellationToken);

                        exec.Result = $"Successfully created {section} '{created.Title}' with ID {created.Id}.";
                        exec.IsSuccess = true;
                        break;
                    }

                case "toggle_item":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var section = await boards.GetItemSectionAsync(userId, itemId, cancellationToken) ?? BoardSection.Todo;
                        var result = await boards.ToggleItemAsync(userId, section, itemId, null, cancellationToken);
                        exec.Result = $"Item {itemId} toggled. IsCompleted: {result.Item?.IsCompleted}.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "habit_plus":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var result = await boards.IncrementHabitPlusAsync(userId, itemId, null, cancellationToken);
                        exec.Result = $"Habit incremented (+). New positive count: {result.Item?.Counter}.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "habit_minus":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var result = await boards.IncrementHabitMinusAsync(userId, itemId, null, cancellationToken);
                        exec.Result = $"Habit decremented (-). New negative count: {result.Item?.NegativeCounter}.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "get_timer_summary":
                    {
                        var today = await boards.TodayAsync(userId, cancellationToken);
                        var summary = await stats.GetTimerSummaryAsync(userId, today, cancellationToken);
                        exec.Result = JsonSerializer.Serialize(summary, Json);
                        exec.IsSuccess = true;
                        break;
                    }

                default:
                    exec.Result = $"Unknown tool '{toolName}'.";
                    exec.IsSuccess = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing AI tool {Tool}", toolName);
            exec.Result = $"Tool execution error: {ex.Message}";
            exec.IsSuccess = false;
        }

        return exec;
    }

    private static string BuildSystemPrompt(DateOnly today, string? customInstructions)
    {
        var custom = string.IsNullOrWhiteSpace(customInstructions) ? "" : $"\nUser Custom Instructions:\n{customInstructions.Trim()}\n";
        return
            $"You are Habitinator AI, a productivity assistant and habit coach inside the Habitinator app.\n" +
            $"Today is {today:yyyy-MM-dd} ({today.DayOfWeek}).\n" +
            "You have direct access to the user's board tools:\n" +
            "- get_board_summary: Use this to see pending and completed dailies, active and overdue todos, and habits.\n" +
            "- get_snapshot: Use this to inspect items with their IDs.\n" +
            "- create_item: Creates a Habit, Daily, or Todo on the board.\n" +
            "- toggle_item: Toggles a Daily or Todo as completed or uncompleted.\n" +
            "- habit_plus: Increments a positive habit tick.\n" +
            "- habit_minus: Increments a negative habit tick.\n" +
            "- get_timer_summary: Inspects focus timer and Pomodoro minutes.\n\n" +
            "Rules:\n" +
            "1. When the user asks what to do, what's left, or to plan their day, always call get_board_summary first.\n" +
            "2. When creating items, specify section ('Habit', 'Daily', or 'Todo') and title clearly.\n" +
            "3. Format your responses with clear Markdown bullet points and bold highlights.\n" +
            "4. Keep answers encouraging, actionable, and concise.\n" +
            custom;
    }

    private static JsonArray GetToolsSchema()
    {
        return
        [
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_board_summary",
                    ["description"] = "Returns a summary of today's active habits, completed/pending dailies, overdue and active todos, and streaks.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["format"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray { "markdown", "json" },
                                ["description"] = "Format: 'markdown' or 'json'. Defaults to 'markdown'."
                            }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_snapshot",
                    ["description"] = "Reads the full list of habits, dailies, and todos with their IDs for mutations.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "create_item",
                    ["description"] = "Creates a new item on the user's board (Habit, Daily, or Todo).",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["section"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray { "Habit", "Daily", "Todo" },
                                ["description"] = "Which section to add to: 'Habit', 'Daily', or 'Todo'."
                            },
                            ["title"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The title of the item."
                            },
                            ["notes"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional notes or description."
                            },
                            ["tags"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional comma-separated tags, e.g. 'work, health'."
                            },
                            ["dueDate"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional due date for Todos (yyyy-MM-dd)."
                            }
                        },
                        ["required"] = new JsonArray { "section", "title" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "toggle_item",
                    ["description"] = "Toggles a Daily or Todo item between done and pending.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the item to toggle."
                            }
                        },
                        ["required"] = new JsonArray { "itemId" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "habit_plus",
                    ["description"] = "Increments a habit's positive counter.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the habit."
                            }
                        },
                        ["required"] = new JsonArray { "itemId" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "habit_minus",
                    ["description"] = "Increments a habit's negative counter.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the habit."
                            }
                        },
                        ["required"] = new JsonArray { "itemId" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_timer_summary",
                    ["description"] = "Returns focus timer statistics, Pomodoro sessions, and total focus minutes.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            }
        ];
    }

    private static JsonArray GetFlatToolsSchema()
    {
        var schema = GetToolsSchema();
        var flat = new JsonArray();
        foreach (var item in schema)
        {
            if (item is JsonObject obj && obj["function"] is JsonObject fn)
            {
                var flatObj = new JsonObject
                {
                    ["type"] = "function",
                    ["name"] = fn["name"]?.GetValue<string>(),
                    ["description"] = fn["description"]?.GetValue<string>(),
                    ["parameters"] = fn["parameters"]?.DeepClone()
                };
                flat.Add(flatObj);
            }
        }
        return flat;
    }

    private static bool IsResponsesProtocol(string model, string endpoint)
    {
        if (endpoint.Contains("/responses", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (model.StartsWith("muse-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string ResolveEndpoint(string endpoint, string model)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.Equals("https://opencode.ai/inference/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://opencode.ai/inference/go/openai/v1";
        }

        if (IsResponsesProtocol(model, trimmed))
        {
            if (trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^"/chat/completions".Length] + "/responses";
            }
            return $"{trimmed}/responses";
        }
        else
        {
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            if (trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^"/responses".Length] + "/chat/completions";
            }
            return $"{trimmed}/chat/completions";
        }
    }

    private static string NormalizeModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return AiPreferences.DefaultModel;
        }

        var trimmed = model.Trim();
        if (trimmed.StartsWith("opencode-go/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed["opencode-go/".Length..];
        }

        return trimmed;
    }

    private static string ResolveApiKey(string? explicitKey, string? preferencesKey)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey.Trim();
        }
        if (!string.IsNullOrWhiteSpace(preferencesKey))
        {
            return preferencesKey.Trim();
        }
        return GetFallbackApiKey();
    }

    private static string GetFallbackApiKey()
    {
        var env = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        try
        {
            // Search upward from current working directory
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir is not null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envPath))
                {
                    var found = ParseApiKeyFromEnvFile(envPath);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
                dir = dir.Parent;
            }

            // Also search upward from base directory
            dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envPath))
                {
                    var found = ParseApiKeyFromEnvFile(envPath);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore file access exceptions
        }

        return string.Empty;
    }

    private static string? ParseApiKeyFromEnvFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("OPENCODE_API_KEY=", StringComparison.OrdinalIgnoreCase))
            {
                var val = trimmed["OPENCODE_API_KEY=".Length..].Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }
        return null;
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                if (errorProp.ValueKind == JsonValueKind.String)
                {
                    return errorProp.GetString();
                }
                if (errorProp.TryGetProperty("message", out var msgProp))
                {
                    return msgProp.GetString();
                }
            }
        }
        catch
        {
            // Ignore parse failures
        }
        return null;
    }
}
