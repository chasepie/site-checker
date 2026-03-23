import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SiteDetails } from './site-details';

describe('SiteDetails', () => {
  let component: SiteDetails;
  let fixture: ComponentFixture<SiteDetails>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteDetails]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SiteDetails);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
