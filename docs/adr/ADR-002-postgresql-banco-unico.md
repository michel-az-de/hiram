# ADR-002: PostgreSQL como banco único, schemas por contexto e JSONB

**Status:** Aceito
**Data:** 2026-06-10
**Decisores:** Felipe (arquiteto)

## Contexto

O núcleo do domínio é transacional: o invariante fundador do projeto exige gravar `NotificationRequest` e `OutboxMessage` na mesma transação ACID (a ausência desse invariante causou o incidente P0 no EasyStok). O metering é um ledger financeiro por natureza: append-only, sem débito duplo, agregação consistente. Ao mesmo tempo, payloads variam por canal e por tenant, o que sugere flexibilidade de schema. A operação alvo é um VPS único com custo mínimo.

## Decisão

PostgreSQL como único banco relacional do sistema. Schemas por contexto (`notifications`, `tenancy`, `metering`, `intelligence`), `tenant_id` em toda tabela de domínio, colunas JSONB com índice GIN para payloads variáveis, particionamento por mês em `delivery_attempts` quando o volume justificar. Redis permanece como key-value para quota, idempotência, rate limit e cache, que é o componente NoSQL certo no lugar certo.

## Opções consideradas

### Opção A: PostgreSQL

| Dimensão | Avaliação |
|---|---|
| Complexidade | Baixa, uma peça para operar, backup e monitorar |
| Custo | Zero licença, leve no VPS |
| Escalabilidade | Suficiente por anos com índices e particionamento |
| Familiaridade | Alta (stack do EasyStok) |

**Prós:** ACID real para outbox e ledger, JSONB cobre a flexibilidade necessária, EF Core maduro com Npgsql, uma única peça de estado relacional.
**Contras:** escala vertical primeiro; multi-região ativa-ativa não é trivial.

### Opção B: MongoDB

| Dimensão | Avaliação |
|---|---|
| Complexidade | Média, segunda tecnologia de dados para operar |
| Custo | Mais RAM no VPS, ou Atlas pago |
| Escalabilidade | Boa para documentos, fraca para ledger |
| Familiaridade | Baixa no contexto do projeto |

**Prós:** flexibilidade de schema nativa.
**Contras:** transação multi-documento existe mas é cidadã de segunda classe e fácil de usar errado; ledger financeiro e outbox em Mongo são nadar contra a corrente; nada aqui exige a força real dele.

### Opção C: DynamoDB / Cosmos DB

**Prós:** operação gerenciada, escala enorme.
**Contras:** acoplamento a nuvem específica, custo imprevisível, modelagem orientada a acesso que pune um domínio ainda em evolução, contraria o princípio de custo mínimo em VPS próprio.

## Análise de trade-off

O que se buscaria em NoSQL (payload flexível) o Postgres entrega com JSONB. O que o Postgres entrega (ACID no outbox e no ledger) o NoSQL não entrega com a mesma garantia e simplicidade. NoSQL se justificaria com throughput classe Cassandra ou multi-região ativa-ativa, cenários fora do horizonte do produto.

## Consequências

- Fica mais fácil: garantir o invariante do outbox, auditar o ledger, operar uma peça só, usar a experiência existente da stack.
- Fica mais difícil: um dia escalar escrita além de uma máquina grande.
- Mitigação: particionamento por tempo, réplicas de leitura, e o desenho modular permite extrair um contexto com seu schema para outra instância antes de qualquer troca de tecnologia.

## Gatilho de revisão

Volume sustentado acima da capacidade de uma instância grande com particionamento, ou requisito real de multi-região ativa-ativa.

## Itens de ação

1. [ ] F0: migration inicial já com schema por contexto e `tenant_id`.
2. [ ] F3: ledger em tabela append-only com constraint de não atualização.
3. [ ] Artigo derivado: JSONB me deu o Mongo que eu precisava dentro do Postgres.
