import { describe, expect, it } from "vitest";
import { parseSseFrames } from "../lib/api";

describe("chat stream adapter contract", () => {
  it("parses text_chunk + done SSE sequence", () => {
    const raw = [
      "event: text_chunk",
      'data: {"type":"text_chunk","sessionId":"main-1","delta":"hello"}',
      "",
      "event: done",
      'data: {"type":"done","sessionId":"main-1","reason":"completed"}',
      "",
      "",
    ].join("\n");

    const parsed = parseSseFrames(raw, { flushTrailing: true });

    expect(parsed.frames).toHaveLength(2);
    expect(parsed.frames[0]).toEqual({
      eventName: "text_chunk",
      data: '{"type":"text_chunk","sessionId":"main-1","delta":"hello"}',
    });
    expect(parsed.frames[1]).toEqual({
      eventName: "done",
      data: '{"type":"done","sessionId":"main-1","reason":"completed"}',
    });
  });

  it("preserves the trailing rest buffer for incomplete frames", () => {
    const raw = [
      "event: error",
      'data: {"type":"error","sessionId":"main-unavailable"}',
      "",
      "event: text_chunk",
      'data: {"type":"text_chunk"',
    ].join("\n");

    const parsed = parseSseFrames(raw);

    expect(parsed.frames).toHaveLength(1);
    expect(parsed.rest).toContain('data: {"type":"text_chunk"');
  });
});
