# Runbook operacional do Hiram Core

## 1. Topologia suportada

O runtime padrão contém uma imagem `hiram` e PostgreSQL 17. API e workers executam no mesmo
processo. SMTP ou Resend fazem a última milha; um coletor OTLP é opcional.

Copie `.env.hiram.example`, troque todos os valores `CHANGE_ME` e mantenha o arquivo fora do
repositório. O diretório indicado por `DataProtection__KeysPath` deve ser persistente e acompanhado
pelo mesmo ciclo de backup do banco.

Antes de cada rollout, execute a migration com a mesma imagem que será publicada:

```bash
docker run --rm --env-file .env ghcr.io/michel-az-de/hiram:<sha> --migrate-only
docker compose up -d
curl --fail http://localhost:3357/health/live
curl --fail http://localhost:3357/health/ready
```

`live` verifica o processo. `ready` verifica PostgreSQL e a configuração mínima. Não habilite
`Hiram__MigrateOnStartup` em produção.

## 2. Onboarding de tenant

Comece em `shadow` para provar autenticação, persistência e resolução do provider sem enviar:

```bash
curl --fail-with-body -X POST https://hiram.example/v1/admin/tenants \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Projeto","deliveryMode":"shadow"}'
```

Guarde o `id` retornado e emita uma chave. A chave em claro aparece uma única vez:

```bash
curl --fail-with-body -X POST https://hiram.example/v1/admin/api-keys \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<tenant-id>","name":"producao"}'
```

Armazene a chave no cofre do projeto consumidor. Configure o provider do próprio tenant, por
exemplo SMTP:

```bash
curl --fail-with-body -X PUT https://hiram.example/v1/providers/email \
  -H "X-Api-Key: $HIRAM_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"provider":"smtp","settings":{"host":"smtp.example.com","port":"587","from":"notifications@example.com","security":"starttls","username":"user"},"secret":"<password>"}'
```

Envie uma notificação com `Idempotency-Key` e confirme estado e tentativas em
`GET /v1/notifications/{id}`. A API atual não altera o modo de um tenant existente. Depois de provar
o fluxo em shadow, crie o tenant de produção com `deliveryMode: live`, configure seu provider e emita
uma chave própria. Não reutilize a chave de homologação.

## 3. Rotação de API key

1. Emita uma segunda chave com nome identificável.
2. Atualize o segredo do consumidor e confirme tráfego autenticado com a chave nova.
3. Revogue a antiga pelo `id`, nunca pelo valor em claro:

```bash
curl --fail-with-body -X DELETE https://hiram.example/v1/admin/api-keys/<api-key-id> \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY"
```

Não revogue antes de observar o consumidor novo. Resposta `404` exige conferir o `id`; não repita a
operação contra outra chave.

## 4. Provider indisponível

Confirme primeiro PostgreSQL e `/health/ready`. Depois filtre logs por `notification_id`, provider e
tenant, sem registrar recipient, body ou segredo. Falhas transitórias permanecem no outbox até
`available_at`; falhas esgotadas viram dead letter.

Se o provider estiver indisponível:

1. preserve os workers ligados para que a política de retry controle a pressão;
2. não reduza `available_at` manualmente;
3. acompanhe backlog, item elegível mais antigo e dead letters;
4. corrija credencial, rede ou provider;
5. observe a drenagem e a taxa de `sent`;
6. faça replay somente de dead letters cujo motivo foi entendido.

## 5. Fila crescente e leases

As consultas abaixo são operacionais e não devem virar endpoints públicos. Execute com uma role de
leitura, sempre em conexão segura.

```sql
SELECT count(*) AS backlog
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL;

SELECT min(available_at) AS oldest_eligible_at
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND available_at <= now()
  AND (lease_until IS NULL OR lease_until <= now());

SELECT count(*) AS expired_leases
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND lease_until <= now();

SELECT type, attempt_count, last_error, available_at, lease_until
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
ORDER BY created_at_utc
LIMIT 50;
```

Backlog com `available_at` futuro pode ser retry normal. Backlog elegível crescendo, sem
`hiram.outbox.dispatched`, indica worker parado ou incapaz de reivindicar. Lease vencido volta a ser
elegível automaticamente; não altere a linha manualmente.

## 6. Dead letter e replay

```sql
SELECT id, tenant_id, notification_id, channel, reason, attempt_count, created_at_utc
FROM notifications.dead_letter_messages
WHERE replayed_at_utc IS NULL
ORDER BY created_at_utc;
```

Depois de corrigir a causa, use a API autenticada do tenant:

```bash
curl --fail-with-body -X POST \
  https://hiram.example/v1/notifications/<notification-id>/replay \
  -H "X-Api-Key: $HIRAM_API_KEY"
```

O replay cria trabalho novo de forma auditável. Não edite status, outbox ou dead letter diretamente.

## 7. Resultado incerto pós-provider

Timeout ou queda depois da chamada pode esconder um aceite do provider. Antes de qualquer replay,
consulte `GET /v1/notifications/{id}` e correlacione `delivery_attempts.provider_message_id` com o
painel ou API do provider. Se houver aceite, reconcilie o estado sem reenviar. Se não houver evidência
conclusiva, escale e preserve a linha, os logs e o trace. Reenvio cego pode duplicar comunicação e é
proibido.

## 8. Backup e restore

Execute `deploy/dr/hiram-backup.sh` diariamente com `PGHOST`, `PGUSER`, `BACKUP_DIR` e
`KEYRING_DIR`. O destino deve sair do host. A ausência do key ring torna o backup incompleto e faz o
script falhar.

Restaure em ambiente isolado:

```bash
createdb hiram_restored
pg_restore --exit-on-error --dbname=hiram_restored hiram-<stamp>.dump
tar -xzf hiram-keyring-<stamp>.tar.gz -C /var/hiram/dataprotection-keys
```

Valide contagens, um tenant, uma notificação e a leitura de configuração cifrada antes de promover.
`deploy/dr/verify-backup.sh` automatiza uma prova descartável de banco e key ring e roda no CI.

## 9. Observabilidade e escalonamento

Monitore as métricas documentadas em `deploy/observability/slos.md`, `/health/ready`, backlog e
dead letters. Registre em todo incidente: início, tenant afetado, provider, IDs de notificação,
primeiro sintoma, ação tomada e critério de encerramento. Nunca inclua body, recipient, API key ou
segredo do provider.
