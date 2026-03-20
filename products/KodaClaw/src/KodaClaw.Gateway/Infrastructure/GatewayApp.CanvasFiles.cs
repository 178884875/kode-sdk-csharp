using KodaClaw.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;

public static partial class GatewayApp
{
    private static async Task<IResult> ServeCanvasFileAsync(
        HttpContext context,
        IConfiguration configuration,
        IWorkspaceService workspaceService,
        IDiagnosticsService diagnosticsService,
        string? path,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorize(context, configuration))
        {
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.auth",
                eventType: "gateway.auth.failed",
                level: "warning",
                message: "Unauthorized access to canvas filesystem endpoint.");
            return Results.Unauthorized();
        }

        await workspaceService.EnsureInitializedAsync(cancellationToken);

        if (!TryNormalizeCanvasPath(path, out var normalizedPath, out var validationError))
        {
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: "gateway.canvas.invalid_request",
                level: "warning",
                message: validationError!.Message,
                attributes: new Dictionary<string, string?>
                {
                    ["requestedPath"] = path,
                });
            return Results.BadRequest(validationError);
        }

        if (!TryResolveCanvasFilePath(workspaceService.RootPath, normalizedPath, out var resolvedPath, out validationError))
        {
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: "gateway.canvas.invalid_request",
                level: "warning",
                message: validationError!.Message,
                attributes: new Dictionary<string, string?>
                {
                    ["requestedPath"] = normalizedPath,
                });
            return Results.BadRequest(validationError);
        }

        var fallbackPath = Path.Combine(
            workspaceService.RootPath,
            KodaClawWorkspaceLayout.WorkspaceDirectory,
            "canvas",
            "index.html");

        var servedPath = File.Exists(resolvedPath)
            ? resolvedPath
            : fallbackPath;

        if (!File.Exists(servedPath))
        {
            return Results.NotFound(new ErrorResponse(
                Code: "canvas.file_not_found",
                Message: "Canvas file was not found."));
        }

        RecordDiagnosticEvent(
            diagnosticsService,
            context,
            source: "gateway.canvas",
            eventType: "gateway.canvas.fs_served",
            level: "info",
            message: "Served canvas filesystem asset.",
            attributes: new Dictionary<string, string?>
            {
                ["requestedPath"] = normalizedPath,
                ["servedPath"] = servedPath == resolvedPath ? normalizedPath : DefaultCanvasEntryPath,
                ["fallback"] = (servedPath != resolvedPath).ToString(),
            });

        return Results.File(servedPath, ResolveCanvasContentType(servedPath));
    }

    private static bool TryNormalizeCanvasPath(
        string? rawPath,
        out string normalizedPath,
        out ErrorResponse? error)
    {
        normalizedPath = string.IsNullOrWhiteSpace(rawPath)
            ? DefaultCanvasEntryPath
            : rawPath.Trim().Replace('\\', '/');
        error = null;

        if (normalizedPath.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalizedPath))
        {
            error = new ErrorResponse(
                Code: "validation.canvas_path_invalid",
                Message: "Canvas path must be workspace-relative under workspace/canvas.");
            return false;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.None);
        if (segments.Length < 2)
        {
            error = new ErrorResponse(
                Code: "validation.canvas_path_invalid",
                Message: "Canvas path must be under workspace/canvas.");
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                error = new ErrorResponse(
                    Code: "validation.canvas_path_invalid",
                    Message: "Canvas path contains invalid path segments.");
                return false;
            }
        }

        var hasWorkspacePrefix =
            string.Equals(segments[0], KodaClawWorkspaceLayout.WorkspaceDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "canvas", StringComparison.OrdinalIgnoreCase);
        if (!hasWorkspacePrefix)
        {
            error = new ErrorResponse(
                Code: "validation.canvas_path_invalid",
                Message: "Canvas path must be under workspace/canvas.");
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    private static bool TryResolveCanvasFilePath(
        string workspaceRoot,
        string normalizedPath,
        out string resolvedPath,
        out ErrorResponse? error)
    {
        error = null;
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        resolvedPath = Path.GetFullPath(Path.Combine(workspaceRoot, Path.Combine(segments)));

        var canvasRoot = Path.GetFullPath(Path.Combine(
            workspaceRoot,
            KodaClawWorkspaceLayout.WorkspaceDirectory,
            "canvas"));
        if (!IsPathUnderRoot(resolvedPath, canvasRoot))
        {
            error = new ErrorResponse(
                Code: "validation.canvas_path_invalid",
                Message: "Canvas path must stay within workspace/canvas.");
            return false;
        }

        return true;
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(normalizedRoot, comparison) ||
               string.Equals(candidatePath, rootPath, comparison);
    }

    private static string BuildCanvasEntryUrl(string entryPath)
    {
        var normalized = entryPath.Trim().Replace('\\', '/');
        var encodedPath = string.Join(
            '/',
            normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return "/api/canvas/fs/" + encodedPath;
    }

    private static string ResolveCanvasContentType(string filePath)
    {
        return CanvasContentTypeProvider.TryGetContentType(filePath, out var contentType)
            ? contentType
            : "text/plain; charset=utf-8";
    }

    private static FileExtensionContentTypeProvider CreateCanvasContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".html"] = "text/html; charset=utf-8";
        provider.Mappings[".css"] = "text/css; charset=utf-8";
        provider.Mappings[".js"] = "application/javascript; charset=utf-8";
        provider.Mappings[".json"] = "application/json; charset=utf-8";
        provider.Mappings[".txt"] = "text/plain; charset=utf-8";
        return provider;
    }
}
