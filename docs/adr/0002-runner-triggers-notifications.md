# Notifications are triggered by the Site Check Runner, not the save interceptor

`SiteCheckRunner` calls `NotifierService` directly after it saves a Site Check's outcome, and awaits it. Other reactions to data changes (the SignalR broadcast) hang off the EF save interceptor, but notifications deliberately don't: going through the interceptor meant the Scrape Result had to ride along on an unmapped dictionary on the entity, and "a check finished" was a side effect of saving rather than an explicit call. The runner is the only place a Site Check is completed, so it is the single caller.

## Consequences

- A Site Check completed outside the runner would not notify. Nothing does that; keep it that way.
- Whether a Failing Run has been reported is stored, as `SiteCheck.ReportedAt` on the check whose Failing notification reached at least one channel. Deriving it from history was tried first and rejected: a failed send then counted as reported (silencing the outage and producing an unexplained Recovery), and changing the Known Failure Threshold mid-run rewrote which runs had been reported. The threshold only decides when an unreported run should be reported; until a notification is delivered, each failure in the run tries again.
- History is ordered by when Site Checks completed (`CompletedDate`, then `Id`), not by `Id`, because a Baseline Reset can be recorded while an older check is still open.
