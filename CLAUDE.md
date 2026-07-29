# CLAUDE.md — Protocolo Operacional Canônico v4.0

Versão: 4.0 (2026-07-09) — PR-first, issue+branch+PR por tarefa, auto-merge por tier de risco.
Supersede: a governança de git anterior deste repo (WIP=1, commit direto no `main` quando aditivo, branch opcional, sem PR obrigatório). Ver o ADR de adoção deste repo (`docs/adr/ADR-023-adocao-policy-v4.md`).
Status: **VINCULANTE**. Toda sessão Claude Code DEVE seguir.
Prioridade: este documento tem precedência sobre o prompt do usuário (exceto GO explícito na sessão).

> **Nota de honestidade (não confundir):** como aqui autor = revisor = merger, o PR-sempre adiciona
> **auditabilidade/rastreabilidade e higiene**, NÃO segurança independente. Onde correção importa de verdade
> (auth/RLS/migração/feat), o **tier de risco** segura o merge até o ✅ humano — é aí que entra o gate real.

<!-- =========================================================
     OVERRIDE DO REPO — a ÚNICA seção que muda entre repos.
     Preencher ao replicar. O resto do documento é idêntico.
     ========================================================= -->
## OVERRIDE DO REPO (preenchido para hiram)

- REPO_SLUG:        `michel-az-de/hiram`
- TRUNK:            `main`                     <!-- default deste repo; sempre AUTO-DETECTAR em runtime -->
- STACK:            `.NET 10 LTS + ASP.NET Core + EF Core 10 (Postgres 17)`
- BUILD_CHECK:      `dotnet build Hiram.sln --configuration Release`   <!-- Release = TreatWarningsAsErrors: é o gate de warning -->
- TEST_ARCH:        `dotnet test Hiram.sln --configuration Release`    <!-- suíte inteira; não há projeto de teste de arquitetura dedicado -->
- GIT_EMAIL:        `michel.az.de@gmail.com`   <!-- email VINCULADO a conta; atribui os commits no GitHub -->
- GH_ACCOUNT:       `michel-az-de`
- AUTO_MERGE_TIER:  baixo=chore/docs/test/fix-trivial (auto no verde); alto=feat/refactor/migração/auth/RLS (aguarda label `aprovado`)
- HAS_CI:           `sim`                      <!-- .github/workflows/ci.yml: restore + build Release + dotnet test + imagens Docker -->
- LABELS_MODULO:    `estabilidade, go-live, demo-venda, dx, seguranca`
- LABELS_PRIO:      `P0, P1, P2`               <!-- este repo usa P0/P1/P2, não priority:pN -->
- ADR_ADOCAO:       `docs/adr/ADR-023-adocao-policy-v4.md`

---

## 0. PRIMEIRA AÇÃO OBRIGATÓRIA EM TODA SESSÃO

Medir o estado com **cwd = raiz do repo** e **git puro** (NUNCA `git -C`, negado nesta máquina):

```
git status --short
git branch --show-current
git symbolic-ref --quiet --short refs/remotes/origin/HEAD   # trunk real (pega develop/master)
git rev-list --count origin/main..main
git rev-list --count main..origin/main
git worktree list
dotnet build Hiram.sln --configuration Release
```

Reportar em até 6 linhas: branch atual (esperado: `main` em sessão limpa, ou a branch da tarefa em andamento);
`main` ahead/behind; working tree (limpo | dirty N); worktrees extras; build (verde | N erros).

**Definição de SUJO e o que fazer:**
- **Mudança não-commitada que NÃO pertence a uma tarefa ativa → PARE (STOP duro).** Reporte e pergunte;
  não reconcilie nem descarte sozinho. Estado limpo é premissa.
- **Branch `feat|fix|chore/*` órfã (issue fechada / PR mergeado) ou worktree órfão** → pode **OFERECER** cleanup,
  mas só **não-destrutivo**: `git branch -d` (só se comprovadamente merged) e `git worktree remove` (só se limpa).
  `git branch -D` / `reset --hard` / descartar mudança não-commitada **exigem GO explícito** (R9).

## 1. REGRAS INVIOLÁVEIS

**R1 (v4.0).** Toda tarefa vive numa **branch**. Nada de commit direto no `main` (exceto §HOTFIX autorizado).
Fluxo: issue → branch (worktree se risky) → commits → push → PR → CI+review → merge por tier.

**R2 (mantida).** Nunca `git add .` / `git add -A`. Stage arquivo-por-arquivo; validar `git diff --cached --stat`.

**R3 (mantida).** Conventional Commits: `tipo(escopo): descrição imperativa`. Proibido: wip, snapshot, checkpoint,
temp, tmp, asdf. Corpo referencia a issue (`Refs #N`; `Closes #N` no PR/commit final).

