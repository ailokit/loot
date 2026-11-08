using Loot.Data;

namespace Loot.Services;

public sealed class StartupRecoveryService
{
    private readonly MessageRepository _messages;

    public StartupRecoveryService(MessageRepository messages) => _messages = messages;

    public Task<int> RecoverAsync(CancellationToken cancellationToken = default) =>
        _messages.CancelIncompleteMessagesAsync(cancellationToken);
}
