import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EditSite } from './edit-site';

describe('EditSite', () => {
  let component: EditSite;
  let fixture: ComponentFixture<EditSite>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EditSite]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EditSite);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
