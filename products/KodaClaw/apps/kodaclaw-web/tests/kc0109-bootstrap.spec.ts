import { expect, test } from "@playwright/test";

test("KC-0109 bootstrap flow: onboarding can be committed and returns to main mode", async ({
  page,
}) => {
  let bootstrapCompleted = false;
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
        userFilePath: "/tmp/.kodaclaw/workspace/USER.md",
        bootstrapFileArchived: true,
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await expect(page.getByText("引导编排")).toBeVisible();
  await page.getByTestId("bootstrap-identity-input").fill("# Koda Identity\n\n- Name: Koda");
  await page.getByTestId("bootstrap-user-input").fill("# User Profile\n\n- Boundaries: direct");
  await page.getByTestId("bootstrap-submit").click();

  await expect.poll(() => completionPayload).not.toBeNull();
  expect(completionPayload).toEqual({
    identityMarkdown: "# Koda Identity\n\n- Name: Koda",
    userMarkdown: "# User Profile\n\n- Boundaries: direct",
    archiveBootstrapFile: true,
  });

  await expect(page.getByText("主控对话已激活")).toBeVisible();
  await expect(page.getByText("引导已归档")).toBeVisible();
});
