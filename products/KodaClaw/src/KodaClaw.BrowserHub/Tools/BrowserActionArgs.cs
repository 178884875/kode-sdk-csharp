using System.Collections.Generic;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.BrowserHub.Tools;

/// <summary>
/// Arguments for the browser_action tool.
/// </summary>
public sealed record class BrowserActionArgs
{
    [ToolParameter(
        Description = "Browser action name. Supported values: navigate, snapshot, screenshot, get_url, list_tabs, click, type, scroll, key_press, go_back, close_tab, switch_tab, evaluate, evaluate_write, cookies, form_state, console, wait, upload_file, intercept, intercept_clear, intercept_result.")]
    public required string Action { get; init; }

    [ToolParameter(
        Description = "Optional browser tab id. Required for actions that target a specific tab such as snapshot, screenshot, get_url, close_tab, and switch_tab. For switch_tab, this is the target tab id.",
        Required = false)]
    public string? TabId { get; init; }

    [ToolParameter(
        Description = "Action-specific parameters as a dictionary. Supported params by action: navigate { url }; snapshot { selector? }; screenshot { quality?, format? }; click { elementIndex, offsetX?, offsetY? }; type { elementIndex, text, clearFirst?, delayMs? }; scroll { direction?='down', amount?, elementIndex? }; key_press { key }; evaluate { script, sandboxed? }; evaluate_write { script }; cookies { url? }; console { sinceTimestamp? }; wait { durationMs?, waitForSelector?, waitUntil? }; upload_file { elementIndex, filePath }; intercept { urlPattern?, resourceTypes?, requestHeaders?, responseBody? }; intercept_result { urlPattern?, sinceTimestamp?, resourceTypes?, limit? }. Actions list_tabs, get_url, go_back, close_tab, switch_tab, form_state, and intercept_clear do not require extra params.",
        Required = false)]
    public Dictionary<string, object?>? Params { get; init; }
}
