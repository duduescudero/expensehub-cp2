# ExpenseHub â€” Checkpoint 2 de C#

**Aluno:** Eduardo Pires Escudero â€” **RM:** 556527  
**RepositÃ³rio:** https://github.com/duduescudero/expensehub-cp2

API REST de reembolsos implementada em ASP.NET Core 10, Identity, EF Core e SQLite.
ImplementaÃ§Ã£o assistida por Codex, com revisÃ£o de regras, testes unitÃ¡rios e validaÃ§Ã£o HTTP local.

## PrÃ©-requisitos

- SDK .NET 10 suportado.
- Git; nenhum servidor de banco Ã© necessÃ¡rio.

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

A senha deve cumprir a polÃ­tica do Identity: pelo menos oito caracteres, maiÃºscula,
minÃºscula, nÃºmero e sÃ­mbolo. NÃ£o salve a senha em arquivos rastreados, prints ou commits.
O bootstrap exige o e-mail configurado e cria **somente um usuÃ¡rio Admin**. Em reinÃ­cios,
o mesmo e-mail reaproveita a conta existente; mantenha a configuraÃ§Ã£o consistente.
As cinco roles sÃ£o criadas de forma idempotente. Outros usuÃ¡rios sÃ£o cadastrados por HTTP.

Em Linux/macOS, forneÃ§a as mesmas configuraÃ§Ãµes pelas variÃ¡veis `Bootstrap__Email`
e `Bootstrap__Password` antes de executar `dotnet run`.

## Banco de dados

Provider: `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12.
Identity utiliza `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12.
O arquivo `expensehub.db` Ã© criado automaticamente no diretÃ³rio de execuÃ§Ã£o atravÃ©s de
`EnsureCreatedAsync`. Categorias iniciais: 1 Transporte, 2 AlimentaÃ§Ã£o, 3 Hospedagem, 4 Outros.
Este procedimento equivale Ã  criaÃ§Ã£o inicial do esquema e **nÃ£o aplica migraÃ§Ãµes incrementais**.
Para recriar o banco durante desenvolvimento, pare a API e remova apenas o banco local descartÃ¡vel,
ou informe outro caminho em `ConnectionStrings__ExpenseHub`.
NÃ£o versione `.db`, arquivos auxiliares, `bin`, `obj`, tokens nem credenciais.

## AutenticaÃ§Ã£o e usuÃ¡rios

1. `POST /login` com `email` e `password`: resposta com `accessToken`, `refreshToken`,
   `tokenType` e `expiresIn`. Use `Authorization: Bearer <accessToken>` nas rotas protegidas.
   O token Ã© o bearer opaco protegido pelo Identity; JWT nÃ£o Ã© necessÃ¡rio ao contrato.
2. `POST /register` com `email` e `password`: cria usuÃ¡rio sem roles.
   Propriedades extras nunca atribuem roles.
3. Admin usa `GET /api/admin/users` para identificar o usuÃ¡rio e
   `PUT /api/admin/users/{id}/roles` com `{"roles":["Employee"]}` para conceder acesso.
4. FaÃ§a login novamente apÃ³s mudar roles; tokens jÃ¡ emitidos representam o perfil anterior
   atÃ© expirarem. Admin nÃ£o pode remover sua prÃ³pria role Admin.

Roles: Employee, Approver, Finance, Auditor e Admin. Roles se acumulam, mas nÃ£o permitem
aprovar, reprovar ou pagar a prÃ³pria despesa. Admin isoladamente nÃ£o opera reembolsos.

## Fluxo de reembolso

Corpo de criaÃ§Ã£o/ediÃ§Ã£o (substitua a data por uma data nÃ£o futura):

```json
{"description":"Transporte em visita ao cliente","amount":125.50,"expenseDate":"2026-10-12","categoryId":1}
```

