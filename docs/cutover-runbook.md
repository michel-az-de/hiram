# Runbook de provisionamento, shadow e corte (passos 1.10, 1.11 e Onda 2)

Operacional, executado contra o ambiente real. Fecha a Onda 1 (provisionamento e coleta de paridade) e
descreve o corte da Onda 2. Pré-requisitos: Onda 0 (deploy) e Onda 1 (motor) no ar; emissão do EasyStok
(guia em docs/easystok-emission-guide.md) emitindo em shadow.

## 1.10 Provisionamento e shadow

1. Para cada empresa do EasyStok, provisionar um tenant no Hiram (API admin `/v1/admin/tenants`),
   `DeliveryMode = shadow`, e uma API key (`/v1/admin/api-keys`).
2. Configurar o provider de email do tenant igual ao que o EasyStok usa hoje (mesmo SendGrid/SMTP, mesmo
   From e Return-Path), para o shadow comparar maçãs com maçãs (paridade de envelope).
3. Migrar dados para o Hiram com dry-run primeiro (contagem e diff, sem escrever), depois aplicar:
   - Templates do EasyStok -> templates do Hiram (com aprovação preservada do campo Aprovado).
   - Rotinas (mapa evento -> template + canais + categoria + janela/fuso).
   - Consentimento por usuário (dual-write já mantém sincronizado; a carga inicial popula o store).
4. Ligar a emissão do EasyStok em tee/shadow para email (feature flag por canal): o EasyStok continua
   entregando pelo canal local e emite o evento ao Hiram, que registra shadow sem enviar.

## 1.11 Coleta das três séries de paridade (pré-condição do soak)

Instrumentar e coletar, por tipo de evento, durante o shadow:

1. Contagem (dois lados): `easystok.notifications.sent` versus `hiram.notifications.shadowed`. Alerta
   separado para sobra (Hiram maior, risco de spam/LGPD) e falta (Hiram menor). Critério:
   `|hiram - easystok| / easystok` menor ou igual a 0.5% por tipo, 3 dias consecutivos.
2. Decisão (por evento): conjunto de canais escolhidos e conjunto de destinatários iguais entre os dois.
   Contagem igual com destinatário diferente é falha de decisão.
3. Conteúdo (hash canonicalizado): o EasyStok calcula o mesmo SHA-256 canônico (From/Return-Path,
   destinatário, assunto, corpo) que ELE enviaria; o Hiram já grava esse hash no DeliveryAttempt shadow.
   A função de canonicalização é a única do contrato congelado (remove unsubscribe, tracking, datas e
   ids voláteis). Critério: divergência menor ou igual a 0.1%, cada caso explicável.

Dashboard com as três séries lado a lado (ver docs/observability/slos.md). Gate da Onda 1 para a Onda 2:
as três séries atingidas por 7 dias contínuos, zero erro de ingestão, dual-write de consentimento
reconciliando.

## Onda 2: corte (referência)

1. 2.0 Cutover de leitura de consentimento (cross-channel, único): UI e worker do EasyStok passam a ler
   consentimento via API do Hiram; dual-write permanece ligado com soak; rollback é flag (volta a leitura
   ao local sincronizado). Precede o corte de entrega.
2. 2.2 Cutover de entrega por canary, fronteira por sequência (`emission_seq`): no instante T0 de uma
   empresa, captura W; do T0 em diante o Hiram entrega `> W` e o EasyStok para de enviar email local
   daquela empresa; o local drena o backlog `<= W` (drain completo = nenhum pendente `<= W` E nenhuma
   transação pré-T0 aberta). Rollback move W e religa o local.
3. 2.3 Callbacks de provider (bounce/complaint/delivery): tornam a entrega real observável; até lá
   "sent" significa "aceito pelo provider".
4. Expandir o canary com taxa de entrega maior ou igual ao baseline local e bounce menor ou igual ao
   baseline. Critério de pronto do programa: EasyStok migrado e estável por 30 dias, zero notificação
   perdida.
