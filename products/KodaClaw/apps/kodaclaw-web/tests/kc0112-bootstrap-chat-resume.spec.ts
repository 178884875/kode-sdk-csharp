import { expect, test } from "@playwright/test";

test("KC-0112 acceptance: bootstrap, chat, reopen, and resume main session", async ({
  page,
}) => {
  const context = page.context();
  let bootstrapCompleted = false;
  let activeMainSessionId: string | null = null;

  await context.route("**/api/system/health", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        name: "KodaClaw Gateway",
        status: "healthy",
        mode: bootstrapCompleted ? "Normal" : "Bootstrap",
      }),
    });
  });

  await context.route("**/api/system/bootstrap-state", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw-acceptance",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: !bootstrapCompleted,
        activeMainSessionId,
        mode: bootstrapCompleted ? "Normal" : "Bootstrap",
      }),
    });
  });

  await context.route("**/api/system/bootstrap-complete", async (route) => {
    bootstrapCompleted = true;

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw-acceptance",
        bootstrapCompleted: true,
        identityFilePath: "/tmp/.kodaclaw-acceptance/workspace/IDENTITY.md",
        soulFilePath: "/tmp/.kodaclaw-acceptance/workspace/SOUL.md",
        userFilePath: "/tmp/.kodaclaw-acceptance/workspace/USER.md",
        bootstrapFileArchived: true,
      }),
    });
  });

  await context.route("**/api/chat/stream", async (route) => {
    const payload = route.request().postDataJSON() as { message?: string };
    const message = payload.message?.trim() ?? "";

    if (!activeMainSessionId) {
      activeMainSessionId = "main-acceptance-001";
    }

    const assistantDelta = `stub:${message}`;
    await route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body: [
        `event: text_chunk\ndata: ${JSON.stringify({ type: "text_chunk", sessionId: activeMainSessionId, delta: assistantDelta })}`,
        `event: done\ndata: ${JSON.stringify({ type: "done", sessionId: activeMainSessionId, reason: "completed" })}`,
        "",
      ].join("\n\n"),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await expect(page.getByText("引导编排")).toBeVisible();
  await page.getByTestId("bootstrap-identity-input").fill("# Koda Identity\n\n- Name: Koda");
  await page.getByTestId("bootstrap-soul-input").fill("# Koda Soul\n\n- Rule: protect trust");
  await page.getByTestId("bootstrap-user-input").fill("# User Profile\n\n- Boundaries: direct");
  await page.getByTestId("bootstrap-submit").click();

  await expect(page.getByText("主控对话已激活")).toBeVisible();

  await page.getByTestId("chat-input").fill("first turn");
  await page.getByRole("button", { name: "发送给 Koda" }).click();
  await expect(page.getByText("stub:first turn")).toBeVisible();

  await page.close();
  const reopenedPage = await context.newPage();
  await reopenedPage.goto("/", { waitUntil: "domcontentloaded" });

  await expect(reopenedPage.getByText("主控对话已激活")).toBeVisible();
  await expect(reopenedPage.getByText("当前主会话：main-acceptance-001")).toBeVisible();

  await reopenedPage.getByTestId("chat-input").fill("second turn");
  await reopenedPage.getByRole("button", { name: "发送给 Koda" }).click();
  await expect(reopenedPage.getByText("stub:second turn")).toBeVisible();
});
