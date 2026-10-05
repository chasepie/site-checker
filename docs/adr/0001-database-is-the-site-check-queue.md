# The database is the Site Check queue

Pending work is the set of Site Checks in the `Queued` status, and the Site Check Runner claims the oldest one with a conditional update to `Checking`. An in-memory channel exists only to wake the runner early; it holds no state and losing it on restart costs nothing. We chose this over an in-memory queue (or one mirrored to the database) because the app is a single process on SQLite running one check at a time, and keeping a second copy of "what's pending" in sync with the `Status` column is what previously left Sites stuck forever after a restart.

## Consequences

- Restart recovery is one rule: Site Checks left in `Checking` are reset to `Queued` on startup.
- A Site has at most one open (`Queued` or `Checking`) Site Check; requesting another returns the open one.
- Running checks in parallel later means adding workers, not redesigning the queue: the claim is already a conditional update.
