using Dapper;

using Npgsql;

namespace Aiakos.Data;

/// <summary>Read model of a row in <c>aiakos.tenant</c> (ADR 0012).</summary>
/// <param name="TenantId">Tenant key.</param>
/// <param name="Slug">Unique slug used in addresses.</param>
/// <param name="Name">Display name.</param>
/// <param name="CreatedAt">Creation time (UTC).</param>
public sealed record Tenant(Guid TenantId, string Slug, string Name, DateTime CreatedAt);

/// <summary>Repository for <c>aiakos.tenant</c>.</summary>
public sealed class TenantRepository(NpgsqlDataSource dataSource)
{
    /// <summary>Returns the tenant, or <see langword="null"/> if it does not exist.</summary>
    public async Task<Tenant?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tenant_id, slug, name, created_at
            FROM aiakos.tenant
            WHERE tenant_id = @tenantId
            """;

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<Tenant>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
}
