using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Wraps an <see cref="AIFunction"/> (e.g. the MCP get_weather tool) so that, whenever the model
/// invokes it, the call's arguments and result are recorded in <see cref="RenderableToolCapture"/>.
/// The orchestrator later forwards those to the browser to render generative UI. Name, description and
/// schema delegate to the inner function, so the model sees an identical tool. Execution is unchanged —
/// the inner function still runs and its result is returned to the model as usual.
/// </summary>
public sealed class CapturingAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly RenderableToolCapture _capture;

    public CapturingAIFunction(AIFunction inner, RenderableToolCapture capture)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await _inner.InvokeAsync(arguments, cancellationToken);

        var resultText = ExtractResultText(result);

        var args = arguments is null
            ? new Dictionary<string, object?>()
            : arguments.ToDictionary(kv => kv.Key, kv => kv.Value);

        _capture.Add(new CapturedToolCall(Guid.NewGuid().ToString(), Name, args, resultText));

        return result;
    }

    // MCP tools return their payload wrapped in a content block (e.g. {"$type":"text","Text":"<json>"})
    // or a list of such blocks. Unwrap to the inner text so the browser receives the tool's raw JSON.
    private static string ExtractResultText(object? result)
    {
        if (result is null) return string.Empty;
        if (result is string s) return s;

        var element = result is JsonElement je ? je : JsonSerializer.SerializeToElement(result);
        return ExtractFromElement(element);
    }

    private static string ExtractFromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;

            case JsonValueKind.Object:
                // MCP text content block exposes the payload under "Text" (or "text").
                if (element.TryGetProperty("Text", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString() ?? string.Empty;
                if (element.TryGetProperty("text", out var t2) && t2.ValueKind == JsonValueKind.String)
                    return t2.GetString() ?? string.Empty;
                // Some shapes nest blocks under "content": [ ... ].
                if (element.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                    return ExtractFromArray(c);
                return element.GetRawText();

            case JsonValueKind.Array:
                return ExtractFromArray(element);

            default:
                return element.GetRawText();
        }
    }

    private static string ExtractFromArray(JsonElement array)
    {
        var sb = new StringBuilder();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("Text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
            else if (item.TryGetProperty("text", out var t2) && t2.ValueKind == JsonValueKind.String)
                sb.Append(t2.GetString());
        }
        return sb.Length > 0 ? sb.ToString() : array.GetRawText();
    }
}
