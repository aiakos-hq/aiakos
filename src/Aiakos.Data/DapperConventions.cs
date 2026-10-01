using System.Runtime.CompilerServices;

using Dapper;

namespace Aiakos.Data;

/// <summary>Process-wide Dapper settings (spec 0001, Design → Database → Dapper conventions).</summary>
internal static class DapperConventions
{
    // A module initializer runs before any code of this assembly executes, so every repository
    // sees the conventions without an explicit setup call (required by spec 0001).
#pragma warning disable CA2255 // ModuleInitializer in a library: intended; the setting is global and idempotent.
    [ModuleInitializer]
    internal static void Initialize()
    {
        // snake_case columns (tenant_id) map to PascalCase members (TenantId).
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
#pragma warning restore CA2255
}
