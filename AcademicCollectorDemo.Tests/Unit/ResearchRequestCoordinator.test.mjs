import assert from "node:assert/strict";
import test from "node:test";
import {
    PublicationRefreshPoller,
    ResearchRequestCoordinator,
    updateSelectionTarget
} from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearchRequestCoordinator.ts";
import { LatestRequestGuard } from "../../Modules/AcademicPerformance/WebClient/Publications/LatestRequestGuard.ts";

test("starting a new research run aborts and invalidates the previous run", () => {
    const coordinator = new ResearchRequestCoordinator();
    const first = coordinator.begin();
    const second = coordinator.begin();

    assert.equal(first.signal.aborted, true);
    assert.equal(coordinator.isCurrent(first), false);
    assert.equal(second.signal.aborted, false);
    assert.equal(coordinator.isCurrent(second), true);
});

test("cancelling a run prevents its late result from becoming current", () => {
    const coordinator = new ResearchRequestCoordinator();
    const run = coordinator.begin();

    coordinator.cancel();

    assert.equal(run.signal.aborted, true);
    assert.equal(coordinator.isCurrent(run), false);
});

test("requerying the same researcher still creates a new request generation", () => {
    const coordinator = new ResearchRequestCoordinator();
    const first = coordinator.begin();
    coordinator.complete(first);

    const second = coordinator.begin();

    assert.notEqual(first.id, second.id);
    assert.equal(coordinator.isCurrent(first), false);
    assert.equal(coordinator.isCurrent(second), true);
});

test("publication polling refreshes immediately, repeats, and stops cleanly", () => {
    const callbacks = [];
    const clearedHandles = [];
    let refreshCount = 0;
    const poller = new PublicationRefreshPoller(
        () => refreshCount++,
        1250,
        (callback, milliseconds) => {
            callbacks.push({ callback, milliseconds });
            return 17;
        },
        handle => clearedHandles.push(handle));

    poller.start();
    assert.equal(refreshCount, 1);
    assert.equal(callbacks.length, 1);
    assert.equal(callbacks[0].milliseconds, 1250);

    callbacks[0].callback();
    assert.equal(refreshCount, 2);

    poller.stop();
    poller.stop();
    assert.deepEqual(clearedHandles, [17]);
});

test("publication polling calls browser timers with their required receiver", () => {
    const originalSetInterval = globalThis.setInterval;
    const originalClearInterval = globalThis.clearInterval;
    const timerHandle = 23;
    const browserTimers = {
        setInterval(callback, milliseconds) {
            assert.equal(this, globalThis);
            assert.equal(milliseconds, 1500);
            return timerHandle;
        },
        clearInterval(handle) {
            assert.equal(this, globalThis);
            assert.equal(handle, timerHandle);
        }
    };

    globalThis.setInterval = browserTimers.setInterval;
    globalThis.clearInterval = browserTimers.clearInterval;

    try {
        const poller = new PublicationRefreshPoller(() => undefined);
        poller.start();
        poller.stop();
    }
    finally {
        globalThis.setInterval = originalSetInterval;
        globalThis.clearInterval = originalClearInterval;
    }
});

test("only the latest publication request may publish results, errors, or cleanup", () => {
    const guard = new LatestRequestGuard();
    const oldRequest = guard.begin();
    const forcedFinalRefresh = guard.begin();

    assert.equal(guard.isCurrent(oldRequest), false);
    assert.equal(guard.isCurrent(forcedFinalRefresh), true);
});

test("provider-only success supplies the personnel id used to load selections", () => {
    const linkedPersonelId = updateSelectionTarget("", true, "P-1001");

    assert.equal(linkedPersonelId, "P-1001");
});

test("a later failed YOKSIS request preserves the provider selection target", () => {
    let linkedPersonelId = updateSelectionTarget("", true, "P-1001");

    linkedPersonelId = updateSelectionTarget(linkedPersonelId, false, undefined);

    assert.equal(linkedPersonelId, "P-1001");
});
