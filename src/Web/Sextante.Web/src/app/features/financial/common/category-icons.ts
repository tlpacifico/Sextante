/**
 * Allowlist de PrimeIcons usados em categorias. Mantém em sincronia com
 * `Sextante.Modules.Financial.Domain.Categories.AllowedCategoryIcons`.
 */
export interface CategoryIcon {
  name: string;
  label: string;
}

export const CATEGORY_ICONS: CategoryIcon[] = [
  { name: 'pi-shopping-cart', label: 'Compras' },
  { name: 'pi-car', label: 'Carro' },
  { name: 'pi-money-bill', label: 'Dinheiro' },
  { name: 'pi-heart', label: 'Saúde' },
  { name: 'pi-home', label: 'Casa' },
  { name: 'pi-book', label: 'Livro' },
  { name: 'pi-briefcase', label: 'Trabalho' },
  { name: 'pi-gift', label: 'Presente' },
  { name: 'pi-globe', label: 'Globo' },
  { name: 'pi-graduation-cap', label: 'Educação' },
  { name: 'pi-coffee', label: 'Café' },
  { name: 'pi-credit-card', label: 'Cartão' },
  { name: 'pi-wallet', label: 'Carteira' },
  { name: 'pi-chart-line', label: 'Gráfico' },
  { name: 'pi-bolt', label: 'Energia' },
  { name: 'pi-phone', label: 'Telefone' },
  { name: 'pi-shield', label: 'Seguro' },
  { name: 'pi-tag', label: 'Etiqueta' },
  { name: 'pi-utensils', label: 'Restauração' },
  { name: 'pi-plane', label: 'Viagem' },
  { name: 'pi-sun', label: 'Sol' },
  { name: 'pi-star', label: 'Estrela' },
  { name: 'pi-cog', label: 'Configuração' },
  { name: 'pi-bookmark', label: 'Marcador' },
];
