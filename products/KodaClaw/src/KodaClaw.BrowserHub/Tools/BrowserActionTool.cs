using System.Text.Json;
using System.Collections;
using KodaClaw.BrowserHub.Models;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.BrowserHub.Tools;

/// <summary>
/// Agent tool that exposes BrowserHub actions for browser automation.
/// </summary>
public sealed class BrowserActionTool : ToolBase<BrowserActionArgs>
{
    private readonly IBrowserHubService _browserHubService;

    public BrowserActionTool(IBrowserHubService browserHubService)
    {
        ArgumentNullException.ThrowIfNull(browserHubService);
        _browserHubService = browserHubService;
    }

    public override string Name => "browser_action";

    public override string Description =>
        "Execute BrowserHub browser actions through a single tool entry point. " +
        "Supports navigation, DOM inspection, screenshots, tab management, input, JavaScript evaluation, cookies, console logs, waits, file upload, and network interception.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<BrowserActionArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        BrowserActionArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;

        try
        {
            if (string.IsNullOrWhiteSpace(args.Action))
            {
                return ToolResult.Fail("Parameter 'action' is required.");
            }

            var action = args.Action.Trim().ToLowerInvariant();
            var deviceResult = await GetDefaultDeviceIdAsync(CancellationToken.None).ConfigureAwait(false);
            if (!deviceResult.Success)
            {
                return deviceResult.ErrorResult!;
            }

            var deviceId = deviceResult.DeviceId!;

            return action switch
            {
                "navigate" => ToToolResult(
                    await _browserHubService.NavigateAsync(
                        url: GetRequiredStringParam(args.Params, "url"),
                        tabId: args.TabId,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "snapshot" => ToToolResult(
                    await _browserHubService.SnapshotAsync(
                        tabId: GetRequiredTabId(args),
                        selector: GetOptionalStringParam(args.Params, "selector"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "screenshot" => ToToolResult(
                    await _browserHubService.ScreenshotAsync(
                        tabId: GetRequiredTabId(args),
                        format: GetOptionalEnumParam(args.Params, "format", ScreenshotFormat.Jpeg),
                        quality: GetOptionalIntParam(args.Params, "quality") ?? 80,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "get_url" => ToToolResult(
                    await _browserHubService.GetUrlAsync(
                        tabId: GetRequiredTabId(args),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "list_tabs" => ToToolResult(
                    await _browserHubService.ListTabsAsync(CancellationToken.None).ConfigureAwait(false),
                    action),

                "click" => ToToolResult(
                    await _browserHubService.ClickAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        elementIndex: GetRequiredIntParam(args.Params, "elementIndex"),
                        offsetX: GetOptionalIntParam(args.Params, "offsetX"),
                        offsetY: GetOptionalIntParam(args.Params, "offsetY"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "type" => ToToolResult(
                    await _browserHubService.TypeAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        elementIndex: GetRequiredIntParam(args.Params, "elementIndex"),
                        text: GetRequiredStringParam(args.Params, "text"),
                        clearFirst: GetOptionalBoolParam(args.Params, "clearFirst") ?? false,
                        delayMs: GetOptionalIntParam(args.Params, "delayMs") ?? 50,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "scroll" => ToToolResult(
                    await _browserHubService.ScrollAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        direction: GetOptionalEnumParam(args.Params, "direction", ScrollDirection.Down),
                        amount: GetOptionalIntParam(args.Params, "amount"),
                        elementIndex: GetOptionalIntParam(args.Params, "elementIndex"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "key_press" => ToToolResult(
                    await _browserHubService.KeyPressAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        key: GetRequiredStringParam(args.Params, "key"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "go_back" => ToToolResult(
                    await _browserHubService.GoBackAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "close_tab" => ToToolResult(
                    await _browserHubService.CloseTabAsync(
                        deviceId: deviceId,
                        tabId: GetRequiredTabId(args),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "switch_tab" => ToToolResult(
                    await _browserHubService.SwitchTabAsync(
                        deviceId: deviceId,
                        tabId: GetRequiredTabId(args),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "evaluate" => ToToolResult(
                    await _browserHubService.EvaluateAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        script: GetRequiredStringParam(args.Params, "script"),
                        sandboxed: GetOptionalBoolParam(args.Params, "sandboxed") ?? true,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "evaluate_write" => ToToolResult(
                    await _browserHubService.EvaluateWriteAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        script: GetRequiredStringParam(args.Params, "script"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "cookies" => ToToolResult(
                    await _browserHubService.GetCookiesAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        url: GetOptionalStringParam(args.Params, "url"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "form_state" => ToToolResult(
                    await _browserHubService.GetFormStateAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "console" => ToToolResult(
                    await _browserHubService.GetConsoleMessagesAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        sinceTimestamp: GetOptionalIntParam(args.Params, "sinceTimestamp"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "wait" => ToToolResult(
                    await _browserHubService.WaitAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        durationMs: GetOptionalIntParam(args.Params, "durationMs"),
                        waitForSelector: GetOptionalStringParam(args.Params, "waitForSelector"),
                        waitUntil: GetOptionalStringParam(args.Params, "waitUntil"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "upload_file" => ToToolResult(
                    await _browserHubService.UploadFileAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        elementIndex: GetRequiredIntParam(args.Params, "elementIndex"),
                        filePath: GetRequiredStringParam(args.Params, "filePath"),
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "intercept" => ToToolResult(
                    await _browserHubService.StartInterceptAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        urlPattern: GetOptionalStringParam(args.Params, "urlPattern"),
                        resourceTypes: GetOptionalStringArrayParam(args.Params, "resourceTypes"),
                        requestHeaders: GetOptionalBoolParam(args.Params, "requestHeaders") ?? false,
                        responseBody: GetOptionalBoolParam(args.Params, "responseBody") ?? false,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "intercept_clear" => ToToolResult(
                    await _browserHubService.StopInterceptAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                "intercept_result" => ToToolResult(
                    await _browserHubService.GetInterceptedRequestsAsync(
                        deviceId: deviceId,
                        tabId: args.TabId,
                        urlPattern: GetOptionalStringParam(args.Params, "urlPattern"),
                        sinceTimestamp: GetOptionalLongParam(args.Params, "sinceTimestamp"),
                        resourceTypes: GetOptionalStringArrayParam(args.Params, "resourceTypes"),
                        limit: GetOptionalIntParam(args.Params, "limit") ?? 50,
                        ct: CancellationToken.None).ConfigureAwait(false),
                    action),

                _ => ToolResult.Fail($"Unsupported browser action '{args.Action}'.")
            };
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Browser action failed: {ex.Message}");
        }
    }

    private async Task<(bool Success, string? DeviceId, ToolResult? ErrorResult)> GetDefaultDeviceIdAsync(CancellationToken cancellationToken)
    {
        var devices = await _browserHubService.GetConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
        var deviceId = devices.FirstOrDefault()?.DeviceId;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return (false, null, ToolResult.Fail("No connected browser device is available."));
        }

        return (true, deviceId, null);
    }

    private static ToolResult ToToolResult<T>(BrowserResult<T> result, string action)
    {
        if (result.Ok)
        {
            return ToolResult.Ok(result.Data);
        }

        var error = string.IsNullOrWhiteSpace(result.Error)
            ? $"Browser action '{action}' failed."
            : $"Browser action '{action}' failed: {result.Error}";

        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            error = $"{error} (code: {result.ErrorCode})";
        }

        return ToolResult.Fail(error);
    }

    private static string GetRequiredTabId(BrowserActionArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.TabId))
        {
            throw new ArgumentException("Parameter 'tabId' is required for this action.");
        }

        return args.TabId.Trim();
    }

    private static string GetRequiredStringParam(IReadOnlyDictionary<string, object?>? parameters, string name)
    {
        var value = GetParamValue(parameters, name);
        var converted = ConvertToString(value);

        if (string.IsNullOrWhiteSpace(converted))
        {
            throw new ArgumentException($"Parameter '{name}' is required.");
        }

        return converted;
    }

    private static string? GetOptionalStringParam(IReadOnlyDictionary<string, object?>? parameters, string name)
    {
        var value = GetParamValue(parameters, name);
        return value is null ? null : ConvertToString(value);
    }

    private static int GetRequiredIntParam(IReadOnlyDictionary<string, object?>? parameters, string name)
    {
        var value = GetParamValue(parameters, name);
        var converted = ConvertToInt(value, name);

        if (!converted.HasValue)
        {
            throw new ArgumentException($"Parameter '{name}' is required.");
        }

        return converted.Value;
    }

    private static int? GetOptionalIntParam(IReadOnlyDictionary<string, object?>? parameters, string name)
        => ConvertToInt(GetParamValue(parameters, name), name);

    private static long? GetOptionalLongParam(IReadOnlyDictionary<string, object?>? parameters, string name)
        => ConvertToLong(GetParamValue(parameters, name), name);

    private static bool? GetOptionalBoolParam(IReadOnlyDictionary<string, object?>? parameters, string name)
        => ConvertToBool(GetParamValue(parameters, name), name);

    private static TEnum GetOptionalEnumParam<TEnum>(
        IReadOnlyDictionary<string, object?>? parameters,
        string name,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        var value = GetParamValue(parameters, name);
        if (value is null)
        {
            return defaultValue;
        }

        var raw = ConvertToString(value);
        if (string.IsNullOrWhiteSpace(raw) || !Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed))
        {
            var validValues = string.Join(", ", Enum.GetNames<TEnum>());
            throw new ArgumentException($"Parameter '{name}' must be one of: {validValues}.");
        }

        return parsed;
    }

    private static string[]? GetOptionalStringArrayParam(IReadOnlyDictionary<string, object?>? parameters, string name)
    {
        var value = GetParamValue(parameters, name);
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            string text => [text],
            string[] array => array,
            IEnumerable<string> enumerable => enumerable.ToArray(),
            JsonElement element when element.ValueKind == JsonValueKind.Array =>
                element.EnumerateArray().Select(item =>
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        throw new ArgumentException($"Parameter '{name}' must be an array of strings.");
                    }

                    return item.GetString()!;
                }).ToArray(),
            IEnumerable enumerable => enumerable.Cast<object?>().Select(item => ConvertToString(item) ?? throw new ArgumentException($"Parameter '{name}' must be an array of strings.")).ToArray(),
            _ => throw new ArgumentException($"Parameter '{name}' must be an array of strings."),
        };
    }

    private static object? GetParamValue(IReadOnlyDictionary<string, object?>? parameters, string name)
    {
        if (parameters is null)
        {
            return null;
        }

        foreach (var (key, value) in parameters)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ConvertToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => value.ToString(),
        };
    }

    private static int? ConvertToInt(object? value, string name)
    {
        return value switch
        {
            null => null,
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) => number,
            JsonElement element when element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var number) => number,
            string text when int.TryParse(text, out var number) => number,
            _ => throw new ArgumentException($"Parameter '{name}' must be an integer."),
        };
    }

    private static long? ConvertToLong(object? value, string name)
    {
        return value switch
        {
            null => null,
            long number => number,
            int number => number,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number) => number,
            JsonElement element when element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var number) => number,
            string text when long.TryParse(text, out var number) => number,
            _ => throw new ArgumentException($"Parameter '{name}' must be an integer."),
        };
    }

    private static bool? ConvertToBool(object? value, string name)
    {
        return value switch
        {
            null => null,
            bool boolean => boolean,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonElement element when element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var boolean) => boolean,
            string text when bool.TryParse(text, out var boolean) => boolean,
            _ => throw new ArgumentException($"Parameter '{name}' must be a boolean."),
        };
    }
}
