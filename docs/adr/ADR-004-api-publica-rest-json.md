# ADR-004: API pública REST/JSON, gRPC adiado para adapter de batch

**Status:** Aceito
**Data:** 2026-06-10
**Decisores:** Felipe (arquiteto)

## Contexto

A API de ingestão é o produto: tenants de qualquer stack precisam integrar com o mínimo de atrito, e a documentação navegável (OpenAPI + Scalar) faz parte da proposta de valor como dev portal. Internamente, a comunicação entre módulos é assíncrona via RabbitMQ, então quase não existe RPC síncrono interno onde gRPC brilharia. Há, porém, interesse de portfolio em demonstrar transporte como detalhe de Clean Architecture.

## Decisão

API pública exclusivamente REST/JSON, versionada na rota (`/v1`), autenticada por API key, documentada com OpenAPI e Scalar, erros em ProblemDetails. gRPC não entra no MVP. Fica registrado como candidato pós-F6: um segundo adapter de ingestão em batch sobre o mesmo application service, adotado somente com benchmark medido (ADR-012).

## Opções consideradas

### Opção A: REST/JSON

| Dimensão | Avaliação |
|---|---|
| Complexidade | Baixa |
| Atrito de adoção | Mínimo: curl + API key em qualquer linguagem |
| Performance | Suficiente para ingestão unitária |
| Valor de produto | Alto: OpenAPI/Scalar é parte do dev portal |

**Prós:** integração universal, tooling maduro, documentação navegável de graça, webhooks de retorno já são HTTP de qualquer forma.
**Contras:** payload maior e serialização mais cara que protobuf em volume extremo.

### Opção B: gRPC

**Prós:** contratos fortes, streaming, eficiência de rede e CPU em alto volume.
**Contras:** exige code-gen no cliente, grpc-web e proxy para browser, atrito de adoção alto para tenants pequenos; sem RPC interno relevante nesta arquitetura, seria resume-driven engineering.

### Opção C: Ambos desde o início

**Prós:** cobertura total.
**Contras:** dobra superfície de teste, documentação e versionamento numa fase em que o produto precisa de foco; viola o princípio de escopo cruel.

## Análise de trade-off

O critério é adoção e foco, não elegância de protocolo. REST maximiza adoção e entrega o dev portal como diferencial. gRPC só agrega valor demonstrável num cenário de batch de alto volume, e esse valor precisa ser provado com números, não presumido.

## Consequências

- Fica mais fácil: onboard de tenant, documentação como produto, testes de contrato.
- Fica mais difícil: ingestão de lotes muito grandes com eficiência máxima, cenário hoje inexistente.
- Mitigação: endpoint de batch REST (array de notificações) cobre o caso intermediário antes de qualquer gRPC.

## Gatilho de revisão

Tenant real com necessidade de ingestão em lote de alto volume, ou a fase pós-F6 do portfolio. Nesse momento, ADR-012 com benchmark REST vs gRPC (p99, CPU, payload) para lote de 10 mil notificações decide.

## Itens de ação

1. [ ] F0: rota `/v1`, ProblemDetails e OpenAPI desde o primeiro endpoint.
2. [ ] F1: Scalar publicado como dev portal inicial.
3. [ ] Pós-F6: ADR-012 com benchmark antes de qualquer linha de gRPC.
