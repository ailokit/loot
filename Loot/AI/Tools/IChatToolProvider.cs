namespace Loot.AI.Tools;

public interface IChatToolProvider
{
    Task<IEnumerable<ChatToolRegistration>> GetTools();
}

