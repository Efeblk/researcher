interface ProviderIdentifierInput {
    dataset: { providerIdentifier?: string };
    value: string;
}

export function restoreProviderIdentifiers(
    inputs: ProviderIdentifierInput[], storedValue: string | null): number {
    const identifiers = readRememberedProviderIdentifiers(storedValue);

    let filledCount = 0;
    for (const input of inputs) {
        const name = input.dataset.providerIdentifier;
        input.value = name && Object.hasOwn(identifiers, name) ? identifiers[name] : "";
        if (input.value)
            filledCount++;
    }
    return filledCount;
}

export function readRememberedProviderIdentifiers(storedValue: string | null) {
    let identifiers: Record<string, unknown> = {};
    try {
        const parsed: unknown = storedValue ? JSON.parse(storedValue) : null;
        if (parsed && typeof parsed === "object" && !Array.isArray(parsed))
            identifiers = parsed as Record<string, unknown>;
    }
    catch {
        // Invalid or obsolete storage behaves like an empty saved profile.
    }

    const remembered: Record<string, string> = {};
    for (const [name, value] of Object.entries(identifiers)) {
        if (typeof value === "string" && value.trim())
            remembered[name] = value.trim();
    }
    return remembered;
}
