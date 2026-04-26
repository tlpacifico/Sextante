# ADR-011 — Frontend UI stack (Angular 21 + PrimeNG Aura + Tailwind + Signals)

- **Status**: Accepted
- **Data**: 2026-04-26
- **Phase**: 1b (Auth UI)
- **Refs cruzadas**: `specs/tech-stack.md` §17, §19; `specs/replanning_2026_04_26.md`;
  `specs/2026-04-26-phase-1b-auth-ui/{plan,requirements}.md`

## Contexto

Phase 0 entregou um Angular 19.2.0 mínimo (uma landing page) em
`src/Web/Sextante.Web/`. Phase 1b introduz a **primeira UI real** —
quatro páginas de auth, app shell autenticada, dark mode — e fixa, em
código, a stack que vai sustentar **todos** os dashboards (Phase 2,
Phase 3, Phase 4, Phase 5, ...). As escolhas tomadas aqui ficam de pé
até pós-MVP — qualquer reescrita do frontend posterior tem o custo de
refactor multi-phase.

Decisões em aberto à entrada da Phase 1b:

1. **Versão do Angular**: ficar em 19, ou subir já para 21 LTS?
2. **UI library**: PrimeNG vs Material vs PrimeUIX vs roll-our-own?
3. **CSS framework**: Tailwind vs estilização ad-hoc por componente?
4. **State management**: Signals vs NgRx vs RxJS-only?
5. **Dark mode**: in-scope ou diferido?
6. **PrimeNG preset**: Aura, Lara, Material, ou Nora?

## Decisão

### 1. Angular 21 LTS

`@angular/{core,common,forms,router,platform-browser,
platform-browser-dynamic,compiler,animations}@^21.x`,
`@angular/cli@^21.x`, `@angular-devkit/build-angular@^21.x`,
`typescript@~5.9.x` (matriz oficial Angular 21).

### 2. PrimeNG (preset Aura)

`primeng@^21.x`, `primeicons@^7.x`, `@primeng/themes@^21.x` configurado
via `providePrimeNG({ theme: { preset: Aura, ... } })`. Aura é o preset
moderno default do PrimeNG ≥18; substitui Lara como recomendação
oficial. Inclui dual theme (light/dark) sem necessidade de ficheiros
CSS extra.

`chart.js` é peer dep de PrimeNG Chart — instalada já em Phase 1b para
não bloquear Phase 2.

### 3. Tailwind CSS + plugin tailwindcss-primeui

`tailwindcss@^3.4.x`, `postcss`, `autoprefixer`, `tailwindcss-primeui`.
Configuração:

```js
// tailwind.config.js
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  darkMode: 'class',
  plugins: [require('tailwindcss-primeui')],
};
```

`darkMode: 'class'` alinha com a estratégia Aura (`darkModeSelector:
'.dark'`). `tailwindcss-primeui` expõe as cores do preset Aura como
utilities Tailwind (`bg-primary`, `text-surface-700`, ...).

### 4. State management — Angular Signals + services

Sem NgRx. Sem store global. Cada feature é um service injetável (Angular
DI) que detém um ou mais `WritableSignal<T>` privados e expõe
`computed()` públicos read-only. Os components consomem via
`inject(MyService)` e leitura direta dos signals em templates.

`AuthService` é o exemplo template: signal privado
`WritableSignal<AuthState | null>`, `computed` `isAuthenticated`,
`tenantName`, `tenantRole`.

### 5. Dark mode dentro de scope

`ThemeService` mantém `WritableSignal<boolean> isDark`, persistido em
`localStorage` (`sextante.theme = 'dark'|'light'`). Toggle adiciona /
remove `.dark` no `document.documentElement`. Inicialização:
`localStorage` → `prefers-color-scheme: dark` → `light`.

### 6. Standalone components + Reactive Forms

Zero `NgModule` em código novo (Phase 1b+). `imports` arrays directos
no decorator de cada componente. Formulários financeiros usam **Reactive
Forms** (`FormBuilder.group`, `Validators.compose`); `FormsModule` /
`ngModel` proibido para inputs domain. Validação cross-field (e.g.
`passwordsMatch`) via `ValidatorFn` declarado no nível do form group.

## Alternativas consideradas

### A. Angular Material (Component dev kit + Material 3 theme)

- **Prós**: maintained por Google; rica em data tables; design system
  consistente com Material 3.
- **Contras**: estética muito Google (cards arredondados grandes,
  padding generoso); preset Aura do PrimeNG é mais neutro e mais
  alinhado com o tom "ferramenta financeira". PrimeNG tem mais
  componentes prontos (`p-treetable`, `p-organizationchart`,
  `p-listbox` com filtros) que provavelmente vão ser úteis em
  Phase 4 (Investments) e Phase 5 (Forecast).
- **Veredicto**: rejeitado.

### B. PrimeUIX (sucessor anunciado para v20+)

- **Prós**: API mais "headless", mais simbiótica com Tailwind.
- **Contras**: ainda em flux na altura desta decisão; nem todos os
  componentes migrados; documentação mais escassa.
