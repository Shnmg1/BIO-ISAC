using System;
using System.Data.Common;
using MySqlConnector;

namespace api.Extensions;

public static class MySqlDataReaderExtensions
{
    public static int GetInt32ByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return 0;
        return reader.GetInt32(ordinal);
    }

    public static int? GetInt32NullableByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return null;
        return reader.GetInt32(ordinal);
    }

    public static string GetStringByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return string.Empty;
        return reader.GetString(ordinal);
    }

    public static string? GetStringNullableByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return null;
        return reader.GetString(ordinal);
    }

    public static DateTime GetDateTimeByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return DateTime.MinValue;
        return DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    public static DateTime? GetDateTimeNullableByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return null;
        return DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    public static decimal GetDecimalByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return 0;
        return reader.GetDecimal(ordinal);
    }

    public static decimal? GetDecimalNullableByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return null;
        return reader.GetDecimal(ordinal);
    }

    public static bool IsDBNullByName(this DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal);
    }
}

