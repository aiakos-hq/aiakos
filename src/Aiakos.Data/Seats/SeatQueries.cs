using Dapper;

using Npgsql;

namespace Aiakos.Data.Seats;

/// <summary>Reads seat status, launch, transition, finding, and command data for a tenant.</summary>
public sealed class SeatQueries(NpgsqlDataSource dataSource)
{
    private const string StatusSql = """
        SELECT seat.seat_id,
               seat.address,
               rig.name AS rig,
               seat.member,
               seat.kind,
               seat.harness,
               seat.node_name AS node,
               seat.desired,
               seat_state.session,
               seat_state.session_reason,
               seat_state.session_since,
               seat_state.activity,
               seat_state.activity_detail,
               seat_state.activity_reason,
               seat_state.activity_since,
               seat_state.resumability,
               seat_state.resumability_reason,
               seat_state.resumability_since,
               seat_session.native_session_id,
               seat_launch.launch_id,
               seat_launch.outcome AS launch_outcome,
               seat_launch.decision AS launch_decision,
               COALESCE(
                   seat_state.session IN ('starting', 'present', 'unknown')
                   AND (seat_launch.spec_hash IS DISTINCT FROM seat.spec_hash
                        OR seat_launch.binding_hash IS DISTINCT FROM seat.binding_hash),
                   false) AS spec_drift,
               (SELECT seat_command.kind
                FROM aiakos.seat_command AS seat_command
                WHERE seat_command.tenant_id = seat.tenant_id
                  AND seat_command.seat_id = seat.seat_id
                  AND seat_command.status IN ('pending', 'sent')
                ORDER BY seat_command.created_at, seat_command.command_id
                LIMIT 1) AS pending_op,
               (SELECT seat_command.outcome
                FROM aiakos.seat_command AS seat_command
                WHERE seat_command.tenant_id = seat.tenant_id
                  AND seat_command.seat_id = seat.seat_id
                  AND seat_command.kind = 'deliver'
                ORDER BY seat_command.created_at DESC, seat_command.command_id DESC
                LIMIT 1) AS last_delivery_outcome,
               (seat_state.usage ->> 'context_used_percent')::int AS context_used_percent,
               seat_state.usage ->> 'model_id' AS model,
               seat_state.last_event_at,
               findings.open_findings,
               findings.worst_severity
        FROM aiakos.seat AS seat
        JOIN aiakos.rig AS rig
          ON rig.tenant_id = seat.tenant_id AND rig.rig_id = seat.rig_id
        LEFT JOIN aiakos.seat_state AS seat_state
          ON seat_state.tenant_id = seat.tenant_id AND seat_state.seat_id = seat.seat_id
        LEFT JOIN aiakos.seat_session AS seat_session
          ON seat_session.tenant_id = seat_state.tenant_id
         AND seat_session.session_id = seat_state.current_session_id
        LEFT JOIN aiakos.seat_launch AS seat_launch
          ON seat_launch.tenant_id = seat_state.tenant_id
         AND seat_launch.launch_id = seat_state.current_launch_id
        LEFT JOIN LATERAL (
            SELECT count(*)::bigint AS open_findings,
                   CASE
                       WHEN bool_or(seat_finding.severity = 'error') THEN 'error'
                       WHEN bool_or(seat_finding.severity = 'warning') THEN 'warning'
                       WHEN bool_or(seat_finding.severity = 'info') THEN 'info'
                       ELSE NULL
                   END AS worst_severity
            FROM aiakos.seat_finding AS seat_finding
            WHERE seat_finding.tenant_id = seat.tenant_id
              AND seat_finding.seat_id = seat.seat_id
              AND seat_finding.status = 'open'
        ) AS findings ON true
        WHERE seat.tenant_id = @tenantId
          AND seat.retired_at IS NULL
          AND (@rigName IS NULL OR rig.name = @rigName)
          AND (@address IS NULL OR seat.address = @address)
        ORDER BY rig.name, seat.member
        """;

