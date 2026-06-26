# ADR-005: RabbitMQ com client puro e outbox próprio, sem MassTransit

**Status:** Aceito (formaliza prática estabelecida na F0)
**Data:** 2026-06-12
**Decisores:** Felipe (arquiteto)

## Contexto

A F0 implementou o relay do outbox e os consumers de canal diretamente sobre o RabbitMQ.Client 7, com transação Postgres, lotes via FOR UPDATE SKIP LOCKED e propagação de trace W3C pelos headers AMQP. A pergunta que este ADR responde em definitivo: deveria um framework de mensageria (MassTransit, NServiceBus, Rebus) mediar esse acesso, ou um broker de log (Kafka) substituir a fila?

## Decisão

RabbitMQ com o client oficial puro, outbox e relay próprios, topologia declarada explicitamente pelo código dos hosts. Nenhum framework de mensageria intermediário.

## Opções consideradas

### Opção A: RabbitMQ.Client puro com outbox próprio

**Prós:** o padrão outbox é o ativo central do projeto (de produção e de portfolio), e implementá-lo às claras é o que o torna explicável em artigo e em entrevista; controle total de topologia, ack, prefetch e headers; zero camada de abstração entre o invariante e o broker; menos dependências para auditar.
**Contras:** features que frameworks dão de graça (retry agendado, saga, multi-transporte) precisam ser construídas quando forem necessárias.

### Opção B: MassTransit

**Prós:** outbox pronto, retries, sagas, comunidade grande.
**Contras:** esconde exatamente o mecanismo que o projeto quer exibir; convenções de topologia próprias que dificultam o controle fino; mudança de licenciamento da v9 (comercial) torna o futuro do free incerto; dependência pesada para um produto que usa uma fração do framework.

### Opção C: Kafka

**Prós:** throughput e replay nativos, familiaridade prévia do decisor (background bancário).
**Contras:** semântica de log particionado não é o modelo do problema (filas de trabalho com ack individual e roteamento por canal); custo operacional num VPS único é desproporcional; KEDA com profundidade de fila é mais direto no Rabbit.

## Análise de trade-off

O critério decisivo é o objetivo do projeto: demonstrar domínio do padrão, não escondê-lo. O custo da opção A (construir retry agendado e DLQ na F2) é trabalho que gera artigo e competência demonstrável, ou seja, é custo que paga o próprio objetivo.

## Consequências

- Fica mais fácil: explicar e auditar o caminho da mensagem, ajustar topologia, manter o trace de ponta a ponta.
- Fica mais difícil: padrões avançados (saga, scheduling sofisticado) exigem implementação própria ou revisão desta decisão.
- A F2 implementa DLQ e replay manualmente, conforme já previsto no MASTER-PLAN (ADR-011 detalhará).

## Gatilho de revisão

Necessidade real de sagas distribuídas, segundo transporte de mensageria, ou volume que exija particionamento de consumo além do que filas competing-consumers entregam.

## Itens de ação

1. [x] F0: relay e consumers com client puro, trace propagado por headers AMQP.
2. [x] F2: DLQ e replay próprios, endereçado pela ADR-011.
