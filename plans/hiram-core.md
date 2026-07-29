# Plano executável do Hiram Core

> Este plano executa o ADR-027. Cada etapa vive em issue, branch e PR próprios. Mudanças de
> persistência ou caminho de entrega são tier alto e exigem CI verde e label `aprovado`.

## Objetivo

Reduzir o Hiram a uma infraestrutura interna de notificações confiáveis que rode, por padrão, como
um host e um PostgreSQL, usando providers externos na última milha.

## Invariantes preservados

1. `NotificationRequest` e `OutboxMessage` são gravados na mesma transação.
2. Toda operação e consulta é isolada por tenant.
3. `Idempotency-Key` é durável no PostgreSQL.
4. Toda chamada ao provider deixa evidência em `DeliveryAttempt`.
5. Falha esgotada é visível e replayable, nunca descartada em silêncio.
6. Segredos não aparecem em código, logs ou arquivos versionados.
7. A API `/v1` permanece compatível durante a migração.

## Passo 1: retirar escopo de produto

- Remover metering da submissão e do modelo executado.
- Remover WhatsApp parcial e o MTA opt-in.
- Retirar k3s, KEDA, PgBouncer e a stack conjunta com Levante deste repositório.
- Desacoplar site e console de demonstração do host.
- Manter migrations antigas intactas e tabelas retiradas dormentes.
- Atualizar CI para validar apenas artefatos que continuam suportados.

Gate:

- uma notificação email direta continua sendo aceita, persistida e entregue;
- nenhuma rota ou configuração removida é anunciada na documentação;
- build e suíte completa verdes.

## Passo 2: remover Redis

**Status:** concluído na issue #89.

- Substituir `IIdempotencyKeys` por fluxo PostgreSQL-first.
- Tratar conflito do índice único como replay da requisição original.
- Remover throttle Redis de `last_used_at`; registrar uso com atualização condicionada no banco ou
  retirar o campo se não houver consumidor operacional.
- Remover health check, connection string, pacote e Testcontainer Redis.
- Comprovar concorrência de chaves iguais no mesmo tenant e independência entre tenants.

Gate:

- replay sequencial e concorrente devolve uma única notificação;
- indisponibilidade de cache deixa de ser modo de falha;
- API requer apenas a connection string do PostgreSQL.

## Passo 3: introduzir a fila PostgreSQL

**Status:** fundação de lease entregue na issue #91 e dispatcher PostgreSQL ativado na #92.

- Estender o outbox com `available_at`, `lease_until`, `attempt_count` e `last_error`.
- Criar operação atômica de claim usando `FOR UPDATE SKIP LOCKED`.
- Renovar ou concluir lease de forma explícita.
- Recuperar leases vencidos sem perder item.
- Mover para dead-letter após política de retry.
- Manter RabbitMQ ativo durante a primeira fatia, protegido por configuração de transição.

Gate:

- dois workers concorrentes não processam a mesma linha simultaneamente;
- crash simulado após claim torna a linha elegível depois do lease;
- retry respeita `available_at`;
- poison message termina em dead-letter.

## Passo 4: migrar processadores e remover RabbitMQ

**Status:** concluído pelas issues #92 e #95.

- Introduzir um dispatcher por tipo de mensagem sobre o outbox.
- Adaptar email, webhook e extensões comprovadamente usadas.
- Preservar trace context entre submissão e worker.
- Remover relay, consumers, topologia, pacote RabbitMQ e Testcontainer RabbitMQ.
- Reescrever replay para recolocar o item na fila PostgreSQL.

Gate:

- testes ponta a ponta cobrem submissão, envio, falha, retry, dead-letter e replay;
- redelivery não chama provider depois de estado terminal;
- nenhuma connection string ou health check de RabbitMQ permanece.

## Passo 5: consolidar host e deploy

**Status:** concluído na issue #97.

- Hospedar workers no processo ASP.NET Core.
- Permitir desligar workers apenas para migração e diagnóstico, não como segunda topologia permanente.
- Remover o projeto e a imagem do Dispatcher.
- Publicar uma imagem `hiram`.
- Entregar um Compose de referência com Hiram e PostgreSQL.
- Manter `--migrate-only` para deploy controlado.

Gate:

- uma instalação vazia sobe com um comando após preencher secrets;
- health live não consulta dependências;
- health ready valida PostgreSQL e configuração mínima;
- shutdown aguarda o item em processamento ou libera o lease.

## Passo 6: fechar operação

**Status:** backup, restore e runbook concluídos na issue #99. A observação de 30 dias permanece
aberta como gate temporal antes de novo escopo.

- Executar backup e restore em ambiente descartável.
- Documentar onboarding de tenant e rotação de API key.
- Definir métricas mínimas: aceitas, enviadas, falhas, dead-letter, lease vencido e duração.
- Criar runbook para provider indisponível, fila crescente e item pós-provider incerto.
- Validar 30 dias de uso real antes de abrir novo escopo.

## Extensões em quarentena

Templates, eventos, rotinas e Web Push permanecem em quarentena. Cada uma deve ter evidência de
consumidor ativo; sem evidência, a extensão recebe issue própria de remoção.

## Rollback

Cada passo permite rollback pelo PR correspondente. A fila PostgreSQL não possui caminho paralelo de
dispatch: somente um worker pode deter o lease de cada item.

## Definition of Done

- [x] Um host e um PostgreSQL são suficientes para produção.
- [x] Redis e RabbitMQ não fazem parte do runtime, CI ou documentação ativa.
- [x] Escopos de SaaS foram removidos.
- [x] API `/v1` compatível para recursos mantidos.
- [x] Build Release e suíte completa verdes.
- [x] Uma imagem e um Compose suportados.
- [x] Backup e restore comprovados.
- [x] Runbook publicado.
- [ ] 30 dias de operação real sem perda silenciosa.
