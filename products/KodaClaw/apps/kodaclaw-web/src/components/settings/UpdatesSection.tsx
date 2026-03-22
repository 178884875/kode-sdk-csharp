import { useEffect, useState } from "react";
import { Skeleton } from "../ui/Skeleton";
import { fetchSettings, runUpdateCheck } from "../../lib/api";
import { getRuntimeConfig } from "../../lib/config";
import { useI18n, useLocaleText } from "../../i18n/I18nProvider";
import type { UpdateAvailability, UpdateCheckRequest, UpdateStateResponse } from "../../types/contracts";
import "../ControlPlaneDesk.css";

function buildUpdateCheckRequest(): UpdateCheckRequest {
  const runtimeConfig = getRuntimeConfig();
  if (!runtimeConfig.desktopMode || !runtimeConfig.appVersion) {
    return {};
  }

  return {
    desktopCurrentVersion: runtimeConfig.appVersion,
    desktopReleaseChannel: runtimeConfig.releaseChannel,
  };
}

function toSafeExternalUrl(url: string): string | null {
  try {
    const parsed = new URL(url);
    const protocol = parsed.protocol.toLowerCase();
    if (protocol !== "http:" && protocol !== "https:") {
      return null;
    }

    return parsed.toString();
  } catch {
    return null;
  }
}

function openExternalUrl(url: string) {
  const safeUrl = toSafeExternalUrl(url);
  if (!safeUrl) {
    return;
  }

  window.open(safeUrl, "_blank", "noopener,noreferrer");
}

