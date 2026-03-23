import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SiteList } from './site-list';

describe('SiteList', () => {
  let component: SiteList;
  let fixture: ComponentFixture<SiteList>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SiteList]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SiteList);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
