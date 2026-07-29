# ADR-027: Hiram Core como infraestrutura interna enxuta

**Status:** Aceito
**Data:** 2026-07-29
**Decisores:** Felipe (arquiteto)

## Contexto

O Hiram nasceu para extrair do EasyStok a entrega confiável de notificações e evoluiu para uma
plataforma horizontal. O roadmap acumulou metering e cobrança, IA, portal administrativo, novos
canais, MTA próprio, k3s, KEDA e uma stack de demonstração compartilhada com outro produto.

Essa direção não corresponde mais ao uso pretendido. O Hiram não será vendido como SaaS. Ele será
infraestrutura interna para produtos próprios e clientes selecionados. Nesse contexto, cada serviço
stateful, canal e superfície administrativa adiciona custo de operação, atualização e suporte que
precisa ser justificado por uso real.

O estado medido antes desta decisão é:

- seis projetos de código e dois hosts de produção, API e Dispatcher;
- PostgreSQL como autoridade durável;
- RabbitMQ entre outbox e processadores;
- Redis apenas como fast-path de idempotência e throttle de uso de API key;
- dois providers de email, SMTP e Resend;
- email, push, templates, webhooks, eventos, rotinas, consentimentos e bloqueios implementados;
- metering parcial, WhatsApp parcial, MTA opt-in e deploy k3s/KEDA sem adoção que justifique a
  manutenção;
- build Release e CI verdes.

O próprio código já trata PostgreSQL como garantia de idempotência quando Redis não está disponível.
O relay também reivindica linhas do outbox com `FOR UPDATE SKIP LOCKED`. Portanto, há base para
reduzir o runtime sem abandonar a propriedade central do projeto: uma notificação aceita é
persistida de forma durável, processada com rastreabilidade e entregue por um provider substituível.

## Decisão

Redefinir o Hiram como **Hiram Core**, um gateway interno multi-tenant para notificações
transacionais. O runtime padrão terá:

1. um único host Hiram, contendo API e workers;
2. PostgreSQL como única peça obrigatória de estado;
3. providers externos na última milha;
4. OpenTelemetry opcional, sem stack de observabilidade acoplada ao deploy;
5. uma imagem e um Compose de referência;
6. compatibilidade da API `/v1` durante a migração.

O núcleo obrigatório contém:

- tenants e API keys;
- submissão e consulta de notificações;
- idempotência durável no PostgreSQL;
- outbox transacional;
- lease de processamento, retry, tentativas, dead-letter e replay;
- email via SMTP e provider HTTP;
- configuração de provider por tenant;
- consentimento e bloqueio;
- webhooks de status;
- health checks e telemetria.

Templates, eventos, rotinas e Web Push são extensões de compatibilidade. Permanecem enquanto houver
uso concreto em EasyStok, Levante ou outro projeto ativo. Uma extensão sem consumidor comprovado no
gate final da migração será removida, não mantida por possibilidade futura.

Saem do produto:

- credit ledger, metering, quotas e billing;
- IA e autonomia configurável;
- Portal Blazor;
- WhatsApp incompleto;
- MTA Stalwart;
- k3s, KEDA e PgBouncer;
- deploy conjunto com Levante;
- site e console de demonstração servidos pelo host de produção.

## Topologia alvo

```text
Projeto ou cliente
        |
        | HTTP / SDK .NET
        v
Hiram Core
  API + workers
        |
        v
PostgreSQL
  notificações + outbox + tentativas + dead-letter
        |
        v
Provider externo
  SMTP / Resend / adapter aprovado sob demanda
```

O worker reivindica itens do outbox no PostgreSQL com lease explícito. O lease impede dois workers
de processarem a mesma linha ao mesmo tempo e permite recuperação após crash. O modelo registra
disponibilidade, vencimento do lease, tentativas e último erro. Falhas esgotadas geram dead-letter.

Essa fila é at-least-once. Nenhuma topologia pode garantir exatamente uma chamada ao provider quando
há crash após o provider aceitar a mensagem e antes da confirmação local. O Hiram mantém claim
durável, callbacks de provider e recuperação fail-safe para tornar essa incerteza visível, nunca para
prometer exatamente-once.

## Migração

