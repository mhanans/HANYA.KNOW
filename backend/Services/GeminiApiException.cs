using System;
using System.Net;

namespace backend.Services;

public class GeminiApiException : InvalidOperationException
{
    public GeminiApiException(string apiPath, HttpStatusCode statusCode, string userMessage, string? responseContent)
        : base($"Gemini API request to '{apiPath}' failed with status {(int)statusCode}: {userMessage}")
    {
        ApiPath = apiPath;
        StatusCode = statusCode;
        UserMessage = string.IsNullOrWhiteSpace(userMessage)
            ? $"Gemini API request failed with status {(int)statusCode}."
            : userMessage;
        ResponseContent = responseContent;
    }

    public string ApiPath { get; }

    public HttpStatusCode StatusCode { get; }

    public string UserMessage { get; }

    public string? ResponseContent { get; }
}
