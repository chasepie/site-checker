import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { SignalRConstants } from '../generated/model';
import { SignalrService } from '../services/signalr.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const signalr = inject(SignalrService);
  if (!signalr.connectionId) {
    return next(req);
  }

  const headerName = SignalRConstants.UserID as string;
  const newReq = req.clone({
    headers: req.headers.set(headerName, signalr.connectionId)
  });

  return next(newReq);
};
