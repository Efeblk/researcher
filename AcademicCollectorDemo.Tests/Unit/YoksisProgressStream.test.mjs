import assert from "node:assert/strict";
import test from "node:test";
import { collectYoksisStream, getYoksisConnectionSilenceSeconds } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/YoksisProgressStream.ts";

function streamedResponse(chunks) {
    const encoder = new TextEncoder();
    return new Response(new ReadableStream({
        start(controller) {
            for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
            controller.close();
        }
    }), { status: 200 });
}

test("YOKSIS stream parses split NDJSON chunks and returns terminal result", async () => {
    const events = [];
    const result = await collectYoksisStream({}, new AbortController().signal,
        event => events.push(event), async () => streamedResponse([
            '{"Type":"progress","Message":"first"}\n{"Type":"res',
            'ult","Result":{"IsSaved":true}}\n'
        ]));
    assert.equal(events.length, 2);
    assert.equal(result.IsSaved, true);
});

test("YOKSIS stream rejects an EOF without a terminal result", async () => {
    await assert.rejects(() => collectYoksisStream({}, new AbortController().signal,
        () => undefined, async () => streamedResponse(['{"Type":"heartbeat"}\n'])),
        /sonuç alınmadan/);
});

test("YOKSIS stream surfaces a safe terminal error", async () => {
    await assert.rejects(() => collectYoksisStream({}, new AbortController().signal,
        () => undefined, async () => streamedResponse([
            '{"Type":"error","Message":"Güvenli hata"}\n'
        ])), /Güvenli hata/);
});

test("YOKSIS connection silence warns only after twelve seconds", () => {
    assert.equal(getYoksisConnectionSilenceSeconds(1_000, 12_999), 0);
    assert.equal(getYoksisConnectionSilenceSeconds(1_000, 13_000), 12);
});

test("YOKSIS stream resolves and cancels the reader as soon as result arrives", async () => {
    const encoder = new TextEncoder();
    let cancelled = false;
    const response = new Response(new ReadableStream({
        start(controller) {
            controller.enqueue(encoder.encode('{"Type":"result","Result":{"IsSaved":true}}\n'));
        },
        cancel() { cancelled = true; }
    }));

    const result = await collectYoksisStream({}, new AbortController().signal,
        () => undefined, async () => response);

    assert.equal(result.IsSaved, true);
    assert.equal(cancelled, true);
});

test("YOKSIS stream rejects a terminal result without payload without waiting for EOF", async () => {
    const encoder = new TextEncoder();
    const response = new Response(new ReadableStream({
        start(controller) {
            controller.enqueue(encoder.encode('{"Type":"result"}\n'));
        }
    }));

    await assert.rejects(() => collectYoksisStream({}, new AbortController().signal,
        () => undefined, async () => response), /geçerli bir sonuç/);
});
