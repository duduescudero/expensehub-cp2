# ExpenseHub — CP3

Eduardo Pires Escudero — RM 556527 — entrega individual.

## Continuidade e arquitetura

O repositório original foi restaurado pelo recurso de recuperação do GitHub em 03/11/2026. O commit entregue na CP2 é `460eb83b085ebb867cd2fc945afdffb9daf5ed0c`; seus commits e PRs anteriores permanecem na história. A CP3 acrescenta relatórios sem modificar o fluxo de pagamento.

Web registra/autentica/publica; RabbitMQ armazena a mensagem; worker calcula e persiste; web consulta/exporta. SQLite é compartilhado por volume local entre os dois processos. Não são microserviços autônomos. O schema de Reports é criado aditivamente, sem apagar tabelas anteriores. O worker aguarda/reexecuta após indisponibilidade transitória; só a web inicializa o banco.

## Executar a versão entregue

Pré-requisitos: Docker com contêineres Linux e Compose. Para testes de código, SDK .NET 10. Para o roteiro automatizado de experimentos, Node.js 22 ou superior.

```powershell
# No diretório raiz do clone, na main entregue:
./scripts/Initialize-Cp3.ps1
docker compose pull
docker compose up -d
docker compose ps
```

O script gera um `.env` local ignorado pelo Git, com senhas aleatórias e IMAGE_TAG igual ao SHA atual. Cada imagem usa exatamente esse SHA: `ghcr.io/duduescudero/expensehub-cp2-web:<SHA>` e `ghcr.io/duduescudero/expensehub-cp2-worker:<SHA>`. Aguarde a publicação correspondente. O arquivo de entrega final identifica SHA, execução CI e digests verificados.

API: http://localhost:5187/health; contrato OpenAPI: http://localhost:5187/openapi/v1.json; versão: http://localhost:5187/version. RabbitMQ: http://localhost:15672, com usuário e senha locais do `.env`. Os serviços só expõem portas no loopback. Não envie o `.env` ou tokens ao professor; ele gera suas próprias credenciais.

O volume `database` preserva SQLite; `identity-keys` preserva chaves dos tokens; `rabbit-data` preserva mensagens e configuração. Não execute `down -v` para demonstrar persistência.

## Contrato

POST `/api/reports` recebe `{ "fromDate":"2026-09-01", "toDate":"2026-09-30", "scope":"Mine" }`. Datas inclusivas por ExpenseDate. Employee permite Mine; Auditor permite Global; ambos permitem os dois. Admin/Approver/Finance isolados não concedem esses escopos.

GET `/api/reports` lista pedidos próprios; GET `/api/reports/{id}` retorna id, scope, fromDate, toDate, status, createdAt, startedAt, completedAt, result e error. Instantes UTC. Estados: Pending, Processing, Completed, Failed. Solicitante vem do token, nunca de ownerId no corpo ou na mensagem.

Resultado contém generatedAt, totalCount, totalAmount e byStatus com Draft, Submitted, Approved, Rejected e Paid, inclusive zeros. Valores calculados como decimal e persistidos como JSON. O Excel usa esse resultado salvo, sem consultar despesas novamente. Cabeçalhos de download: Content-Type XLSX e Content-Disposition attachment. Contagens e valores são células numéricas.

400: entrada inválida; 401: sem autenticação; 403: escopo negado; 404: detalhe/Excel inexistente ou alheio, mesmo para Auditor; 409: Excel próprio não Completed; 202 + Location: pedido salvo e publicação confirmada; 503 + id: publicação sem confirmação depois do registro. O corpo de erro de publicação é sanitizado.

## Confiabilidade e limites

Fila `expensehub.reports`, durável e sem autoexclusão, mensagem persistente `{ "reportId":"GUID" }`. Publicação mandatory com confirmação do broker e timeout. O worker usa consumo manual por BasicGet e confirma somente após salvar. Configuração por Rabbit__Host, Rabbit__User e Rabbit__Password.

