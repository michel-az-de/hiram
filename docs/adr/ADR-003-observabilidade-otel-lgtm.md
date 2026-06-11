# ADR-003: Observabilidade com OpenTelemetry e Grafana LGTM self-hosted

**Status:** Aceito
**Data:** 2026-06-10
**Decisores:** Felipe (arquiteto)

## Contexto

Observabilidade é requisito de primeira classe (trace de ponta a ponta do request à confirmação do provider) e também ativo de portfolio. Restrições: custo zero de licença, operação em VPS único com RAM limitada, dados sob controle próprio. O ciclo de dev local precisa de feedback de traces sem subir uma stack pesada.

## Decisão

Instrumentação exclusivamente via OpenTelemetry SDK em todos os hosts (a skill vendor-neutral é o ativo). Em produção, stack Grafana LGTM self-hosted no próprio k3s: Prometheus (métricas), Loki (logs), Tempo (traces), Grafana (dashboards e alertas). No dev, Aspire Dashboard standalone como receiver OTLP no Docker Compose. Trocar de destino é trocar o endpoint do exporter.

## Opções consideradas

### Opção A: Grafana LGTM self-hosted

| Dimensão | Avaliação |
|---|---|
| Complexidade | Média, quatro componentes para operar |
| Custo | Zero licença, roda no VPS já pago |
| Aderência | Padrão de mercado, conta história de operação real |
| Consumo | 1 a 2 GB com retenção curta e sampling |

**Prós:** padrão da indústria, separação limpa por sinal, experiência de operação que vira artigo.
**Contras:** mais peças que uma solução tudo-em-um; exige disciplina de retenção num VPS pequeno.

### Opção B: SigNoz

**Prós:** tudo-em-um OTel-nativo sobre ClickHouse, UX de APM comercial, ótimo para dev solo.
**Contras:** ClickHouse é faminto de RAM e compete com a aplicação no mesmo VPS.

### Opção C: OpenObserve

**Prós:** binário único em Rust, levíssimo, ideal para VPS mínimo.
**Contras:** ecossistema e comunidade menores, menos valor de mercado como experiência declarável.

### Opção D: Grafana Cloud free tier

**Prós:** zero operação.
**Contras:** limites apertam rápido, dados fora da infra própria, e o objetivo de portfolio é justamente demonstrar operação self-hosted.

## Análise de trade-off

A decisão de instrumentação (OTel) vale mais que a decisão de backend e desacopla as duas: qualquer opção B, C ou D continua disponível mudando configuração. Entre os backends, LGTM maximiza valor de portfolio e conhecimento de mercado ao custo de disciplina operacional, que é exatamente a história que se quer contar.

## Consequências

- Fica mais fácil: depurar o caminho assíncrono (trace atravessa API, fila e consumer), publicar dashboards reais em artigos, trocar de backend no futuro.
- Fica mais difícil: nada de relevante, dado que a operação é objetivo e não custo.
- Mitigações obrigatórias: retenção curta (ex.: 7 dias de traces, 14 de logs), tail sampling, alerta de disco, limites de memória por container.

## Gatilho de revisão

Se a stack LGTM consumir recursos que comprometam a aplicação no VPS, degradar para OpenObserve sem tocar na instrumentação.

## Itens de ação

1. [ ] F0: OTel SDK nos hosts com OTLP exporter, Aspire Dashboard no compose de dev.
2. [ ] F0: propagação de trace context pelos headers do RabbitMQ.
3. [ ] F6: LGTM no k3s com retenção e sampling definidos em runbook.