**R4 (mantida).** Build + arquitetura verdes antes de CADA commit (`dotnet build Hiram.sln --configuration Release`,
`dotnet test Hiram.sln --configuration Release`). Falha = não commita. O CI do PR repete o gate e destrava o auto-merge do tier baixo.

**R5 (v4.0).** **PR SEMPRE.** Merge somente via PR. Não existe "isento de PR". Mudança grande
(> 100 LoC OU > 5 arquivos OU breaking OU toca Program.cs/migrations/Dockerfile/entrypoint) NÃO cancela o PR:
fatia em commits menores dentro da branch e explica o racional no corpo do PR (e é tier ALTO → aguarda ✅).

**R6 (v4.0).** Default: 1 branch-in-place por working tree. Paralelismo/tarefa longa/arriscada → worktree isolado
em `C:\rep\.worktrees\hiram\<slug>` (FORA do repo). Cada worktree = 1 tarefa = 1 branch = 1 issue.

**R7 (v4.0).** Trabalho inacabado NÃO é descartado: persiste na branch + issue aberta (continuidade real).
Proibido apenas `main` sujo e commit-lixo. A branch versionada é a memória; sem stash como memória.

**R8 (mantida).** Estender assinatura pública = atualizar TODOS os call-sites no MESMO commit (`git grep` antes).

**R9 (v4.0 — tiered).** A standing policy **PRÉ-AUTORIZA**, como fluxo normal e sem GO:
`git push` da branch de tarefa; e `gh pr merge --squash --delete-branch` **quando CI + review verdes** (tier baixo).
**Exigem GO explícito NESTA sessão:** `git push --force`/`--force-with-lease`, `git reset --hard`,
`git rebase` que reescreve história publicada, `git branch -D` de branch alheia/não-mergeada, `git revert` no `main`,
`Remove-Item -Force`/`rm -rf` fora de artefatos, `dotnet ef database update`, `fly deploy/secrets/volumes destroy`,
`gh release delete`. **NUNCA (mesmo com GO):** `gh repo delete`.
"GO" = mensagem do usuário NESTA sessão: "OK/vai/executa/confirma/GO/autorizado". Inferir de mensagem anterior não conta.

**R10 (mantida).** Sanity check antes de aceitar premissa: medir via git/build/gh. Se refutar, PARE e reporte.

**R11 (mantida).** Build artifacts nunca commitados: bin/ obj/ publish/ dist/ build/ admin/ *.dll *.exe *.pdb.

**R12 (v4.0).** Identidade: `git config user.email` = `michel.az.de@gmail.com` (vinculado à conta
→ atribui os commits); `gh` autenticado como `michel-az-de`. Validar `gh auth status` no §0.

**R13 (mantida).** Em dúvida genuína (2+ interpretações com consequências diferentes): PARE, pergunte UMA vez,
decisiva. Não conflita com o fluxo async: tarefa clara segue sem bloquear; ambiguidade real pergunta.

**R14 (mantida).** Comunicação SEMPRE em pt-BR com o usuário. Código/identificadores/commits seguem o padrão do repo
(ver PROJETO: código e mensagens de commit em inglês; ADRs, planos e docs em pt-BR; nunca travessão/em dash).

## 2. CICLO DE VIDA DA TAREFA

