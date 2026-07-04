# Onboarding do tenant Levante

O Levante emite eventos de domínio para o Hiram via `POST /v1/events` (contrato congelado em
`docs/contracts/v1-events.md`). Para o Hiram entregar essas notificações, o tenant `levante`
precisa de uma API key, templates de e-mail aprovados e routines que mapeiam cada evento a um
template. Este diretório provisiona isso.

## Uso

```bash
HIRAM_BASE_URL=http://localhost:8080 \
HIRAM_ADMIN_KEY=<X-Admin-Key> \
./provision-levante.sh
```

Na primeira execução o script cria o tenant, cria a API key (mostrada **uma única vez**) e
semeia templates e routines. Guarde a API key em `Hiram:ApiKey` do Levante (user-secrets em dev,
variável de ambiente em produção) e nas re-execuções passe-a de volta:

```bash
HIRAM_BASE_URL=... HIRAM_ADMIN_KEY=... HIRAM_LEVANTE_API_KEY=hk_live_... ./provision-levante.sh
```

## Idempotência

- O id do tenant é persistido em `.provision-state` e reutilizado (criar tenant não é idempotente
  no Hiram, não há busca por nome). **Inclua `.provision-state` no backup off-host** junto do key
  ring do Data Protection; perdê-lo em uma recriação da VM leva a um tenant duplicado.
- Criação de template tolera `409` (já existe); a aprovação e a criação de routine
  (`POST /v1/admin/routines`, que retorna `200` quando a routine já existe) são idempotentes.

## Mapa evento → template

| eventType (Levante)     | template                | canal | categoria     | dados usados                        |
|-------------------------|-------------------------|-------|---------------|-------------------------------------|
| `assinatura_solicitada` | `newsletter-confirmacao`| email | transactional | `token`, `confirmUrlBase`           |
| `assinante_confirmado`  | `newsletter-boas-vindas`| email | transactional | (nenhum)                            |
| `comentario_pendente`   | `moderacao-comentario`  | email | operational   | `artigoId`, `comentarioId`, `dataCriacao` |

O renderer é Scriban com `StrictVariables`: as variáveis do template têm que bater exatamente com
as chaves de `data` enviadas pelo Levante, senão o render falha e a mensagem é descartada.
