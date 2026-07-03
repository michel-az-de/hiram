# ADR-022: Ambiente Demo público na VM dedicada

- Status: aceito
- Data: 2026-07-03
- Relacionados: ADR-021 (gatilho de revisão acionado: servir o hub fora de Development), ADR-016, issue #15

## Contexto

O ADR-021 entregou o hub pedagógico e o console de demo servidos pela Api somente em Development. A necessidade comercial mudou: precisamos de uma URL pública onde um prospect vê o produto funcionando de verdade, sem depender da máquina do apresentador. Existe uma VM dedicada de demo (hiram-demo-rg, Standard_B2s, 4 GB de RAM), separada da VM de produção do EasyStok, que hoje roda apenas a infra de dev e a Api manualmente, restrita por NSG a um único IP.

O próprio ADR-021 listou como gatilho de revisão a necessidade de servir o console fora de Development. Este ADR responde a esse gatilho.

## Decisão

1. **Ambiente `Demo` de primeira classe.** A Api passa a servir o site estático (hub e console) e o endpoint `POST /demo/bootstrap` quando `ASPNETCORE_ENVIRONMENT` é `Development` ou `Demo`. Todo o resto do comportamento de `Demo` é idêntico a produção: mesmo pipeline, mesmas migrations, mesma autenticação. O bootstrap continua atrás de `X-Admin-Key`.
2. **Compose provisório na VM de demo, não k3s.** Com 4 GB de RAM, o k3s completo do ADR-016 não cabe com folga ao lado da stack. O deploy da demo usa `deploy/demo/docker-compose.demo.yml` com as mesmas imagens Docker de produção (Api e Dispatcher). O ADR-016 continua sendo o alvo para a VM do EasyStok; a demo não é produção.
3. **Caddy como borda com TLS automático.** Um contêiner Caddy termina TLS para `20.98.234.200.sslip.io` (Let's Encrypt, HTTP-01), faz proxy para a Api e serve o Mailpit sob o caminho `/mailpit`. O sslip.io resolve o host para o IP da VM sem exigir registro de domínio. Quando houver domínio definitivo, muda apenas o Caddyfile.
4. **Mailpit como destino visível dos emails.** O provider SMTP da plataforma no ambiente Demo aponta para o Mailpit. Um tenant de demonstração em modo live entrega no Mailpit e a mensagem aparece na UI pública; nada sai para a internet. O tenant do console permanece em shadow, como no ADR-021.
5. **Segredos reais.** As credenciais de dev (hiram/hiram) não sobem na demo pública. Postgres, RabbitMQ e AdminKey recebem valores gerados, entregues via `.env` fora do repositório, conforme o padrão do `.env.hiram.example`.

## Consequências

Fica fácil: demonstrar o produto com uma URL, derrubar e recriar a demo com dois comandos, evoluir para domínio próprio.

Fica difícil: o compose da demo é um segundo artefato de deploy para manter ao lado dos manifests k3s; a superfície pública exige atenção contínua (rate limit da issue #20 passa a ser mais urgente).

Riscos aceitos: a UI do Mailpit é pública e mostra apenas conteúdo de demonstração; o endpoint de bootstrap é público mas exige a admin key e só provisiona o tenant fixo de demo em shadow; a VM B2s não tem headroom para observabilidade LGTM completa, os traces da demo ficam para uma iteração futura.

## Gatilhos de revisão

- Domínio definitivo registrado (hiram.dev ou outro): substituir o host sslip.io.
- Demo passar a receber tráfego que caracterize produção: migrar para o caminho do ADR-016.
- Necessidade de mostrar traces ao vivo na demo: avaliar Aspire Dashboard standalone ou LGTM enxuto contra o orçamento de memória.
