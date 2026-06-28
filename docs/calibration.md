# Ledger de calibração

Registro de probabilidade estimada versus resultado real por passo do plano de absorção do EasyStok
(plans/easystok-absorcao-total.md). Atualizar é parte do Definition of Done de cada passo.

P = M x C x R (maturidade do componente x cobertura de teste do caminho x precedente copiável).
Detalhe da fórmula na seção de medição do plano. Após cada passo, comparar o estimado com o real
calibra os pesos do próximo.

| Passo | P estimado | Fechou de primeira | Nota |
|---|---|---|---|
| 0.0 ADR-016 deploy k3s e KEDA no EasyStok | 0.90 | Sim | ADR de deploy: k3s single-node co-residente, Postgres compartilhado com database `hiram` e role de menor privilégio (sem RLS, sem superuser, sem BYPASSRLS), PgBouncer só para o Hiram (caminho do ERP intocado), orçamento de conexões versionado e `oom_score_adj` do Postgres. Topologia decidida com o usuário (k3s mais KEDA sobre compose co-localizado). Sem código, aceite do ADR. |
| 0.1 Dockerfiles Api e Dispatcher | 0.85 | Sim | Multi-stage non-root (.NET 10), build a partir da raiz, restore cacheado, publish Release (warnings as errors). Api serve em +:8080 via override de Kestrel por env; Dispatcher sem HTTP (runtime image, liveness por pgrep no k8s). `.dockerignore` enxuga o contexto e tira appsettings.Development.json da imagem. Job `docker-images` no CI valida o build das duas imagens. Local: `dotnet publish -c Release` dos dois OK; `docker build` valida no CI (sem Docker no dev). |
| 0.3 Data Protection key ring compartilhado | 0.80 | Sim | Fix localizado em `AddHiramDataProtection` (SetApplicationName mais PersistKeysToFileSystem) e wiring nos dois hosts via `DataProtection:KeysPath`. O teste cross-process exigiu um probe dedicado para ser um segundo processo do SO real, não in-proc. Vermelho reproduziu o bug exato (key not found in key ring), verde passou. Release sem warning, unit 112/112. Suíte Testcontainers não roda no dev local (Docker ausente), validação no CI. |
| 0.4 Migrations em produção via Job --migrate-only | 0.85 | Sim | `HiramSchema` (GenerateScript idempotente via IMigrator mais ApplyAsync); dispatch `--migrate-only [--dry-run]` no host; gate de Development trocado por flag `Hiram:MigrateOnStartup` (decisão tomada com o usuário) ligada em appsettings.Development.json, para não quebrar 9 testes que sobem o host. Dry-run Docker-free vermelho para verde local mais smoke do host real (exit 0, script idempotente sem DB). Ajuste em voo: top-level Main virou Task int, exigiu return explícito. Migrate_OnEmptyDb e Migrate_OnCurrentDb precisam de Docker, validam no CI. Release sem warning, unit 112/112. |
