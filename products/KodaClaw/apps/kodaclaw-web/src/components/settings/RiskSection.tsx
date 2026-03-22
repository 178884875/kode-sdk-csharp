import { useEffect, useState } from "react";
import { Skeleton } from "../ui/Skeleton";
import { fetchSandboxRiskOverview } from "../../lib/api";
import { useLocaleText } from "../../i18n/I18nProvider";
import type { ChannelRiskItem, PluginRiskItem, SandboxExecutionProfile, SandboxRiskOverviewResponse } from "../../types/contracts";
import "../ControlPlaneDesk.css";

export function RiskSection() {
  const text = useLocaleText({
    zh: {
      errors: {
        loadRisk: "加载沙箱/风险简报失败。",
      },
      sections: {
        riskTitle: "沙箱与风险简报",
        riskIntro: "诚实展示当前执行边界：现阶段默认是主机本地沙箱 + 边界约束，Docker 仍只是 SDK 支持但未激活的更强选项，插件与渠道能力仍可能扩大外发范围。",
      },
      risk: {
        unavailable: "沙箱/风险简报当前不可用：",
        executionTitle: "当前沙箱姿态",
        guardrails: "护栏",
        residualRisk: "剩余风险",
        approvalTitle: "已持久化的操作员偏好",
        externalActions: "外部动作",
        approvalPreferred: "建议审批",
        approvalRelaxed: "审批放宽",
        quietHours: "静默时段",
        quietHoursEnabled: "已启用",
        disabled: "已关闭",
        notifications: "通知",
        enabled: "已启用",
        pluginTitle: "权限爆炸半径",
        highRiskPlugins: "高风险插件",
        trustBreakdown: "签名 / 信任 / 未信任",
        scopeBreakdown: "网络 / 后台 / Secrets",
        noPluginEscalations: "当前没有可见的插件权限升级。",
        channelTitle: "回复门控姿态",
        autoSend: "自动发送",
        draftOrApproval: "草稿 / 显式审批",
        pendingApprovals: "待处理审批",
        noChannelRisk: "当前没有活跃渠道线程在扩大外发风险。",
        outboundCapable: "具备外发能力的连接器。",
        inboundOnly: "仅支持入站；风险面主要局限于消息摄取。",
        pendingApprovalHint: "当前有审批等待操作员处理。",
      },
      labels: {
        accountState: "账户",
        trustEvidence: "信任证据",
        noExtraScopes: "没有声明额外权限范围。",
      },
      deliveryModeLabels: {
        AutoSend: "自动发送",
        DraftApproval: "草稿审批",
        RequireApproval: "显式审批",
      },
      trustStateLabels: {
        Signed: "已签名",
        Trusted: "已信任",
        Untrusted: "未信任",
      },
      executionStatusLabels: {
        active: "当前生效",
        supported: "SDK 支持",
        unsupported: "不可用",
      },
    },
    en: {
      errors: {
        loadRisk: "Failed to load sandbox/risk briefing.",
      },
      sections: {
        riskTitle: "Sandbox & Risk Briefing",
        riskIntro: "Keep the blast radius honest: current sessions run in a host-local sandbox with boundary enforcement, Docker is only an SDK-supported stronger option for now, and plugin/channel surfaces can still widen outbound reach.",
      },
      risk: {
        unavailable: "Sandbox/risk briefing is unavailable right now:",
        executionTitle: "Current sandbox posture",
        guardrails: "Guardrails",
        residualRisk: "Residual risk",
        approvalTitle: "Persisted operator intent",
        externalActions: "External actions",
        approvalPreferred: "Approval preferred",
        approvalRelaxed: "Approval relaxed",
        quietHours: "Quiet hours",
        quietHoursEnabled: "Enabled",
        disabled: "Disabled",
        notifications: "Notifications",
        enabled: "Enabled",
        pluginTitle: "Permission blast radius",
        highRiskPlugins: "High-risk plugins",
        trustBreakdown: "Signed / Trusted / Untrusted",
        scopeBreakdown: "Network / Background / Secrets",
        noPluginEscalations: "No plugin permission escalations are visible yet.",
        channelTitle: "Reply gating posture",
        autoSend: "Auto-send",
        draftOrApproval: "Draft / Explicit approval",
        pendingApprovals: "Pending approvals",
        noChannelRisk: "No active channel threads are widening outbound risk right now.",
        outboundCapable: "Outbound-capable connector.",
        inboundOnly: "Inbound-only connector; blast radius is limited to ingestion.",
        pendingApprovalHint: "An approval is currently waiting on operator review.",
      },
      labels: {
        accountState: "account",
        trustEvidence: "Trust evidence",
        noExtraScopes: "No extra scopes declared.",
      },
      deliveryModeLabels: {
        AutoSend: "Auto-send",
        DraftApproval: "Draft approval",
        RequireApproval: "Explicit approval",
      },
      trustStateLabels: {
        Signed: "Signed",
        Trusted: "Trusted",
        Untrusted: "Untrusted",
      },
      executionStatusLabels: {
        active: "Active profile",
        supported: "Supported by SDK",
        unsupported: "Unavailable",
      },
    },
  });

  const [riskOverview, setRiskOverview] = useState<SandboxRiskOverviewResponse | null>(null);
  const [riskError, setRiskError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const formatDeliveryMode = (mode: ChannelRiskItem["deliveryMode"]): string => {
    return text.deliveryModeLabels[mode] ?? mode;
  };

  const formatTrustState = (state: PluginRiskItem["trustState"]): string => {
    return text.trustStateLabels[state] ?? state;
  };

  const formatExecutionStatus = (profile: SandboxExecutionProfile): string => {
    if (profile.active) {
      return text.executionStatusLabels.active;
    }

    return profile.supported ? text.executionStatusLabels.supported : text.executionStatusLabels.unsupported;
  };

  useEffect(() => {
    setIsLoading(true);
    setRiskError(null);

    fetchSandboxRiskOverview()
      .then((data) => {
        if (data && typeof data === "object" && "approvalPosture" in data) {
          setRiskOverview(data);
        }
      })
      .catch((err: unknown) => {
        const detail = err instanceof Error ? err.message : text.errors.loadRisk;
        setRiskOverview(null);
        setRiskError(detail);
      })
      .finally(() => setIsLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <section
      className="status-card status-card--normal risk-briefing control-plane-stage-panel"
      data-testid="settings-risk-briefing"
    >
      <h3 className="desk-section-title">{text.sections.riskTitle}</h3>
      <p className="desk-section-desc">{text.sections.riskIntro}</p>

      {riskOverview ? (
        <div className="risk-briefing__banner">
          {(riskOverview.operatorWarnings ?? []).map((warning) => (
            <div key={warning} className="risk-briefing__warning">
              {warning}
            </div>
          ))}
        </div>
      ) : null}

      {riskError ? (
        <p className="desk-feedback desk-feedback--error" data-testid="settings-risk-error">
          {text.risk.unavailable} {riskError}
        </p>
      ) : null}

      {isLoading && !riskOverview && !riskError ? (
        <Skeleton height={40} count={4} />
      ) : null}

      {riskOverview ? (
        <div className="risk-briefing__grid">
          <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-execution-card">
            <h4 className="risk-briefing__title">{text.risk.executionTitle}</h4>
            <div className="risk-briefing__stack">
              {(riskOverview.executionProfiles ?? []).map((profile) => (
                <div key={profile.key} className="metric-item">
                  <div className="control-plane-flex-spread">
                    <strong>{profile.displayName}</strong>
                    <span className={`risk-briefing__pill ${profile.active ? "risk-briefing__pill--active" : ""}`}>
                      {formatExecutionStatus(profile)}
                    </span>
                  </div>
                  <span className="metric-value">{profile.summary}</span>
                  <span className="metric-label">{profile.blastRadius}</span>
                  <div className="risk-briefing__points">
                    <div>
                      <span className="metric-label">{text.risk.guardrails}</span>
                      <ul>
                        {(profile.guardrails ?? []).map((item) => (
                          <li key={item}>{item}</li>
                        ))}
                      </ul>
                    </div>
                    <div>
                      <span className="metric-label">{text.risk.residualRisk}</span>
                      <ul>
                        {(profile.residualRisks ?? []).map((item) => (
                          <li key={item}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </article>

          <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-approval-card">
            <h4 className="risk-briefing__title">{text.risk.approvalTitle}</h4>
            <div className="risk-briefing__metrics">
              <div className="metric-item">
                <span className="metric-label">{text.risk.externalActions}</span>
                <strong className="metric-value">
                  {riskOverview.approvalPosture.requireApprovalForExternalActions ? text.risk.approvalPreferred : text.risk.approvalRelaxed}
                </strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.quietHours}</span>
                <strong className="metric-value">
                  {riskOverview.approvalPosture.quietHoursEnabled
                    ? riskOverview.approvalPosture.quietHoursWindow ?? text.risk.quietHoursEnabled
                    : text.risk.disabled}
                </strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.notifications}</span>
                <strong className="metric-value">
                  {riskOverview.approvalPosture.notificationsEnabled ? text.risk.enabled : text.risk.disabled}
                </strong>
              </div>
            </div>
            <p className="desk-section-desc control-plane-zero-margin">{riskOverview.approvalPosture.advisory}</p>
          </article>

          <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-plugins-card">
            <h4 className="risk-briefing__title">{text.risk.pluginTitle}</h4>
            <div className="risk-briefing__metrics">
              <div className="metric-item">
                <span className="metric-label">{text.risk.highRiskPlugins}</span>
                <strong className="metric-value">{riskOverview.pluginRisk.highRiskCount}</strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.trustBreakdown}</span>
                <strong className="metric-value">
                  {riskOverview.pluginRisk.signedCount} / {riskOverview.pluginRisk.trustedCount} / {riskOverview.pluginRisk.untrustedCount}
                </strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.scopeBreakdown}</span>
                <strong className="metric-value">
                  {riskOverview.pluginRisk.networkEnabledCount} / {riskOverview.pluginRisk.backgroundCount} / {riskOverview.pluginRisk.secretAccessCount}
                </strong>
              </div>
            </div>
            <p className="desk-section-desc">{riskOverview.pluginRisk.advisory}</p>
            {(riskOverview.pluginRisk.riskItems ?? []).length > 0 ? (
              <ul className="risk-briefing__list">
                {(riskOverview.pluginRisk.riskItems ?? []).map((item) => (
                  <li key={item.pluginId}>
                    <div className="metric-item">
                      <div className="control-plane-flex-spread">
                        <strong>{item.displayName}</strong>
                        <span className={`risk-briefing__pill ${item.highRiskReasons.length > 0 ? "risk-briefing__pill--warning" : ""}`}>
                          {formatTrustState(item.trustState)} · {item.runtimeState}
                        </span>
                      </div>
                      <span className="metric-label">{(item.requestedScopes ?? []).join(" • ") || text.labels.noExtraScopes}</span>
                      {(item.highRiskReasons ?? []).length > 0 ? (
                        <span className="metric-value">{(item.highRiskReasons ?? []).join(" ")}</span>
                      ) : null}
                      {(item.mediumRiskReasons ?? []).length > 0 ? (
                        <span className="desk-section-desc">{(item.mediumRiskReasons ?? []).join(" ")}</span>
                      ) : null}
                      {item.trustEvidenceSummary ? (
                        <span className="desk-section-desc">{text.labels.trustEvidence}: {item.trustEvidenceSummary}</span>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="desk-section-desc control-plane-zero-margin">{text.risk.noPluginEscalations}</p>
            )}
          </article>

          <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-channels-card">
            <h4 className="risk-briefing__title">{text.risk.channelTitle}</h4>
            <div className="risk-briefing__metrics">
              <div className="metric-item">
                <span className="metric-label">{text.risk.autoSend}</span>
                <strong className="metric-value">{riskOverview.channelRisk.autoSendCount}</strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.draftOrApproval}</span>
                <strong className="metric-value">
                  {riskOverview.channelRisk.draftApprovalCount} / {riskOverview.channelRisk.requireApprovalCount}
                </strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.risk.pendingApprovals}</span>
                <strong className="metric-value">{riskOverview.channelRisk.pendingApprovalCount}</strong>
              </div>
            </div>
            <p className="desk-section-desc">{riskOverview.channelRisk.advisory}</p>
            {(riskOverview.channelRisk.riskItems ?? []).length > 0 ? (
              <ul className="risk-briefing__list">
                {(riskOverview.channelRisk.riskItems ?? []).map((item) => (
                  <li key={item.bindingId}>
                    <div className="metric-item">
                      <div className="control-plane-flex-spread">
                        <strong>{item.displayTitle}</strong>
                        <span className={`risk-briefing__pill ${item.hasPendingApproval ? "risk-briefing__pill--warning" : ""}`}>
                          {formatDeliveryMode(item.deliveryMode)}
                        </span>
                      </div>
                      <span className="metric-label">
                        {item.connectorKind} · {item.threadType} · {text.labels.accountState} {item.accountState}
                      </span>
                      <span className="desk-section-desc">
                        {item.supportsOutbound ? text.risk.outboundCapable : text.risk.inboundOnly}
                        {item.hasPendingApproval ? ` ${text.risk.pendingApprovalHint}` : ""}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="desk-section-desc control-plane-zero-margin">{text.risk.noChannelRisk}</p>
            )}
          </article>
        </div>
      ) : null}
    </section>
  );
}
