# Validation — Phase 6: Polish + Deploy + Dogfooding

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui — são
> wishlist.
>
> **Gates de merge** (escolhidos em kickoff, `requirements.md` D7): o
> live check (§4) e o restore de teste (§3). Os restantes bullets são
> obrigatórios para a phase fechar, mas só estes dois bloqueiam o merge
> se falharem — os outros são recuperáveis sem risco para os dados.

## Definition of done

### Export CSV

1. **Filtros de `kind` / descrição / valor aplicados no servidor** —
   `transactions.page.ts` já não filtra em memória.
2. **`GET /api/financial/transactions/export`** devolve CSV com os
   filtros activos, `Content-Disposition: attachment`.
3. **Formato PT-PT** — separador `;`, decimal `,`, UTF-8 com BOM,
   descrições com `;` corretamente escapadas.
4. **Botão na UI** descarrega o ficheiro com o token em memória
   (sem `<a href>` directo) e passa sanity 375 / 768 / 1280 px.
5. **Isolamento multi-tenant** no export provado por teste.

### Email

6. **`SmtpEmailSender` substitui o stub** — `NotImplementedEmailSender`
   apagado, registo actualizado em `DependencyInjection.cs`.
7. **Entrega via Hangfire job** com retry, e destino mascarado no log.
8. **Degradação graciosa** — sem `SMTP__HOST` a app arranca e
   `/forgot-password` responde 200.
9. **Banner de confirmação** não bloqueante, dispensável, com
   "reenviar" funcional.
10. **Reset de password end-to-end** pela UI com link real.

### Backups

11. **Script `infra/backup/backup.sh`** faz dump por schema, aplica
    retenção 30 dias e falha ruidosamente em dump vazio.
12. **Timer diário instalado na VPS** (03:00 UTC) e documentado.
13. **Runbook `docs/runbooks/restore.md`** escrito.
14. **Restore de teste executado e documentado** com data e output.

### Deploy

15. **Live check verde** — landing, `/api/health` 200, certificado
    Let's Encrypt.
16. **Hangfire dashboard não exposto** a anónimos.
17. **Erro 500 forçado chega ao Sentry** com `traceId` correlacionável
    no log de ficheiro.
18. **Sanity responsivo em telemóvel real** no domínio real, nas 6
    páginas principais.
19. **README de deploy actualizado** com provider, sizing, domínio e
    desvios reais ao runbook.

### CD

20. **ADR-013 escrito** antes do workflow.
21. **Push em `main` → imagem no registry → deploy na VPS**, com smoke
    check pós-deploy a falhar o job se `/api/health` não responder 200.
22. **`docker-compose.yml` parametrizado** para `pull` do registry sem
    perder o `build` local.
23. **README + `tech-stack.md` §15/§18 alinhados** com o CD real.

### Fecho

24. **CHANGELOG actualizado**; **sub-agent deep review 🛡️ verde**;
    roadmap com a Phase 6 marcada.
25. **CI verde** — build, test, format.

## How to verify each bullet

