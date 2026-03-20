import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InboxApprovalDesk } from "../components/InboxApprovalDesk";
import type { Approval, InboxItem } from "../types/contracts";
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

function resolveRequestUrl(input: string | URL | Request): string {
  if (typeof input === "string") {
    return input;
  }

  if (input instanceof URL) {
    return input.toString();
  }

  return input.url;
}

describe("InboxApprovalDesk", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    global.fetch = originalFetch;
  });

  it("loads inbox and approvals with stable test ids", async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const url = resolveRequestUrl(input);

      if (url.includes("/api/inbox")) {
        return jsonResponse({
          items: [
            {
              id: "inbox-001",
              kind: "Approval",
              status: "Open",
              title: "Outbound approval",
              summary: "Need user confirmation",
              source: "runtime.main_session.approval",
              createdAt: "2026-03-18T09:00:00.000Z",
              updatedAt: "2026-03-18T09:00:00.000Z",
              requiresAction: true,
              approvalId: "approval-001",
            } satisfies InboxItem,
          ],
        });
      }

      if (url.includes("/api/approvals")) {
        return jsonResponse({
          items: [
            {
              id: "approval-001",
              kind: "ExternalAction",
              status: "Pending",
              title: "Send channel message",
              summary: "Tool requires approval",
              source: "runtime.main_session.approval",
              requestedAt: "2026-03-18T09:00:00.000Z",
              updatedAt: "2026-03-18T09:00:00.000Z",
              inboxItemId: "inbox-001",
            } satisfies Approval,
          ],
        });
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    renderWithI18n(<InboxApprovalDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("inbox-approval-desk")).toBeInTheDocument();
    });

    expect(screen.getByTestId("inbox-list")).toBeInTheDocument();
    expect(screen.getByTestId("approval-list")).toBeInTheDocument();
    expect(screen.getByTestId("inbox-refresh")).toBeInTheDocument();
    expect(screen.getAllByTestId("approval-approve")[0]).toBeInTheDocument();
    expect(screen.getAllByTestId("approval-reject")[0]).toBeInTheDocument();
    expect(screen.getByText("Outbound approval")).toBeInTheDocument();
    expect(screen.getByText("Send channel message")).toBeInTheDocument();
  });

  it("supports approval decisions and inbox status updates", async () => {
    const approvals: Approval[] = [
      {
        id: "approval-002",
        kind: "ExternalAction",
        status: "Pending",
        title: "Call risky tool",
        summary: "Awaiting decision",
        source: "runtime.main_session.approval",
        requestedAt: "2026-03-18T10:00:00.000Z",
        updatedAt: "2026-03-18T10:00:00.000Z",
        inboxItemId: "inbox-002",
      },
    ];

    const inboxItems: InboxItem[] = [
      {
        id: "inbox-002",
        kind: "Approval",
        status: "Open",
        title: "Risky tool",
        summary: "Handle approval result",
        source: "runtime.main_session.approval",
        createdAt: "2026-03-18T10:00:00.000Z",
        updatedAt: "2026-03-18T10:00:00.000Z",
        requiresAction: true,
        approvalId: "approval-002",
      },
    ];

    const decisionPayloads: unknown[] = [];

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = resolveRequestUrl(input);
      const method = init?.method ?? "GET";

      if (url.includes("/api/approvals/approval-002/approve") && method === "POST") {
        decisionPayloads.push(JSON.parse((init?.body as string) ?? "{}"));
        approvals[0] = {
          ...approvals[0],
          status: "Approved",
          updatedAt: "2026-03-18T10:02:00.000Z",
          decisionNote: "ship it",
        };
        return jsonResponse(approvals[0]);
      }

      if (url.includes("/api/inbox/inbox-002/status") && method === "PATCH") {
        const payload = JSON.parse((init?.body as string) ?? "{}") as { status: InboxItem["status"] };
        inboxItems[0] = {
          ...inboxItems[0],
          status: payload.status,
          updatedAt: "2026-03-18T10:03:00.000Z",
        };
        return jsonResponse(inboxItems[0]);
      }

      if (url.includes("/api/inbox")) {
        return jsonResponse({ items: inboxItems });
      }

      if (url.includes("/api/approvals")) {
        return jsonResponse({ items: approvals });
      }

      throw new Error(`Unexpected request: ${method} ${url}`);
    });

    renderWithI18n(<InboxApprovalDesk />);

    const user = userEvent.setup();

    await waitFor(() => {
      expect(screen.getByText("Call risky tool")).toBeInTheDocument();
    });

    await user.type(screen.getByTestId("approval-note-approval-002"), "ship it");
    await user.click(screen.getAllByTestId("approval-approve")[0]);

    await waitFor(() => {
      expect(
        within(screen.getByTestId("approval-item-approval-002")).getByText("已批准"),
      ).toBeInTheDocument();
    });

    expect(decisionPayloads).toEqual([{ note: "ship it" }]);

    await user.selectOptions(screen.getByTestId("inbox-status-inbox-002"), "Resolved");

    await waitFor(() => {
      const element = screen.getByTestId("inbox-status-inbox-002") as HTMLSelectElement;
      expect(element.value).toBe("Resolved");
    });
  });

  it("renders error state when endpoint fails", async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const url = resolveRequestUrl(input);

      if (url.includes("/api/inbox")) {
        return jsonResponse({ message: "inbox unavailable" }, 500);
      }

      if (url.includes("/api/approvals")) {
        return jsonResponse({ items: [] });
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    renderWithI18n(<InboxApprovalDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("inbox-approval-error")).toBeInTheDocument();
    });

    expect(screen.getByText("inbox unavailable")).toBeInTheDocument();
  });
});
