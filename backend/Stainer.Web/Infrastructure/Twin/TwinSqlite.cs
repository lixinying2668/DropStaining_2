using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Stainer.Web.Infrastructure.Twin;

// 对应 stainer_twin_fastapi/stainer_twin_api/database.py。
// 关键语义：one/many 在任何 SQLite 错误时返回 null/空（这是数字孪生 null-policy 的根基——
// 数据库表/行/字段缺失时统一回退为 null，绝不抛异常中断快照生成）。
internal static class TwinSqlite
{
    public static bool TableExists(SqliteConnection connection, string table)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t";
            command.Parameters.AddWithValue("@t", table);
            return command.ExecuteScalar() is not null;
        }
        catch
        {
            return false;
        }
    }

    public static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        var columns = new HashSet<string>(System.StringComparer.Ordinal);
        if (!TableExists(connection, table))
        {
            return columns;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
        }
        catch
        {
            // 与 Python 一致：忽略错误，返回当前已收集到的列集合。
        }

        return columns;
    }

    public static bool HasColumns(SqliteConnection connection, string table, params string[] required)
    {
        var columns = Columns(connection, table);
        return columns.Count > 0 && System.Linq.Enumerable.All(required, c => columns.Contains(c));
    }

    public static Dictionary<string, object?>? One(SqliteConnection connection, string sql, params object?[] args)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, args);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadRow(reader) : null;
        }
        catch
        {
            return null;
        }
    }

    public static List<Dictionary<string, object?>> Many(SqliteConnection connection, string sql, params object?[] args)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, args);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(ReadRow(reader));
            }
        }
        catch
        {
            // 与 Python many() 一致：出错返回空列表。
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, params object?[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue("@p" + i, args[i] ?? DBNull.Value);
        }
    }

    private static Dictionary<string, object?> ReadRow(SqliteDataReader reader)
    {
        var row = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return row;
    }
}
