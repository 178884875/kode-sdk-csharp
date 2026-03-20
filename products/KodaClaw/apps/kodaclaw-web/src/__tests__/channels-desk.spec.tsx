import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ChannelsDesk } from "../components/ChannelsDesk";
import { renderWithI18n } from "./test-utils";

const originalFetch = global.fetch;

function jsonResponse(payload: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? "OK" : "ERROR",
    json: async () => payload,
    text: async () => JSON.stringify(payload),
  } as Response;
}

describe("ChannelsDesk", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    global.fetch = originalFetch;
  });

  it("loads connectors, accounts, threads, and thread detail", async () => {
    const connectors = [
      {
        kind: "Telegram",
        displayName: "Telegram",
        implemented: true,
        supportsInbound: true,
        supportsOutbound: true,
        productOwned: true,
      },
      {
        kind: "GenericWebhook",
        displayName: "Generic Webhook",
        implemented: true,
        supportsInbound: true,
        supportsOutbound: false,
        productOwned: true,
      },
    ];

    const accounts = [
      {
        id: "telegram-main",
        connectorKind: "Telegram",
        displayName: "Telegram Bot",
        state: "Connected",
        createdAt: "2026-03-19T08:00:00Z",
        updatedAt: "2026-03-19T08:30:00Z",
        inboundEnabled: true,
      },
    ];

    const threads = {
      items: [
        {
          bindingId: "binding-telegram-001",
          connectorKind: "Telegram",
          accountId: "telegram-main",
          externalThreadId: "10001",
          threadType: "DirectMessage",
          sessionId: "channel-dm-001",
          sessionKind: "ChannelDirectMessage",
          displayTitle: "Alice",
          deliveryMode: "DraftApproval",
          accountState: "Connected",
          updatedAt: "2026-03-19T08:30:00Z",
          lastInboundAt: "2026-03-19T08:29:00Z",
          lastOutboundAt: null,
          lastMessagePreview: "hello from telegram",
          pendingApprovalId: "approval-001",
          hasPendingDraft: true,
        },
      ],
    };

    const detail = {
      account: accounts[0],
      binding: {
        id: "binding-telegram-001",
        connectorKind: "Telegram",
        accountId: "telegram-main",
        externalThreadId: "10001",
        threadType: "DirectMessage",
        sessionId: "channel-dm-001",
        sessionKind: "ChannelDirectMessage",
        channelIdentity: {
          id: "20001",
          username: "alice",
          displayName: "Alice",
          isBot: false,
        },
        policyId: "policy-default-dm",
        deliveryRuleId: "delivery-default-dm",
        createdAt: "2026-03-19T08:00:00Z",
        updatedAt: "2026-03-19T08:30:00Z",
      },
      policy: {
        id: "policy-default-dm",
        threadType: "DirectMessage",
        updatedAt: "2026-03-19T08:30:00Z",
        loadAgents: true,
        loadIdentity: true,
        loadSoul: true,
        loadUserProfile: true,
        loadLongTermMemory: false,
        loadRecentThreadSummary: true,
        allowDirectReply: true,
        requireExplicitMention: false,
        workspaceMuted: false,
        connectorMuted: false,
        threadMuted: false,
        notes: null,
      },
      deliveryRule: {
        id: "delivery-default-dm",
        mode: "DraftApproval",
        updatedAt: "2026-03-19T08:30:00Z",
        allowProactiveSend: false,
        muteDuringQuietHours: true,
      },
      recentAudit: [],
      session: {
        sessionId: "channel-dm-001",
        sessionKind: "ChannelDirectMessage",
        status: {
          isActiveMainSession: false,
          breakpointState: null,
          messageCount: 4,
          pendingApprovalCount: 1,
        },
        createdAt: "2026-03-19T08:00:00Z",
        lastEventAt: "2026-03-19T08:29:00Z",
      },
      pendingApprovalId: "approval-001",
      hasPendingDraft: true,
    };

    const audit = [
      {
        id: "audit-001",
        bindingId: "binding-telegram-001",
        connectorKind: "Telegram",
        accountId: "telegram-main",
        externalThreadId: "10001",
        threadType: "DirectMessage",
        eventType: "message.received",
        createdAt: "2026-03-19T08:29:00Z",
        sessionId: "channel-dm-001",
        approvalId: null,
        deliveryMode: "DraftApproval",
        externalMessageId: "11",
        summary: "hello from telegram",
        metadataJson: null,
      },
    ];

    vi.mocked(fetch).mockImplementation(async (input) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/channels/connectors")) {
        return jsonResponse(connectors);
      }
      if (url.includes("/api/channels/accounts")) {
        return jsonResponse(accounts);
      }
      if (url.includes("/api/channels/threads?")) {
        return jsonResponse(threads);
      }
      if (url.endsWith("/api/channels/threads/binding-telegram-001")) {
        return jsonResponse(detail);
      }
      if (url.includes("/api/channels/threads/binding-telegram-001/audit")) {
        return jsonResponse(audit);
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    renderWithI18n(<ChannelsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("channels-connectors-accounts")).toHaveTextContent("Telegram");
    });
    expect(screen.getByTestId("channels-summary")).toHaveTextContent("1 个账号 · 1 条线程");
    expect(screen.getByTestId("channels-threads")).toHaveTextContent("Alice");
    expect(screen.getByTestId("channel-thread-detail")).toHaveTextContent("草稿审批");
    expect(screen.getByTestId("channel-thread-audit")).toHaveTextContent("message.received");
    expect(screen.getByTestId("channel-thread-detail")).toHaveTextContent("approval-001");

    const user = userEvent.setup();
    await user.selectOptions(screen.getByTestId("channels-connector-filter"), "Telegram");
    await waitFor(() => {
      expect(screen.getByTestId("channels-connectors-accounts")).toHaveTextContent("Telegram Bot");
    });
  });

  it("renders error when channels requests fail", async () => {
    vi.mocked(fetch).mockImplementation(async () => jsonResponse({ message: "channels unavailable" }, 500));

    renderWithI18n(<ChannelsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("channels-error")).toHaveTextContent("channels unavailable");
    });
  });
});
