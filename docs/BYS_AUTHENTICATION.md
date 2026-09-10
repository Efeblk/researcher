# BYS authentication contract

The academic-performance WebClient obtains the current personnel key only from
the authenticated `HttpContext.User`. The BYS host must add exactly one
`PersonelID` claim to an authenticated identity before mapping the application
endpoints. The value must be non-empty after trimming and at most 200 characters.

The WebClient rejects anonymous requests, missing or empty claims, overlong
claims, and conflicting `PersonelID` values across authenticated identities.
Claims on unauthenticated identities are ignored. It does not fall back to the
user name, name identifier, HTTP headers, browser storage, or a generated value.

The standalone development host intentionally does not configure authentication
or create a fake user session. A production BYS integration must register its
authentication handler and call `UseAuthentication()` and `UseAuthorization()`
at the appropriate points in the ASP.NET Core pipeline. The existing versioned
V1 and bulk APIs continue to accept explicit personnel IDs for trusted external
and background workflows; their authorization remains the host's responsibility.
