# F2 parte 2, relatório de fechamento

> Templates de mensagem com Scriban. Relatório de Definição de Pronto item a item, com evidências e desvios. Acompanha o plano em plans/F2-2-templates.md e a decisão em docs/adr/ADR-013-template-engine.md.

## Definição de pronto

| Item | Status | Evidência |
|---|---|---|
| ADR-013 aceita antes do código (escolha de motor e momento de render) | Feito | `docs/adr/ADR-013-template-engine.md`, commit `docs: add adr-013 template rendering engine`. |
| Entidade `Template` com invariantes e update | Feito | `Hiram.Domain.Templates.Template`, `UpdateContent`. Testes: `TemplateTests`. |
| Schema `templates` com unique por tenant, canal e nome | Feito | `TemplateConfiguration`, índice `ux_templates_tenant_channel_name`, migration `AddTemplates`. Teste: `TemplateStoreTests`. |
| Render com Scriban, modo estrito, validação de sintaxe | Feito | Porta `ITemplateRenderer`, adapter `ScribanTemplateRenderer` (StrictVariables, `TryValidate`, normalização de JsonElement). Testes: `ScribanTemplateRendererTests`. |
| CRUD de templates escopado por tenant | Feito | `POST/GET/GET{id}/PUT/DELETE /v1/templates`, 400 em sintaxe inválida, 409 em nome duplicado, escopo por tenant. Testes: `TemplateEndpointsTests`. |
| Segundo modo do submit: template mais dados, render no submit | Feito | `SubmitNotificationRequest` ganhou `Template` e `Data`; o endpoint valida o modo, resolve, renderiza e persiste o conteúdo final. Testes: e2e `TemplatedSubmit_RendersAndDeliversToMailpit`, mais `TemplatedSubmit_MissingTemplate_Returns404` e `TemplatedSubmit_MissingVariable_Returns400`. |
| Nenhuma migration aplicada alterada, build Release sem warning, suíte unit verde | Feito | Migration nova `AddTemplates`. Build Release `0 Aviso(s)`. 87 testes unitários verdes localmente. |
| Biblioteca nova só com ADR | Feito | Scriba 7.2.5 adicionada sob a ADR-013. Nenhuma outra dependência nova. |

## Desvios e notas

### Verificação local sem Docker

Como na F2 parte 1, o ambiente não tinha Docker, então a suíte de integração com Testcontainers não rodou inteira localmente. O gate local foi build Release sem warning mais os 87 testes unitários, e os testes do `ScribanTemplateRenderer` rodaram localmente por filtro, já que não usam container. O restante da integração e o e2e são validados pela CI. Confirmar a run verde após o push.

### Render no submit, não no envio (ADR-013)

O conteúdo é renderizado na borda síncrona do submit e persistido na `NotificationRequest` e no payload do outbox. Isso mantém a fidelidade decidida na ADR-011: o replay reenvia o conteúdo armazenado, sem re-renderizar. O dispatcher, o dead letter e o replay continuam tratando `subject` e `body` opacos, sem conhecer template.

### Variáveis estritas

Variável usada e não fornecida falha a renderização e vira 400 no submit, em vez de enviar mensagem meio renderizada. Erro de sintaxe é pego no cadastro do template, também com 400. Modo leniente com valor padrão fica deferido.

### PII em repouso

O conteúdo renderizado fica em `notification_requests` e no payload do outbox, com a mesma exposição já existente de `body`. Sem mudança de postura nesta fatia.

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

# cadastra um template e envia só os dados:
curl -s -X POST http://localhost:3357/v1/templates -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"channel":"email","name":"welcome","subject":"Hi {{ name }}","body":"Welcome {{ name }}"}'

curl -i -X POST http://localhost:3357/v1/notifications -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"ops@example.com","template":"welcome","data":{"name":"Ada"}}'
```

Esperado: o email entregue no Mailpit traz `Hi Ada` no assunto; enviar com `data` sem `name` devolve 400, e referenciar um template inexistente devolve 404.
