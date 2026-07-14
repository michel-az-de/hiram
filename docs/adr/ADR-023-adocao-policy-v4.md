# ADR-023: Adocao da Policy v4.0 (PR-first, issue-driven)

- Status: aceito
- Data: 2026-07-09
- Supersede: a governanca de git da politica anterior do hiram (secao "Git" do CLAUDE.md v3: WIP=1, commit direto no main quando aditivo e coberto por teste, branch curta opcional quando arriscado, sem PR obrigatorio). Nao havia ADR previo dedicado a esse fluxo, ele vivia apenas no CLAUDE.md.

## Contexto

Os repos em `C:\rep` sao desenvolvidos por Claude Code. A politica de git anterior do hiram era hibrida e centrada no trunk: commit direto no `main` quando a mudanca era aditiva e coberta por teste, branch curta apenas quando o passo era arriscado, WIP=1 (um passo do plano por vez), e nenhum PR obrigatorio. Isso deu velocidade, mas a direcao passou a exigir: **PR sempre** (exceto hotfix urgente autorizado), fluxo alinhado ao GitHub (issue, PR, close), limpeza de branches e worktrees ao terminar, e historico e memoria para continuidade entre sessoes.

A licao registrada no MASTER-PLAN (secao 10, risco de agentes paralelos quebrarem o main, apos a recuperacao de 14 dias do EasyStok) reforca a necessidade de um gate explicito antes de qualquer coisa chegar ao trunk.

## Decisao

Adotar o **Protocolo Operacional Canonico v4.0** (ver `CLAUDE.md`): toda tarefa = issue + branch + PR, com **auto-merge por tier de risco** (baixo, como chore/docs/test/fix trivial, mergeia sozinho no verde; alto, como feat/refactor/migracao/auth/RLS, aguarda o label `aprovado`), worktrees fora do repo (`C:\rep\.worktrees\hiram`), e Definition of Done com criterio de Aceite verificavel.

O bloco OVERRIDE do CLAUDE.md fixa os valores do hiram: trunk `main`, stack .NET 10 + EF Core 10, build check `dotnet build Hiram.sln --configuration Release` (Release trata warning como erro), suite via `dotnet test Hiram.sln --configuration Release`, CI presente (`.github/workflows/ci.yml`), labels de modulo `estabilidade, go-live, demo-venda, dx, seguranca` e prioridade `P0, P1, P2`.

Todo o conhecimento especifico do repo (arquitetura de dependencias para dentro, invariante NotificationRequest + OutboxMessage na mesma transacao, regras de codigo humanizado, testes, stack e fases) foi preservado na secao "PROJETO" do novo CLAUDE.md. A regra de idioma tambem foi mantida: codigo e commits em ingles, ADRs e docs em pt-BR, sem travessao.

## Consequencias

- O `main` deixa de receber commit direto (salvo hotfix autorizado com issue post-hoc). O antigo "direto no main quando aditivo" sai; toda mudanca passa por branch e PR.
- Ganha-se auditabilidade (revert granular, trilha issue-PR-merge), NAO seguranca independente (autor = revisor = merger). O gate real de correcao e o tier alto mais o ✅ humano via label `aprovado`.
- Identidade de commit passa a `michel.az.de@gmail.com` (vinculado a conta, atribui os commits no GitHub); `gh` autenticado como `michel-az-de`.
- WIP=1 e a nocao de "um passo do plano por sessao" deixam de ser regra de git; o paralelismo controlado passa a ser feito por worktrees isolados, uma tarefa por branch por issue.
- Automacao via `/tarefa-inicio`, `/tarefa-fim`, `/hotfix` mais hooks SessionStart/Stop.
- O Dev Janitor pula repos fora do trunk (guard) e nao toca `C:\rep\.worktrees`.

## Alternativas consideradas

- Manter o fluxo trunk-hibrido anterior (commit direto no main + WIP=1): rejeitada, nao atende PR-sempre nem a gestao por issue pedida, e mantem o main exposto ao risco de agentes paralelos citado no MASTER-PLAN.
- CLAUDE.md global unico para todos os repos: rejeitada, preferencia por politica por-repo com bloco de override.
- Auto-merge total sem tier: rejeitada, daria falsa sensacao de gate; mudanca de alto risco (auth, RLS, migracao, feat) sem ✅ humano e inaceitavel.
