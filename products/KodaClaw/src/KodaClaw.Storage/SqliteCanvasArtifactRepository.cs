using System.Globalization;
using System.Text;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.Storage;

public sealed class SqliteCanvasArtifactRepository : ICanvasArtifactRepository
{
    private readonly SqliteStorageDatabase _database;

    public SqliteCanvasArtifactRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteStorageDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task UpsertAsync(CanvasArtifact artifact, CancellationToken cancellationToken = default)
    {
        CanvasArtifactValidation.ValidateArtifact(artifact);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO canvas_artifacts (
                id,
                title,
                kind,
                summary,
                source,
                route,
                entry_path,
                asset_directory,
                session_id,
                correlation_id,
                created_at,
                updated_at,
                metadata_json
            )
            VALUES (
                $id,
                $title,
                $kind,
                $summary,
                $source,
                $route,
                $entryPath,
                $assetDirectory,
                $sessionId,
                $correlationId,
                $createdAt,
                $updatedAt,
                $metadataJson
            )
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                kind = excluded.kind,
                summary = excluded.summary,
                source = excluded.source,
                route = excluded.route,
                entry_path = excluded.entry_path,
                asset_directory = excluded.asset_directory,
                session_id = excluded.session_id,
                correlation_id = excluded.correlation_id,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                metadata_json = excluded.metadata_json;
            """;

        BindArtifact(command, artifact);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CanvasArtifact?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        CanvasArtifactValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                title,
                kind,
                summary,
                source,
                route,
                entry_path,
                asset_directory,
                session_id,
                correlation_id,
                created_at,
                updated_at,
                metadata_json
            FROM canvas_artifacts
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapArtifact(reader);
    }

    public async Task<IReadOnlyList<CanvasArtifact>> ListAsync(
        CanvasArtifactQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new CanvasArtifactQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                title,
                kind,
                summary,
                source,
                route,
                entry_path,
                asset_directory,
                session_id,
                correlation_id,
                created_at,
                updated_at,
                metadata_json
            FROM canvas_artifacts
            """);

        var filters = new List<string>();
        if (query.Kind is { } kind)
        {
            filters.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", kind.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            filters.Add("source = $source");
            command.Parameters.AddWithValue("$source", query.Source.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.SessionId))
        {
            filters.Add("session_id = $sessionId");
            command.Parameters.AddWithValue("$sessionId", query.SessionId.Trim());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, created_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", CanvasArtifactValidation.NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var artifacts = new List<CanvasArtifact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            artifacts.Add(MapArtifact(reader));
        }

        return artifacts;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        CanvasArtifactValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM canvas_artifacts
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static void BindArtifact(SqliteCommand command, CanvasArtifact artifact)
    {
        command.Parameters.AddWithValue("$id", artifact.Id);
        command.Parameters.AddWithValue("$title", artifact.Title);
        command.Parameters.AddWithValue("$kind", artifact.Kind.ToString());
        command.Parameters.AddWithValue("$summary", artifact.Summary);
        command.Parameters.AddWithValue("$source", artifact.Source);
        command.Parameters.AddWithValue("$route", (object?)NormalizeNullableText(artifact.Route) ?? DBNull.Value);
        command.Parameters.AddWithValue("$entryPath", artifact.EntryPath);
        command.Parameters.AddWithValue("$assetDirectory", artifact.AssetDirectory);
        command.Parameters.AddWithValue("$sessionId", (object?)NormalizeNullableText(artifact.SessionId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$correlationId", (object?)NormalizeNullableText(artifact.CorrelationId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(artifact.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(artifact.UpdatedAt));
        command.Parameters.AddWithValue("$metadataJson", (object?)NormalizeNullableText(artifact.MetadataJson) ?? DBNull.Value);
    }

    private static CanvasArtifact MapArtifact(SqliteDataReader reader)
    {
        return new CanvasArtifact(
            Id: reader.GetString(0),
            Title: reader.GetString(1),
            Kind: ParseEnum<CanvasArtifactKind>(reader.GetString(2)),
            Summary: reader.GetString(3),
            Source: reader.GetString(4),
            EntryPath: reader.GetString(6),
            AssetDirectory: reader.GetString(7),
            CreatedAt: ParseTimestamp(reader.GetString(10)),
            UpdatedAt: ParseTimestamp(reader.GetString(11)),
            Route: reader.IsDBNull(5) ? null : reader.GetString(5),
            SessionId: reader.IsDBNull(8) ? null : reader.GetString(8),
            CorrelationId: reader.IsDBNull(9) ? null : reader.GetString(9),
            MetadataJson: reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static string? NormalizeNullableText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string rawValue)
    {
        return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static TEnum ParseEnum<TEnum>(string rawValue)
        where TEnum : struct, Enum
    {
        return Enum.Parse<TEnum>(rawValue, ignoreCase: false);
    }
}
