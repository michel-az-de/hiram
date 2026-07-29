# ADR-026: Servidor de email próprio (MTA self-hosted Stalwart) com relay autenticado no último salto

**Status:** Supersedido pelo ADR-027
**Data:** 2026-07-14
**Decisores:** Felipe (arquiteto)

## Contexto

Levantou-se a possibilidade de o Hiram enviar todo o email por um servidor SMTP próprio, sem depender do
Resend nem de app de terceiros, que não entregue spam e siga as leis: um "Resend autoral e open source". A
motivação declarada é portfólio (autoral) e independência (soberania), não redução de custo.

Medição do estado atual antes de decidir (PS1):

- O Hiram já é, na prática, o cliente de um Resend próprio. Existe o port `IEmailProvider` com resolução por
  tenant (`src/Hiram.Application/Delivery/EmailProviderResolver.cs`) e dois adapters concretos: SMTP via
  MailKit (`src/Hiram.Infrastructure/Delivery/SmtpEmailProvider.cs`) e Resend via HTTP
  (`src/Hiram.Infrastructure/Delivery/ResendEmailProvider.cs`). O SMTP é o default de plataforma e já roda
  testado ponta a ponta contra um MTA real (Mailpit) no CI. Apontar o Hiram para um MTA próprio é, no caminho
  do default de plataforma, trocar variáveis de ambiente, sem código de aplicação.
- O pedido embute duas coisas muito diferentes. (A) Ser dono da plataforma de envio (API, fila, outbox,
  assinatura DKIM, supressão, tracking, compliance) já está quase todo construído. (B) Ser dono do último
  salto, o MTA falando direto com o MX do destinatário a partir da nossa infraestrutura, é a parte difícil.

Bloqueios duros do salto (B), medidos:

- Porta 25 de saída bloqueada nos dois lados. A Azure bloqueia SMTP outbound na porta 25 por padrão na maioria
  das assinaturas (a VM de demo não é Enterprise Agreement), e recomenda relay autenticado na 587. O Brasil
  bloqueia a porta 25 por política nacional (Gerência de Porta 25 do CGI.br, em vigor desde 2013), exigindo
  submissão autenticada na 587. Entrega direta ao MX a partir da VM atual está fora.
- Sem domínio próprio. O projeto não controla nenhum domínio (só `20.98.234.200.sslip.io`). Sem um domínio
  registrado não há como publicar SPF, DKIM e DMARC nem alinhar PTR/rDNS.
- Reputação de IP. IP novo de nuvem costuma nascer em blocklist (Spamhaus PBL) e exige semanas de warmup.
- VM de demo apertada. B2s/4GB, já sem folga (o ADR-022 tirou a observabilidade da demo por falta de RAM).

Requisitos externos de entregabilidade e lei, que qualquer envio real precisa cumprir:

- Gmail e Yahoo (fevereiro de 2024) e Microsoft (maio de 2025) exigem, para remetentes de volume, SPF e DKIM,
  DMARC com alinhamento no From, one-click unsubscribe (RFC 8058) para marketing, PTR válido, TLS, e taxa de
  spam no Postmaster Tools abaixo de 0,3% como teto de enforcement, com 0,1% como alvo recomendado.
- LGPD e a autorregulamentação CAPEM (Brasil) exigem consentimento comprovável, link de opt-out honrado em até
  2 dias úteis, e submissão autenticada na 587 (Gerência de Porta 25).

Lacunas hoje no repositório, que um MTA próprio obrigaria a fechar:

