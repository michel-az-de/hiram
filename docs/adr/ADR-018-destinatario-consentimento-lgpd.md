# ADR-018: Destinatário, contato e consentimento LGPD no Hiram

**Status:** Aceito
**Data:** 2026-06-28
**Decisores:** Felipe (arquiteto)

## Contexto

A absorção total move a decisão de consentimento para o Hiram. Consentimento é por usuário e cruza
canais (por usuário, ou por usuário mais categoria), não por canal. O email-first cria uma restrição
de sequência: quando o consentimento migra para o Hiram, os canais que o EasyStok ainda serve (SMS,
WhatsApp, InApp) precisam do mesmo consentimento por usuário. Sem tratar isso, vira split-brain de
consentimento, com risco LGPD real. Além disso, o Hiram precisa saber para onde enviar (o contato), e
o contato é dado do EasyStok.

## Decisão

O Hiram passa a ser dono do store de consentimento e da API de consentimento. O contato (email,
telefone) viaja no payload do evento, no instante da emissão. A transição usa dual-write em shadow e um
cutover de leitura único e cross-channel, mantendo o dual-write ligado com soak para o rollback ser uma
flag.

## Opções consideradas

### Opção A: consentimento no Hiram, contato no payload (escolhida)

**Prós:** consentimento centralizado onde a decisão de envio acontece; contato fresco no momento do
evento, sem sync de contato por usuário.
**Contras:** transição sensível (LGPD); EasyStok precisa ler consentimento via API enquanto servir
algum canal.

### Opção B: consentimento fica no EasyStok, Hiram consulta a cada envio

**Prós:** sem migração de dados.
**Contras:** inverte a dependência e acopla o Hiram ao ERP; latência por envio.

### Opção C: contato no store do Hiram (sync por usuário)

**Prós:** Hiram autossuficiente.
**Contras:** sync de contato por usuário, dado volátil, fonte de drift; rejeitada.

## Decisões de borda cravadas

1. **Contato no payload.** O EasyStok é a fonte de verdade do contato; ele vai no evento. Trade-off:
   se o usuário trocar o email entre emissão e envio, usa-se o da emissão. Aceitável para transacional.
2. **Consentimento move como unidade, não por canal.** Por ser por usuário cross-channel, a autoridade
   de escrita não migra canal a canal.
3. **Dual-write em shadow.** A UI do EasyStok grava no store local e, na mesma operação, chama a API de
   consentimento do Hiram. Falha do Hiram não bloqueia o EasyStok (best effort com reconciliação). A
   auditoria de paridade ignora eventos cujo consentimento mudou dentro de uma janela de carência.
4. **Cutover de leitura único e cross-channel.** A autoridade de leitura migra para o Hiram; UI e
   worker do EasyStok passam a ler consentimento via API do Hiram (não só a UI), com cache curto e
   fail-safe conservador (na dúvida, não enviar). O dual-write permanece ligado com soak; só depois a
   escrita local é desligada. Rollback vira flag (volta a leitura ao store local já sincronizado). O
   cutover de consentimento acontece no, ou antes do, primeiro cutover de entrega.

## Consequências

- **Fica mais fácil:** decisão de consentimento única e auditável; rollback leve por flag.
- **Fica mais difícil:** migração de dados de consentimento; EasyStok depende da API de consentimento
  do Hiram enquanto servir canais; consequência legal exige cuidado no cutover.

## Gatilho de revisão

Mudança no modelo de consentimento (por canal, por finalidade), ou requisito de residência de dados.

## Itens de ação

1. [ ] Modelo de destinatário e consentimento no Hiram, contato no payload (passo 1.4).
2. [ ] Dual-write em shadow e reconciliação (passo 1.4).
3. [ ] Cutover de leitura cross-channel com soak e rollback por flag (passo 2.0).
