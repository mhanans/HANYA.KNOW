using System;
using System.Net;

namespace backend.Services;

public class LlmApiException : InvalidOperationException
{
    public LlmApiException(string provider, string apiPath, HttpStatusCode statusCode, string userMessage, string? responseContent)
        : base($"{provider} API request to '{apiPath}' failed with status {(int)statusCode}: {userMessage}")
    {
        Provider = provider;
        ApiPath = apiPath;
        StatusCode = statusCode;
        UserMessage = string.IsNullOrWhiteSpace(userMessage)
            ? $"{provider} API request failed with status {(int)statusCode}."
            : userMessage;
        ResponseContent = responseContent;
    }

    public string Provider { get; }

    public string ApiPath { get; }

    public HttpStatusCode StatusCode { get; }

    public string UserMessage { get; }

    public string? ResponseContent { get; }
}