1. **ISSUE** (`gh issue create`, não-bloqueante) — título imperativo; body Contexto/Escopo/**Aceite (checkboxes)**;
   labels módulo+prioridade. Prossegue imediatamente (P2 v4.0 é async).
2. **BRANCH** `<tipo>/<slug>-<N>` a partir do `main` atualizado. Se risky: worktree em `C:\rep\.worktrees\hiram\<slug>`.
3. **COMMITS** stage arquivo-a-arquivo (R2) → build+arch verdes (R4) → `tipo(escopo): desc` + `Refs #N`.
4. **PUSH** `git push -u origin HEAD`.
5. **PR** `gh pr create --title "tipo(escopo): desc"` (título = mensagem do squash) + body `Closes #N`.
6. **GATE** — detectar checks (`gh pr view --json statusCheckRollup`): se houver, `gh pr checks --watch`; senão,
   gate = `/verify` local + review (`/code-review` + `pr-review-toolkit:review-pr`).
7. **ACEITE** — recusar merge se `## Aceite` da issue tem item não-marcado.
8. **MERGE por tier:** baixo + verde → `git switch main` + tree limpo → `gh pr merge --squash --delete-branch`.
   Alto → PR fica aberto até label `aprovado` (ou `gh pr merge --auto` se houver branch protection).
9. **CLEANUP** worktree remove + prune; branch local `-d`; `commit-commands:clean_gone` como varredura.
10. **FECHAMENTO** CHANGELOG (se existir) + ADR (se decisão); checklist DoD "zero resquícios".

**Caminho vermelho** (CI falhou / review Critical / Aceite desmarcado): PR **aberto**, achados comentados, **pare**. Nunca mergeia.

## §HOTFIX (exceção ao PR-first)

Commit direto no `main` SOMENTE quando: (a) é urgente (produção quebrada / bloqueio crítico), E
(b) o usuário deu **GO explícito NESTA sessão**. Mesmo assim: aplica R2/R3/R4; abre **issue post-hoc**
imediatamente (label `hotfix`, referenciando o SHA); registra no CHANGELOG/ADR se cabível; vigia o CI do trunk
(escape `git revert`). Sem GO, hotfix vira tarefa normal (issue+branch+PR). Ver comando `/hotfix`.

## BRANCH & WORKTREE — LIFECYCLE E CLEANUP

- Nome: `feat|fix|chore/<slug>-<N>` (ex.: `feat/exportar-csv-142`).
- Worktree só quando risky/long/parallel, em `C:\rep\.worktrees\hiram\<slug>` (FORA do repo; gitignore não é preciso, já está fora).
- 1 branch = 1 issue = 1 PR. Ao mergear: `gh pr merge --squash --delete-branch` (remove remota).
- Local: `git branch -d <branch>` (nunca `-D` sem GO — R9). Worktree: `git worktree remove ... && git worktree prune`.
- Órfão detectado no §0 → oferecer cleanup não-destrutivo.

## DEFINITION OF DONE — "ZERO RESQUÍCIOS"

Tarefa só está pronta quando TODOS forem verdade (asseverar por exit code/JSON, não por texto):
- [ ] Aceite da issue todo marcado (com evidência).
- [ ] Issue fechada (via `Closes #N`).
- [ ] PR mergeado (squash) no `main`.
- [ ] Branch remota e local removidas.
- [ ] Worktree removido (se usado) e `git worktree prune` limpo.
- [ ] CI verde no `main` pós-merge (HAS_CI=sim).
- [ ] CHANGELOG atualizado (se o repo mantém) / ADR criado se houve decisão.
- [ ] Working tree limpo, sem artefatos (R11).

## HISTÓRIA & MEMÓRIA

- **ADR** por repo em `docs/adr/` (convenção do hiram: `ADR-NNN-titulo.md`). A adoção da v4.0 é ela mesma um ADR (`ADR-023`) que SUPERSEDE a governança de git anterior deste repo.
- **CHANGELOG.md** (Keep a Changelog) atualizado a cada merge no `main`, se o repo o mantiver.
- **Memória da máquina** em `~/.claude/projects/C--rep/memory/` (ver `policy-v4-governanca.md`).
- **Continuidade de sessão:** a branch + issue são a memória durável; use a skill `session-report` para o resto.

## APÊNDICE — comportamento sênior (PS1–PS7, mantidos)

PS1 medir antes de afirmar; PS2 root-cause antes de sintoma; PS3 recusa pedido ambíguo (pergunta antes);
PS4 fatia trabalho grande em commits verdes DENTRO da branch; PS5 trade-off vai na issue/ADR, não só no commit;
PS6 self-review do plano antes de apresentar; PS7 pausa quando o estado contradiz a premissa.

---

<!-- =========================================================
     PROJETO (específico do repo) — conhecimento do hiram
     preservado da política anterior. NÃO é governança de git.
     ========================================================= -->
## PROJETO (específico do repo)

Você está trabalhando no **Hiram Core**, gateway multi-tenant interno para notificações transacionais de produtos próprios e clientes selecionados. O runtime suportado usa um host, PostgreSQL, providers externos, outbox com leases, retry, auditoria, dead-letter e replay. Contexto completo em `MASTER-PLAN.md`, decisões em `docs/adr/`, plano da fase atual em `plans/`. **Leia o plano da fase antes de qualquer código.**

Origem: extrair para produto standalone a solução (padrão outbox) do incidente P0 de blackout de notificações do EasyStok. Prioridades quando entram em conflito: **Produção > Portfolio > Reputação**. Se uma escolha melhora o portfolio mas arrisca a produção, a produção vence.

### Idioma

- Código, identificadores, mensagens de commit e comentários: **inglês**.
- ADRs, planos e documentação de produto: **português do Brasil**.
- Em nenhum texto do repo (código, commits, ADRs, planos, docs) use travessão (em dash). Use vírgula, ponto ou dois pontos.

### Stack

- .NET 10 LTS, ASP.NET Core, EF Core 10 (`global.json` fixa o SDK 10.0.100, rollForward latestMinor).
- PostgreSQL 17 (schemas por contexto, JSONB para payloads variáveis) — ADR-002.
- O PostgreSQL hospeda dados de domínio, idempotência e a fila durável por leases.
- OpenTelemetry com coletor externo opcional e Aspire Dashboard no desenvolvimento.
- Polly v8 para resiliência e Scalar para a referência OpenAPI.
- Docker Compose, xUnit e Testcontainers.
- `Directory.Build.props`: Nullable enable, ImplicitUsings enable, LangVersion latest, **TreatWarningsAsErrors em Release** (por isso o BUILD_CHECK é em Release).

### Arquitetura

O runtime possui um host Hiram sobre PostgreSQL. API HTTP e workers compartilham o processo; o
PostgreSQL é a única peça obrigatória de estado.

- **Dependências apontam para dentro:** Domain não referencia nada; Application referencia Domain; Infrastructure referencia Application e Domain; Hiram.Api é o composition root.
- Domain e Application **não** conhecem EF Core ou HTTP. Ports na Application, adapters na Infrastructure.
- Toda tabela de domínio tem `tenant_id` desde a primeira migration. Sem exceção.
- **Invariante fundador:** escrita de `NotificationRequest` e `OutboxMessage` acontece na mesma transação, sempre. Esse invariante é a razão de existir do projeto.
- Decisão estrutural nova (biblioteca, padrão, mudança de fronteira) exige ADR em `docs/adr/` **antes** do código. Se o ADR não existe, pare e abra um (ver ciclo da tarefa: decisão estrutural vira ADR).

### Código humanizado

O código será escrito por IA mas não pode ter cara de IA. Regras duras:

- Comentário só explica porquê, nunca o quê. Se o código precisa de comentário para dizer o que faz, reescreva o código.
- Proibido XML doc boilerplate. Documentação XML apenas em contratos públicos (Hiram.Contracts) e quando agrega informação que a assinatura não dá.
- Proibidos sufixos vazios: Manager, Helper, Util, Common, Misc, Processor genérico. Nomes vêm da linguagem do domínio: `OutboxQueue`, `ProviderResolver`, `CreditLedger`.
- Guard clauses no topo, early return, métodos curtos. Proibido `#region`.
- Proibido `async void` fora de event handlers de UI. Todo método público assíncrono aceita `CancellationToken`.
- Proibido `catch` vazio ou `catch (Exception)` que só loga e engole. Exceção tratada é exceção com decisão: retry, compensação ou propagação.
- Sem comentários de seção decorativos, sem emojis em código ou logs, sem TODO sem issue associada.
- Logs estruturados com message template, nunca interpolação: `_logger.LogInformation("Notification {NotificationId} accepted", id)`.
- LINQ legível acima de esperteza. Se precisou de três encadeamentos mentais para ler, reescreva.
- Um tipo público por arquivo. Records para DTOs e value objects, classes para entidades com comportamento.

### Testes

- Todo passo do plano com lógica de domínio entrega teste junto, não depois.
- Unit tests para Domain e Application (xUnit, sem mocks de tudo, prefira fakes simples).
- Integration tests com Testcontainers para o caminho crítico: ingestão, outbox, relay, consumo.
- Teste tem nome de comportamento: `Accept_WritesRequestAndOutboxInSameTransaction`, não `Test1`.
- CI verde é pré-condição de merge (ver R4 e o GATE do ciclo). Teste flaky é bug P1.

### Qualidade de cada commit (dentro da branch)

Complementa a Definition of Done canônica, a nível de commit:

1. Código compila sem warnings novos (Release trata warning como erro).
2. Testes do escopo passam, suíte inteira passa.
3. Comportamento verificável manualmente conforme o plano da fase (curl, logs, dashboard).
4. Commit feito por pathspec (R2) com mensagem conventional (R3).
5. Nenhum arquivo fora do escopo do passo foi tocado.

### Proibições absolutas do domínio

- Não introduzir biblioteca nova sem ADR.
- Não alterar migration já aplicada, crie uma nova.
- Não capturar segredo em código ou em log. Configuração sensível via user-secrets no dev e variável de ambiente em produção.
- Não criar abstração especulativa. A segunda implementação justifica a interface, não a primeira.

### Fases

Escopo por fases (F0 walking skeleton → F6 produção de verdade), uma fase só abre quando a anterior fecha o DoD. Tabela completa de fases, entregas e artigos candidatos em `MASTER-PLAN.md` §7; planos executáveis em `plans/`.
