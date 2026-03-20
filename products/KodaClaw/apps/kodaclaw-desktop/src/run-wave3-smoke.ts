import assert from "node:assert/strict";
import http, { type IncomingMessage, type ServerResponse } from "node:http";
import { spawn } from "node:child_process";
import path from "node:path";

type SmokePayload = {
  smokeMode: boolean;
  gatewayStatus: string;
  gatewayLifecycleMode: string;
  gatewayUrl: string;
  appVersion: string;
  releaseChannel: string;
  initialTarget?: {
    desk: string;
    entityId?: string;
    route?: string;
    reason?: string;
  } | null;
  trayReady: boolean;
  shortcutRegistered?: string | null;
  capturedNotifications?: Array<{
    id: string;
    title: string;
    target: {
      desk: string;
      entityId?: string;
      route?: string;
      reason?: string;
    };
  }>;
};

type GatewayFixture = {
  close: () => Promise<void>;
  requests: string[];
  tokenChecks: boolean[];
  url: string;
};

function createGatewayFixture(): Promise<GatewayFixture> {
  const requests: string[] = [];
  const tokenChecks: boolean[] = [];

  const server = http.createServer((request: IncomingMessage, response: ServerResponse) => {
    const url = new URL(request.url ?? "/", "http://127.0.0.1");
    requests.push(url.pathname + url.search);

    const authorization = request.headers.authorization ?? "";
    if (url.pathname !== "/api/system/health") {
      tokenChecks.push(authorization === "Bearer desktop-test-token");
    }

    response.setHeader("Content-Type", "application/json");

    if (url.pathname === "/api/system/health") {
      response.writeHead(200).end(JSON.stringify({
        status: "Healthy",
        appMode: "Normal",
        workspaceRootPath: "/tmp/kodaclaw",
        workspaceVersion: 1,
        activeSessionCount: 0,
        pendingApprovalCount: 1,
        pendingInboxCount: 2,
        diagnosticsBacklogCount: 0,
        pluginCount: 0,
        timestamp: "2026-03-19T00:00:00Z",
      }));
      return;
    }

    if (authorization !== "Bearer desktop-test-token") {
      response.writeHead(401).end(JSON.stringify({ message: "Unauthorized" }));
      return;
    }

    if (url.pathname === "/api/settings") {
      response.writeHead(200).end(JSON.stringify({
        defaultLandingRoute: "/chat",
        theme: "System",
        requireApprovalForExternalActions: true,
        notificationsEnabled: true,
        quietHoursEnabled: false,
        quietHoursStartLocalTime: null,
        quietHoursEndLocalTime: null,
        updatedAt: "2026-03-19T00:00:00Z",
      }));
      return;
    }

    if (url.pathname === "/api/approvals") {
      response.writeHead(200).end(JSON.stringify({
        items: [
          {
            id: "approval-1",
            title: "Channel send requires approval",
            summary: "Review before sending to Telegram",
            updatedAt: "2026-03-19T00:00:00Z",
            inboxItemId: "inbox-1",
          },
        ],
      }));
      return;
    }

    if (url.pathname === "/api/inbox") {
      response.writeHead(200).end(JSON.stringify({
        items: [
          {
            id: "inbox-1",
            title: "Approval inbox entry",
            summary: "Approval-linked inbox item should be deduped",
            updatedAt: "2026-03-19T00:00:00Z",
            route: "/inbox",
          },
          {
            id: "inbox-2",
            title: "Automation finished",
            summary: "Daily digest is ready",
            updatedAt: "2026-03-19T00:05:00Z",
            route: "/automations/digest-1",
          },
        ],
      }));
      return;
    }

    response.writeHead(404).end(JSON.stringify({ message: "Not found" }));
  });

  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (!address || typeof address === "string") {
        reject(new Error("Failed to bind fixture server."));
        return;
      }

      resolve({
        close: () =>
          new Promise<void>((closeResolve, closeReject) => {
            server.close((error) => {
              if (error) {
                closeReject(error);
                return;
              }

              closeResolve();
            });
          }),
        requests,
        tokenChecks,
        url: `http://127.0.0.1:${address.port}`,
      });
    });
  });
}

