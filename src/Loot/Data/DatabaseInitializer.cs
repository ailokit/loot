using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Loot.Data;

/// <summary>Creates the current SQLite schema for a new Loot data directory.</summary>
public sealed class DatabaseInitializer(SqliteDatabase database)
{
    private const string SchemaResourceName = "Loot.Data.CurrentSchema.sql";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var schemaStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException($"Missing database schema resource '{SchemaResourceName}'.");
        using var reader = new StreamReader(schemaStream);
        var schema = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
