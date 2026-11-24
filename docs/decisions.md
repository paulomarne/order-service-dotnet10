# Decisões de arquitetura

Registro das escolhas do Order Service e dos trade-offs que eu sustentaria numa conversa de arquitetura. O contexto é o desafio de Order Service da nstech, entregue como uma API pronta para rodar, não como um esboço.

## 1. Clean Architecture com vertical slices na aplicação

**Decisão.** Quatro projetos, com a dependência apontando para dentro:

- `OrderService.Domain` não referencia nada. Agregados, value objects e erros de negócio.
- `OrderService.Application` referencia só o domínio. Cada caso de uso vive na própria pasta: `CreateOrder`, `ConfirmOrder`, `CancelOrder`, `GetOrderById`, `ListOrders`, `IssueToken`.
- `OrderService.Infrastructure` implementa as portas (repositórios, consultas, relógio, idempotência, EF Core).
- `OrderService.Api` é um adaptador HTTP fino: autenticação, ProblemDetails, OpenAPI e os endpoints.

**Por que não MediatR.** O despacho é um handler concreto por caso de uso, registrado no composition root. MediatR passou a ter licença comercial; um `ICommandHandler` genérico não compraria clareza aqui e esconderia o fluxo. O endpoint chama o handler, o handler chama as portas. Dá para ler o caso de uso de cima a baixo.

**Trade-off.** Há um pouco mais de registro manual do que num mediator. Em troca, a fatia continua explícita e o domínio não sabe que existe HTTP, EF ou JWT.

## 2. O pedido nasce `Placed`

O enunciado lista `Draft`, mas aceita o pedido já `Placed`. Não há regra de rascunho (editar item, expirar, submeter). Introduzir `Draft` só para uma transição imediata aumentaria estados sem invariante nova. O valor permanece no enum para o contrato anunciado e para um fluxo futuro de checkout; hoje `Confirm` e `Cancel` recusam qualquer estado que não seja o de origem permitido.

## 3. Estoque é reservado na confirmação

**Modelo.** `Product.AvailableQuantity` é o saldo vendável. Criar o pedido:

- exige item, quantidade > 0, produto existente e quantidade ≤ saldo visto naquele momento;
- grava o preço unitário e o nome como snapshot;
- calcula `total = soma(preço unitário × quantidade)`;
- **não** baixa o estoque.

Confirmar (`Placed → Confirmed`) baixa o saldo. A segunda confirmação devolve o mesmo pedido e não mexe no estoque. Cancelar `Placed` só muda o estado, porque nada foi reservado. Cancelar `Confirmed` devolve a quantidade ao saldo. Cancelar de novo é no-op.

**Por que não reservar na criação.** O texto pede que a confirmação reserve/baixe estoque e que o cancelamento libere o que for aplicável. Reservar na criação e de novo na confirmação contaria duas vezes. O check da criação é uma regra de negócio contra o snapshot; a confirmação é a fronteira transacional, que é onde duas confirmações concorrentes podem estourar o saldo.

**Trade-off.** Dois `POST /orders` simultâneos podem passar no check e só um `confirm` vence. O perdedor permanece `Placed` com 409. Em produção eu reservaria na colocação (`Reserved` separado de `OnHand`, com TTL e um job que libera reserva abandonada) e trataria a confirmação como o commit da reserva. O modelo atual é o que o enunciado descreve, com a corrida fechada no ponto que realmente baixa estoque.

## 4. Concorrência otimista com `xmin` e retry

`orders` e `products` usam a coluna de sistema `xmin` do PostgreSQL como token de concorrência. O EF inclui `xmin` no `WHERE` do `UPDATE`. Não há coluna artificial e a migration não tenta criar `xmin` (o provider omite colunas de sistema no SQL).

`DbUpdateConcurrencyException` vira `ConcurrencyException` na aplicação. `ConcurrencyRetry` limpa o change tracker e refaz o caso de uso, relendo pedido e produto. Na retentativa:

