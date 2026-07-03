# Demo pública na VM dedicada

Stack da demo conforme ADR-022: imagens de produção da Api e do Dispatcher atrás de um Caddy com TLS
automático no host sslip.io, Mailpit como caixa de entrada visível em `/mailpit`, tenant de console em
shadow e tenant live entregando no Mailpit. Nada aqui é produção; produção segue o ADR-016.

## Provisionamento na VM

```bash
cd /opt/hiram && git pull

docker build -f src/Hiram.Api/Dockerfile -t hiram-api:demo .
docker build -f src/Hiram.Dispatcher/Dockerfile -t hiram-dispatcher:demo .

cd deploy/demo
cp .env.demo.example .env   # preencher senhas, admin key e VAPID
docker compose -f docker-compose.demo.yml up -d
```

O serviço `migrate` aplica o schema uma vez e sai; a Api só sobe depois dele. O NSG da VM precisa de
80 e 443 abertos para a internet; as portas de gestão (8025, 15672) continuam restritas ao IP do
operador.

## Dados da demo

```bash
# Tenant do console (shadow), a demo key volta na resposta e o apresentador cola no modo ao vivo:
curl -X POST https://$DEMO_HOST/demo/bootstrap -H "X-Admin-Key: $HIRAM_ADMIN_KEY"

# Tenant live para o momento do email chegando no /mailpit (provisionado via admin api):
curl -X POST https://$DEMO_HOST/v1/admin/tenants -H "X-Admin-Key: $HIRAM_ADMIN_KEY" \
  -H "Content-Type: application/json" -d '{"name":"hiram-demo-live","deliveryMode":"live"}'
```

## Atualização da demo

`git pull`, rebuild das duas imagens e `docker compose up -d` de novo. O volume `pgdata` preserva os
dados entre atualizações; para zerar a demo, `docker compose down -v`.
