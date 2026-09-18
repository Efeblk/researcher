export function createDoiUrl(value: unknown) {
    if (typeof value !== "string")
        return null;

    let doi = value.trim();
    while (true) {
        const doiPrefix = doi.match(/^doi:\s*/iu);
        if (doiPrefix) {
            doi = doi.slice(doiPrefix[0].length).trim();
            continue;
        }

        if (!/^(?:https?:\/\/)?(?:dx\.)?doi\.org\//iu.test(doi))
            break;

        try {
            const url = new URL(/^https?:\/\//iu.test(doi) ? doi : `https://${doi}`);
            if ((url.protocol !== "http:" && url.protocol !== "https:") ||
                (url.hostname !== "doi.org" && url.hostname !== "dx.doi.org") ||
                url.username || url.password || url.port)
                return null;

            doi = decodeURIComponent(url.pathname.slice(1)).trim();
        }
        catch {
            return null;
        }
    }

    doi = doi.toLowerCase();
    if (doi.length > 500 || !/^10\.\d{4,9}\/\S+$/u.test(doi))
        return null;

    try {
        const encodedPath = doi.split("/")
            .map(segment => encodeURIComponent(segment))
            .join("/");
        return `https://doi.org/${encodedPath}`;
    }
    catch {
        return null;
    }
}
