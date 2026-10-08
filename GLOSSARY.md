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

**Succeeded**:
A Site Check whose scrape produced content.
_Avoid_: Done, success

**Failed**:
A Site Check whose scrape, or the work around it, did not produce content. It is either a Known Failure or an Unexpected Failure.
_Avoid_: Error

**Completed**:
A Site Check that has finished, whether it Succeeded or Failed.
_Avoid_: Done

**Baseline**:
The content of a Site's latest Succeeded Site Check, which the next one is compared against to decide whether the Site's content changed.
_Avoid_: Previous content, last result

**Baseline Reset**:
A Site Check recorded without a scrape that resets the Site's Baseline to empty, so the next Succeeded Site Check notifies Updated with its content. It never notifies, and it ends any Failing Run without a Recovery.
_Avoid_: Empty Check, blank check, dummy check

**Site Check Runner**:
The single owner of the Site Check lifecycle: deciding which Sites are due, accepting requests for Site Checks, and moving each Site Check through its statuses.
_Avoid_: Check queue, scheduler, processor

## Scraping

**Scraper**:
What a Site runs to get its content. Each Site has exactly one, of one kind: a **Script Scraper**, **Steps Scraper** or **Prompt Scraper**.
_Avoid_: Crawler, parser

**Scrape Result**:
The outcome of running a Scraper: content, a Known Failure with its Requested Actions, or an Unexpected Failure's error, optionally with a screenshot.

**Known Failure**:
A Failed Site Check whose Scraper recognised the state it found (such as access denied or a blank page).
_Avoid_: Expected error

**Unexpected Failure**:
A Failed Site Check its Scraper didn't recognise, such as an error, a timeout or a broken page.
_Avoid_: Unknown failure, crash

**Requested Action**:
Something a Known Failure asks the Site Check Runner to do after recording it: change the VPN Location, or retry.
_Avoid_: Recovery action, follow-up

**Test Run**:
A scrape of a Site's Scraper, usually an unsaved one, run to try it out. It isn't a Site Check: it isn't recorded, never notifies, and carries out no Requested Actions.
_Avoid_: Dry run, preview, test check

**VPN Location**:
The PIA region that VPN-routed Site Checks are scraped from; rotated on an interval.
_Avoid_: Region, server

## Notifications

**Notification Channel**:
A destination that notifications are sent to (Pushover, Discord), enabled and configured per Site and per outcome.
_Avoid_: Notifier, provider, target

**Failing Run**:
The consecutive Failed Site Checks of a Site between two Succeeded Site Checks, in the order they Completed. A Failing Run should be reported on its first Unexpected Failure, or when its Known Failures reach the Known Failure Threshold; it is reported once a notification about it actually reaches a Notification Channel, and only once.
_Avoid_: Outage, failure streak, incident

**Known Failure Threshold**:
The number of Known Failures within a Failing Run at which the run is reported.
_Avoid_: Retry limit, failure limit

**Recovery**:
A Succeeded Site Check that ends a Failing Run. It is reported only if its Failing Run was.
_Avoid_: Resolved, back online
