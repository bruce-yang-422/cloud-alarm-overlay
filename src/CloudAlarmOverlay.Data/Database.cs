using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
namespace CloudAlarmOverlay.Data;

public sealed class Database(ISqliteConnectionFactory factory)
{
    static Database() => SqlMapper.AddTypeHandler(new DateOnlyHandler());
    public Task<SqliteConnection> OpenAsync(CancellationToken ct = default) => factory.OpenConnectionAsync(ct);
    public static DynamicParameters Parameters(object? value)
    {
        var result = new DynamicParameters();
        if (value is null) return result;
        foreach (var property in value.GetType().GetProperties())
        {
            var item = property.GetValue(value);
            result.Add(property.Name, item switch
            {
                DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => item
            });
        }
        return result;
    }
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        return (await c.QueryAsync<T>(new CommandDefinition(sql, Parameters(args), cancellationToken: ct)).ConfigureAwait(false)).AsList();
    }
    public async Task<int> ExecuteAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        return await c.ExecuteAsync(new CommandDefinition(sql, Parameters(args), cancellationToken: ct)).ConfigureAwait(false);
    }
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly value) => parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