- se o pedido já está confirmado, a operação devolve sucesso e não baixa de novo;
- se o saldo ficou insuficiente, responde 409 `insufficient_stock` e o pedido continua `Placed`.

O número de tentativas vem de `Concurrency:MaxAttempts` (3 por padrão). Isolamento `READ COMMITTED` mais token otimista escala melhor do que `SERIALIZABLE` para este write set pequeno. Um `UPDATE ... SET available_quantity = available_quantity - @q WHERE available_quantity >= @q` também serviria; `xmin` protege o agregado inteiro (pedido + produto) na mesma `SaveChanges`, que é o que impede duas confirmações do mesmo pedido de baixarem o estoque duas vezes mesmo quando o conflito acontece na linha do pedido.

Há ainda `CHECK (available_quantity >= 0)` e `CHECK (quantity > 0)` como rede de segurança no banco.

## 5. `Idempotency-Key` opcional

Confirm e cancel já são idempotentes pela máquina de estados. A criação não é: um retry cego do cliente criaria outro pedido. O header opcional `Idempotency-Key` cobre os `POST /orders*`.

Fluxo:

1. Sem header, segue o comportamento normal.
2. Com header, grava uma linha `InProgress` com hash de método + path + corpo (índice único).
3. Mesma chave e mesmo hash, já concluída: devolve o status, o corpo e o `Location` originais, sem reexecutar.
4. Mesma chave e hash diferente: 409 `idempotency_conflict`.
5. Chave ainda em progresso: 409 `idempotency_in_progress`.
6. Resposta 2xx é persistida. Falha de validação ou de negócio abandona a chave, para o cliente poder corrigir e repetir.

**Trade-off.** A chave é gravada numa transação e o efeito de negócio noutra. Se o processo morrer no meio, a chave fica `InProgress` e o cliente recebe 409 em vez de duplicar o pedido. É o lado seguro. Em produção eu uniria as duas escritas na mesma transação (ou usaria uma inbox) e expiraria chaves antigas. Não há takeover automático de chave velha, de propósito: retomar poderia duplicar um create que já commitou.

## 6. Value objects onde eles seguram invariante

`Currency`, `Money` e `Quantity` são `readonly record struct`.

- `Currency` só aceita código ISO de 3 letras e define a escala (2 na maior parte, 0 em JPY/KRW, 3 em BHD/KWD/OMR).
- `Money` recusa negativo, arredonda half-even na escala da moeda e não soma moedas diferentes.
- `Quantity` só existe se for > 0.

O agregado usa esses tipos para calcular o total. No mapeamento EF eu persisto os primitivos (`decimal`, código da moeda, `int`) e ignoro as propriedades calculadas (`Total`, `UnitPrice`, `LineTotal`). Owned type de `record struct` com construtor que valida e arredonda briga com o materializador do EF e com a projeção. A invariante continua no domínio; a coluna fica simples de indexar e de projetar. Preço e nome do item são snapshot: uma mudança posterior no catálogo não reescreve o pedido.

## 7. JWT de demonstração, autorização mínima de verdade

`POST /auth/token` é client credentials em memória (`Auth:ClientId` / `Auth:ClientSecret`), comparação em tempo constante via hash SHA-256. O token é HMAC-SHA256, com `sub`, `jti` e `scope=orders`. Os endpoints de pedido exigem a policy `orders`: usuário autenticado **e** a claim `scope`. Health e token são anônimos.

A chave `dev-only-signing-key-change-me-32chars`, o client e a senha do Postgres do compose são **somente desenvolvimento**, marcados no `appsettings.json` e no compose. Fora de `Development` e `Testing`, uma chave que contenha `dev-only` impede a subida. Produção deve injetar `Jwt__SigningKey`, `Auth__ClientSecret` e `ConnectionStrings__Orders` por ambiente. Nada disso é um vault; é o mínimo para o avaliador rodar sem passo secreto.

## 8. Leitura sem tracking e sem N+1

`GetOrderById` e `ListOrders` usam `AsNoTracking` e projetam só as colunas do DTO. A coleção de itens vai com `AsSplitQuery`: uma ida para a página de pedidos e outra para os itens da página, não uma query por pedido. O total da página é um `COUNT` separado, que é o custo honesto de paginação com `totalCount`. Ordenação estável por `created_at DESC, id`.

