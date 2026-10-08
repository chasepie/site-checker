import { PiaLocation, SiteCheck, TestRunResult } from '../generated/model';

type RequestedAction = SiteCheck['requestedActions'][number];
type ScrapeOutcome = TestRunResult['outcome'];

export function requestedActionLabel(action: RequestedAction): string {
  switch (action) {
    case 'ChangeVpnLocation':
      return 'Change VPN Location';
    case 'Retry':
      return 'Retry';
  }
}

export function scrapeOutcomeLabel(outcome: ScrapeOutcome): string {
  switch (outcome) {
    case 'Succeeded':
      return 'Succeeded';
    case 'KnownFailure':
      return 'Known Failure';
    case 'UnexpectedFailure':
      return 'Unexpected Failure';
  }
}

/**
 * A Site Check's VPN Location by name, falling back to its ID while the locations are unknown.
 */
export function vpnLocationLabel(
  locationId: string | null,
  locations: Partial<Record<string, PiaLocation>>,
): string {
  if (locationId == null) {
    return 'No VPN';
  }
  return locations[locationId]?.name ?? locationId;
}
