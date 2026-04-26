# Sextante.Web

Frontend Angular do Sextante. Phase 1b moveu a stack para
**Angular 21 LTS + PrimeNG (preset Aura) + Tailwind CSS + Signals**.
Decisões e alternativas discutidas ficam documentadas em
[`docs/adr/ADR-011-frontend-ui-stack.md`](../../../docs/adr/ADR-011-frontend-ui-stack.md).

## Regra de ouro de styling

> **Tailwind para layout / spacing / responsividade; PrimeNG para
> componentes interativos; nunca os dois a estilizar o mesmo elemento.**

`tech-stack.md` §19.2. Tradução prática:

- Wrappers, grids, spacing, breakpoints, typography utilities ⇒ Tailwind
  (`flex`, `grid`, `gap-4`, `md:p-6`, `text-sm`).
- Botões, inputs, dialogs, menus, toasts, tabelas, charts ⇒ PrimeNG
  (`p-button`, `p-inputText`, `p-dialog`, `p-menubar`, `p-toast`,
  `p-treetable`, `p-chart`).
- **Nunca** redefinir cores ou bordas de um componente PrimeNG via
  Tailwind utilities. Usar tokens Aura (`--p-primary-500`,
  `--p-surface-700`) ou customizar o preset via `definePreset(...)`.

## Dark mode

`ThemeService` (`app/core/theme.service.ts`) mantém um `signal<boolean>
isDark` persistido em `localStorage` (`sextante.theme`). Toggle
adiciona / remove a classe `.dark` em `document.documentElement`.
A inicialização segue: `localStorage` → `prefers-color-scheme: dark`
→ `light`.

A classe `.dark` é resolvida pelo Aura preset (`darkModeSelector:
'.dark'` em `app.config.ts`) e por Tailwind (`darkMode: 'class'` em
`tailwind.config.js`).

## Inner-loop dev (npm start)

Para iterar sobre o frontend sem ter de re-buildar o Host a cada
alteração, correr Angular dev server e Host em paralelo:

```bash
# Terminal 1 — backend
docker compose up postgres -d
cd src/Bootstrap/Sextante.Host
dotnet run

# Terminal 2 — frontend
cd src/Web/Sextante.Web
npm start  # http://localhost:4200/
```

A `proxy.conf.json` (a adicionar quando necessário) mapeia
`/api/*` para o Host. Em desenvolvimento o cookie `refresh_token`
é emitido com `Secure=false` (porque `IsHttps=false` em `localhost`);
em produção mantém-se sempre `Secure=true`.

Para builds de produção embutidos no Host (single-binary):

```bash
dotnet build -c Release    # dispara `npm run build` + copia para wwwroot/
```

A flag MSBuild `-p:SkipAngularBuild=true` salta o passo do `npm run
build` (CI usa-a no job de testes para evitar instalar Node).

## Testing

```bash
npm test -- --watch=false --browsers=ChromeHeadless
```

CI corre o mesmo comando.

## Build

```bash
npm run build -- --configuration=production
```

Output vai para `../../Bootstrap/Sextante.Host/wwwroot/` (configurado em
`angular.json`).

## Component scaffolding

Standalone components only — sem `NgModule` em código novo. Reactive
Forms only para forms de domínio (`FormsModule` / `ngModel` proibido).

```bash
ng generate component features/<feature>/<name> --standalone --change-detection=OnPush
```
