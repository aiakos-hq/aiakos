using Aiakos.Core;

using Npgsql;

namespace Aiakos.Data.Tests.Infrastructure;

/// <summary>Creates the seat query scenario used by the data tests.</summary>
internal static class SeatSeed
{
    public const string DeliveryBody = "SENTINEL-BODY";
    public const string TokenHash = "TOKEN-HASH-SENTINEL";

    public sealed record Scenario(
        Guid OtherTenantId,
        Guid ImplSeatId,
        Guid ReviewSeatId,
        Guid DriftSeatId,
        Guid StaleSeatId,
        Guid HumanSeatId,
        Guid RetiredSeatId,
        Guid OtherRigSeatId,
        Guid TenantSeatId,
        Guid ImplLaunchId,
        Guid ImplStartCommandId,
        Guid ImplOlderDeliveryId,
        Guid ImplNewerDeliveryId);

    public static async Task<Scenario> InsertAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var result = new Scenario(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7());

        var demoRigId = Guid.CreateVersion7();
        var otherRigId = Guid.CreateVersion7();
        var tenantDemoRigId = Guid.CreateVersion7();
        var implSessionId = Guid.CreateVersion7();
        var reviewSessionId = Guid.CreateVersion7();
        var driftSessionId = Guid.CreateVersion7();
        var staleSessionId = Guid.CreateVersion7();
        var driftLaunchId = Guid.CreateVersion7();
        var staleLaunchId = Guid.CreateVersion7();
        var implNodeId = Guid.CreateVersion7();

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.tenant (tenant_id, slug, name) VALUES (@tenant, 'seat-query-other', 'Seat query other');
            INSERT INTO aiakos.rig (tenant_id, rig_id, name, spec_hash, binding_hash, tool_version, resolved)
            VALUES (@defaultTenant, @demoRig, 'demo', 'demo-spec', 'demo-binding', 'test', '{}'::jsonb),
                   (@defaultTenant, @otherRig, 'other', 'other-spec', 'other-binding', 'test', '{}'::jsonb),
                   (@otherTenant, @tenantDemoRig, 'demo', 'tenant-spec', 'tenant-binding', 'test', '{}'::jsonb);
            """, cancellationToken,
            ("tenant", result.OtherTenantId), ("defaultTenant", TenantIds.Default), ("demoRig", demoRigId),
            ("otherRig", otherRigId), ("otherTenant", result.OtherTenantId), ("tenantDemoRig", tenantDemoRigId));

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat (tenant_id, seat_id, rig_id, member, address, kind, harness, node_name, desired, spec_hash, binding_hash)
            VALUES (@tenant, @impl, @demoRig, 'impl', 'impl@demo', 'agent', 'claude', 'node-a', 'up', 'demo-spec', 'demo-binding'),
                   (@tenant, @review, @demoRig, 'review', 'review@demo', 'agent', 'claude', 'node-a', 'down', 'demo-spec', 'demo-binding'),
                   (@tenant, @drift, @demoRig, 'drift', 'drift@demo', 'agent', 'claude', 'node-a', 'up', 'demo-spec', 'demo-binding'),
                   (@tenant, @stale, @demoRig, 'stale', 'stale@demo', 'agent', 'claude', 'node-a', 'down', 'demo-spec', 'demo-binding'),
                   (@tenant, @human, @demoRig, 'lead', 'lead@demo', 'human', NULL, NULL, 'down', 'demo-spec', 'demo-binding'),
                   (@tenant, @otherRigSeat, @otherRig, 'impl', 'impl@other', 'agent', 'claude', 'node-b', 'up', 'other-spec', 'other-binding'),
                   (@otherTenant, @tenantSeat, @tenantDemoRig, 'impl', 'impl@demo', 'agent', 'claude', 'node-u', 'down', 'tenant-spec', 'tenant-binding');
            """, cancellationToken,
            ("tenant", TenantIds.Default), ("otherTenant", result.OtherTenantId), ("demoRig", demoRigId),
            ("otherRig", otherRigId), ("tenantDemoRig", tenantDemoRigId), ("impl", result.ImplSeatId),
            ("review", result.ReviewSeatId), ("drift", result.DriftSeatId), ("stale", result.StaleSeatId),
            ("human", result.HumanSeatId), ("retired", result.RetiredSeatId), ("otherRigSeat", result.OtherRigSeatId),
            ("tenantSeat", result.TenantSeatId));

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat (tenant_id, seat_id, rig_id, member, address, kind, harness, node_name,
                desired, spec_hash, binding_hash, retired_at)
            VALUES (@tenant, @seat, @rig, 'gone', 'gone@demo', 'agent', 'claude', 'node-a', 'down',
                    'demo-spec', 'demo-binding', now());
            """, cancellationToken, ("tenant", TenantIds.Default), ("seat", result.RetiredSeatId), ("rig", demoRigId));

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat_session (tenant_id, session_id, seat_id, harness, native_session_id, decision)
            VALUES (@tenant, @implSession, @impl, 'claude', 'native-impl', 'new-session'),
                   (@tenant, @reviewSession, @review, 'claude', 'native-review', 'new-session'),
                   (@tenant, @driftSession, @drift, 'claude', 'native-drift', 'new-session'),
                   (@tenant, @staleSession, @stale, 'claude', 'native-stale', 'new-session');
            INSERT INTO aiakos.seat_launch (tenant_id, launch_id, seat_id, session_id, mode, decision, decided_by,
                command_id, node_name, spec_hash, binding_hash, seat_token_hash, outcome, observed_session_id,
                evidence, requested_at, outcome_at)
            VALUES (@tenant, @implLaunch, @impl, @implSession, 'resume', 'resume', 'operator', @startCommand,
                    'node-a', 'demo-spec', 'demo-binding', decode('544f4b454e2d484153482d53454e54494e454c', 'hex'),
                    'ready', 'native-impl', NULL, '2026-01-01T00:00:00Z', '2026-01-01T00:00:02Z'),
                   (@tenant, @driftLaunch, @drift, @driftSession, 'fresh', 'new-session', 'operator', @driftCommand,
                    'node-a', 'old-spec', 'demo-binding', decode('00', 'hex'), 'ready', 'native-drift', NULL,
                    '2026-01-01T00:00:00Z', '2026-01-01T00:00:02Z'),
                   (@tenant, @staleLaunch, @stale, @staleSession, 'fresh', 'new-session', 'operator', @staleCommand,
                    'node-a', 'old-spec', 'demo-binding', decode('00', 'hex'), 'failed', NULL, NULL,
                    '2026-01-01T00:00:00Z', '2026-01-01T00:00:02Z');
            """, cancellationToken,
            ("tenant", TenantIds.Default), ("implSession", implSessionId), ("impl", result.ImplSeatId),
            ("reviewSession", reviewSessionId), ("review", result.ReviewSeatId), ("driftSession", driftSessionId),
            ("drift", result.DriftSeatId), ("staleSession", staleSessionId), ("stale", result.StaleSeatId),
            ("implLaunch", result.ImplLaunchId), ("startCommand", result.ImplStartCommandId),
            ("driftLaunch", driftLaunchId), ("driftCommand", Guid.CreateVersion7()),
            ("staleLaunch", staleLaunchId), ("staleCommand", Guid.CreateVersion7()));

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat_command (tenant_id, command_id, seat_id, launch_id, kind, status, outcome,
                payload, result, requested_by, created_at, sent_at, completed_at)
            VALUES (@tenant, @startCommand, @impl, @launch, 'start', 'completed', NULL, '{}'::jsonb,
                    '{"started":true}'::jsonb, 'operator', '2026-01-01T00:00:00Z', '2026-01-01T00:00:01Z', '2026-01-01T00:00:02Z'),
                   (@tenant, @oldDeliver, @impl, @launch, 'deliver', 'completed', 'confirmed',
                    jsonb_build_object('body', @body), '{"confirmed":true}'::jsonb, 'operator',
                    '2026-01-01T00:00:03Z', '2026-01-01T00:00:04Z', '2026-01-01T00:00:05Z'),
                   (@tenant, @newDeliver, @impl, @launch, 'deliver', 'sent', NULL,
                    jsonb_build_object('body', @body), NULL, 'operator',
                    '2026-01-01T00:00:06Z', '2026-01-01T00:00:07Z', NULL);
            """, cancellationToken,
            ("tenant", TenantIds.Default), ("startCommand", result.ImplStartCommandId),
            ("impl", result.ImplSeatId), ("launch", result.ImplLaunchId),
            ("oldDeliver", result.ImplOlderDeliveryId), ("newDeliver", result.ImplNewerDeliveryId),
            ("body", DeliveryBody));

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat_state (tenant_id, seat_id, version, session, session_since, activity,
                activity_detail, activity_since, resumability, resumability_since, known_session, known_activity,
                current_launch_id, current_session_id, node_instance_id, usage)
            VALUES (@tenant, @impl, 3, 'present', '2026-01-01T00:00:00Z', 'working', 'tool:Bash',
                    '2026-01-01T00:00:01Z', 'resumable', '2026-01-01T00:00:02Z', 'present', 'working',
                    @implLaunch, @implSession, @node, '{"context_used_percent":42,"model_id":"opus"}'::jsonb),
                   (@tenant, @review, 0, 'absent', '2026-01-01T00:00:00Z', 'none', NULL,
                    '2026-01-01T00:00:00Z', 'none', '2026-01-01T00:00:00Z', 'absent', 'none', NULL, NULL, NULL, NULL),
                   (@tenant, @drift, 1, 'present', '2026-01-01T00:00:00Z', 'idle', NULL,
                    '2026-01-01T00:00:00Z', 'fresh-only', '2026-01-01T00:00:00Z', 'present', 'idle',
                    @driftLaunch, @driftSession, @node, NULL),
                   (@tenant, @stale, 2, 'absent', '2026-01-01T00:00:00Z', 'none', NULL,
                    '2026-01-01T00:00:00Z', 'resumable', '2026-01-01T00:00:00Z', 'absent', 'none',
                    @staleLaunch, @staleSession, @node, NULL);
            """, cancellationToken,
            ("tenant", TenantIds.Default), ("impl", result.ImplSeatId), ("review", result.ReviewSeatId),
            ("drift", result.DriftSeatId), ("stale", result.StaleSeatId), ("implLaunch", result.ImplLaunchId),
            ("driftLaunch", driftLaunchId), ("staleLaunch", staleLaunchId), ("implSession", implSessionId),
            ("driftSession", driftSessionId), ("staleSession", staleSessionId), ("node", implNodeId));

        for (var index = 0; index < 25; index++)
        {
            await ExecuteAsync(dataSource, """
                INSERT INTO aiakos.seat_transition (tenant_id, transition_id, seat_id, axis, reported, from_value,
                    to_value, cause_type, at)
                VALUES (@tenant, @transitionId, @seat, 'activity', true, 'idle', 'working', 'event',
                        '2026-01-02T00:00:00Z'::timestamptz + @index * interval '1 second');
                """, cancellationToken, ("tenant", TenantIds.Default), ("transitionId", Guid.CreateVersion7()),
                ("seat", result.ImplSeatId), ("index", index));
        }

        await ExecuteAsync(dataSource, """
            INSERT INTO aiakos.seat_finding (tenant_id, finding_id, seat_id, kind, severity, status, summary,
                occurrences, first_seen_at, last_seen_at)
            VALUES (@tenant, @errorFinding, @seat, 'turn-failed', 'error', 'open', 'Turn failed', 2,
                    '2026-01-01T00:00:00Z', '2026-01-01T00:00:02Z'),
                   (@tenant, @warningFinding, @seat, 'activity-stale', 'warning', 'open', 'Activity stale', 1,
                    '2026-01-01T00:00:01Z', '2026-01-01T00:00:03Z'),
                   (@tenant, @resolvedFinding, @seat, 'observation-gap', 'warning', 'resolved', 'Gap resolved', 1,
                    '2026-01-01T00:00:00Z', '2026-01-01T00:00:01Z');
            """, cancellationToken,
            ("tenant", TenantIds.Default), ("seat", result.ImplSeatId),
            ("errorFinding", Guid.CreateVersion7()), ("warningFinding", Guid.CreateVersion7()),
            ("resolvedFinding", Guid.CreateVersion7()));

        return result;
    }

    private static async Task ExecuteAsync(
        NpgsqlDataSource dataSource,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
