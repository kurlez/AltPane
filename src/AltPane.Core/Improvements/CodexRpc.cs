using System.Text.Json;
using System.Text.Json.Nodes;

namespace AltPane.Core.Improvements;

public enum RpcKind { Invalid, Request, Notification, Response, Error }

/// <summary>
/// One line of the Codex app-server protocol. It is JSON-RPC without the
/// "jsonrpc" member: a message with an id and a method is a request, one with
/// only a method is a notification, and one with an id and no method answers a request.
/// <see cref="IdRaw"/> keeps the id as JSON text so a number or a string is echoed back unchanged.
/// </summary>
public sealed record RpcMessage(
    RpcKind Kind,
    string? IdRaw,
    string? Method,
    JsonElement? Params,
    JsonElement? Result,
    string? Error);

public static class CodexRpc
{
    public static RpcMessage Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid;
            string? id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null
                ? idElement.GetRawText()
                : null;
            string? method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
                ? methodElement.GetString()
                : null;
            JsonElement? parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : null;
            JsonElement? result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : null;
            string? error = null;
            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
            {
                error = errorElement.ValueKind == JsonValueKind.Object
                    && errorElement.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString() ?? "error"
                    : errorElement.GetRawText();
            }

            if (method is not null)
                return new RpcMessage(id is null ? RpcKind.Notification : RpcKind.Request, id, method, parameters, null, null);
            if (id is null)
                return Invalid;
            if (error is not null)
                return new RpcMessage(RpcKind.Error, id, null, null, null, error);
            return new RpcMessage(RpcKind.Response, id, null, null, result, null);
        }
        catch (JsonException)
        {
            return Invalid;
        }
    }

    static RpcMessage Invalid { get; } = new(RpcKind.Invalid, null, null, null, null, null);

    public static string Request(long id, string method, JsonNode? parameters)
    {
        var message = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        return message.ToJsonString();
    }

    public static string Notification(string method, JsonNode? parameters = null)
    {
        var message = new JsonObject { ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        return message.ToJsonString();
    }

    public static string Response(string idRaw, JsonNode result) =>
        new JsonObject { ["id"] = JsonNode.Parse(idRaw), ["result"] = result }.ToJsonString();

    public static string ErrorResponse(string idRaw, int code, string message) =>
        new JsonObject
        {
            ["id"] = JsonNode.Parse(idRaw),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();

    /// <summary>Reads a nested string such as <c>turn.status</c>. Returns null when any step is missing.</summary>
    public static string? GetString(JsonElement? element, params string[] path)
    {
        if (element is null) return null;
        var current = element.Value;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