| # | Verificação |
|---|-------------|
| 1 | Teste de integração: filtro por descrição + intervalo de valor devolve linhas de além da 1ª página. Grep a `transactions.page.ts` não encontra o filtro local ("Filter locally by kind and description"). |
| 2 | `curl -H "Authorization: Bearer <t>" "https://<dominio>/api/financial/transactions/export?dateFrom=...&categoryIds=..." -D -` → 200, header `Content-Disposition`. Contar linhas vs `GET ""` com os mesmos filtros. |
| 3 | Abrir o CSV no Excel PT-PT: colunas separadas, acentos correctos, valores numéricos reconhecidos. Unit test do `TransactionCsvWriter` para escaping. |
| 4 | Browser: filtrar, exportar, abrir o ficheiro. DevTools em 375 / 768 / 1280 px sem overflow horizontal. |
| 5 | Teste de integração: tenant A exporta e o CSV não contém nenhuma linha de tenant B (usar o padrão dos testes de multi-tenancy existentes). |
| 6 | `git grep NotImplementedEmailSender` → 0 resultados em `src/`. Build verde. |
| 7 | Dashboard Hangfire mostra o job `SendEmailJob` executado; log de ficheiro mostra o resultado com o destino mascarado (`[email]`). |
| 8 | Correr o container sem `SMTP__HOST`: app arranca, `curl -X POST .../api/auth/forgotPassword` → 200, log tem `Warning` "email não configurado". Teste de integração equivalente. |
| 9 | Login com utilizador não confirmado: banner aparece, "reenviar" dispara 2xx, dispensar esconde-o e a navegação nunca é bloqueada. |
| 10 | Fluxo real: `/forgot-password` → email recebido → link → `/reset-password` → login com a nova password. |
| 11 | Correr `infra/backup/backup.sh` na VPS: 4 ficheiros `.dump` não vazios criados. Correr com um schema inexistente → exit code ≠ 0. `touch -d '40 days ago'` num dump antigo e confirmar que é apagado. |
| 12 | `systemctl list-timers` (ou `crontab -l`) mostra o agendamento; correr uma vez manualmente e ver o dump com timestamp de hoje. |
| 13 | Um leitor que não escreveu o script consegue seguir o runbook do início ao fim sem adivinhar nada. |
| 14 | Executar o runbook: base limpa restaurada, login feito, transações e metas visíveis. Colar output + data na secção "Restore de teste executado". |
| 15 | `curl -I https://<dominio>/api/health` → 200; `openssl s_client -connect <dominio>:443 -servername <dominio> </dev/null \| openssl x509 -noout -issuer` → Let's Encrypt; browser mostra a landing. |
| 16 | `curl -I https://<dominio>/hangfire` sem cookie/token → 401/404, nunca 200. |
| 17 | Chamar um endpoint que force 500; encontrar o evento no Sentry e o mesmo `traceId` no ficheiro de log dentro do volume `logs`. |
| 18 | Percorrer no telemóvel: login, dashboard, `/transactions` + export, wizard de import, recorrentes, metas. Registar o que falhar. |
| 19 | Ler o README de topo a baixo e confrontar com o que foi feito: qualquer passo divergente está corrigido. |
| 20 | `docs/adr/ADR-013-cd-github-actions.md` existe com Context / Decision / Consequences / Alternatives, e é anterior ao commit do workflow (`git log --follow`). |
| 21 | Push trivial em `main` → Actions verde → `docker compose ps` na VPS mostra o container recriado com a nova tag → smoke check no log do job. Depois, quebrar o health check de propósito e confirmar que o job fica vermelho. |
| 22 | `docker compose config` resolve a imagem do registry com `SEXTANTE_IMAGE` definido, e continua a construir localmente sem ela. |
| 23 | `git grep "CD automático fica fora do MVP"` → 0 resultados. |
| 24 | Ficheiro `CHANGELOG.md` com entrada de hoje; log do sub-agent review sem findings HIGH abertos; `specs/roadmap.md` Phase 6 com checkboxes marcados. |
| 25 | `dotnet build Sextante.slnx -c Release` + `dotnet test Sextante.slnx -c Release --no-build` + `npm test -- --watch=false --browsers=ChromeHeadless` verdes, localmente e no CI. |

## Fora do scope de validação

Esta phase **não** prova:

- **Que o MVP está completo.** O critério de `mission.md` §6 (1 mês sem
  Excel) arranca *depois* deste merge; o dogfooding é o gate seguinte,
  não parte desta validação.
- **Entregabilidade do email a longo prazo.** Verificamos um reset
  end-to-end (bullet 10). Reputação de domínio, taxa de spam ao longo do
  tempo e warm-up do relay não são medidos — se falharem, a decisão D1
  é revisitada no Replanning.
- **Resiliência do backup a perda da VPS.** Os dumps vivem no mesmo
  disco; cópia off-site está declarada fora de scope
  (`requirements.md` open question 5).
- **Performance / carga.** 1 utilizador, 1 VPS; sem baseline de
  latência nem teste de carga.
- **Correcção de rollback do CD.** O workflow entrega; o caminho de
  rollback está documentado no ADR-013 mas não é exercitado nesta phase.
- **Auditoria responsiva formal.** É sanity check (`tech-stack.md`
  §19.5), não audit de acessibilidade.
