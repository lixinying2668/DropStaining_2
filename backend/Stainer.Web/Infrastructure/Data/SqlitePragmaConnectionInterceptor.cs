using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Stainer.Web.Infrastructure.Data;

public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        TryEnableWal(connection);
        ExecuteNonQuery(connection, $"PRAGMA busy_timeout = {DatabaseInitializer.MinimumBusyTimeoutMilliseconds};");
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await TryEnableWalAsync(connection, cancellationToken);
        await ExecuteNonQueryAsync(connection, $"PRAGMA busy_timeout = {DatabaseInitializer.MinimumBusyTimeoutMilliseconds};", cancellationToken);
    }

    private static void ExecuteNonQuery(DbConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void ExecuteScalar(DbConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        _ = command.ExecuteScalar();
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteScalarAsync(DbConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void TryEnableWal(DbConnection connection)
    {
        try
        {
            ExecuteScalar(connection, "PRAGMA journal_mode = WAL;");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 10)
        {
            // EF design-time migrations can open a just-created SQLite file before WAL sidecar files are available.
            // Runtime database health still checks the final journal mode explicitly.
        }
    }

    private static async Task TryEnableWalAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteScalarAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 10)
        {
            // EF design-time migrations can open a just-created SQLite file before WAL sidecar files are available.
            // Runtime database health still checks the final journal mode explicitly.
        }
    }
}