    private const string LaunchSql = """
        SELECT launch_id,
               seat_id,
               mode,
               decision,
               decided_by,
               decision_note,
               node_name,
               spec_hash,
               binding_hash,
               outcome,
               outcome_reason,
               observed_session_id,
               exit_code,
               exit_signal,
               evidence,
               requested_at,
               outcome_at,
               ended_at,
               end_reason
        FROM aiakos.seat_launch
        WHERE tenant_id = @tenantId AND launch_id = @launchId
        """;

    private const string CommandSql = """
        SELECT command_id,
               seat_id,
               launch_id,
               kind,
               status,
               outcome,
               error_reason,
               forced,
               turn_id,
               result::text AS result,
               requested_by,
               created_at,
               sent_at,
               completed_at
        FROM aiakos.seat_command
        WHERE tenant_id = @tenantId AND command_id = @commandId
        """;

    private const string TransitionSql = """
        SELECT axis, reported, from_value, to_value, reason, cause_type, launch_id, at
        FROM aiakos.seat_transition
        WHERE tenant_id = @tenantId AND seat_id = @seatId
        ORDER BY at DESC, transition_id DESC
        LIMIT 20
        """;

    private const string FindingSql = """
        SELECT finding_id, kind, severity, summary, occurrences, first_seen_at, last_seen_at, launch_id
        FROM aiakos.seat_finding
        WHERE tenant_id = @tenantId AND seat_id = @seatId AND status = 'open'
        ORDER BY CASE severity WHEN 'error' THEN 0 WHEN 'warning' THEN 1 WHEN 'info' THEN 2 ELSE 3 END,
                 last_seen_at DESC,
                 finding_id DESC
        """;

    private const string DeliverySql = """
        SELECT command_id,
               seat_id,
               launch_id,
               kind,
               status,
               outcome,
               error_reason,
               forced,
               turn_id,
               result::text AS result,
               requested_by,
               created_at,
               sent_at,
               completed_at
        FROM aiakos.seat_command
        WHERE tenant_id = @tenantId AND seat_id = @seatId AND kind = 'deliver'
        ORDER BY created_at DESC, command_id DESC
        LIMIT 5
        """;

    /// <summary>Lists non-retired seats for a tenant, optionally limited to one rig.</summary>
    public async Task<IReadOnlyList<SeatStatusRow>> ListAsync(
        Guid tenantId,
        string? rigName,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<SeatStatusRow>(new CommandDefinition(
                StatusSql,
                new { tenantId, rigName, address = (string?)null },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows.AsList();
        }
    }

    /// <summary>Returns a non-retired seat and its current related records, or null when absent.</summary>
    public async Task<SeatDetail?> GetDetailAsync(Guid tenantId, string address, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var seat = await connection.QuerySingleOrDefaultAsync<SeatStatusRow>(new CommandDefinition(
                StatusSql,
                new { tenantId, rigName = (string?)null, address },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (seat is null)
            {
                return null;
            }

            var launch = seat.LaunchId is { } launchId
                ? await QueryLaunchAsync(connection, tenantId, launchId, cancellationToken).ConfigureAwait(false)
                : null;
            var parameters = new { tenantId, seat.SeatId };
            var transitions = (await connection.QueryAsync<SeatTransitionRow>(new CommandDefinition(
                TransitionSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
            var findings = (await connection.QueryAsync<SeatFindingRow>(new CommandDefinition(
                FindingSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
            var deliveries = (await connection.QueryAsync<SeatCommandRow>(new CommandDefinition(
                DeliverySql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

            return new SeatDetail(seat, launch, transitions, findings, deliveries);
        }
    }

    /// <summary>Returns a launch belonging to the tenant, or null when it does not exist there.</summary>
    public async Task<SeatLaunchRow?> GetLaunchAsync(Guid tenantId, Guid launchId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryLaunchAsync(connection, tenantId, launchId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns a command belonging to the tenant, or null when it does not exist there.</summary>
    public async Task<SeatCommandRow?> GetCommandAsync(Guid tenantId, Guid commandId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<SeatCommandRow>(new CommandDefinition(
                CommandSql,
                new { tenantId, commandId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static Task<SeatLaunchRow?> QueryLaunchAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid launchId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<SeatLaunchRow>(new CommandDefinition(
            LaunchSql,
            new { tenantId, launchId },
            cancellationToken: cancellationToken));
}
