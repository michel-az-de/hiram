# CLAUDE.md, regras de operação deste repositório

Você está trabalhando no Hiram, plataforma multi-tenant de notificações. Contexto completo em MASTER-PLAN.md, decisões em docs/adr/, plano da fase atual em plans/. Leia o plano da fase antes de qualquer código.

## Idioma

- Código, identificadores, mensagens de commit e comentários: inglês.
- ADRs, planos e documentação de produto: português do Brasil.
- Em nenhum texto use travessão (em dash). Use vírgula, ponto ou dois pontos.

## Arquitetura

- Dependências apontam para dentro: Domain não referencia nada; Application referencia Domain; Infrastructure referencia Application e Domain; hosts (Api, Dispatcher, Webhooks, Intelligence, Portal) são composition roots.
- Domain e Application não conhecem EF Core, RabbitMQ, Redis ou HTTP. Ports na Application, adapters na Infrastructure.
- Toda tabela de domínio tem `tenant_id` desde a primeira migration. Sem exceção.
- Escrita de `NotificationRequest` e `OutboxMessage` acontece na mesma transação, sempre. Esse invariante é a razão de existir do projeto.
- Decisão estrutural nova (biblioteca, padrão, mudança de fronteira) exige ADR em docs/adr/ antes do código. Se o ADR não existe, pare e abra um.

## Código humanizado

O código será escrito por IA mas não pode ter cara de IA. Regras duras:

- Comentário só explica porquê, nunca o quê. Se o código precisa de comentário para dizer o que faz, reescreva o código.
- Proibido XML doc boilerplate. Documentação XML apenas em contratos públicos (Hiram.Contracts) e quando agrega informação que a assinatura não dá.
- Proibidos sufixos vazios: Manager, Helper, Util, Common, Misc, Processor genérico. Nomes vêm da linguagem do domínio: `OutboxRelay`, `QuotaGate`, `ProviderResolver`, `CreditLedger`.
- Guard clauses no topo, early return, métodos curtos. Proibido `#region`.
- Proibido `async void` fora de event handlers de UI. Todo método público assíncrono aceita `CancellationToken`.
- Proibido `catch` vazio ou `catch (Exception)` que só loga e engole. Exceção tratada é exceção com decisão: retry, compensação ou propagação.
- Sem comentários de seção decorativos, sem emojis em código ou logs, sem TODO sem issue associada.
- Logs estruturados com message template, nunca interpolação: `_logger.LogInformation("Notification {NotificationId} accepted", id)`.
- LINQ legível acima de esperteza. Se precisou de três encadeamentos mentais para ler, reescreva.
- Um tipo público por arquivo. Records para DTOs e value objects, classes para entidades com comportamento.

## Git

- WIP=1. Um passo do plano por vez, do início ao commit, antes de abrir o próximo.
- Commits pequenos e por pathspec: `git add src/Hiram.Domain/Notifications/`. Proibido `git add .` e `git add -A`.
- Conventional commits escritos como humano escreveria: `feat: persist notification with outbox row in one transaction`. Proibido qualquer rodapé de IA, co-authored-by de bot ou emoji.
- Branch curta por passo quando o passo for arriscado, direto no master quando for aditivo e coberto por teste. Em dúvida, branch.
- Nunca force push no master. Nunca rebase de branch compartilhada.

## Testes

- Todo passo do plano com lógica de domínio entrega teste junto, não depois.
- Unit tests para Domain e Application (xUnit, sem mocks de tudo, prefira fakes simples).
- Integration tests com Testcontainers para o caminho crítico: ingestão, outbox, relay, consumo.
- Teste tem nome de comportamento: `Accept_WritesRequestAndOutboxInSameTransaction`, não `Test1`.
- CI verde é pré-condição de merge. Teste flaky é bug P1.

## Definição de pronto de um passo

1. Código compila sem warnings novos.
2. Testes do passo passam, suíte inteira passa.
3. Comportamento verificável manualmente conforme o plano da fase (curl, logs, dashboard).
4. Commit feito por pathspec com mensagem conventional.
5. Nenhum arquivo fora do escopo do passo foi tocado.

## Proibições absolutas

- Não introduzir biblioteca nova sem ADR.
- Não alterar migration já aplicada, crie uma nova.
- Não capturar segredo em código ou em log. Configuração sensível via user-secrets no dev e variável de ambiente em produção.
- Não criar abstração especulativa. A segunda implementação justifica a interface, não a primeira.
- Não tocar em mais de um passo do plano por sessão sem instrução explícita.
