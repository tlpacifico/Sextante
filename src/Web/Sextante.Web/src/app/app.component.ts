import { Component } from '@angular/core';
import { APP_VERSION } from '../environments/version';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  readonly version = APP_VERSION;
}
