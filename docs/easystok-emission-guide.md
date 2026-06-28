# Guia de implementação: emissão durável EasyStok -> Hiram (passo 1.9)

Este passo vive no repositório do EasyStok (`C:\easy\EasyStok`, .NET 9), que é produção do ERP. Por
segurança, ele é entregue como guia executável em vez de código não validado commitado no repo do ERP:
a validação é ao vivo, do lado do EasyStok. Segue os padrões já existentes lá: `OutboxEventoIntegracao`
(entidade de outbox de integração) e `IntegrationOutboxBackgroundService` (relay por polling).

O contrato de destino já está congelado no Hiram: `POST /v1/events` com `SubmitEventRequest`
(`docs/contracts/v1-events.md`), incluindo `emission_seq`.

## Por que uma outbox dedicada (e não reusar OutboxEventoIntegracao)

A `OutboxEventoIntegracao` tem `ShardKey = hash[0] % 4` (shard por hash) e `Id` GUID, sem sequência
monotônica. O cutover por watermark (ADR-017) exige uma sequência monotônica por tenant. Por isso a
emissão para o Hiram usa uma tabela dedicada com `emission_seq bigserial`. Aditivo, não toca a outbox
de integração existente nem os advisory locks de sessão do EasyStok.

## 1. Entidade (EasyStock.Domain/Notifications ou Integration)

```csharp
public sealed class HiramEmissao
{
    public Guid Id { get; private set; }
    public long EmissionSeq { get; private set; }      // bigserial atribuído pelo banco (watermark)
    public Guid EmpresaId { get; private set; }
    public string EventType { get; private set; }      // string canônica (ver mapa dos 37 tipos)
    public string PayloadJson { get; private set; }    // recipient + data + logicalAlertId + timezone
    public StatusEmissao Status { get; private set; }  // Pendente, Enviada, Falhada
    public int Tentativas { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? EnviadoEm { get; private set; }
    public string? ErroUltimaTentativa { get; private set; }
    // EmissionSeq NUNCA é setado no código: vem do banco (ValueGeneratedOnAdd + bigserial).
}
```

## 2. EF config

```csharp
b.ToTable("hiram_emissoes");
b.HasKey(x => x.Id);
b.Property(x => x.EmissionSeq).HasColumnName("emission_seq").ValueGeneratedOnAdd();   // bigserial
b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
// ... tentativas, criado_em, enviado_em, erro
b.HasIndex(x => new { x.Status, x.EmissionSeq })
    .HasFilter("status = 1")              // pendentes em ordem de sequência, para o relay flag-based
    .HasDatabaseName("ix_hiram_emissoes_pendentes");
b.HasIndex(x => x.EmissionSeq).IsUnique();
```

Migration: `dotnet ef migrations add AddHiramEmissoes` no projeto Infra.Postgre do EasyStok. A coluna
`emission_seq` deve sair como `bigserial`/`GENERATED ... AS IDENTITY` (conferir o SQL gerado).

## 3. Emissão durável (mesmo padrão do EnfileirarEventoAsync)

Onde hoje o EasyStok enfileira o evento de notificação (`NotificadorService.EnfileirarEventoAsync` e os
detectores cron), na MESMA transação da mutação de negócio, gravar também uma `HiramEmissao` com o
`event_type` canônico e o payload (contato + dados + logical_alert_id + timezone). Comeca atrás de
feature flag por canal (email primeiro), em modo tee: o EasyStok continua enviando pelo canal local E
grava a emissão para o Hiram (que estará em shadow).

## 4. Relay flag-based (espelha IntegrationOutboxBackgroundService)

BackgroundService no `EasyStock.Worker`, polling configurável, scope por tick. Lê pendentes por
`emission_seq` crescente (flag-based: marca Enviada/Falhada, NUNCA cursor por `emission_seq > last`, que
pularia um late-committer). Para cada pendente, POST no Hiram:

```
POST {HiramBaseUrl}/v1/events
X-Api-Key: {chave do tenant}
Idempotency-Key: {event_id}              // dedupe ponta a ponta
{ eventType, eventId, emissionSeq, recipient, logicalAlertId, timezone, data }
```

Resiliência: retry com backoff (reusar o padrão de Resilience das integrações). 202/409(replay) =
sucesso (marca Enviada). 5xx/timeout = falha (incrementa tentativa, reprocessa). A emissão é durável:
falha não perde, reenvia.

## 5. Cliente do Hiram

Gerar por codegen a partir do OpenAPI do Hiram (não espelho manual), conforme o contrato congelado, e
registrar um HttpClient tipado com base URL e a API key do tenant.

## 6. Watermark no cutover (lado EasyStok)

No flip de uma empresa (passo 2.2), capturar W = maior `emission_seq` emitida até T0. Daí em diante o
EasyStok para de enviar email local para `emission_seq > W` daquela empresa (o Hiram passa a entregar
esses). O local drena o backlog `<= W`. Drain completo = nenhuma `HiramEmissao` pendente `<= W` E
nenhuma transação iniciada antes de T0 ainda aberta (bigserial commita fora de ordem). Rollback move W.

## Validação (ao vivo, lado EasyStok)

- Unit/integração no EasyStok: emissão gravada na mesma transação da mutação; relay marca Enviada em
  202; falha do Hiram não afeta o envio local (tee em shadow).
- Build da solução do EasyStok verde, migration aplicada, relay emitindo para o Hiram em staging.