- Loop de bounce e complaint desenhado (ADR-019) mas não implementado (issue #13). Sem ele, `sent` mente e não
  há dado para supressão nem para o SLO de bounce.
- Suppression list inexistente como modelo (só "supressão futura" no ADR-019).
- List-Unsubscribe / one-click ausente.
- Enforcement de consentimento e bloqueio órfão (ADR-024, issues #36 e #37): os componentes existem mas nenhum
  caminho de envio os invoca, o que é exposição LGPD.

## Decisão

Adotar uma arquitetura de três camadas:

1. O Hiram é dono da plataforma de envio (já é). Essa é a parte "autoral" e onde mora o valor.
2. Um MTA self-hosted entra como provider de primeira classe, plugado via o adapter SMTP existente. A escolha
   do MTA é o Stalwart (ver decisão de borda 1). Serve dev, demo e mail interno, e é a vitrine "nós rodamos o
   nosso próprio servidor".
3. O último salto do MTA sai por um smarthost autenticado na porta 587 (relay), contornando o bloqueio da 25 e
   emprestando reputação de IP. O Hiram é dono de tudo exceto o IP final, cerca de 90% de independência.

Um provider gerenciado permanece disponível como default para mail crítico de produção. A prioridade fundadora
é Produção > Portfolio: o projeto nasceu de um blackout P0 de notificações, então a entrega não pode ficar
refém de um MTA de mão numa VM de 4GB. Multi-provider com o MTA próprio como uma das opções é maturidade, não
concessão, e é o que Resend, SendGrid e SES todos fazem por baixo.

## Opções consideradas

### Opção A: MTA self-hosted (Stalwart) como provider, com relay 587 no último salto (escolhida)

**Prós:** independência real da plataforma (autoral); um provider "próprio" plugável no que já existe; produção
protegida porque o salto final usa um IP com reputação; contorna o bloqueio de porta 25 sem depender de EA da
Azure nem de sair da nuvem.
**Contras:** o relay 587 ainda é um terceiro no último salto (independência de ~90%, não 100%); mais um serviço
para operar.

### Opção B: entrega direta ao MX a partir de host próprio

Registrar domínio, mover o envio para um VPS que libere porta 25 e PTR (Hetzner, OVH), aquecer o IP, monitorar
RBL e FBL.
**Prós:** independência de 100%, sem terceiro no último salto.
**Contras:** custo operacional alto e contínuo; risco real de cair em spam durante e depois do warmup; conflita
com Produção > Portfolio; exige sair da Azure para o sender. Rejeitada nesta fase, vira gatilho de revisão.

### Opção C: não rodar MTA, só formalizar multi-provider e a camada de compliance

Construir bounce, suppression, unsubscribe e enforcement sobre um provider gerenciado, sem servidor próprio.
**Prós:** menor risco; inbox garantido por terceiro.
**Contras:** não satisfaz a motivação de ser autoral e independente. Rejeitada como objetivo final, mas seus
componentes (bounce, suppression, unsubscribe, enforcement) são pré-requisito da Opção A e entram de qualquer
forma.

## Decisões de borda cravadas

1. **MTA escolhido: Stalwart.** Matriz verificada:

   | MTA | RAM mínima | Stack | Relay 587 nativo | DKIM | Dashboard | Observação |
   |---|---|---|---|---|---|---|
   | Stalwart (escolhido) | ~512MB | binário Rust único | sim (`Relay` MtaRoute, auth+TLS) | nativo, com rotação automática de chave | sim (admin) | leve e completo; projeto mais novo, lado MTA maduro |
   | Haraka | leve (Node.js) | Node.js + plugins | sim (`force route`) | por plugin | não | comprovado em produção (DuckDuckGo, Craigslist); minimalista |
   | Postal | 4GB (8GB rec.), host dedicado | Rails + MariaDB + RabbitMQ | via smarthost | sim | melhor dashboard | inviável na B2s/4GB; dashboard duplica o tracking que o Hiram já tem |

   Racional: a motivação é autoral e independente, não "melhor dashboard". O footprint do Postal (4-8GB
   dedicados, MariaDB + RabbitMQ + Rails) conflita com a VM apertada, e seu trunfo (dashboard de tracking) é
   redundante com `DeliveryAttempt`, webhooks e SLOs que o Hiram já possui. Stalwart é leve e traz DKIM com
   rotação e rota de relay nativos. Haraka fica como alternativa de fallback.
2. **Stalwart roda como serviço Docker Compose, não como dependência .NET.** Não toca `Directory.Build.props`
   nem o build. O Hiram fala com ele pelo adapter SMTP existente.
3. **Domínio é pré-requisito bloqueante.** Candidato `hiram.dev` (já referenciado no site, issue #17), com
   confirmação de disponibilidade e preço no registrar antes de amarrar; `.com.br` como fallback (muda o
   registro para registro.br). Observação: o `.dev` força HTTPS via HSTS preload apenas na web, o que não afeta
   SMTP, DKIM nem o email. Sem domínio não há SPF/DKIM/DMARC nem PTR, então nada de envio real.
4. **Relay 587.** SES SMTP ou um relay dedicado reputado preservam a independência (o Hiram continua dono da
   fila, assinatura, supressão e tracking; o relay é só transporte do último salto). Usar o próprio Resend como
   relay é transitório, uma ponte de transição, nunca uma opção equivalente, porque a motivação é justamente
   independência do Resend.
5. **Compliance é pré-condição, não opcional.** Bounce (ADR-019 / #13), suppression, List-Unsubscribe one-click
   e enforcement de consent/block (ADR-024 / #36, #37) precisam existir antes de assumir tráfego real, senão o
   "não entrega spam / segue as leis" não se sustenta.
6. **A VM de demo não hospeda o MTA em produção.** A B2s/4GB não comporta o MTA mais o resto com folga. Pré
   check de go/no-go: só subir o Stalwart na VM se houver pelo menos 700MB livres sustentados (cerca de 512MB do
   Stalwart mais margem), medido com Postgres, Redis, RabbitMQ e API já rodando; senão, host dedicado.
7. **Segurança do SMTP self-service do tenant.** Deixar um tenant apontar o SMTP para host arbitrário é um vetor
   de SSRF e exfiltração (metadata da Azure em 169.254.169.254, Postgres e Redis internos, loopback). Portanto:
   a origem da config de email é explícita e obrigatória no código (`Platform` ou `Tenant`, fail-closed); um
   guard de destino resolve o host e rejeita IPs de loopback, RFC1918, link-local, ULA e CGNAT no caminho do
   tenant, tanto na escrita quanto antes de conectar; TLS é obrigatório para SMTP de tenant; e o default de
   plataforma (operador aponta um MTA interno legitimamente) fica isento do guard. Detalhe de implementação nas
   fatias S2a e S2b do plano.
8. **Taxa de spam.** O gate objetivo de entregabilidade cita os dois limiares do Gmail: teto de enforcement de
   0,3% e alvo recomendado de 0,1%, medidos no Postmaster Tools.

## Consequências

- **Fica mais fácil:** independência real da plataforma; um provider próprio plugável no que já existe; a base
  de compliance que faltava (bounce, suppression, unsubscribe, enforcement) passa a existir; narrativa de
  produto forte de rodar o próprio MTA.
- **Fica mais difícil:** mais um serviço para operar (Stalwart, chaves DKIM, webhooks de status); dependência de
  registrar um domínio e configurar DNS; o relay 587 mantém um terceiro no último salto; o trabalho de
  compliance que estava adiado vira pré-requisito; o SMTP self-service exige um guard de segurança que não
  existia.

## Gatilho de revisão

- Volume ou custo que justifique sair do relay para IP dedicado próprio (Opção B).
- Assinatura Azure virar Enterprise Agreement (libera porta 25) ou migração do sender para host que libere 25 e
  PTR.
- Requisito de soberania total de dados que proíba qualquer terceiro no último salto.
- Bloqueio por contato (kill-switch por destinatário) que o ADR-019 alimenta via hard bounce e complaint, que a
  suppression list desta decisão passa a exigir.

## Nota de known issue: colisão de numeração ADR-023

Há dois arquivos com o número 023 (`ADR-023-canal-whatsapp-cloud-api.md`, Proposto, e
`ADR-023-adocao-policy-v4.md`, aceito e referenciado pelo CLAUDE.md). Este ADR toma o próximo número livre
(026) e não perpetua a ambiguidade. A renumeração é rastreada na issue #39 (reconciliar MASTER-PLAN, CHANGELOG
e numeração de ADR), fora do escopo desta decisão.

## Itens de ação

Organizados em três trilhas, com grafo de dependência sem circularidade. Cada item vira issue, branch e PR.

Trilha A, PoC do MTA próprio (depende de operação, não de código puro):

1. [ ] Registrar domínio (confirmar `hiram.dev`, ou `.com.br`) e publicar SPF, DKIM, DMARC e PTR (issue #17).
2. [ ] Subir o Stalwart como serviço, gerar chave DKIM, configurar `Relay` MtaRoute para o smarthost 587,
   apontar o default de plataforma via env. Pré check de headroom (decisão de borda 6). Depende de A1.
3. [ ] PoC ponta a ponta: Hiram -> Stalwart -> relay 587 -> inbox real. Aceite: 100% de aprovação de SPF, DKIM e
   DMARC (objetivo) mais score de seed-list ou ferramenta (por exemplo mail-tester maior ou igual a 8 de 10) e
   taxa de spam abaixo de 0,1% após warmup. Não usar "80% de inbox em IP frio" como gate. Depende de A2.

Trilha B, compliance (paralela, pré-condição do tráfego real, não do PoC):

4. [ ] Enforcement de consent e block no caminho de envio (ADR-024, issues #36 e #37).
5. [ ] Loop de bounce e complaint idempotente (ADR-019, issue #13).
6. [ ] Suppression list (modelo e consumo no processor), alimentada por hard bounce e complaint.
7. [ ] List-Unsubscribe e one-click (RFC 8058) para a categoria marketing. Bloqueada por B6 e #36.

Trilha C, self-service:

8. [ ] Endpoint de escrita de provider config por tenant, com o guard de segurança da decisão de borda 7.
