# Notifications are triggered by the Site Check Runner, not the save interceptor

`SiteCheckRunner` calls `NotifierService` directly after it saves a Site Check's outcome, and awaits it. Other reactions to data changes (the SignalR broadcast) hang off the EF save interceptor, but notifications deliberately don't: going through the interceptor meant the Scrape Result had to ride along on an unmapped dictionary on the entity, and "a check finished" was a side effect of saving rather than an explicit call. The runner is the only place a Site Check is completed, so it is the single caller.

## Consequences

- A Site Check completed outside the runner would not notify. Nothing does that; keep it that way.
- Whether a Failing Run has been reported is derived from the Site's check history each time, never stored.
