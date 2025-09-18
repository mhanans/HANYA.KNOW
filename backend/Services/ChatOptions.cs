using System;

namespace backend.Services;

public class ChatOptions
{
    /// <summary>
    /// Minimum number of seconds a client must wait between chat requests.
    /// </summary>
    public int CooldownSeconds { get; set; } = 0;

    /// <summary>
    /// Minimum similarity score required for search results to be included in the LLM context.
    /// </summary>
    public double ScoreThreshold { get; set; } = 0.7;

    /// <summary>
    /// Prompt template used to construct the LLM request.
    /// </summary>
    public string PromptTemplate { get; set; } = string.Empty;
}
