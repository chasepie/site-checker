import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgbDropdownModule } from '@ng-bootstrap/ng-bootstrap';
import { AgGridAngular } from 'ag-grid-angular';
import { ClientSideRowModelModule, ColumnAutoSizeModule, GridOptions, ModuleRegistry, ValidationModule } from 'ag-grid-community';
import { VpnLocationEntry, VpnStore } from '../../services/vpn.store';

// Register all Community features
ModuleRegistry.registerModules([
  ClientSideRowModelModule,
  ValidationModule,
  ColumnAutoSizeModule,
]);

@Component({
  selector: 'app-vpn',
  imports: [AgGridAngular, NgbDropdownModule],
  templateUrl: './vpn.html',
  styleUrl: './vpn.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Vpn {
  private readonly _vpnStore = inject(VpnStore);
  protected readonly rowData = this._vpnStore.entities;

  protected readonly gridOptions: GridOptions<VpnLocationEntry> = {
    columnDefs: [
      {
        field: 'name',
        sort: 'asc',
        sortIndex: 1
      },
      { field: 'id' },
      {
        field: 'excluded',
        sort: 'desc',
        sortIndex: 0
      },
    ],
    onFirstDataRendered: (event) => {
      event.api.sizeColumnsToFit();
    },
  };

  protected readonly currentLocation = this._vpnStore.currentLocation;

  protected readonly isChanging = signal(false);

  public changeLocation(excludeCurrent: boolean = false): void {
    this.isChanging.set(true);
    void this._vpnStore.changeLocation(excludeCurrent)
      .finally(() => {
        this.isChanging.set(false);
      });
  }
}
