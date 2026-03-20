import { expect, test } from "@playwright/test";

test("KC-0109 bootstrap flow: onboarding can be committed and returns to main mode", async ({
  page,
}) => {
  let bootstrapCompleted = false;
  let draftPayload: Record<string, unknown> | null = null;
  let completionPayload: Record<string, unknown> | null = null;

  await page.route("**/api/system/health", async (route) => {
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

  await page.route("**/api/system/bootstrap-state", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: !bootstrapCompleted,
        activeMainSessionId: bootstrapCompleted ? "main-001" : null,
        mode: bootstrapCompleted ? "Normal" : "Bootstrap",
      }),
    });
  });

  await page.route("**/api/system/bootstrap-complete", async (route) => {
    completionPayload = route.request().postDataJSON() as Record<string, unknown>;
    bootstrapCompleted = true;

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw",
        bootstrapCompleted: true,
        identityFilePath: "/tmp/.kodaclaw/workspace/IDENTITY.md",
        soulFilePath: "/tmp/.kodaclaw/workspace/SOUL.md",
        userFilePath: "/tmp/.kodaclaw/workspace/USER.md",
        bootstrapFileArchived: true,
      }),
    });
  });

  await page.route("**/api/chat/stream", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body: [
        `event: text_chunk\ndata: ${JSON.stringify({ type: "text_chunk", sessionId: "main-bootstrap", delta: "我会优先保护你的本地数据边界。" })}`,
        `event: done\ndata: ${JSON.stringify({ type: "done", sessionId: "main-bootstrap", reason: "completed" })}`,
        "",
      ].join("\n\n"),
    });
  });

  await page.route("**/api/system/bootstrap-draft", async (route) => {
    draftPayload = route.request().postDataJSON() as Record<string, unknown>;

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        identityMarkdown: "# Koda Identity\n\n- Name: Koda",
        soulMarkdown: "# Koda Soul\n\n- Rule: protect trust",
        userMarkdown: "# User Profile\n\n- Boundaries: direct",
        summary: "Captured identity, soul, and user boundaries from the onboarding conversation.",
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await expect(page.getByText("引导编排")).toBeVisible();
  await page.getByTestId("chat-input").fill("我是你的长期用户，希望沟通直接并保护隐私。");
  await page.getByRole("button", { name: "发送给 Koda" }).click();
  await expect(page.getByText("我会优先保护你的本地数据边界。")).toBeVisible();

  await page.getByTestId("bootstrap-generate-draft").click();
  await expect(page.getByTestId("bootstrap-draft-summary")).toContainText("Captured identity, soul, and user boundaries");

  await expect.poll(() => draftPayload).not.toBeNull();
  await page.getByTestId("bootstrap-submit").click();

  await expect.poll(() => completionPayload).not.toBeNull();
  expect(draftPayload).toEqual({
    conversation: [
      { role: "user", text: "我是你的长期用户，希望沟通直接并保护隐私。" },
      { role: "assistant", text: "我会优先保护你的本地数据边界。" },
    ],
    identityMarkdown: expect.any(String),
    soulMarkdown: expect.any(String),
    userMarkdown: expect.any(String),
  });
  expect(completionPayload).toEqual({
    identityMarkdown: "# Koda Identity\n\n- Name: Koda",
    soulMarkdown: "# Koda Soul\n\n- Rule: protect trust",
    userMarkdown: "# User Profile\n\n- Boundaries: direct",
    archiveBootstrapFile: true,
  });

  await expect(page.getByText("主控对话已激活")).toBeVisible();
  await expect(page.getByText("引导已归档")).toBeVisible();
});
