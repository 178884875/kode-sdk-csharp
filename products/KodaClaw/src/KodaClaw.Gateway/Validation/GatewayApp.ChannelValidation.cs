using System.Text.Json;
using KodaClaw.Contracts;

public static partial class GatewayApp
{
    private static bool TryValidateChannelAccountRequest(
        UpsertChannelAccountRequest request,
        out ErrorResponse? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            error = new ErrorResponse(
                Code: "validation.channel_account_id_required",
                Message: "Channel account id is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            error = new ErrorResponse(
                Code: "validation.channel_account_display_name_required",
                Message: "Channel account display name is required.");
            return false;
        }

        var normalizedConfigurationJson = NormalizeOptionalString(request.ConfigurationJson);
        if (normalizedConfigurationJson is null)
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = new ErrorResponse(
                    Code: "validation.channel_account_configuration_invalid",
                    Message: "Channel account configuration must be a JSON object.");
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = new ErrorResponse(
                Code: "validation.channel_account_configuration_invalid",
                Message: "Channel account configuration must be valid JSON.");
            return false;
        }
        catch (ArgumentException ex)
        {
            error = new ErrorResponse(
                Code: "validation.channel_account_configuration_invalid",
                Message: ex.Message);
            return false;
        }
    }

    private static ChannelAccountState ResolveChannelAccountState(
        UpsertChannelAccountRequest request,
        ChannelAccount? existing)
    {
        if (!request.InboundEnabled)
        {
            return ChannelAccountState.Disconnected;
        }

        return request.ConnectorKind switch
        {
            ChannelConnectorKind.GenericWebhook => ChannelAccountState.Connected,
            ChannelConnectorKind.Telegram => existing?.State ?? ChannelAccountState.Disconnected,
            _ => existing?.State ?? ChannelAccountState.Disconnected,
        };
    }

    private static string? NormalizeOptionalJson(string? value)
    {
        var normalized = NormalizeOptionalString(value);
        if (normalized is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(normalized);
        return document.RootElement.GetRawText();
    }
}