- **Veredicto**: rejeitado para Phase 1b. Reavaliável quando atingir
  paridade com PrimeNG e estabilizar (provavelmente Phase 6+).

### C. Bootstrap 5 + ng-bootstrap

- **Prós**: familiar; CSS classy; comunidade enorme.
- **Contras**: ng-bootstrap tem cobertura mais limitada (sem
  `treetable`, sem organização chart, datatable básica). Estética
  Bootstrap data o produto. Tech-stack §19 já tinha decidido por
  PrimeNG no replanning.
- **Veredicto**: rejeitado.

### D. NgRx (Redux store + actions + effects)

- **Prós**: padrão estabelecido; debugging via Redux DevTools;
  time-travel.
- **Contras**: cerimônia desproporcional para um single-developer
  modular monolith (mission §2). Cinco ficheiros para mudar uma
  flag; testes verbose. Signals + services dão a mesma reactividade
  com 1/10 da boilerplate.
- **Veredicto**: rejeitado. Reavaliável **apenas** se o frontend crescer
  para um nível de complexidade onde "qual feature mudou este state?"
  passe a ser uma pergunta legítima e frequente. Na prática, mission §2
  exige que isso nunca aconteça.

### E. Preset PrimeNG: Lara / Material / Nora

- **Prós (Lara)**: bem testado, era o default antes de Aura.
- **Prós (Material)**: consistente com Material spec.
- **Prós (Nora)**: mais minimalista, design system enterprise.
- **Contras**: Aura é o default moderno do PrimeNG v18+; receber
  bug-fixes e novos componentes primeiro; estética neutra. Lara está
  em manutenção. Material colide com a possibilidade futura de
  considerar Angular Material directo (e adiciona complexidade
  visual). Nora é mais opinionado para enterprise B2B.
- **Veredicto**: Aura. Outros presets disponíveis para troca via
  `provideeerimeNG` se necessário em Phase 6+.

### F. Diferir dark mode para Phase 6 ou pós-MVP

- **Prós**: scope mais pequeno em Phase 1b; "ship one mode at a time".
- **Contras**: retrofit de dark mode num codebase que cresceu sem ele
  é caro (todas as cores hard-coded em `bg-white` em vez de
  `bg-surface-0` precisam de migrar). Aura entrega dark mode
  praticamente grátis; o custo marginal em Phase 1b é muito baixo.
- **Veredicto**: dark mode in-scope; ThemeService entregue em Phase 1b.

## Consequências

### Positivas

- **Tailwind + Aura coexistem sem colisão**. Regra (no `README.md` do
  `Sextante.Web`): "Tailwind para layout / spacing / responsividade;
  PrimeNG para componentes interativos; nunca os dois a estilizar o
  mesmo elemento."
- **Cobertura PrimeNG**. Quando Phase 2 precisar de tabelas
  (`p-treetable`), Phase 4 de gráficos (`p-chart`), Phase 5 de date
  pickers avançados (`p-calendar` com range), a stack já está pronta.
- **Bundle inicial controlado**. Phase 1b só importa os componentes
  que usa (standalone components fazem tree-shaking automático). O
  budget no `angular.json` (`maximumWarning: 500kB`) protege contra
  regressões.
- **Signals dão reactividade simples**. `computed`, `effect`, e leitura
  direta em templates permitem state management que cabe num service
  de 50 linhas.
- **Dark mode agora**. Sem retrofit em Phase 6.

### Negativas / mitigações

- **Angular 21 é recente**. Risco de bugs de regressão. Mitigação:
  pinning rigoroso em `package.json`; testes Karma em CI; manual
  walkthrough antes de cada merge.
- **Lock-in em PrimeNG**. Trocar de UI library no futuro custa
  refactor em todas as páginas. Mitigação: a regra "Tailwind para
  layout, PrimeNG para componentes" minimiza o blast radius — o
  layout é portátil.
- **Curva de aprendizagem do Aura preset**. O uso de
  `darkModeSelector: '.dark'`, design tokens via `surface-*`,
  `primary-*`, etc. é PrimeNG-específico. Mitigação: README do
  `Sextante.Web` documenta o vocabulário e os exemplos.
- **Tailwind aumenta CSS bundle se mal-configurado**. Mitigação:
  `content: ['./src/**/*.{html,ts}']` faz purge correto; budget no
  `angular.json` falha o build se passar.

## Pendentes

- **Path exato de import do preset Aura** (`@primeng/themes/aura`
  vs `@primeuix/themes/aura`) confirmado em Phase 1b durante a
  implementação. Documentar a versão escolhida em `package.json`.
- **Plugin de unit-test** (Karma → Jest/Vitest) é re-avaliável em
  Phase 6 conforme cresce a regressão. Phase 1b mantém Karma porque
  já está scaffolded.
- **Storybook** (tech-stack §19.3) diferido até ~20 componentes
  próprios — provavelmente Phase 4 ou 5.
- **PrimeUIX** reavaliável quando atingir paridade com PrimeNG.
- **NgRx** reavaliável apenas se o frontend crescer para um nível em
  que "qual feature mudou este state?" passe a ser uma pergunta
  recorrente — improvável dado mission §2.