| MÃ©todo e rota | Papel e comportamento |
|---|---|
| POST /api/expenses | Employee cria Draft, proprietÃ¡rio obtido do token |
| PUT /api/expenses/{id} | Employee proprietÃ¡rio edita somente Draft |
| POST /api/expenses/{id}/submit | Employee proprietÃ¡rio envia Draft |
| GET /api/expenses | Filtro aplicado no banco conforme roles |
| GET /api/expenses/{id} | Detalhe visÃ­vel; fora do escopo retorna 404 |
| POST /api/expenses/{id}/approve | Approver nÃ£o proprietÃ¡rio: Submitted â†’ Approved |
| POST /api/expenses/{id}/reject | Approver nÃ£o proprietÃ¡rio: Submitted â†’ Rejected |
| POST /api/expenses/{id}/pay | Finance nÃ£o proprietÃ¡rio: Approved â†’ Paid |
| GET /api/expenses/{id}/history | HistÃ³rico no mesmo escopo de leitura |

ReprovaÃ§Ã£o: `{"reason":"Despesa nÃ£o atende Ã  polÃ­tica corporativa"}`.
DescriÃ§Ã£o e justificativa: 10 a 500 caracteres apÃ³s remover espaÃ§os externos.
Valor: decimal de 0,01 a 2.147.483.647. Data: vÃ¡lida e nÃ£o futura, comparada Ã  data UTC do servidor.
Paid e Rejected sÃ£o finais. NÃ£o hÃ¡ exclusÃ£o, reabertura, cancelamento ou reenvio.
Atores, horÃ¡rios UTC, proprietÃ¡rio e estados vÃªm exclusivamente do servidor.

## SeguranÃ§a e consistÃªncia

- Role na rota e regras contextuais na camada de serviÃ§o.
- Employee lÃª somente prÃ³prias despesas; Approver lÃª Submitted; Finance lÃª Approved/Paid;
  Auditor lÃª tudo sem conceder escrita; perfis combinados unem os escopos.
- 400 entrada invÃ¡lida; 401 sem credencial vÃ¡lida; 403 operaÃ§Ã£o nÃ£o permitida;
  404 recurso inexistente/invisÃ­vel; 409 estado incompatÃ­vel, repetiÃ§Ã£o ou concorrÃªncia.
- AlteraÃ§Ã£o, histÃ³rico e pagamento sÃ£o persistidos juntos em um `SaveChangesAsync`,
  com transaÃ§Ã£o automÃ¡tica do EF Core. Revision Ã© token de concorrÃªncia otimista.
- HistÃ³rico registra criaÃ§Ã£o, dados anteriores/posteriores na ediÃ§Ã£o e transiÃ§Ãµes.
- Identity armazena hash de senha, nunca senha em texto puro. Sem credenciais versionadas.

## VerificaÃ§Ã£o

```shell
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet format ./sources/ExpenseHub.slnx --verify-no-changes
```

41 casos unitÃ¡rios executam sem banco, rede ou serviÃ§o externo e cobrem transiÃ§Ãµes,
repetiÃ§Ãµes, ownership, justificativa, pagamento, histÃ³rico, escopos e limites de entrada.
ValidaÃ§Ã£o HTTP adicional local executou 73 verificaÃ§Ãµes de cadastro, roles, consultas,
fluxos positivos e negativos. Esses testes HTTP complementam a revisÃ£o; nÃ£o substituem a suÃ­te unitÃ¡ria.

`GET /health` confirma disponibilidade. Em Development, `/openapi/v1.json` documenta as rotas.
O workflow original `code-quality` permanece intacto; consulte o artefato e o score,
pois workflow verde sozinho nÃ£o garante 100 pontos.

## Entrega e autoria

O backlog original estÃ¡ em https://github.com/Racass/checkpoint-csharpracass-expensehub/issues.
Branches e PRs identificam as dez issues; referÃªncias nÃ£o fecham as issues do professor.
A entrega deve informar URL pÃºblica, SHA exato da main e resultado do pipeline nesse SHA.
NÃ£o publique uma branch intermediÃ¡ria como versÃ£o final. Nenhum deploy ou frontend Ã© necessÃ¡rio.

DecisÃµes: SQLite para execuÃ§Ã£o reproduzÃ­vel, bearer do Identity, regras em serviÃ§o,
concorrÃªncia otimista e histÃ³rico transacional. Uso de IA declarado acima; validaÃ§Ãµes nÃ£o foram inventadas.
