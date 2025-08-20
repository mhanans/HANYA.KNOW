using System.Collections.Concurrent;

namespace backend.Services;

public class ConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public string CreateConversation()
    {
        var id = Guid.NewGuid().ToString("N");
        _conversations.TryAdd(id, new List<ChatMessage>());
        return id;
    }

    public List<ChatMessage> GetOrCreate(string id)
    {
        return _conversations.GetOrAdd(id, _ => new List<ChatMessage>());
    }

    public bool Exists(string id) => _conversations.ContainsKey(id);
}
