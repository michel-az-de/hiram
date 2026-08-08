# Provisionamento do tenant Jornada do Candidato

A Jornada do Candidato emite eventos para o Hiram em `POST /v1/events`. Para que esses eventos virem
e-mail, o tenant precisa existir com api key, templates aprovados e uma rotina por eventType. O
`provision.sh` faz esse caminho inteiro de forma idempotente, no lugar da sequencia de curl feita a mao.

Esta e a fase de e-mail. SMS e WhatsApp entram com as fatias do ADR-028; ate la o script avisa e segue
apenas com e-mail.

## Pre-requisitos

- `bash` e `curl` (sem `jq`).
- Um Hiram no ar e respondendo em `GET /health/ready`. Local: `docker compose -f docker-compose.dev.yml up -d`
  na raiz do repo, que sobe a API em `http://localhost:3357` com Mailpit em `http://localhost:8025`.
- A admin key do ambiente (`Hiram__AdminKey`, `admin-dev-local` no compose de desenvolvimento).

## Uso

```bash
cd deploy/jornada
cp .env.jornada.example .env   # ajuste a URL e a admin key do ambiente alvo
./provision.sh all
```

Rodar de novo e seguro: o `all` reaproveita tenant e api key dos arquivos de estado, reaproveita
template ja criado e a rotina existente volta como 200 em vez de virar duplicata.

## Subcomandos

| Subcomando | O que faz |
|---|---|
| `tenant` | Cria o tenant live e emite a api key do emissor. Grava `.jornada-tenant` e `.jornada-key` com permissao 600. |
| `templates` | Cria e aprova os quatro templates de e-mail da jornada. Template ja existente tem o conteudo atualizado, quando ele mudou, e e aprovado de novo. |
| `routines` | Liga cada eventType da jornada ao seu template, na categoria `transactional`. |
| `consent` | Registra opt-in de e-mail transacional para os guids em `JORNADA_TEST_USER_IDS`. |
| `all` | Executa os quatro na ordem. |

### Contrato com a Jornada

| eventType | template | assunto |
|---|---|---|
| `VerificacaoDeEmailSolicitada` | `verificacao-de-email` | Confirme seu e-mail na Jornada do Candidato |
| `CandidatoEncaminhado` | `candidato-encaminhado` | Sua Jornada avancou: voce foi encaminhado a uma Loja |
| `CandidatoAprovadoPelaLoja` | `candidato-aprovado` | Sua Jornada foi concluida com sucesso! |
| `CandidatoStatusAlterado` | `candidato-recebido-pela-loja` | A Loja confirmou seu recebimento |

O eventType e comparado por igualdade exata, entao o PascalCase acima e obrigatorio. As variaveis do
corpo (`Protocolo`, `Nome`, `LinkVerificacao`, `ExpiraEm`) tambem sao PascalCase: o renderer roda com
`StrictVariables`, e uma chave com casing diferente derruba a renderizacao e o e-mail nao sai.

## Variaveis

Ordem de precedencia: ambiente, depois `.env` ao lado do script, depois o padrao.

| Variavel | Padrao | Para que serve |
|---|---|---|
| `HIRAM_BASE_URL` | `http://localhost:3357` | Hiram alvo. |
| `HIRAM_ADMIN_KEY` | `admin-dev-local` | Header `X-Admin-Key` das chamadas de admin. |
| `HIRAM_JORNADA_API_KEY` | vazio | Api key ja emitida, para reprovisionar sem gerar outra. |
| `JORNADA_TENANT_NAME` | `jornada-do-candidato` | Nome do tenant. |
| `JORNADA_CHANNELS` | `email` | Canais das rotinas. `sms` e `whatsapp` viram aviso e sao ignorados. |
| `JORNADA_TEST_USER_IDS` | vazio | Guids que recebem opt-in explicito no subcomando `consent`. |

## Estado local

`.jornada-tenant` e `.jornada-key` ficam ao lado do script e estao no `.gitignore` deste diretorio,
junto do `.env`. O script aplica `chmod 600` nos dois; em sistema de arquivos que ignora o modo, como
NTFS pelo Git Bash, a chamada nao tem efeito e a protecao passa a ser a do diretorio.

A api key aparece em texto claro uma unica vez, na execucao que a emite: o Hiram guarda apenas o hash.
Se o arquivo for perdido, o caminho e emitir outra em `POST /v1/admin/api-keys` e revogar a anterior.

Apagar `.jornada-tenant` nao e reversivel do lado do Hiram: nao existe busca de tenant por nome, entao
a proxima execucao cria um tenant novo em vez de reencontrar o antigo. Guarde os dois arquivos junto do
backup do ambiente.

## Avisos

- **O volume `hiram-keyring` precisa persistir.** Ele guarda as chaves de Data Protection que cifram os
  segredos de provider por tenant. Se o volume for descartado (`docker compose down -v`, recriacao da
  maquina sem restore), esses segredos deixam de descriptografar e precisam ser reconfigurados, mesmo
  com o banco intacto. O procedimento de backup e restore esta em `deploy/dr/` e no
  `docs/operations-runbook.md`.
- **Os corpos sao texto puro, nao HTML.** Nenhum provider de e-mail do Hiram entrega HTML hoje: o
  `SmtpEmailProvider` monta `TextPart("plain")` e o `ResendEmailProvider` envia o corpo no campo
  `text`. Um corpo com marcacao chegaria ao candidato com as tags a mostra. A parte `text/html` nos
  adapters esta proposta na issue #114; quando ela existir, basta trocar o conteudo aqui.
- Mudar assunto ou corpo no script e o caminho normal de correcao: uma reexecucao atualiza o template
  que ja existe e o aprova de novo. A atualizacao so acontece quando o conteudo muda de fato, porque
  ela incrementa a versao do template, e a versao compoe a chave de mensagem do fan-out.
- O mesmo vale para rotina ja existente: `POST /v1/admin/routines` devolve a que esta la, sem alterar
  canais ou categoria. Nao existe endpoint de atualizacao de rotina, entao mudar o vinculo hoje exige
  desativar a linha antiga direto no banco.
