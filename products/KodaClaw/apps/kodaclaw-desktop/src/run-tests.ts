import assert from "node:assert/strict";
import {
  resolveInitialLaunchTarget,
  resolveLaunchTargetFromArgv,
  resolveLaunchTargetFromProtocolUrl,
  resolveLaunchTargetFromRoute,
} from "./launch-targets";
import {
  buildNotificationCandidates,
  filterUnseenCandidates,
  isWithinQuietHours,
} from "./notification-policy";

type TestCase = {
  name: string;
  run: () => void;
};

const tests: TestCase[] = [
  {
    name: "route target mapping",
    run: () => {
      assert.deepEqual(resolveLaunchTargetFromRoute("/channels/binding-01", "route"), {
        desk: "channels",
        entityId: "binding-01",
        route: "/channels/binding-01",
        reason: "route",
      });

      assert.deepEqual(resolveLaunchTargetFromRoute("/settings", "route"), {
        desk: "models",
        entityId: undefined,
        route: "/settings",
        reason: "route",
      });
    },
  },
  {
    name: "protocol target parsing",
    run: () => {
      assert.deepEqual(
        resolveLaunchTargetFromProtocolUrl("kodaclaw://open?desk=canvas&entityId=artifact-7&route=/canvas/artifact-7"),
        {
          desk: "canvas",
          entityId: "artifact-7",
          route: "/canvas/artifact-7",
          reason: "protocol",
        },
      );
    },
  },
  {
    name: "argv and env initial target precedence",
    run: () => {
      assert.deepEqual(
        resolveLaunchTargetFromArgv([
          "--ignored",
          "--kodaclaw-target={\"desk\":\"inbox\",\"route\":\"/inbox\",\"reason\":\"tray\"}",
        ]),
        {
          desk: "inbox",
          entityId: undefined,
          route: "/inbox",
          reason: "tray",
        },
      );

      assert.deepEqual(
        resolveInitialLaunchTarget({
          initialTargetJson: "{\"desk\":\"plugins\",\"route\":\"/plugins/example\"}",
          argv: ["--kodaclaw-route=/channels/binding-01"],
        }),
        {
          desk: "plugins",
          entityId: undefined,
          route: "/plugins/example",
          reason: "env",
        },
      );
    },
  },
  {
    name: "quiet hours policy",
    run: () => {
      const settings = {
        notificationsEnabled: true,
        quietHoursEnabled: true,
        quietHoursStartLocalTime: "22:00",
        quietHoursEndLocalTime: "06:30",
      };

      assert.equal(isWithinQuietHours(settings, new Date("2026-03-19T23:15:00")), true);
      assert.equal(isWithinQuietHours(settings, new Date("2026-03-19T05:45:00")), true);
      assert.equal(isWithinQuietHours(settings, new Date("2026-03-19T13:00:00")), false);
    },
  },
  {
    name: "notification candidate dedupe",
    run: () => {
      const candidates = buildNotificationCandidates(
        [
          {
            id: "approval-1",
            title: "Need approval",
            summary: "Please approve this action",
            updatedAt: "2026-03-19T10:00:00Z",
            inboxItemId: "inbox-1",
          },
        ],
        [
          {
            id: "inbox-1",
            title: "Approval inbox entry",
            summary: "Same underlying approval",
            updatedAt: "2026-03-19T10:00:00Z",
            route: "/inbox",
          },
          {
            id: "inbox-2",
            title: "Automation finished",
            summary: "Daily digest is ready",
            updatedAt: "2026-03-19T10:05:00Z",
            route: "/automations/digest-1",
          },
        ],
      );

      assert.equal(candidates.length, 2);
      assert.equal(candidates[0].id, "approval:approval-1");
      assert.equal(candidates[1].id, "inbox:inbox-2");
      assert.equal(candidates[1].target.desk, "automations");

      const unseen = filterUnseenCandidates(candidates, new Set([candidates[0].signature]));
      assert.equal(unseen.length, 1);
      assert.equal(unseen[0].id, "inbox:inbox-2");
    },
  },
];

let failed = false;

for (const test of tests) {
  try {
    test.run();
    console.log(`PASS ${test.name}`);
  } catch (error) {
    failed = true;
    console.error(`FAIL ${test.name}`);
    console.error(error);
  }
}

if (failed) {
  process.exitCode = 1;
}