async function run(): Promise<void> {
  const fixture = await createGatewayFixture();
  const electronBinary = require("electron") as unknown as string;
  const packageRoot = path.resolve(__dirname, "..");

  try {
    const payload = await new Promise<SmokePayload>((resolve, reject) => {
      const child = spawn(
        electronBinary,
        [".", "--kodaclaw-route=/channels/binding-01"],
        {
          cwd: packageRoot,
          env: {
            ...process.env,
            KODACLAW_DESKTOP_SMOKE_MODE: "true",
            KODACLAW_DESKTOP_SMOKE_CAPTURE_NOTIFICATIONS: "true",
            KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE: "AttachOnly",
            KODACLAW_DESKTOP_GATEWAY_URL: fixture.url,
            KODACLAW_DESKTOP_GATEWAY_TOKEN: "desktop-test-token",
            KODACLAW_DESKTOP_NOTIFICATION_POLL_MS: "200",
          },
          stdio: ["ignore", "pipe", "pipe"],
        },
      );

      let stdoutBuffer = "";
      let stderrBuffer = "";

      child.stdout.on("data", (chunk: Buffer) => {
        stdoutBuffer += chunk.toString("utf8");
      });

      child.stderr.on("data", (chunk: Buffer) => {
        stderrBuffer += chunk.toString("utf8");
      });

      child.once("exit", (code) => {
        if (code !== 0) {
          reject(new Error(`Desktop smoke exited with code ${code ?? "unknown"}.\n${stdoutBuffer}\n${stderrBuffer}`));
          return;
        }

        const jsonLine = stdoutBuffer
          .split(/\r?\n/)
          .map((line) => line.trim())
          .filter((line) => line.startsWith("{") && line.endsWith("}"))
          .at(-1);

        if (!jsonLine) {
          reject(new Error(`Desktop smoke did not emit JSON payload.\n${stdoutBuffer}\n${stderrBuffer}`));
          return;
        }

        resolve(JSON.parse(jsonLine) as SmokePayload);
      });
    });

    assert.equal(payload.smokeMode, true);
    assert.equal(payload.gatewayStatus, "healthy");
    assert.equal(payload.gatewayLifecycleMode, "AttachOnly");
    assert.equal(payload.appVersion, "0.1.0");
    assert.equal(payload.releaseChannel, "Stable");
    assert.equal(payload.initialTarget?.desk, "channels");
    assert.equal(payload.initialTarget?.entityId, "binding-01");
    assert.equal(payload.trayReady, true);
    assert.equal(payload.shortcutRegistered, "CommandOrControl+Shift+K");

    const notificationIds = new Set((payload.capturedNotifications ?? []).map((item) => item.id));
    assert.equal(notificationIds.has("approval:approval-1"), true);
    assert.equal(notificationIds.has("inbox:inbox-2"), true);
    assert.equal((payload.capturedNotifications ?? []).length, 2);

    const automationNotification = payload.capturedNotifications?.find((item) => item.id === "inbox:inbox-2");
    assert.equal(automationNotification?.target.desk, "automations");
    assert.equal(automationNotification?.target.entityId, "digest-1");

    assert.equal(fixture.requests.includes("/api/settings"), true);
    assert.equal(fixture.requests.includes("/api/approvals?status=Pending&limit=10"), true);
    assert.equal(fixture.requests.includes("/api/inbox?status=Open&limit=10"), true);
    assert.equal(fixture.tokenChecks.every(Boolean), true);

    console.log("PASS wave3 desktop smoke");
  } finally {
    await fixture.close();
  }
}

void run().catch((error) => {
  console.error("FAIL wave3 desktop smoke");
  console.error(error);
  process.exitCode = 1;
});
