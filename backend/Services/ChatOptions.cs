using System;

namespace backend.Services;

public class ChatOptions
{
    /// <summary>
    /// Minimum number of seconds a client must wait between chat requests.
    /// </summary>
    public int CooldownSeconds { get; set; } = 0;
}
