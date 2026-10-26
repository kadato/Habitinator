using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

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
        var timeZoneLabel = !string.IsNullOrWhiteSpace(request.ClientTimeZoneId)
            ? request.ClientTimeZoneId.Trim()
            : null;
        var systemInstructions = BuildSystemPrompt(today, preferences.CustomInstructions, timeZoneLabel);

        if (IsResponsesProtocol(model, endpoint))
        {
            return await ChatResponsesProtocolAsync(userId, model, endpoint, apiKey, systemInstructions, request, cancellationToken);
        }

        return await ChatCompletionsProtocolAsync(userId, model, endpoint, apiKey, systemInstructions, request, cancellationToken);
    }

    public async IAsyncEnumerable<AiChatStreamEvent> StreamChatAsync(
        Guid userId,
        AiPreferences preferences,
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await ChatAsync(userId, preferences, request, cancellationToken);
        if (!response.Success)
        {
            yield return new AiChatStreamEvent
            {
                Type = "error",
                Error = response.Error ?? "Failed to receive response from AI provider."
            };
            yield break;
        }

        foreach (var tool in response.ToolExecutions)
        {
            yield return new AiChatStreamEvent
            {
                Type = "tool_end",
                ToolExecution = tool
            };
        }

        var fullContent = response.Message?.Content ?? string.Empty;
        var model = response.Message?.Model ?? preferences.Model;

        if (!string.IsNullOrEmpty(fullContent))
        {
            var words = fullContent.Split(' ');
            var batchSize = Math.Max(1, words.Length / 15);
            for (var i = 0; i < words.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, words.Length - i);
                var chunk = string.Join(' ', words.Skip(i).Take(count));
                if (i + count < words.Length)
                {
                    chunk += " ";
                }

                yield return new AiChatStreamEvent
                {
                    Type = "delta",
                    Text = chunk,
                    Model = model
                };
            }
        }

        yield return new AiChatStreamEvent
        {
            Type = "done",
            Model = model
        };
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

        var client = CreateProviderClient();

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = model,
                ["instructions"] = systemInstructions,
                ["input"] = inputArray.DeepClone(),
                ["tools"] = toolsSchema.DeepClone()
            };

            var (root, failure) = await PostProviderJsonAsync(client, endpoint, apiKey, requestBody, toolExecutions, cancellationToken);
            if (failure is not null)
            {
                return failure;
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

        var client = CreateProviderClient();

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messagesArray.DeepClone(),
                ["tools"] = toolsSchema.DeepClone(),
                ["temperature"] = 0.3
            };

            var (root, failure) = await PostProviderJsonAsync(client, endpoint, apiKey, requestBody, toolExecutions, cancellationToken);
            if (failure is not null)
            {
                return failure;
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

    private HttpClient CreateProviderClient()
    {
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        return client;
    }

    private static void AddProviderHeaders(HttpRequestMessage httpRequest, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Habitinator/1.0");
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://habitinator.app");
        httpRequest.Headers.TryAddWithoutValidation("X-Title", "Habitinator");
        httpRequest.Headers.TryAddWithoutValidation("x-opencode-session", $"ses_habitinator_{Guid.NewGuid():N}");
    }

    private async Task<(JsonNode? Root, AiChatResponse? Failure)> PostProviderJsonAsync(
        HttpClient client,
        string endpoint,
        string apiKey,
        JsonObject requestBody,
        List<AiToolExecution> toolExecutions,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        AddProviderHeaders(httpRequest, apiKey);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call AI provider at {Endpoint}", endpoint);
            return (null, new AiChatResponse
            {
                Success = false,
                Error = $"AI request failed: {ex.Message}",
                ToolExecutions = toolExecutions
            });
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorMsg = ExtractErrorMessage(responseJson) ?? $"HTTP {(int)httpResponse.StatusCode} ({httpResponse.ReasonPhrase})";
            return (null, new AiChatResponse
            {
                Success = false,
                Error = $"AI provider returned error: {errorMsg}",
                ToolExecutions = toolExecutions
            });
        }

        try
        {
            return (JsonNode.Parse(responseJson), null);
        }
        catch (Exception ex)
        {
            return (null, new AiChatResponse
            {
                Success = false,
                Error = $"Failed to parse AI response: {ex.Message}",
                ToolExecutions = toolExecutions
            });
        }
    }

    internal async Task<AiToolExecution> ExecuteToolAsync(
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
                            var overdue = snapshot.Todos.Where(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today).ToList();
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Overdue todos:** {overdue.Count}");
                            foreach (var t in overdue.Take(10))
                            {
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {t.Title} (due {t.TodoDueDate!.Value:yyyy-MM-dd}) [ID: {t.Id}]");
                            }
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Dailies:** {snapshot.Dailies.Count(d => d.IsCompleted)}/{snapshot.Dailies.Count} done");
                            foreach (var d in snapshot.Dailies.Where(d => !d.IsCompleted).Take(20))
                            {
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {d.Title} (streak: {d.Counter}) [ID: {d.Id}]");
                            }
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Todos:** {snapshot.Todos.Count(t => !t.IsCompleted)} active");
                            foreach (var t in snapshot.Todos.Where(t => !t.IsCompleted).OrderBy(t => t.TodoDueDate ?? new DateOnly(9999, 12, 31)).Take(20))
                            {
                                var due = t.TodoDueDate.HasValue ? $" (due {t.TodoDueDate.Value:yyyy-MM-dd})" : "";
                                sb.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {t.Title}{due} [ID: {t.Id}]");
                            }
                            sb.AppendLine(CultureInfo.InvariantCulture, $"**Habits:** {snapshot.Habits.Count} tracked");
                            foreach (var h in snapshot.Habits.Take(15))
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

                case "rename_item":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        var title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId) || string.IsNullOrWhiteSpace(title))
                        {
                            exec.Result = "Error: valid itemId GUID and non-empty title are required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var section = await boards.GetItemSectionAsync(userId, itemId, cancellationToken);
                        if (section is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var current = await boards.GetSnapshotAsync(userId, 500, cancellationToken);
                        var item = current.Habits.Concat(current.Dailies).Concat(current.Todos).FirstOrDefault(i => i.Id == itemId);
                        if (item is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var clean = title.Trim();
                        BoardMutationResult result = section switch
                        {
                            BoardSection.Habit => await boards.UpdateHabitAsync(userId, itemId, UpdateHabitArgs.From(item) with { Title = clean }, cancellationToken),
                            BoardSection.Daily => await boards.UpdateDailyAsync(userId, itemId, UpdateDailyArgs.From(item) with { Title = clean }, cancellationToken),
                            _ => await boards.UpdateTodoAsync(userId, itemId, UpdateTodoArgs.From(item) with { Title = clean }, cancellationToken)
                        };
                        exec.Result = $"Renamed to '{clean}'.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "set_due_date":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        var dueStr = root.TryGetProperty("dueDate", out var dProp) ? dProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        DateOnly? due = null;
                        if (!string.IsNullOrWhiteSpace(dueStr))
                        {
                            if (!DateOnly.TryParseExact(dueStr.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDue))
                            {
                                exec.Result = "Error: dueDate must be yyyy-MM-dd or empty to clear.";
                                exec.IsSuccess = false;
                                break;
                            }

                            due = parsedDue;
                        }

                        var snapshot = await boards.GetSnapshotAsync(userId, 500, cancellationToken);
                        var todo = snapshot.Todos.FirstOrDefault(t => t.Id == itemId);
                        if (todo is null)
                        {
                            exec.Result = $"Error: to-do {itemId} not found. set_due_date only applies to todos.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var result = await boards.UpdateTodoAsync(userId, itemId, UpdateTodoArgs.From(todo) with { DueDate = due }, cancellationToken);
                        exec.Result = due.HasValue ? $"Due date set to {due.Value:yyyy-MM-dd}." : "Due date cleared.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "archive_item":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var section = await boards.GetItemSectionAsync(userId, itemId, cancellationToken);
                        if (section is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var result = await boards.ArchiveItemAsync(userId, section.Value, itemId, null, cancellationToken);
                        exec.Result = $"Archived item {itemId}.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "create_items":
                    {
                        if (!root.TryGetProperty("items", out var itemsArr) || itemsArr.ValueKind != JsonValueKind.Array)
                        {
                            exec.Result = "Error: 'items' array is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var createdCount = 0;
                        var summaryList = new List<string>();
                        foreach (var el in itemsArr.EnumerateArray())
                        {
                            var sStr = el.TryGetProperty("section", out var s) ? s.GetString() : "Todo";
                            var tStr = el.TryGetProperty("title", out var t) ? t.GetString() : string.Empty;
                            if (string.IsNullOrWhiteSpace(tStr))
                            {
                                continue;
                            }

                            var nStr = el.TryGetProperty("notes", out var n) ? n.GetString() : null;
                            var tgStr = el.TryGetProperty("tags", out var tg) ? tg.GetString() : null;
                            var dStr = el.TryGetProperty("dueDate", out var d) ? d.GetString() : null;

                            if (!Enum.TryParse<BoardSection>(sStr, true, out var sec))
                            {
                                sec = BoardSection.Todo;
                            }

                            DateOnly? dDate = null;
                            if (!string.IsNullOrWhiteSpace(dStr) && DateOnly.TryParseExact(dStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                            {
                                dDate = dt;
                            }

                            var item = await boards.CreateItemDetailedAsync(
                                userId,
                                sec,
                                tStr.Trim(),
                                nStr,
                                tgStr,
                                startOrDueDate: dDate,
                                itemId: null,
                                cancellationToken: cancellationToken);
                            summaryList.Add($"{sec}: '{item.Title}'");
                            createdCount++;
                        }

                        exec.Result = $"Successfully created {createdCount} items: {string.Join(", ", summaryList)}.";
                        exec.IsSuccess = createdCount > 0;
                        break;
                    }

                case "update_tags":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        var tagsStr = root.TryGetProperty("tags", out var tgProp) ? tgProp.GetString() : null;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var section = await boards.GetItemSectionAsync(userId, itemId, cancellationToken);
                        if (section is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var snapshot = await boards.GetSnapshotAsync(userId, 500, cancellationToken);
                        var item = snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos).FirstOrDefault(i => i.Id == itemId);
                        if (item is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        BoardMutationResult result = section.Value switch
                        {
                            BoardSection.Habit => await boards.UpdateHabitAsync(userId, itemId, UpdateHabitArgs.From(item) with { Tags = tagsStr }, cancellationToken),
                            BoardSection.Daily => await boards.UpdateDailyAsync(userId, itemId, UpdateDailyArgs.From(item) with { Tags = tagsStr }, cancellationToken),
                            _ => await boards.UpdateTodoAsync(userId, itemId, UpdateTodoArgs.From(item) with { Tags = tagsStr }, cancellationToken)
                        };
                        exec.Result = $"Tags updated to '{tagsStr ?? "(cleared)"}'.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "append_notes":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        var notesToAppend = root.TryGetProperty("notes", out var nProp) ? nProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId) || string.IsNullOrWhiteSpace(notesToAppend))
                        {
                            exec.Result = "Error: valid itemId and non-empty notes are required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var section = await boards.GetItemSectionAsync(userId, itemId, cancellationToken);
                        if (section is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var snapshot = await boards.GetSnapshotAsync(userId, 500, cancellationToken);
                        var item = snapshot.Habits.Concat(snapshot.Dailies).Concat(snapshot.Todos).FirstOrDefault(i => i.Id == itemId);
                        if (item is null)
                        {
                            exec.Result = $"Error: item {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var mergedNotes = string.IsNullOrWhiteSpace(item.Notes) ? notesToAppend.Trim() : $"{item.Notes}\n{notesToAppend.Trim()}";
                        BoardMutationResult result = section.Value switch
                        {
                            BoardSection.Habit => await boards.UpdateHabitAsync(userId, itemId, UpdateHabitArgs.From(item) with { Notes = mergedNotes }, cancellationToken),
                            BoardSection.Daily => await boards.UpdateDailyAsync(userId, itemId, UpdateDailyArgs.From(item) with { Notes = mergedNotes }, cancellationToken),
                            _ => await boards.UpdateTodoAsync(userId, itemId, UpdateTodoArgs.From(item) with { Notes = mergedNotes }, cancellationToken)
                        };
                        exec.Result = "Notes updated successfully.";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
                        break;
                    }

                case "set_daily_weekdays":
                    {
                        var idStr = root.TryGetProperty("itemId", out var idProp) ? idProp.GetString() : string.Empty;
                        if (!Guid.TryParse(idStr, out var itemId))
                        {
                            exec.Result = "Error: valid itemId GUID is required.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var mask = DailyWeekdays.All;
                        var propFound = root.TryGetProperty("days", out var wProp) || root.TryGetProperty("weekdays", out wProp);
                        if (propFound)
                        {
                            if (wProp.ValueKind == JsonValueKind.Number)
                            {
                                mask = DailyWeekdays.Normalize(wProp.GetInt32());
                            }
                            else if (wProp.ValueKind == JsonValueKind.String)
                            {
                                var s = wProp.GetString()?.ToLowerInvariant();
                                mask = s switch
                                {
                                    "weekdays" => DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday),
                                    "weekends" => DailyWeekdays.From(DayOfWeek.Saturday, DayOfWeek.Sunday),
                                    "all" or "everyday" => DailyWeekdays.All,
                                    _ => DailyWeekdays.All
                                };
                            }
                            else if (wProp.ValueKind == JsonValueKind.Array)
                            {
                                var days = new List<DayOfWeek>();
                                foreach (var elem in wProp.EnumerateArray())
                                {
                                    if (elem.GetString() is { } str && Enum.TryParse<DayOfWeek>(str, true, out var dow))
                                    {
                                        days.Add(dow);
                                    }
                                }
                                mask = days.Count > 0 ? DailyWeekdays.From([.. days]) : DailyWeekdays.All;
                            }
                        }

                        var snapshot = await boards.GetSnapshotAsync(userId, 500, cancellationToken);
                        var daily = snapshot.Dailies.FirstOrDefault(d => d.Id == itemId);
                        if (daily is null)
                        {
                            exec.Result = $"Error: daily {itemId} not found.";
                            exec.IsSuccess = false;
                            break;
                        }

                        var result = await boards.UpdateDailyAsync(
                            userId,
                            itemId,
                            UpdateDailyArgs.From(daily) with { Weekdays = mask, Repeat = DailyRepeatType.Weekly },
                            cancellationToken);
                        exec.Result = $"Daily schedule updated (mask {mask}).";
                        exec.IsSuccess = result.Status == BoardMutationStatus.Ok;
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

    private static string BuildSystemPrompt(DateOnly today, string? customInstructions, string? timeZoneLabel = null)
    {
        var custom = string.IsNullOrWhiteSpace(customInstructions) ? "" : $"\nUser Custom Instructions:\n{customInstructions.Trim()}\n";
        var tz = string.IsNullOrWhiteSpace(timeZoneLabel) ? "" : $" User timezone: {timeZoneLabel.Trim()}.\n";
        return
            $"You are Habitinator AI, a productivity assistant and habit coach inside the Habitinator app.\n" +
            $"Today is {today:yyyy-MM-dd} ({today.DayOfWeek}).\n" +
            tz +
            "You have direct access to the user's board tools:\n" +
            "- get_board_summary: Use this to see pending and completed dailies, active and overdue todos, and habits.\n" +
            "- get_snapshot: Use this to inspect items with their IDs.\n" +
            "- create_item: Creates a Habit, Daily, or Todo on the board.\n" +
            "- create_items: Creates multiple Habits, Dailies, or Todos at once in batch.\n" +
            "- toggle_item: Toggles a Daily or Todo as completed or uncompleted.\n" +
            "- habit_plus: Increments a positive habit tick.\n" +
            "- habit_minus: Increments a negative habit tick.\n" +
            "- rename_item: Renames any board item by ID.\n" +
            "- update_tags: Updates tags on any board item by ID.\n" +
            "- append_notes: Appends notes to any board item by ID.\n" +
            "- set_daily_weekdays: Sets weekly schedule for a Daily (weekdays, weekends, all).\n" +
            "- set_due_date: Sets or clears a Todo due date (yyyy-MM-dd or empty to clear).\n" +
            "- archive_item: Archives a Habit, Daily, or Todo by ID.\n" +
            "- get_timer_summary: Inspects focus timer and Pomodoro minutes.\n\n" +
            "Rules:\n" +
            "1. When the user asks what to do, what's left, or to plan their day, always call get_board_summary first.\n" +
            "2. When creating items, specify section ('Habit', 'Daily', or 'Todo') and title clearly.\n" +
            "3. Prefer rename_item, set_due_date, and archive_item over delete. Never delete without explicit user confirmation.\n" +
            "4. Format your responses with clear Markdown bullet points, tables where helpful, and bold highlights.\n" +
            "5. Keep answers encouraging, actionable, and concise.\n" +
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
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "rename_item",
                    ["description"] = "Renames any habit, daily, or to-do by ID.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the item to rename."
                            },
                            ["title"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The new title."
                            }
                        },
                        ["required"] = new JsonArray { "itemId", "title" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "set_due_date",
                    ["description"] = "Sets or clears a to-do due date. Use yyyy-MM-dd, or empty to clear.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the to-do."
                            },
                            ["dueDate"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Due date yyyy-MM-dd, or empty to clear."
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
                    ["name"] = "archive_item",
                    ["description"] = "Archives a habit, daily, or to-do by ID.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the item to archive."
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
                    ["name"] = "create_items",
                    ["description"] = "Creates multiple Habits, Dailies, or Todos in a single batch operation.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["description"] = "List of items to create.",
                                ["items"] = new JsonObject
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
                                            ["description"] = "Optional notes."
                                        },
                                        ["tags"] = new JsonObject
                                        {
                                            ["type"] = "string",
                                            ["description"] = "Optional comma-separated tags."
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
                        ["required"] = new JsonArray { "items" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "update_tags",
                    ["description"] = "Updates comma-separated tags for any item by ID.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the item."
                            },
                            ["tags"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The comma-separated tags (or empty string to clear)."
                            }
                        },
                        ["required"] = new JsonArray { "itemId", "tags" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "append_notes",
                    ["description"] = "Appends notes/description to any item by ID.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the item."
                            },
                            ["notes"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Notes to append."
                            }
                        },
                        ["required"] = new JsonArray { "itemId", "notes" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "set_daily_weekdays",
                    ["description"] = "Sets which days of the week a Daily repeats on ('weekdays', 'weekends', 'all', or an integer bitmask 0-127).",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["itemId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The GUID of the Daily."
                            },
                            ["weekdays"] = new JsonObject
                            {
                                ["description"] = "Schedule specification: 'weekdays', 'weekends', 'all', or integer bitmask (0-127)."
                            }
                        },
                        ["required"] = new JsonArray { "itemId", "weekdays" }
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
