namespace Aiakos.Core;

/// <summary>Well-known tenant keys (ADR 0012).</summary>
public static class TenantIds
{
    /// <summary>
    /// The single default tenant created by migration <c>0001_tenant.sql</c>. Until multitenancy
    /// is activated (M8), every write uses it, taken from the caller's context rather than
    /// hard-coded in repositories.
    /// </summary>
    public static readonly Guid Default = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Slug of the default tenant.</summary>
    public const string DefaultSlug = "default";
}
