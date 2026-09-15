export interface ResearchRequestRun {
    readonly id: number;
    readonly signal: AbortSignal;
}

export class ResearchRequestCoordinator {
    private activeController: AbortController | null = null;
    private activeRunId = 0;

    begin(): ResearchRequestRun {
        this.activeController?.abort();
        this.activeController = new AbortController();
        this.activeRunId++;

        return {
            id: this.activeRunId,
            signal: this.activeController.signal
        };
    }

    cancel() {
        this.activeController?.abort();
        this.activeController = null;
        this.activeRunId++;
    }

    complete(run: ResearchRequestRun) {
        if (!this.isCurrent(run))
            return;

        this.activeController = null;
    }

    isCurrent(run: ResearchRequestRun) {
        return run.id === this.activeRunId && !run.signal.aborted;
    }
}

export function updateSelectionTarget(
    currentPersonelId: string,
    isSaved: boolean | undefined,
    savedPersonelId: string | undefined) {
    return isSaved && savedPersonelId ? savedPersonelId : currentPersonelId;
}

type IntervalHandle = ReturnType<typeof setInterval>;

export class PublicationRefreshPoller {
    private intervalHandle: IntervalHandle | null = null;
    private readonly intervalMilliseconds: number;
    private readonly refresh: () => void;
    private readonly schedule: (
        callback: () => void,
        milliseconds: number) => IntervalHandle;
    private readonly unschedule: (handle: IntervalHandle) => void;

    constructor(
        refresh: () => void,
        intervalMilliseconds = 1500,
        schedule: (
            callback: () => void,
            milliseconds: number) => IntervalHandle =
                (callback, milliseconds) => globalThis.setInterval(callback, milliseconds),
        unschedule: (handle: IntervalHandle) => void =
            handle => globalThis.clearInterval(handle)) {
        this.refresh = refresh;
        this.intervalMilliseconds = intervalMilliseconds;
        this.schedule = schedule;
        this.unschedule = unschedule;
    }

    start() {
        this.stop();
        this.refresh();
        this.intervalHandle = this.schedule(
            this.refresh,
            this.intervalMilliseconds);
    }

    stop() {
        if (this.intervalHandle === null)
            return;

        this.unschedule(this.intervalHandle);
        this.intervalHandle = null;
    }
}
