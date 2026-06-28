# ADR-016: Deploy do Hiram em k3s e KEDA no ambiente do EasyStok

**Status:** Aceito
**Data:** 2026-06-28
**Decisores:** Felipe (arquiteto)

## Contexto

O Hiram vai absorver as notificações do EasyStok e rodar toda a sua infraestrutura no ambiente do
EasyStok (plano em plans/easystok-absorcao-total.md). O ambiente de produção real do EasyStok é uma VM
Azure única que sobe Web, Admin, API, Postgres e Caddy via `docker-compose.azure.yml`, com Postgres 17
em container (volume persistente), Redis 7 efêmero, secrets em `.env` e TLS automático pelo Caddy. Não
há RabbitMQ. O MASTER-PLAN já apontava k3s mais KEDA num VPS único como destino (placeholder ADR-010,
nunca escrito); este ADR é a decisão concreta para coabitar com o EasyStok.

O risco central não é o Hiram, é o ERP: a infraestrutura de notificação não pode derrubar o EasyStok
por tempestade de conexões no Postgres compartilhado nem por contenção de memória na VM. E não pode
repetir a classe do P0 de bypass de RLS do EasyStok (role com `rolsuper`/`rolbypassrls`).

## Decisão

k3s single-node co-residente na VM do EasyStok, com KEDA escalando o Dispatcher por profundidade de
fila do RabbitMQ. O RabbitMQ e o Redis do Hiram sobem como cargas no cluster, com volume persistente
no broker. O Postgres é o mesmo container do EasyStok, com um database `hiram` dedicado e um role de
menor privilégio. As conexões do Hiram passam por um PgBouncer dedicado ao Hiram; o caminho de dados do
EasyStok não é tocado. Todo pod tem requests e limits, o teto de réplica do Dispatcher é amarrado a um
orçamento de conexões versionado, e o processo do Postgres é protegido do OOM killer.

## Opções consideradas

### Opção A: k3s mais KEDA single-node na VM, Postgres compartilhado com database dedicado

**Prós:** escala o Dispatcher a partir da profundidade de fila (o caso de uso natural do KEDA, alinhado
ao ADR-005 do Rabbit); é o destino já desenhado no MASTER-PLAN, vira artigo e competência; isola o
database do Hiram sem custo de uma segunda instância de Postgres; orquestrador real com health probes,
limits e rollout.
**Contras:** dois orquestradores na mesma VM (docker-compose do EasyStok e k3s do Hiram) elevam a carga
operacional; k3s sem precedente operacional no EasyStok; exige disciplina dura de orçamento de recursos
para não sufocar o ERP.

### Opção B: estender o docker-compose.azure.yml (compose co-localizado, sem k3s)

**Prós:** caminho mais curto, sem orquestrador novo, reusa o padrão que o time já opera.
**Contras:** sem auto-scaling do Dispatcher por fila (o KEDA é o ponto do desenho); escalar é manual
(`--scale`); não exercita o destino de produção planejado. Foi a recomendação técnica de menor atrito,
mas o usuário optou pelo destino k3s mais KEDA.

### Opção C: VM separada, ou Kubernetes gerenciado, ou Postgres gerenciado

**Prós:** isola o blast radius do ERP por completo; banco gerenciado tira o fardo de backup e tuning.
**Contras:** custo recorrente contra a operação self-hosted de custo mínimo; mais máquinas e rede entre
os dois sistemas; contraria a restrição de ambiente único.

## Decisões de borda cravadas

1. **PgBouncer só para o Hiram.** Instância e porta próprias, na frente apenas das conexões do Hiram. O
   caminho de dados do EasyStok não passa pelo pooler: pôr PgBouncer na frente do ERP seria mudança em
   produção do EasyStok (fura a cerca additive-only) e quebraria os advisory locks de sessão que o
   outbox do EasyStok usa, pois transaction pooling é incompatível com locks de sessão, LISTEN/NOTIFY,
   GUCs de sessão e prepared statements server-side. As conexões do EasyStok entram no orçamento como
   alocação fixa medida.
