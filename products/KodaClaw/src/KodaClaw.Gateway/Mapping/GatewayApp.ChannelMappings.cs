using KodaClaw.ChannelHub;
using KodaClaw.ChannelHub.Connectors.Webhook;
using KodaClaw.Contracts;
using Microsoft.AspNetCore.Http;

public static partial class GatewayApp
{
    private static async Task<ChannelThreadDetail?> LoadChannelThreadDetailAsync(
        IWorkspaceService workspaceService,
        IThreadBindingRepository threadBindingRepository,
        IChannelAccountRepository channelAccountRepository,
        ChannelPolicyEngine channelPolicyEngine,
        ChannelAuditQueryService channelAuditQueryService,
        IApprovalRepository approvalRepository,
        string bindingId,
        CancellationToken cancellationToken)
    {
        var binding = await threadBindingRepository.GetByIdAsync(bindingId, cancellationToken);
        if (binding is null)
        {
            return null;
        }

        var account = await channelAccountRepository.GetByIdAsync(binding.AccountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var snapshot = await workspaceService.GetSnapshotAsync(cancellationToken);
        var sessionDetail = await LoadSessionDetailAsync(
            workspaceService.RootPath,
            binding.SessionId,
            snapshot.ActiveMainSessionId,
            cancellationToken);
        var session = sessionDetail is null
            ? null
            : new SessionSummary(
                SessionId: sessionDetail.SessionId,
                SessionKind: sessionDetail.SessionKind,
                Status: sessionDetail.Status,
                CreatedAt: sessionDetail.CreatedAt,
                LastEventAt: sessionDetail.LastEventAt);
        var audit = await channelAuditQueryService.ListRecentByBindingIdAsync(binding.Id, 20, cancellationToken);
        var pendingApproval = await LoadPendingChannelApprovalAsync(
            approvalRepository,
            binding.SessionId,
            cancellationToken);

        return new ChannelThreadDetail(
            Account: account,
            Binding: binding,
            Policy: channelPolicyEngine.CreateDefaultPolicy(binding.ThreadType, binding.UpdatedAt, binding.PolicyId),
            DeliveryRule: BuildDefaultChannelDeliveryRule(binding.ThreadType, binding.UpdatedAt, binding.DeliveryRuleId),
            RecentAudit: audit,
            Session: session,
            PendingApprovalId: pendingApproval?.Id,
            HasPendingDraft: pendingApproval is not null);
    }

    private static async Task<Approval?> LoadPendingChannelApprovalAsync(
        IApprovalRepository approvalRepository,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalQuery(
                Status: ApprovalStatus.Pending,
                Kind: ApprovalKind.ChannelDelivery,
                SessionId: sessionId,
                Limit: 1),
            cancellationToken);

        return approvals.FirstOrDefault();
    }

    private static ChannelThreadSummary BuildChannelThreadSummary(
        ThreadBinding binding,
        ChannelAccount? account,
        Approval? pendingApproval)
    {
        return new ChannelThreadSummary(
            BindingId: binding.Id,
            ConnectorKind: binding.ConnectorKind,
            AccountId: binding.AccountId,
            ExternalThreadId: binding.ExternalThreadId,
            ThreadType: binding.ThreadType,
            SessionId: binding.SessionId,
            SessionKind: binding.SessionKind,
            DisplayTitle: ResolveChannelDisplayTitle(binding),
            DeliveryMode: ResolveDefaultChannelDeliveryMode(binding.ThreadType),
            AccountState: account?.State ?? ChannelAccountState.Disconnected,
            UpdatedAt: binding.UpdatedAt,
            LastInboundAt: binding.LastInboundAt,
            LastOutboundAt: binding.LastOutboundAt,
            LastMessagePreview: binding.LastMessagePreview,
            PendingApprovalId: pendingApproval?.Id,
            HasPendingDraft: pendingApproval is not null);
    }

    private static string ResolveChannelDisplayTitle(ThreadBinding binding)
    {
        return binding.ChannelIdentity.DisplayName
            ?? binding.ChannelIdentity.Username
            ?? binding.ExternalThreadId;
    }

    private static DeliveryMode ResolveDefaultChannelDeliveryMode(ChannelThreadType threadType)
    {
        return threadType switch
        {
            ChannelThreadType.DirectMessage => DeliveryMode.DraftApproval,
            ChannelThreadType.Group => DeliveryMode.RequireApproval,
            _ => throw new ArgumentOutOfRangeException(nameof(threadType), threadType, null),
        };
    }

    private static DeliveryRule BuildDefaultChannelDeliveryRule(
        ChannelThreadType threadType,
        DateTimeOffset updatedAt,
        string? deliveryRuleId = null)
    {
        return new DeliveryRule(
            Id: string.IsNullOrWhiteSpace(deliveryRuleId)
                ? threadType == ChannelThreadType.DirectMessage
                    ? "delivery-default-dm"
                    : "delivery-default-group"
                : deliveryRuleId.Trim(),
            Mode: ResolveDefaultChannelDeliveryMode(threadType),
            UpdatedAt: updatedAt,
            AllowProactiveSend: false,
            MuteDuringQuietHours: true);
    }

    private static (int StatusCode, ErrorResponse Error) MapWebhookRejection(
        GenericWebhookInboundDispatchResult result)
    {
        return result.RejectionCode switch
        {
            "secret_mismatch" => (
                StatusCodes.Status401Unauthorized,
                new ErrorResponse(
                    Code: "channel.webhook_secret_mismatch",
                    Message: result.RejectionMessage ?? "Webhook shared secret did not match.")),
            "invalid_payload" => (
                StatusCodes.Status400BadRequest,
                new ErrorResponse(
                    Code: "channel.webhook_payload_invalid",
                    Message: result.RejectionMessage ?? "Webhook payload is invalid.")),
            "account_not_started" => (
                StatusCodes.Status409Conflict,
                new ErrorResponse(
                    Code: "channel.account_not_started",
                    Message: result.RejectionMessage ?? "Channel account is not started.")),
            _ => (
                StatusCodes.Status400BadRequest,
                new ErrorResponse(
                    Code: "channel.webhook_rejected",
                    Message: result.RejectionMessage ?? "Webhook event was rejected.")),
        };
    }
}
