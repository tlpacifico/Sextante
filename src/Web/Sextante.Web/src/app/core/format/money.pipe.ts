import { Pipe, PipeTransform } from '@angular/core';
import { Money } from '../api/financial.types';

/**
 * Formata um valor `Money` em PT-PT usando `Intl.NumberFormat`. Aceita um
 * `Money` completo, um número (assume moeda passada via parâmetro), ou
 * `null`/`undefined` (devolve string vazia).
 */
@Pipe({ name: 'money', standalone: true })
export class MoneyPipe implements PipeTransform {
  transform(value: Money | number | null | undefined, currency = 'EUR'): string {
    if (value === null || value === undefined) {
      return '';
    }

    const amount = typeof value === 'number' ? value : value.amount;
    const cur = typeof value === 'number' ? currency : value.currency;

    return new Intl.NumberFormat('pt-PT', {
      style: 'currency',
      currency: cur,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  }
}