export function UpdatesSection() {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      common: {
        notCheckedYet: "尚未检查",
      },
      notes: {
        updateCheckCompleted: "更新检查已完成。",
      },
      errors: {
        loadUpdate: "刷新更新信号失败。",
      },
      sections: {
        updateTitle: "更新观察台",
        updateIntro: "跟踪 Gateway / Desktop 当前版本、发布通道与手动升级交接。本面板不会执行静默安装。",
      },
      update: {
        checkNow: "立即检查",
        checking: "检查中…",
        unavailable: "更新观察台当前不可用：",
        manifestSource: "Manifest 来源",
        evidenceFile: "证据文件",
        generatedAt: "生成时间",
        currentVersion: "当前版本",
        latestKnown: "已知最新",
        releaseChannel: "发布通道",
        lastChecked: "最近检查",
        noPublishedVersion: "未发布",
        noReleaseNotes: "当前通道快照未附带发布说明。",
        openReleaseNotes: "打开发布说明",
        openManualUpgrade: "打开手动升级",
        loading: "正在加载更新姿态…",
      },
      updateAvailabilityLabels: {
        UpToDate: "已是最新",
        UpdateAvailable: "有可用更新",
        CheckFailed: "检查失败",
        Unknown: "状态未知",
      },
    },
    en: {
      common: {
        notCheckedYet: "Not checked yet",
      },
      notes: {
        updateCheckCompleted: "Update check completed.",
      },
      errors: {
        loadUpdate: "Failed to refresh update signals.",
      },
      sections: {
        updateTitle: "Update Watch",
        updateIntro: "Track the current Gateway/Desktop versions, the selected release channel, and the manual upgrade handoff. This surface never performs a silent install.",
      },
      update: {
        checkNow: "Check now",
        checking: "Checking...",
        unavailable: "Update watch is unavailable right now:",
        manifestSource: "Manifest source",
        evidenceFile: "Evidence file",
        generatedAt: "Generated at",
        currentVersion: "Current version",
        latestKnown: "Latest known",
        releaseChannel: "Release channel",
        lastChecked: "Last checked",
        noPublishedVersion: "Not published",
        noReleaseNotes: "No release notes were published for this channel snapshot.",
        openReleaseNotes: "Open release notes",
        openManualUpgrade: "Open manual upgrade",
        loading: "Loading current update posture...",
      },
      updateAvailabilityLabels: {
        UpToDate: "Up to date",
        UpdateAvailable: "Update available",
        CheckFailed: "Check failed",
        Unknown: "Status unknown",
      },
    },
  });

  const [updateState, setUpdateState] = useState<UpdateStateResponse | null>(null);
  const [isRefreshingUpdate, setIsRefreshingUpdate] = useState(false);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const formatTimestamp = (value?: string | null): string => {
    return formatDateTime(value, text.common.notCheckedYet);
  };

  const formatUpdateAvailability = (availability: UpdateAvailability): string => {
    return text.updateAvailabilityLabels[availability] ?? text.updateAvailabilityLabels.Unknown;
  };

  const getUpdateAvailabilityClassName = (availability: UpdateAvailability): string => {
    if (availability === "UpToDate") {
      return "risk-briefing__pill risk-briefing__pill--active";
    }

    if (availability === "UpdateAvailable" || availability === "CheckFailed") {
      return "risk-briefing__pill risk-briefing__pill--warning";
    }

    return "risk-briefing__pill";
  };

  async function load() {
    setIsLoading(true);
    setUpdateError(null);

    try {
      const [updateResult] = await Promise.allSettled([
        runUpdateCheck(buildUpdateCheckRequest()),
        fetchSettings(),
      ]);

      if (updateResult.status === "fulfilled" && Array.isArray(updateResult.value?.components)) {
        setUpdateState(updateResult.value);
      } else if (updateResult.status === "rejected") {
        const detail = updateResult.reason instanceof Error
          ? updateResult.reason.message
          : text.errors.loadUpdate;
        setUpdateState(null);
        setUpdateError(detail);
      }
    } finally {
      setIsLoading(false);
    }
  }

  async function refreshUpdateState() {
    setUpdateError(null);
    setIsRefreshingUpdate(true);

    try {
      const refreshed = await runUpdateCheck(buildUpdateCheckRequest());
      setUpdateState(refreshed);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.loadUpdate;
      setUpdateError(detail);
    } finally {
      setIsRefreshingUpdate(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <section
      className="status-card status-card--normal update-watch control-plane-stage-panel"
      data-testid="settings-update-watch"
    >
      <div className="update-watch__header">
        <div>
          <h3 className="desk-section-title">{text.sections.updateTitle}</h3>
          <p className="desk-section-desc">
            {text.sections.updateIntro}
          </p>
        </div>
        <button
          className="secondary-button"
          type="button"
          data-testid="settings-update-check"
          onClick={() => void refreshUpdateState()}
          disabled={isLoading || isRefreshingUpdate}
        >
          {isRefreshingUpdate ? text.update.checking : text.update.checkNow}
        </button>
      </div>

      {updateError ? (
        <p className="desk-feedback desk-feedback--error" data-testid="settings-update-error">
          {text.update.unavailable} {updateError}
        </p>
      ) : null}

      {updateState ? (
        <>
          <div className="update-watch__meta">
            <div className="metric-item">
              <span className="metric-label">{text.update.manifestSource}</span>
              <strong className="metric-value metric-value--path">{updateState.manifestSource}</strong>
            </div>
            <div className="metric-item">
              <span className="metric-label">{text.update.evidenceFile}</span>
              <strong className="metric-value metric-value--path">{updateState.artifactPath}</strong>
            </div>
            <div className="metric-item">
              <span className="metric-label">{text.update.generatedAt}</span>
              <strong className="metric-value">{formatTimestamp(updateState.generatedAt)}</strong>
            </div>
          </div>

          <div className="update-watch__banner">
            {(updateState.operatorNotes ?? []).map((noteItem) => (
              <div key={noteItem} className="update-watch__note">
                {noteItem}
              </div>
            ))}
          </div>

          <div className="update-watch__grid">
            {(updateState.components ?? []).map((component) => (
              <article
                key={component.component}
                className="update-watch__card control-plane-update-card"
                data-testid={`update-component-${component.component}`}
              >
                <div className="control-plane-flex-spread control-plane-flex-spread--wrap">
                  <h4 className="update-watch__title">{component.displayName}</h4>
                  <span className={getUpdateAvailabilityClassName(component.updateAvailability)}>
                    {formatUpdateAvailability(component.updateAvailability)}
                  </span>
                </div>

                <div className="update-watch__metrics">
                  <div className="metric-item">
                    <span className="metric-label">{text.update.currentVersion}</span>
                    <strong className="metric-value">{component.currentVersion}</strong>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.update.latestKnown}</span>
                    <strong className="metric-value">{component.latestKnownVersion ?? text.update.noPublishedVersion}</strong>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.update.releaseChannel}</span>
                    <strong className="metric-value">{component.releaseChannel}</strong>
                  </div>
                </div>

                <p className="desk-section-desc">{text.update.lastChecked}: {formatTimestamp(component.lastCheckedAt)}</p>
                <p className="desk-section-desc control-plane-zero-margin">{component.guidance}</p>

                {(component.releaseNotes ?? []).length > 0 ? (
                  <ol className="update-watch__list">
                    {(component.releaseNotes ?? []).map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ol>
                ) : (
                  <p className="desk-section-desc control-plane-zero-margin">{text.update.noReleaseNotes}</p>
                )}

                {(component.releaseNotesUrl || component.downloadUrl) ? (
                  <div className="update-watch__actions">
                    {component.releaseNotesUrl ? (
                      <button
                        className="secondary-button"
                        type="button"
                        onClick={() => openExternalUrl(component.releaseNotesUrl!)}
                      >
                        {text.update.openReleaseNotes}
                      </button>
                    ) : null}
                    {component.downloadUrl && component.updateAvailability === "UpdateAvailable" ? (
                      <button
                        className="bootstrap-form__submit"
                        type="button"
                        onClick={() => openExternalUrl(component.downloadUrl!)}
                      >
                        {text.update.openManualUpgrade}
                      </button>
                    ) : null}
                  </div>
                ) : null}
              </article>
            ))}
          </div>
        </>
      ) : (
        <Skeleton height={40} count={4} />
      )}
    </section>
  );
}
