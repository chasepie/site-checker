import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SiteCheckDetails } from './site-check-details';

describe('SiteCheckDetails', () => {
  let component: SiteCheckDetails;
  let fixture: ComponentFixture<SiteCheckDetails>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteCheckDetails]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SiteCheckDetails);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