2. **Orçamento de conexões versionado.** Invariante: `pool_servidor_do_PgBouncer_ao_Postgres +
   alocacao_fixa_do_ERP + reserva_admin` menor que `max_connections`. Como o Hiram fala com o
   PgBouncer e o PgBouncer multiplexa para o Postgres, a pegada física do Hiram no Postgres é o pool de
   servidor do PgBouncer, não o número de réplicas do Dispatcher. Exemplo a confirmar contra a VM
   (`max_connections` padrão 100): ERP 40, reserva admin e `pg_dump` 10, PgBouncer `default_pool_size`
   20 ao Postgres, sobra de folga. O `maxReplicaCount` do KEDA é então limitado pela capacidade de
   cliente do PgBouncer, não pelas conexões físicas do Postgres. Os números finais são calculados
   contra o `max_connections` real e os pools do EasyStok, e versionados, não estimados em runtime.
3. **Pool por réplica explícito.** Cada réplica do Hiram tem `Maximum Pool Size` baixo e explícito por
   host, para o orçamento fechar.
4. **Postgres fora do conjunto que escala e protegido de OOM.** O Postgres é compartilhado e não roda no
   k3s. Limits nos pods não bastam: o OOM killer pontua por RSS e o Postgres é o maior alvo. Reservar
   memória e proteger o processo do Postgres com `oom_score_adj` negativo, para que o ERP nunca seja a
   vítima.
5. **Requests e limits em todo pod do Hiram.** Orçamento de memória explícito da VM: a soma dos limits
   do compose do EasyStok mais os limits do k3s deixa folga para o kernel.
6. **Isolamento por menor privilégio no Postgres compartilhado.** O Hiram usa filtros de tenant na
   aplicação (EF), não RLS, coerente com o desenho atual. O role do Hiram é dono apenas do database
   `hiram`, sem superuser, sem `BYPASSRLS`, sem acesso aos schemas do EasyStok. Database dedicado mais
   role de menor privilégio fecham o blast radius cruzado, sem repetir a classe do P0 de RLS do EasyStok.
7. **Dispatcher escala por fila, base 1 réplica.** KEDA usa a profundidade da fila do RabbitMQ. O
   key ring de Data Protection compartilhado (ADR sem número, já corrigido no passo 0.3) é pré-condição
   de qualquer réplica maior que 1, senão réplicas distintas não decifram os segredos de tenant.
8. **RabbitMQ com volume persistente.** O dev não persiste o broker; produção persiste, senão um
   restart perde estado de DLQ e replay. A recuperação até a publicação é do outbox; depois da
   publicação confirmada, é do broker (ver semântica no plano).

## Consequências

- **Fica mais fácil:** escalar o Dispatcher a zero e de volta com o KEDA; aplicar health probes, limits
  e rollout do k3s; isolar migrations, backup e permissões do Hiram pelo database dedicado; auditar o
  blast radius pelo role de menor privilégio.
- **Fica mais difícil:** operar dois orquestradores na mesma VM; calcular e manter o orçamento de
  conexões e de memória; garantir o `oom_score_adj` do Postgres fora do k3s.

## Gatilho de revisão

Se o orçamento de conexões ou de memória não fechar na VM atual, parar e mover para Postgres dedicado
ou nó separado, em vez de prosseguir e arriscar o ERP. Também revisar se o volume exigir cluster de
RabbitMQ (hoje single-node, perda de disco é risco residual aceito) ou se a coabitação k3s mais compose
se mostrar instável.

## Itens de ação

1. [ ] Dockerfiles de Api e Dispatcher (.NET 10, multi-stage, non-root). Passo 0.1.
2. [ ] Secrets por env mais `.env.hiram.example`. Passo 0.2.
3. [x] Key ring de Data Protection compartilhado. Passo 0.3.
4. [x] Migrations em produção via Job `--migrate-only`. Passo 0.4.
5. [ ] Health e readiness endpoints. Passo 0.5.
6. [ ] Graceful shutdown com draining. Passo 0.6.
7. [ ] Database `hiram`, role de menor privilégio, backup lógico e do volume do key ring. Passo 0.7.
8. [ ] PgBouncer dedicado ao Hiram e orçamento de conexões com números contra a VM. Passo 0.8.
9. [ ] Manifests k3s, RabbitMQ e Redis no cluster com persistência, requests e limits. Passo 0.9.
10. [ ] KEDA ScaledObject por profundidade de fila, com `maxReplicaCount` do orçamento. Passo 0.10.
11. [ ] `oom_score_adj` e reserva de memória do Postgres na VM. Passo 0.9.
