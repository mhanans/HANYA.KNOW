using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace backend.Services;

public class ConversationStore
{
    private class Conversation
    {
        public List<ChatMessage> Messages { get; } = new();
        public DateTime Created { get; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    public string CreateConversation()
    {
        var id = Guid.NewGuid().ToString("N");
        _conversations.TryAdd(id, new Conversation());
        return id;
    }

    public List<ChatMessage> GetOrCreate(string id)
    {
        return _conversations.GetOrAdd(id, _ => new Conversation()).Messages;
    }

    public bool Exists(string id) => _conversations.ContainsKey(id);

    public IEnumerable<(string Id, DateTime Created, string? FirstMessage)> GetHistory()
    {
        foreach (var kv in _conversations)
        {
            var first = kv.Value.Messages.FirstOrDefault()?.Content;
            yield return (kv.Key, kv.Value.Created, first);
        }
    }

    public List<ChatMessage>? GetConversation(string id)
    {
        return _conversations.TryGetValue(id, out var conv) ? conv.Messages : null;
    }
}
