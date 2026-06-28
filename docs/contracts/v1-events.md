# Contrato congelado: POST /v1/events (Onda 1, passo 1.0)

Fonte única referenciada pelos dois repositórios. O cliente do EasyStok é gerado por codegen a partir
do OpenAPI do Hiram, não espelhado à mão. Mudança aqui é breaking e exige bump de versão. Os tipos C
estão em `Hiram.Contracts` (`SubmitEventRequest`, `EventRecipient`, `EventAccepted`).

## Corpo do POST /v1/events

O tenant vem da API key (header `X-Api-Key`), não do corpo.

| Campo | Tipo | Papel |
|---|---|---|
| eventType | string | tipo canônico do evento (tabela abaixo) |
| eventId | string | idempotência de evento (o OutboxId do EasyStok); índice único (tenant_id, event_id) |
| emissionSeq | long | sequência monotônica do banco do EasyStok; atributo de watermark do cutover |
| recipient | objeto | contato no instante da emissão (userId, email, phone) |
| logicalAlertId | string? | dedupe de alerta lógico recorrente ao longo de dias |
| timezone | string? | fuso do tenant para o adiamento por janela (IANA) |
| data | objeto? | variáveis para o template |

## Vocabulário canônico dos tipos de evento (mapa do enum do EasyStok)

snake_case do nome do enum `TipoEventoNotificacao`. Nunca trafegar o ordinal.

| EasyStok | eventType canônico |
|---|---|
| ProdutoVencendo | produto_vencendo |
| ProdutoVencido | produto_vencido |
| TarefaPendente | tarefa_pendente |
| ResetSenha | reset_senha |
| AssinaturaExpirando | assinatura_expirando |
| AssinaturaExpirada | assinatura_expirada |
| BroadcastSuperAdmin | broadcast_super_admin |
| ConfirmacaoEmail | confirmacao_email |
| AlertaEstoqueCritico | alerta_estoque_critico |
| TicketCriado | ticket_criado |
| TicketRespondidoCliente | ticket_respondido_cliente |
| TicketRespondidoAdmin | ticket_respondido_admin |
| TicketStatusAlterado | ticket_status_alterado |
| TicketAtribuido | ticket_atribuido |
| TicketEncaminhadoNivel | ticket_encaminhado_nivel |
| SlaProximoVencer | sla_proximo_vencer |
| SlaViolado | sla_violado |
| BugFixCriado | bug_fix_criado |
| FaturaCriada | fatura_criada |
| FaturaVencendo | fatura_vencendo |
| FaturaPaga | fatura_paga |
| FaturaVencida | fatura_vencida |
| PagamentoConfirmado | pagamento_confirmado |
| PagamentoFalhou | pagamento_falhou |
| ConviteCsat | convite_csat |
| PedidoAgendadoHoje | pedido_agendado_hoje |
| PedidoAgendadoEm1Hora | pedido_agendado_em_1_hora |
| PedidoAgendadoEm10Minutos | pedido_agendado_em_10_minutos |
| RelatorioPronto | relatorio_pronto |
| RelatorioFalhou | relatorio_falhou |
| RelatorioExpirado | relatorio_expirado |
| ContaPagarVencendo | conta_pagar_vencendo |
| ContaPagarVencida | conta_pagar_vencida |
| ContaReceberVencendo | conta_receber_vencendo |
| ContaReceberVencida | conta_receber_vencida |
| ParcelaRecebida | parcela_recebida |
| CaixaAbertoEsquecido | caixa_aberto_esquecido |

Tipo de evento desconhecido pelo Hiram vira dead-letter mais alerta, nunca accept-and-drop silencioso.

## Mapa de canais por nome

Os ordinais divergem entre os dois sistemas (no EasyStok Push=5; no Hiram Push=2). Trafegar sempre o
nome, nunca o ordinal.

| EasyStok CanalNotificacao | nome no wire |
|---|---|
| Email | email |
| Sms | sms |
| WhatsApp | whatsapp |
| InApp | inapp |
| Push | push |

## Labels de métrica OTel

Padronizados e idênticos nos dois sistemas: `event_type`, `channel`, `outcome`. O label `tenant` não
entra nas séries de alto volume (cardinalidade na VM pequena); usar subconjunto curado ou exemplars.

## Canonicalização para o hash de paridade

Uma única função, idêntica nos dois lados, usada pela migração de templates (1.3) e pela auditoria de
paridade (2.1). Antes do hash, normalizar e remover os campos voláteis: token de unsubscribe, query
params de tracking, pixel de tracking, datas e horas renderizadas, ids únicos por envio. A paridade
verifica também o envelope (From, Return-Path, domínio), não só o corpo.