A mudança ocorre em PRs independentes e verdes:

1. registrar a fronteira e reconciliar documentação;
2. retirar escopos de produto e deploys fora da fronteira;
3. remover Redis, usando o índice único do PostgreSQL como autoridade;
4. introduzir lease e dispatch direto no PostgreSQL, com RabbitMQ ainda disponível durante a troca;
5. migrar os processadores e remover RabbitMQ;
6. consolidar API e workers em um host, uma imagem e um Compose;
7. fechar runbook, backup/restore, métricas e evidência de produção.

Não serão alteradas migrations já aplicadas. Entidades retiradas do código permanecem inicialmente
como tabelas dormentes. Uma migration posterior pode removê-las depois de backup, janela de
compatibilidade e comprovação de que não há leitura.

## Alternativas consideradas

### Manter a arquitetura atual

Preserva o investimento e a capacidade de escala independente.

Rejeitada porque RabbitMQ, Redis, k3s, KEDA, MTA e módulos de produto aumentam o custo fixo para um
uso interno de volume ainda não medido.

### Substituir por plataforma gerenciada

Reduz operação e entrega interfaces prontas de workflow, inbox e preferências.

Rejeitada como padrão porque o Hiram já cobre o caso transacional específico, integra o modelo de
tenant dos produtos e mantém dados e regras sob controle próprio. Continua sendo opção para clientes
que precisem de jornadas visuais, inbox ou operação delegada.

### Adotar uma plataforma open source self-hosted

Evita manter código próprio de produto.

Rejeitada porque troca o runtime conhecido por outra plataforma com múltiplos serviços e processo de
upgrade próprio. Não reduz necessariamente a responsabilidade operacional.

### Manter RabbitMQ e Redis como opcionais

Evita uma migração estrutural imediata e preserva escala horizontal.

Rejeitada para o runtime padrão. Caminhos opcionais dobrariam testes e configurações. Um broker só
volta com benchmark e volume reais que excedam a topologia PostgreSQL.

## Consequências

### Positivas

- menos serviços, imagens, secrets, health checks e modos de falha;
- onboarding mais curto para novos projetos;
- testes de integração mais rápidos e menos dependentes de containers;
- custo de operação compatível com infraestrutura interna;
- fronteira de produto honesta e menor superfície de segurança.

### Negativas

- API e workers deixam de escalar de forma independente no runtime padrão;
- PostgreSQL absorve também a carga de fila;
- a migração toca o caminho crítico de entrega e exige rollout incremental;
- recursos removidos terão de ser reconstruídos ou adotados de terceiro se surgir demanda real.

## Limites e gatilhos de revisão

Reavaliar um broker dedicado somente quando métricas de produção demonstrarem contenção persistente
no PostgreSQL ou throughput que o worker com lease não sustente dentro do SLO.

Reavaliar um canal ou módulo removido somente com projeto consumidor, owner operacional e critério de
aceite definidos. Interesse abstrato não reabre escopo.

Uma instância dedicada por cliente só será oferecida quando o contrato remunerar deploy, upgrade,
backup, observabilidade e suporte. O padrão é uma instância central multi-tenant para produtos
próprios e clientes selecionados.

## ADRs afetados

- ADR-001, Portal Blazor: supersedido.
- ADR-003, LGTM self-hosted em produção: parcialmente supersedido. OpenTelemetry permanece; a stack
  LGTM deixa de ser dependência do Hiram.
- ADR-005, RabbitMQ puro: supersedido.
- ADR-007, metering ledger: supersedido.
- ADR-014, Web Push: reclassificado como extensão de compatibilidade.
- ADR-016, k3s e KEDA: supersedido.
- ADR-017, motor de eventos: reclassificado como extensão de compatibilidade; os invariantes de
  idempotência e incerteza pós-provider permanecem.
- ADR-023, WhatsApp Cloud API: supersedido.
- ADR-026, MTA Stalwart: rejeitado e supersedido.

## Critério de conclusão

O Hiram Core está concluído quando uma instalação nova exige apenas o host Hiram, PostgreSQL e as
credenciais do provider; a API `/v1` permanece compatível; build e testes estão verdes; backup e
restore foram executados; e uma carga real opera por 30 dias sem perda silenciosa.
