# ExpenseHub — Checkpoint 2 de C#

**Aluno:** Eduardo Pires Escudero — **RM:** 556527<br>
**Repositório:** https://github.com/duduescudero/expensehub-cp2

API REST de reembolsos implementada em ASP.NET Core 10, Identity, EF Core e SQLite.
Implementação assistida por Codex, com revisão de regras, testes unitários e validação HTTP local.

## Pré-requisitos

- SDK .NET 10 suportado.
- Git; nenhum servidor de banco é necessário.

## Executar no PowerShell

```powershell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
$env:Bootstrap__Email = Read-Host 'E-mail do administrador inicial'
$bootstrapSecret = Read-Host 'Senha inicial do administrador' -AsSecureString
$env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $bootstrapSecret).Password
dotnet run --project ./sources/ExpenseHub.Api --no-launch-profile --urls http://localhost:5187
```

A senha deve cumprir a política do Identity: pelo menos oito caracteres, maiúscula,
minúscula, número e símbolo. Não salve a senha em arquivos rastreados, prints ou commits.
O bootstrap exige o e-mail configurado e cria **somente um usuário Admin**. Em reinícios,
o mesmo e-mail reaproveita a conta existente; mantenha a configuração consistente.
As cinco roles são criadas de forma idempotente. Outros usuários são cadastrados por HTTP.

Em Linux/macOS, forneça as mesmas configurações pelas variáveis `Bootstrap__Email`
e `Bootstrap__Password` antes de executar `dotnet run`.

## Banco de dados

Provider: `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12.
Identity utiliza `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12.
O arquivo `expensehub.db` é criado automaticamente no diretório de execução através de
`EnsureCreatedAsync`. Categorias iniciais: 1 Transporte, 2 Alimentação, 3 Hospedagem, 4 Outros.
Este procedimento equivale à criação inicial do esquema e **não aplica migrações incrementais**.
Para recriar o banco durante desenvolvimento, pare a API e remova apenas o banco local descartável,
ou informe outro caminho em `ConnectionStrings__ExpenseHub`.
Não versione `.db`, arquivos auxiliares, `bin`, `obj`, tokens nem credenciais.

## Autenticação e usuários

1. `POST /login` com `email` e `password`: resposta com `accessToken`, `refreshToken`,
   `tokenType` e `expiresIn`. Use `Authorization: Bearer <accessToken>` nas rotas protegidas.
   O token é o bearer opaco protegido pelo Identity; JWT não é necessário ao contrato.
2. `POST /register` com `email` e `password`: cria usuário sem roles.
   Propriedades extras nunca atribuem roles.
3. Admin usa `GET /api/admin/users` para identificar o usuário e
   `PUT /api/admin/users/{id}/roles` com `{"roles":["Employee"]}` para conceder acesso.
4. Faça login novamente após mudar roles; tokens já emitidos representam o perfil anterior
   até expirarem. Admin não pode remover sua própria role Admin.

Roles: Employee, Approver, Finance, Auditor e Admin. Roles se acumulam, mas não permitem
aprovar, reprovar ou pagar a própria despesa. Admin isoladamente não opera reembolsos.

## Fluxo de reembolso

Corpo de criação/edição (substitua a data por uma data não futura):

```json
{"description":"Transporte em visita ao cliente","amount":125.50,"expenseDate":"2026-10-12","categoryId":1}
```

| Método e rota | Papel e comportamento |
|---|---|
| POST /api/expenses | Employee cria Draft, proprietário obtido do token |
| PUT /api/expenses/{id} | Employee proprietário edita somente Draft |
| POST /api/expenses/{id}/submit | Employee proprietário envia Draft |
| GET /api/expenses | Filtro aplicado no banco conforme roles |
| GET /api/expenses/{id} | Detalhe visível; fora do escopo retorna 404 |
| POST /api/expenses/{id}/approve | Approver não proprietário: Submitted → Approved |
| POST /api/expenses/{id}/reject | Approver não proprietário: Submitted → Rejected |
| POST /api/expenses/{id}/pay | Finance não proprietário: Approved → Paid |
| GET /api/expenses/{id}/history | Histórico no mesmo escopo de leitura |

Reprovação: `{"reason":"Despesa não atende à política corporativa"}`.
Descrição e justificativa: 10 a 500 caracteres após remover espaços externos.
Valor: decimal de 0,01 a 2.147.483.647. Data: válida e não futura, comparada à data UTC do servidor.
Paid e Rejected são finais. Não há exclusão, reabertura, cancelamento ou reenvio.
Atores, horários UTC, proprietário e estados vêm exclusivamente do servidor.

## Segurança e consistência

- Role na rota e regras contextuais na camada de serviço.
- Employee lê somente próprias despesas; Approver lê Submitted; Finance lê Approved/Paid;
  Auditor lê tudo sem conceder escrita; perfis combinados unem os escopos.
- 400 entrada inválida; 401 sem credencial válida; 403 operação não permitida;
  404 recurso inexistente/invisível; 409 estado incompatível, repetição ou concorrência.
- Alteração, histórico e pagamento são persistidos juntos em um `SaveChangesAsync`,
  com transação automática do EF Core. Revision é token de concorrência otimista.
- Histórico registra criação, dados anteriores/posteriores na edição e transições.
- Identity armazena hash de senha, nunca senha em texto puro. Sem credenciais versionadas.

## Verificação

```shell
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet format ./sources/ExpenseHub.slnx --verify-no-changes
```

41 casos unitários executam sem banco, rede ou serviço externo e cobrem transições,
repetições, ownership, justificativa, pagamento, histórico, escopos e limites de entrada.
Validação HTTP adicional local executou 73 verificações de cadastro, roles, consultas,
fluxos positivos e negativos. Esses testes HTTP complementam a revisão; não substituem a suíte unitária.

`GET /health` confirma disponibilidade. Em Development, `/openapi/v1.json` documenta as rotas.
O workflow original `code-quality` permanece intacto; consulte o artefato e o score,
pois workflow verde sozinho não garante 100 pontos.

## Entrega e autoria

O backlog original está em https://github.com/Racass/checkpoint-csharpracass-expensehub/issues.
Branches e PRs identificam as dez issues; referências não fecham as issues do professor.
A entrega deve informar URL pública, SHA exato da main e resultado do pipeline nesse SHA.
Não publique uma branch intermediária como versão final. Nenhum deploy ou frontend é necessário.

Decisões: SQLite para execução reproduzível, bearer do Identity, regras em serviço,
concorrência otimista e histórico transacional. Uso de IA declarado acima; validações não foram inventadas.
