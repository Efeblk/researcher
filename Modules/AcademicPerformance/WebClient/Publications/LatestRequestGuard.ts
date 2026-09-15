export class LatestRequestGuard {
    private latestRequestNumber = 0;

    begin() {
        return ++this.latestRequestNumber;
    }

    isCurrent(requestNumber: number) {
        return requestNumber === this.latestRequestNumber;
    }
}
