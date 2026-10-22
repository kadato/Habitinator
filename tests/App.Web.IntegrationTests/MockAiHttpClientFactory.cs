using System.Net;
using System.Text;

namespace App.Web.IntegrationTests;

internal sealed class MockAiHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new MockAiHttpMessageHandler());
}

internal sealed class MockAiHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(15, cancellationToken);

        var content = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;

        var url = request.RequestUri?.ToString() ?? string.Empty;

        var isTest = content.Contains("Hello! Please reply with 'OK'", StringComparison.OrdinalIgnoreCase);
        var isResponses = url.Contains("/responses", StringComparison.OrdinalIgnoreCase) ||
                          content.Contains("muse-", StringComparison.OrdinalIgnoreCase);

        string responseJson;

        if (isResponses)
        {
            if (isTest)
            {
                responseJson = "{\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"OK\"}]}";
            }
            else if (content.Contains("function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                var habitName = ExtractHabitName(content);
                responseJson = "{\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"You have " + habitName + " on your board.\"}]}";
            }
            else
            {
                responseJson = "{\"output\":[{\"type\":\"function_call\",\"call_id\":\"call_muse_test_1\",\"name\":\"get_board_summary\",\"arguments\":\"{}\"}]}";
            }
        }
        else
        {
            if (isTest)
            {
                responseJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}";
            }
            else if (content.Contains("\"role\":\"tool\"", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("\"role\": \"tool\"", StringComparison.OrdinalIgnoreCase))
            {
                var habitName = ExtractHabitName(content);
                responseJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"You have " + habitName + " on your board.\"}}]}";
            }
            else
            {
                responseJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_mimo_test_1\",\"type\":\"function\",\"function\":{\"name\":\"get_board_summary\",\"arguments\":\"{}\"}}]}}]}";
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };
    }

    private static string ExtractHabitName(string content)
    {
        if (content.Contains("Meditation", StringComparison.OrdinalIgnoreCase))
        {
            return "Daily Morning Meditation";
        }

        if (content.Contains("Reading", StringComparison.OrdinalIgnoreCase))
        {
            return "Evening Reading";
        }

        return "Habit";
    }
}