Completed/Failed já persistidos são reconhecidos como duplicata sem novo efeito. Pending/Processing podem ser retomados após reentrega. Um único worker é o escopo validado; não há garantia de deduplicação concorrente entre vários workers. Falha definitiva de parâmetros é salva como Failed/REPORT_INVALID e reconhecida; falha transitória de banco propaga e reencaminha após espera de três segundos. Falha ao salvar o estado Failed também não confirma consumo. Mensagem JSON inválida ou sem identificador é rejeitada sem reencaminhamento.

**Sem outbox:** banco e publicação não são uma transação única. Uma queda após salvar e antes de publicar pode deixar Pending sem mensagem. Confirmação perdida pode ocorrer depois de a mensagem ter chegado. Por isso retornamos 503 com id e não afirmamos que reiniciar o worker recupera toda falha. Novo POST cria outro pedido. Não há endpoint de negócio para republicação; no laboratório, pode-se publicar o mesmo payload pelo painel do RabbitMQ, exchange padrão, routing key `expensehub.reports` e delivery_mode 2.

Ready é mensagem disponível; Unacked é entrega sem confirmação. Esses contadores não equivalem aos estados do relatório.

## Verificações e experimentos

```powershell
dotnet build sources/ExpenseHub.slnx
dotnet test sources/ExpenseHub.slnx
dotnet format sources/ExpenseHub.slnx --verify-no-changes
node scripts/verify-cp3.mjs
```

O roteiro cria apenas usuários/despesas fictícios, testa E01–E05 e publicação com broker indisponível, preservando volumes. Após terminar, grava `evidence/experiments.json`, `evidence/report-example.xlsx` e logs sem tokens. Não executar contra ambiente de produção. Se docker não estiver no PATH, informe o caminho em DOCKER_EXE. Credenciais são lidas do `.env` e não são gravadas nas evidências.

| Experimento | Procedimento reproduzido pelo roteiro |
| --- | --- |
| E01 | Cadastra identidades/roles, prepara despesas conhecidas, verifica Mine=175, consulta e baixa Excel. |
| E02 | Para somente worker, registra pedido, verifica Pending/409 e Ready, retoma o mesmo id até Completed. |
| E03 | Reinicia worker, republica id concluído, verifica log duplicate e igualdade do envelope persistido. |
| E04 | Testa anonimato, Employee/Global, Auditor/Mine, outros perfis, detalhe/Excel alheios, listagem e perfis combinados. |
| E05 | Recria web/worker sem remover volumes; verifica o mesmo resultado e a versão. |

O resultado efetivamente capturado, com IDs e versões, fica em `evidence/experiments.json` na entrega. O exemplo de 375 no Global vale para uma base com apenas o conjunto de exemplo; reexecuções preservam despesas anteriores e, portanto, aumentam o total global. Cada execução usa identidades novas para que Mine continue verificável com 175.

## Matriz da avaliação

| Item | Implementação / evidência |
| --- | --- |
| I01 / 20 | ReportsController; ReportRules; Excel; ReportBehaviorTests; E01. |
| I02 / 15 | ReportRules.Authorize; consultas com OwnerId; ReportAccessTests; E04. |
| I03 / 20 | RabbitPublisher; ReportProcessor; ReportsWorker; E02/E03 e teste de falha do broker. |
| I04 / 15 | Suíte MSTest isolada e resultados CI; roteiro operacional separado. |
| I05 / 15 | Dockerfiles, Compose, pipeline CP3 Tests and Images, SHA e digests. |
| I06 / 10 | Roteiro e evidence/experiments.json; diagnóstico acima. |
| I07 / 5 | Analisadores anteriores preservados; build/format/qualidade; diff da CP2. |

## Uso de IA e entrega

Desenvolvimento assistido por Codex, autorizado no enunciado. A evidência deve demonstrar a execução real, não ser inferida da geração do código. A entrega é individual; Eduardo deve revisar e compreender o fluxo.

Prazo adotado do anúncio do Teams: 04/11/2026 às 23h59. Apostila e issues contêm 04/10; essa divergência foi registrada. LinkedIn é opcional, individual e exige relato pessoal publicado; não foi publicado automaticamente.
