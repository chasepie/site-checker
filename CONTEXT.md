# Site Checker

Self-hosted monitoring that periodically scrapes websites, records what it found, and notifies when content changes, a Site starts failing, or it recovers.

## Sites and checks

**Site**:
A web page being monitored, with its own Scraper, Schedule, and notification settings.
_Avoid_: Target, page, monitor

**Schedule**:
A Site's daily time window (in local time) and the interval in minutes between checks inside that window.
_Avoid_: Cron, timer

**Site Check**:
One attempt to scrape a Site, recorded with its status, content, and timing.
_Avoid_: Check run, scrape, job

**Due**:
A Site is due when it is inside its Schedule window, has no open Site Check, and its last Site Check started at least one interval ago.

**Open**:
A Site Check that is Queued or Checking. A Site has at most one open Site Check at a time.
_Avoid_: Pending, in-progress, active

**Queued**:
A Site Check waiting to be run. It is the status every Site Check starts with.
_Avoid_: Created, pending

**Checking**:
A Site Check whose scrape is in progress.
_Avoid_: Running, processing

**Done**:
A Site Check whose scrape succeeded and produced content.
_Avoid_: Success, complete

**Failed**:
A Site Check whose scrape, or the work around it, did not produce content.
_Avoid_: Error

**Empty Check**:
A Site Check recorded as Done with placeholder content and no scrape, used to reset the baseline the next Site Check is compared against. It never notifies, and it ends any Failing Run without a Recovery.
_Avoid_: Blank check, dummy check

**Site Check Runner**:
The single owner of the Site Check lifecycle: deciding which Sites are due, accepting requests for Site Checks, and moving each Site Check through its statuses.
_Avoid_: Check queue, scheduler, processor

## Scraping

**Scraper**:
Site-specific logic that extracts content from a page; each Site names exactly one Scraper.
_Avoid_: Crawler, parser

**Scrape Result**:
The outcome of running a Scraper: either content, or an error message, optionally with a screenshot.

**Known Failure**:
A Failed Site Check caused by a recognised condition (such as access denied or a blank page) rather than an unexpected error.
_Avoid_: Expected error

**VPN Location**:
The PIA region that VPN-routed Site Checks are scraped from; rotated on an interval.
_Avoid_: Region, server

## Notifications

**Notification Channel**:
A destination that notifications are sent to (Pushover, Discord), enabled and configured per Site and per outcome.
_Avoid_: Notifier, provider, target

**Failing Run**:
The consecutive Failed Site Checks of a Site between two Done Site Checks, in the order they finished. A Failing Run should be reported on its first unexpected failure, or when its Known Failures reach the Known Failure Threshold; it is reported once a notification about it actually reaches a Notification Channel, and only once.
_Avoid_: Outage, failure streak, incident

**Known Failure Threshold**:
The number of Known Failures within a Failing Run at which the run is reported.
_Avoid_: Retry limit, failure limit

**Recovery**:
A Done Site Check that ends a Failing Run. It is reported only if its Failing Run was.
_Avoid_: Resolved, back online
