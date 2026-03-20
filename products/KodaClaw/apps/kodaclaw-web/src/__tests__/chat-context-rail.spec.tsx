import "@testing-library/jest-dom";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, beforeEach, vi } from "vitest";
import { I18nProvider } from "../i18n/I18nProvider";
import { ChatContextRail } from "../shell-v2/ChatContextRail";
import { fetchSessions } from "../lib/api";

vi.mock("../lib/api", () => ({
  fetchSessions: vi.fn(),
}));

function renderRail(activeSessionId = "main-001", onOpenSessionDetail = vi.fn()) {
  return render(
    <I18nProvider>
      <ChatContextRail
        activeSessionId={activeSessionId}
        onOpenSessionsDesk={vi.fn()}
        onOpenSessionDetail={onOpenSessionDetail}
      />
    </I18nProvider>,
  );
}

describe("ChatContextRail", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it("shows the active main session and recent traces", async () => {
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "channel-002",
          sessionKind: "ChannelDirectMessage",
          status: {
            isActiveMainSession: false,
            breakpointState: null,
            messageCount: 3,
            pendingApprovalCount: 0,
          },
          createdAt: "2026-03-20T02:00:00Z",
          lastEventAt: "2026-03-20T02:03:00Z",
        },
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: "Ready",
            messageCount: 12,
            pendingApprovalCount: 2,
          },
          createdAt: "2026-03-20T03:00:00Z",
          lastEventAt: "2026-03-20T03:05:00Z",
        },
      ],
    });

    renderRail();

    await waitFor(() => {
      expect(screen.getByText("main-001")).toBeInTheDocument();
    });

    expect(screen.getByTestId("v2-chat-session-main-001")).toHaveTextContent("当前主会话");
    expect(screen.getByTestId("v2-chat-session-main-001")).toHaveTextContent("2 个待审批");
    expect(screen.getByTestId("v2-chat-session-channel-002")).toHaveTextContent("channel-002");
  });

  it("opens the selected session detail when a session card is clicked", async () => {
    const onOpenSessionDetail = vi.fn();
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: "Ready",
            messageCount: 12,
            pendingApprovalCount: 0,
          },
          createdAt: "2026-03-20T03:00:00Z",
          lastEventAt: "2026-03-20T03:05:00Z",
        },
      ],
    });

    renderRail("main-001", onOpenSessionDetail);

    const user = userEvent.setup();
    await user.click(await screen.findByTestId("v2-chat-session-main-001"));

    expect(onOpenSessionDetail).toHaveBeenCalledWith("main-001");
  });

  it("renders a recoverable error state when sessions cannot be loaded", async () => {
    vi.mocked(fetchSessions).mockRejectedValue(new Error("sessions failed"));

    renderRail();

    await waitFor(() => {
      expect(screen.getByText("sessions failed")).toBeInTheDocument();
    });
  });
});
