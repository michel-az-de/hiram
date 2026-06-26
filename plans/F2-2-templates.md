# F2 parte 2, templates com Scriban

> Plano executável no estilo de plans/F0, F1 e F2-1. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main. Em nenhum texto use travessão (em dash). Decisão estrutural na ADR-013, aberta antes do código.

## Objetivo

A segunda frente da F2: templates de mensagem por tenant. Resultado demonstrável:

1. O tenant cadastra, lista, lê, atualiza e apaga templates por canal e nome, escopados a ele.
2. Sintaxe Scriban inválida é rejeitada no cadastro com 400; nome duplicado por canal devolve 409.
3. O `POST /v1/notifications` ganha um segundo modo: em vez de `subject` e `body`, o cliente manda `template` e `data`. O conteúdo é renderizado no submit e persistido.
4. Variável faltando vira 400 no submit (modo estrito); template inexistente vira 404.
5. O email renderizado chega ao Mailpit no e2e.
6. Build Release sem warning, suíte verde, CI verde.

Sequenciamento da F2: parte 1 dead letter e replay (feita), **parte 2 templates (esta)**, depois Web Push (VAPID), depois webhooks (HMAC).

## Decisões técnicas fixas

Detalhe e trade-off na ADR-013 (docs/adr/ADR-013-template-engine.md):

- Motor: Scriban, biblioteca nova adicionada sob esta ADR. Nenhuma outra dependência.
- Render no submit, na borda síncrona, com o conteúdo final persistido. Coerente com a fidelidade de replay da ADR-011: o dispatcher, o dead letter e o replay continuam tratando `subject` e `body` opacos.
- Variáveis estritas: variável usada e não fornecida falha a renderização e vira 400. Sintaxe validada no cadastro.
- Template único por `(tenant_id, channel, name)`, resolução por nome e canal, sempre escopada ao tenant.
- Sem execução de código no template. Cache de template em Redis fica deferido.

## Passos

1. **ADR-013** (`docs: add adr-013 template rendering engine`). Escolha do motor, momento de render, decisões de borda.
2. **Domínio** (`feat: add template domain model`). Entidade `Template` com invariantes e `UpdateContent`. Testes unitários.
3. **Persistência** (`feat: persist templates per tenant`). DbSet, `TemplateConfiguration` com unique parcial por tenant, canal e nome, migration `AddTemplates`, porta `ITemplateStore` e `TemplateStore`. Teste de integração.
4. **Renderer** (`feat: render templates with scriban`). Pacote Scriban, porta `ITemplateRenderer` e `TemplateRenderException`, adapter `ScribanTemplateRenderer` com modo estrito, validação de sintaxe e normalização de JsonElement. Testes do renderer.
5. **CRUD** (`feat: expose template management endpoints`). Contratos e endpoints `POST/GET/GET{id}/PUT/DELETE /v1/templates`, escopados por tenant, 400 em sintaxe, 409 em duplicado. Testes de integração.
6. **Submit com template** (`feat: submit notifications from a template`). `SubmitNotificationRequest` ganha `Template` e `Data`; o endpoint valida o modo, resolve, renderiza no submit, 400 em erro de render e 404 em template inexistente. Testes de integração e e2e.
7. **Fechamento** (`docs: document f2 part two templates`). README, relatório e este plano.

## Definição de pronto

Ver checklist completo em docs/F2-2-relatorio.md.

## Não-objetivos e deferidos

Partials e includes entre templates. Versionamento de template. Multi-idioma com fallback. Modo leniente com valor padrão para variável faltando. Render no envio para late binding. Cache de template em Redis. Templates de push, SMS ou WhatsApp (canais ainda não existem). Tudo isso reabre pela ADR-013 ou pelas fases próprias.