Índices em `orders`: `(customer_id, created_at)`, `(status, created_at)` e `(created_at)`. Cobrem os filtros obrigatórios e o `ORDER BY`. Escritas carregam o agregado com `Include` uma vez, porque vão mutá-lo; não passam pela projeção.

Paginação: `page` padrão 1, `pageSize` padrão 20, máximo 100. `from` e `to` são inclusivos em `createdAt`. Status inválido, página inválida ou intervalo invertido respondem 400, não uma lista vazia silenciosa.

## 9. Migrations no startup

`DatabaseInitializer` aplica `MigrateAsync` antes de aceitar tráfego, com retentativa curta para o Postgres ainda estar abrindo o socket, e semeia o catálogo se `products` estiver vazio. Os ids de seed são fixos para os exemplos do README.

**Trade-off.** Mais de uma instância subindo junto pode disputar o lock de migration. Para este compose, uma instância é o cenário. Em produção a migration roda num job anterior ao rollout, e a API só verifica se o schema está compatível. O seed também sairia do startup e iria para um script de ambiente.

## 10. Erros

Exceções de domínio e de aplicação são traduzidas para `application/problem+json` com `errorCode` e `traceId`.

| Situação | HTTP | Código |
| --- | --- | --- |
| Payload inválido | 400 | `validation_error` |
| Credencial ruim / sem token | 401 | `invalid_credentials` / `unauthorized` |
| Pedido ou produto inexistente | 404 | `order_not_found` / `product_not_found` |
| Estoque, transição ilegal, concorrência esgotada, idempotency key | 409 | `insufficient_stock`, `invalid_transition`, `concurrency_conflict`, `idempotency_*` |
| Outra invariante de domínio (moeda divergente, por exemplo) | 422 | código da exceção |
| Falha inesperada | 500 | `unexpected_error`, sem vazar a exceção |

FluentValidation cobre o contrato de entrada. O agregado repete as invariantes para quem chamar o domínio fora do HTTP.

## 11. O que eu faria em seguida, em produção

- **Outbox.** `OrderConfirmed` e `OrderCanceled` gravados na mesma transação do agregado, com um publicador para o estoque ou para o faturamento. Hoje o estoque é local de propósito: um único serviço, uma única transação, sem consistência eventual fingida.
- **OpenTelemetry.** Traces do ASP.NET Core, do EF e um `ActivitySource` nos handlers, exportados por OTLP. O log JSON com `CorrelationId` é o passo que cabe neste escopo; métricas de confirmação, conflito de `xmin` e latência de lista viriam junto.
- **Resiliência.** O retry de concorrência fica no caso de uso porque precisa reler o agregado. Timeout e circuit breaker do Npgsql/HttpClient ficariam no Polly na borda, não escondidos dentro da regra de estoque. `EnableRetryOnFailure` do EF cobre blip de conexão.
- **Catálogo separado.** `Product` aqui é um agregado mínimo para o desafio. Preço, estoque e pedido em módulos ou bounded contexts distintos, com o pedido guardando só o snapshot.
- **Reserva com TTL** na colocação do pedido, como descrito na decisão 3.
- **Autenticação de verdade.** Emissor externo (ou Identity), rotação de chave, segredo fora do arquivo e revogação. O endpoint de token existe para o fluxo do desafio ser autocontido.
- **Testes de carga** no confirm concorrente além do teste de integração que já cobre duas confirmações disputando a última unidade.

## 12. Testes

- Domínio: total, vazio, duplicidade, moeda, estoque, transições e idempotência da máquina de estados.
- Aplicação: handlers com fakes, para a orquestração (reserva só no confirm, release só no cancel de confirmado, validação) sem banco.
- API: `WebApplicationFactory` + Testcontainers (Postgres 17). Se `docker info` falhar, o `Fact` é ignorado com mensagem explícita, em vez de derrubar o `dotnet test`.
