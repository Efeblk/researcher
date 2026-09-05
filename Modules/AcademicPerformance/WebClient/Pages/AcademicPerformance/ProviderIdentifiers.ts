interface ProviderIdentifierInput {
    dataset: { providerIdentifier?: string };
    value: string;
}

export function restoreProviderIdentifiers(
    inputs: ProviderIdentifierInput[], storedValue: string | null): number {
    let identifiers: Record<string, unknown> = {};
    try {
        const parsed: unknown = storedValue ? JSON.parse(storedValue) : null;
        if (parsed && typeof parsed === "object" && !Array.isArray(parsed))
            identifiers = parsed as Record<string, unknown>;
    }
    catch {
        // Invalid or obsolete storage behaves like an empty saved profile.
    }

    let filledCount = 0;
    for (const input of inputs) {
        const name = input.dataset.providerIdentifier;
        const value = name && Object.hasOwn(identifiers, name) ? identifiers[name] : null;
        input.value = typeof value === "string" ? value.trim() : "";
        if (input.value)
            filledCount++;
    }
    return filledCount;
}
