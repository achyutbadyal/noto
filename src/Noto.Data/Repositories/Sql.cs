using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Noto.Data.Repositories;

// Column <-> CLR conversions shared by the repositories. Dates and instants are ISO-8601 text.
static class Sql
{
    public static object Val(DateOnly? d) =>
        d is null ? DBNull.Value : d.Value.ToString("yyyy-MM-dd");

    public static object Val(DateTimeOffset? t) => t is null ? DBNull.Value : t.Value.ToString("O");

    public static object Val(string? s) => s is null ? DBNull.Value : s;

    public static object Val(Guid? g) => g is null ? DBNull.Value : g.Value.ToString();

    public static object Val(int? n) => n is null ? DBNull.Value : n.Value;

    public static object Val(Enum? e) => e is null ? DBNull.Value : e.ToString();

    public static SqliteCommand Cmd(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string sql,
        params (string Name, object Value)[] args
    )
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    public static string? Str(this SqliteDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));

    public static int? Int(this SqliteDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetInt32(r.GetOrdinal(col));

    public static bool Bool(this SqliteDataReader r, string col) =>
        r.GetInt32(r.GetOrdinal(col)) != 0;

    public static Guid? Guid(this SqliteDataReader r, string col) =>
        r.Str(col) is { } s ? System.Guid.Parse(s) : null;

    public static DateOnly? Date(this SqliteDataReader r, string col) =>
        r.Str(col) is { } s
            ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    public static DateTimeOffset? Instant(this SqliteDataReader r, string col) =>
        r.Str(col) is { } s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture) : null;

    public static T? Enum<T>(this SqliteDataReader r, string col)
        where T : struct, Enum => r.Str(col) is { } s ? System.Enum.Parse<T>(s) : null;
}
